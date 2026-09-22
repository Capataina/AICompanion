extern alias live;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain;
using live::AICompanion.Companion.Brain.Activities;
using Audit = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.AuditDecisionContracts;
using BrainTelemetry = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;
using LiveTerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
// The movement core is compiled a second time into this project, so the edit record exists in two
// copies and an announcement naming one leaves the other holding a world the edit never happened in.
using TerrainChanges = AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;

/// <summary>
/// Seeded event sequences nobody wrote, driven through the whole tick, against the six contracts the
/// recorder audits every published decision with.
///
/// <b>Why a generator rather than another scene.</b> Every one of `VerifyDecisionTripwires`' scenes was
/// built by hand from the capture of 22 September 2026, so each contract has been shown to fire on the
/// case somebody already knew about and to stay quiet on the neighbouring case somebody already thought
/// of. What none of them establishes is that the contracts stay quiet on a world nobody composed — and
/// that is the question a tripwire exists to answer, because the next contradiction will arrive in a
/// sequence of spawns, deaths, pickups and budget cuts that no fixture author enumerated.
///
/// <b>It drives the production path end to end rather than the audit's seam.</b> The tripwire rows call
/// <c>AuditDecisionContracts.Audit</c> directly with a payload they compose; that is right for them,
/// because each is about the rule. Here the payload has to be the one the brain really writes, so the
/// live recorder is attached, <c>BrainTelemetry.Load</c> installs the real
/// <c>ReadLiveCourseForAudit</c> source, <c>CompanionNPC.AI</c> records every tick, and what this file
/// reads is the *mod assembly's* own <c>AuditDecisionContracts.Counts</c>. Composing a payload here
/// would make the fuzzer test this file's idea of a decision record; <c>DecideCourseEachTick.Trace</c>
/// builds those fields privately and there is no accessor, so any local copy of them is a drift waiting
/// to happen.
///
/// <b>Two of the six cannot be asserted here, and both say so rather than passing quietly.</b>
/// <c>decide-overran-allowance</c> is a wall-clock contract, and this suite lifts every millisecond
/// allowance on purpose and runs about 2.3 times slower per operation than a standalone process — so a
/// count of overruns here measures how far through the run the case sits, which is the regime rule
/// `Tools/EngineReplay/CLAUDE.md` already carries. Its firings are printed and nothing is asserted from
/// them. And <c>accepted-use-absent-next-tick</c> and <c>activity-exited-during-decision</c> both fire
/// legitimately when the *world* removes what a course was holding, which is exactly what a generator
/// that kills and teleports hostiles does on purpose: each firing is therefore attributed to the tick it
/// happened on and classified against the events this file injected in the three ticks up to it, and the
/// assertion is on the unattributed ones. Both counts are reported, because suppressing the attributed
/// half would hide a real coarseness in those two contracts rather than record it.
/// </summary>
internal static class FuzzTheDecisionContracts
{
    private const string Family = "decision fuzz";

    /// <summary>
    /// The seeds a run always includes, plus one taken from the clock, printed in every row.
    ///
    /// The fixed list is what makes a red reproducible: a firing names its seed and its tick, and
    /// re-running that seed alone replays the same sequence. The clock seed is what stops the fixed list
    /// from becoming the only five worlds this contract set has ever met — it is a different sequence on
    /// every run, so a contradiction reachable by some sequence is eventually reached rather than never.
    /// A red on the clock seed is reproducible too, by naming it back through <c>AIC_FUZZ_SEEDS</c>.
    /// </summary>
    private static readonly int[] FixedSeeds = { 1, 7, 13, 29, 101 };

    /// <summary>How many ticks one seeded sequence drives. Long enough that a course is published,
    /// carried, released and re-decided several times over — `VerifyAdmittedOpportunitiesBind` needs
    /// about 150 ticks of its own scene to reach the second of those — and short enough that six
    /// sequences stay inside the budget a default-suite case may spend.</summary>
    private const int TicksPerSequence = 200;

    /// <summary>How far back from a firing this file looks for an event it injected itself. The audit's
    /// two transition contracts read a window of one tick either side of a publication, and the tick
    /// that records a release is the tick after the one that invalidated the use, so three ticks covers
    /// the causal window with one to spare rather than being a tuned number.</summary>
    private const int WorldEventWindow = 3;

    /// <summary>The contract kinds, in the order the audit's own file declares them.</summary>
    private static readonly string[] Contracts =
    {
        "census-admitted-binder-refused",
        "empty-course-beside-usable-work",
        "accepted-use-absent-next-tick",
        "activity-exited-during-decision",
        "fact-count-above-bound",
        "decide-overran-allowance",
    };

    /// <summary>The two whose firings a world event legitimately explains, so the assertion is on the
    /// firings no injected event accounts for.</summary>
    private static readonly HashSet<string> WorldExplainable = new(StringComparer.Ordinal)
    {
        "accepted-use-absent-next-tick",
        "activity-exited-during-decision",
    };

    /// <summary>One contract firing, dated and attributed.</summary>
    private readonly record struct Firing(int Seed, int Tick, string Kind, bool WorldCaused, string Nearby);

    public static int Run()
    {
        int seedFromTheClock = (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
        int[] seeds = Environment.GetEnvironmentVariable("AIC_FUZZ_SEEDS") is { Length: > 0 } named
            ? named.Split(',').Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray()
            : FixedSeeds.Append(seedFromTheClock).ToArray();

        var firings = new List<Firing>();
        var sequences = new List<SequenceOutcome>();
        FieldInfo savePath = typeof(Terraria.Program).GetField("SavePath",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Terraria save-path backing field is unavailable");
        object? priorSavePath = savePath.GetValue(null);
        WorkPolicy priorMining = WorkPolicies.Mining, priorChopping = WorkPolicies.Chopping;
        long priorAllowance = Brain.PlanningOperationAllowance;
        Vector2 priorScreen = Main.screenPosition;
        string root = Path.Combine(Path.GetTempPath(), "aic-decision-fuzz-" + Guid.NewGuid().ToString("N"));
        savePath.SetValue(null, root);

        // A recorded row with a hostile in it writes `NPC.TypeName`, which reads Lang's NPC name cache;
        // a headless process never loaded localisation, so every entry is null and the writer throws.
        // Empty names are supplied for exactly the null entries and restored afterwards, so no later
        // case inherits them. Measured here rather than assumed: without it the first tick of the first
        // sequence threw out of `BrainTelemetry.Record` and every contract row below reported green.
        var nameCache = (Terraria.Localization.LocalizedText[])typeof(Lang)
            .GetField("_npcNameCache", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var filledNames = new List<int>();
        for (int type = 0; type < nameCache.Length; type++)
            if (nameCache[type] == null) { nameCache[type] = Terraria.Localization.LocalizedText.Empty; filledNames.Add(type); }

        // Recording is what carries a decision to the audit at all, so it is set rather than inherited
        // from whatever the previous case left on the shared config instance.
        var config = Terraria.ModLoader.ModContent.GetInstance<live::AICompanion.Companion.DiagnosticsConfiguration.CompanionDiagnosticsConfig>();
        if (config == null)
        {
            config = new live::AICompanion.Companion.DiagnosticsConfiguration.CompanionDiagnosticsConfig();
            Terraria.ModLoader.ContentInstance.Register(config);
        }
        bool priorRecording = config.RecordTelemetry;
        config.RecordTelemetry = true;
        config.OnChanged();
        // A throw from inside a game path arrives here with no seed and no tick on it, and this fixture
        // drives six worlds: the whole exception goes into the row, because finding which sequence threw
        // any other way costs a rerun per seed.
        Exception? instrumentFault = null;
        try
        {
            foreach (int seed in seeds) sequences.Add(DriveOneSequence(seed, firings));
        }
        catch (Exception failure) when (failure is not InvalidOperationException)
        {
            instrumentFault = failure;
        }
        finally
        {
            OpenTheRecorderOnACompanion.Close();
            config.RecordTelemetry = priorRecording;
            config.OnChanged();
            foreach (int type in filledNames) nameCache[type] = null!;
            Brain.PlanningOperationAllowance = priorAllowance;
            WorkPolicies.Mining = priorMining;
            WorkPolicies.Chopping = priorChopping;
            Main.screenPosition = priorScreen;
            ClearTheScene();
            savePath.SetValue(null, priorSavePath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        long decisions = sequences.Sum(s => s.Decisions), observations = sequences.Sum(s => s.Observations);
        long unsettled = sequences.Sum(s => s.UnsettledTicks), cuts = sequences.Sum(s => s.AllowanceCuts);
        string header = $"{seeds.Length} seeded sequence(s) of {TicksPerSequence} ticks "
            + $"(seeds {string.Join(",", seeds)}, the last from the clock unless AIC_FUZZ_SEEDS named them): "
            + $"{decisions} decision(s) audited, {observations} frozen observation(s) read, "
            + $"{unsettled} unsettled tick(s), {cuts} allowance cut(s)";
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail(Family + ": " + header);

        int failed = 0;
        if (instrumentFault != null)
        {
            AICompanion.Tools.Ledger.EmitLedgerRows.Detail(Family + " threw rather than reporting: " + instrumentFault);
            failed++;
        }
        // The premises first, because every row below is vacuous without them and a vacuous row is worse
        // than an absent one: it reports a contract as unbroken by a fuzzer that never reached it.
        failed += RunOneRow.Case("the generator reached the audit through the live recorder",
            () => ThePremisesHold(decisions, observations, unsettled), Family);
        foreach (string kind in Contracts)
            failed += RunOneRow.Case($"no seeded sequence broke {kind}",
                () => TheContractHeld(kind, firings, seeds), Family);
        return failed;
    }

    /// <summary>What one sequence established, kept so the rows can say whether the fuzzer reached the
    /// thing it claims to have found nothing in.</summary>
    private readonly record struct SequenceOutcome(int Seed, long Decisions, long Observations, int UnsettledTicks, int AllowanceCuts);

    /// <summary>
    /// The premises, asserted rather than assumed.
    ///
    /// A run with no observations read is the broken-installer shape the audit's own comments describe:
    /// every decision counted, no frozen observation consulted, and four of the six contracts silently
    /// unable to fire. A run with no unsettled tick has not exercised either transition contract at all.
    /// Both would otherwise read as five green rows.
    /// </summary>
    private static void ThePremisesHold(long decisions, long observations, long unsettled)
    {
        Require(decisions > 0, "no decision reached the audit, so every row below is about a fuzzer that never ran");
        Require(observations > 0,
            $"{decisions} decision(s) audited and {observations} frozen observation(s) read: the audit's source was "
            + "never installed, which leaves four of the six contracts unable to fire while every row still passes "
            + "— this is BrainTelemetry.Load not reaching ReadLiveCourseForAudit.Install in this host");
        Require(unsettled > 0,
            "no tick left its decision unsettled, so the two transition contracts were never reachable; the "
            + "operation cuts this generator applies are drawn from 150..400, the window "
            + "VerifyADecisionInFlightKeepsTheFight measured, and a tree where that window no longer holds a "
            + "decision open needs the window measured again rather than this row believed");
    }

    /// <summary>
    /// One contract's verdict over every seed.
    ///
    /// <c>decide-overran-allowance</c> is reported and never asserted, for the regime reason at the head
    /// of this file. The two world-explainable kinds assert on the firings no injected event accounts
    /// for, and print both halves.
    /// </summary>
    private static void TheContractHeld(string kind, List<Firing> firings, int[] seeds)
    {
        var mine = firings.Where(f => f.Kind == kind).ToList();
        if (kind == "decide-overran-allowance")
        {
            AICompanion.Tools.Ledger.EmitLedgerRows.Detail(
                $"{Family} decide-overran-allowance: {mine.Count} firing(s), reported and not asserted — this suite "
                + "lifts every millisecond allowance and runs about 2.3x slower per operation than a standalone "
                + "process, so a count here measures where in the run the case sits"
                + (mine.Count == 0 ? "" : "; first " + Describe(mine[0])));
            return;
        }

        var unattributed = mine.Where(f => !f.WorldCaused).ToList();
        if (WorldExplainable.Contains(kind) && mine.Count != unattributed.Count)
            AICompanion.Tools.Ledger.EmitLedgerRows.Detail(
                $"{Family} {kind}: {mine.Count - unattributed.Count} of {mine.Count} firing(s) stand within "
                + $"{WorldEventWindow} tick(s) of an event this generator injected, which the contract cannot tell "
                + "from the loop it is named for; they are reported rather than asserted on");

        Require(unattributed.Count == 0,
            $"{unattributed.Count} of {mine.Count} firing(s) of {kind} are explained by nothing this generator did "
            + $"to the world; seeds run were {string.Join(",", seeds)}; first three: "
            + string.Join(" | ", unattributed.Take(3).Select(Describe)));
    }

    private static string Describe(Firing firing)
        => $"seed {firing.Seed} tick {firing.Tick}: {firing.Kind} [{firing.Nearby}]";

    /// <summary>
    /// One seeded sequence: a live recorder, a real scene, and a world that changes underneath the brain
    /// on a schedule nobody wrote.
    ///
    /// The counts are read out of the mod assembly's own audit after every tick rather than at the end,
    /// because a total says a contract fired and nothing about when — and when is the whole of the
    /// attribution below.
    /// </summary>
    private static SequenceOutcome DriveOneSequence(int seed, List<Firing> firings)
    {
        var recorder = new BrainTelemetry();
        // The production entry point rather than `ReadLiveCourseForAudit.Install()` by name: the install
        // is a wiring no headless row can witness, and going in through the override at least witnesses
        // that `Load` still does it. The helper installs the source too, so this is belt and braces
        // rather than the only route.
        recorder.Load();
        Audit.Reset();
        ActionContext ctx = Scene(seed);
        OpenTheRecorderOnACompanion.Open(recorder, ctx.Companion);

        var random = new Random(seed);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var recentEvents = new List<(int Tick, string What)>();
        int unsettled = 0, cuts = 0, cutUntil = -1;
        long allowance = Brain.PlanningOperationAllowance;
        try
        {
            for (int tick = 0; tick < TicksPerSequence; tick++)
            {
                string injected = Disturb(ctx, random, tick, ref cutUntil, ref cuts);
                if (injected.Length > 0) recentEvents.Add((tick, injected));
                if (tick == cutUntil) Brain.PlanningOperationAllowance = allowance;

                VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
                ctx.Companion.AI();

                if (!ctx.Companion.Brain.Course.Last.Settled) unsettled++;
                CollectFirings(seed, tick, counts, recentEvents, firings);
            }
        }
        finally
        {
            Brain.PlanningOperationAllowance = allowance;
            recorder.OnWorldUnload();
            ClearTheScene();
        }
        return new SequenceOutcome(seed, Audit.Audited, Audit.ObservationsRead, unsettled, cuts);
    }

    /// <summary>
    /// Which kinds moved on this tick, and whether anything this file did to the world in the last few
    /// ticks accounts for them.
    ///
    /// The attribution is deliberately generous to the contract rather than to the fuzzer: any injected
    /// event inside the window excuses the firing. A stricter rule would need to know which body the
    /// course was holding, which is exactly the fact the contract itself does not carry — and inventing
    /// it here would make this file a second, private implementation of the rule under test.
    /// </summary>
    private static void CollectFirings(int seed, int tick, Dictionary<string, long> counts,
        List<(int Tick, string What)> recentEvents, List<Firing> firings)
    {
        IReadOnlyDictionary<string, long> now = Audit.Counts;
        foreach (string kind in Contracts)
        {
            long total = now.TryGetValue(kind, out long value) ? value : 0;
            long had = counts.TryGetValue(kind, out long before) ? before : 0;
            if (total <= had) continue;
            counts[kind] = total;
            var nearby = recentEvents.Where(e => tick - e.Tick <= WorldEventWindow).ToList();
            firings.Add(new Firing(seed, tick, kind, nearby.Count > 0,
                nearby.Count == 0 ? "no injected event in the window"
                    : string.Join("; ", nearby.Select(e => $"t{e.Tick} {e.What}"))));
        }
        recentEvents.RemoveAll(e => tick - e.Tick > WorldEventWindow);
    }

    /// <summary>
    /// The seeded disturbance: what happens to the world on this tick, and its name for the record.
    ///
    /// Every arm is something a real minute of play does — a hostile arrives, a hostile dies, a hostile
    /// is thrown by knockback, a drop appears, a drop is picked up, a tile is mined, the player walks or
    /// stops, a work policy is switched on the profile card, and the brain's share of the tick runs out.
    /// What the generator adds is the *order*, which is the part no scene author enumerates.
    /// </summary>
    private static string Disturb(ActionContext ctx, Random random, int tick, ref int cutUntil, ref int cuts)
    {
        int roll = random.Next(100);
        switch (roll)
        {
            case < 8:
            {
                int slot = 30 + random.Next(7);
                if (Main.npc[slot].active) return "";
                Spawn(slot, new Vector2((40 + random.Next(24)) * 16, 60 * 16));
                return $"hostile {slot} spawned";
            }
            case < 14:
            {
                int slot = FirstLive(30, 37);
                if (slot < 0) return "";
                Main.npc[slot].life = 0;
                Main.npc[slot].active = false;
                return $"hostile {slot} killed";
            }
            case < 18:
            {
                int slot = FirstLive(30, 37);
                if (slot < 0) return "";
                Main.npc[slot].Bottom = new Vector2((40 + random.Next(24)) * 16, 60 * 16);
                return $"hostile {slot} teleported";
            }
            case < 24:
            {
                int slot = 10 + random.Next(3);
                if (Main.item[slot].active) return "";
                Item drop = VerifyCollectionContracts.Drop(ItemID.CopperOre, 1 + random.Next(5),
                    new Vector2((44 + random.Next(16)) * 16 + 8, 60 * 16), slot);
                drop.playerIndexTheItemIsReservedFor = Main.myPlayer;
                return $"drop {slot} appeared";
            }
            case < 29:
            {
                for (int slot = 10; slot < 13; slot++)
                    if (Main.item[slot].active) { Main.item[slot].active = false; return $"drop {slot} taken"; }
                return "";
            }
            case < 34:
            {
                // A mined tile, announced through both copies of the edit record. The engine's own
                // `GlobalTile` dispatch does not reach this host, so an edit written straight into
                // `Main.tile` would be invisible to every retained search — which is the class of
                // silence `Tools/WorldRun/CLAUDE.md` names for its own loader-hook sweep.
                int x = 40 + random.Next(24);
                Main.tile[x, 60].ClearEverything();
                TerrainChanges.Changed(x, 60);
                LiveTerrainChanges.Changed(x, 60);
                return $"tile {x},60 mined";
            }
            case < 44:
            {
                ctx.Player.velocity = new Vector2((random.Next(2) == 0 ? -1 : 1) * (1f + random.Next(4)), 0f);
                ctx.Player.Bottom += ctx.Player.velocity;
                return "player walking";
            }
            case < 50:
            {
                ctx.Player.velocity = Vector2.Zero;
                return "player stopped";
            }
            case < 54:
            {
                WorkPolicy policy = (WorkPolicy)random.Next(Enum.GetValues<WorkPolicy>().Length);
                if (random.Next(2) == 0) WorkPolicies.Mining = policy; else WorkPolicies.Chopping = policy;
                return $"work policy -> {policy}";
            }
            case < 60 when cutUntil < tick:
            {
                // The window `VerifyADecisionInFlightKeepsTheFight` measured on 22 September 2026: above
                // about four thousand operations every search finishes and no decision spans a tick, and
                // below about a hundred combat is starved of a plan before the decision can hold one.
                Brain.PlanningOperationAllowance = 150 + random.Next(251);
                cutUntil = tick + 1 + random.Next(4);
                cuts++;
                return $"allowance cut to {Brain.PlanningOperationAllowance} until t{cutUntil}";
            }
            default:
                // The player keeps travelling on a tick nothing else happens on, so the intent region
                // leads rather than resting: a generator whose player only moves on its own arm spends
                // most of its ticks beside a standing player, which is the one scene already covered.
                ctx.Player.Bottom += ctx.Player.velocity;
                return "";
        }
    }

    private static int FirstLive(int from, int toExclusive)
    {
        for (int slot = from; slot < toExclusive; slot++) if (Main.npc[slot].active) return slot;
        return -1;
    }

    /// <summary>
    /// The scene every sequence starts from: the tail of capture <c>2026-09-22_10-05-56-125</c> reduced
    /// to what the brain reads — a player on a floor with hostiles in his region and drops around him —
    /// seeded so two seeds do not begin identically. It is the scene
    /// <c>VerifyAdmittedOpportunitiesBind</c> reproduces, deliberately, because a contradiction the
    /// generator finds should be readable against a recording somebody has already looked at.
    /// </summary>
    private static ActionContext Scene(int seed)
    {
        var random = new Random(seed);
        ActionContext ctx = VerifyCollectionContracts.SetUpFloor();
        ctx.Player.Bottom = new Vector2(50 * 16, 60 * 16);
        ctx.Player.velocity = Vector2.Zero;
        ctx.Player.dead = false;
        ctx.Npc.Bottom = new Vector2(50 * 16, 59 * 16);
        // The screen is centred on the player as the live game centres it, because combat's own nearness
        // term reads the screen and a fixture that leaves it where the previous case left it is scoring
        // a fight against somebody else's viewport.
        Main.screenPosition = ctx.Player.Center - new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
        ctx.Player.GetModPlayer<CompanionPlayer>().Gear.Slots[0].SetDefaults(ItemID.WoodenBow);
        ctx.Player.GetModPlayer<CompanionPlayer>().Gear.Slots[1] = new Item();
        WorkPolicies.Mining = WorkPolicy.Opportunistic;
        WorkPolicies.Chopping = WorkPolicy.Opportunistic;
        int hostiles = 2 + random.Next(4);
        for (int i = 0; i < hostiles; i++) Spawn(30 + i, new Vector2((44 + i * 3) * 16, 60 * 16));
        int drops = random.Next(3);
        for (int i = 0; i < drops; i++)
        {
            Item drop = VerifyCollectionContracts.Drop(ItemID.CopperOre, 5, new Vector2((52 + i) * 16 + 8, 60 * 16), 10 + i);
            drop.playerIndexTheItemIsReservedFor = Main.myPlayer;
        }
        ctx.Senses.Update(ctx.Npc, ctx.Player);
        return ctx;
    }

    private static NPC Spawn(int slot, Vector2 bottom)
    {
        NPC hostile = Main.npc[slot];
        hostile.SetDefaults(NPCID.Zombie);
        hostile.whoAmI = slot;
        hostile.active = true;
        hostile.velocity = Vector2.Zero;
        hostile.Bottom = bottom;
        return hostile;
    }

    /// <summary>The per-case reset rebuilds the tile map and restores the statics, and it does not
    /// deactivate a slot in <c>Main.item</c> or <c>Main.npc</c> — so a sequence that left a hostile
    /// standing hands it to whatever runs next.</summary>
    private static void ClearTheScene()
    {
        for (int slot = 10; slot <= 12; slot++) { Main.item[slot] = new Item(); Main.item[slot].active = false; }
        for (int slot = 30; slot <= 36; slot++) { Main.npc[slot].active = false; Main.npc[slot].life = 0; }
        // And the companion's own slot: a body left in `Main.npc` under the registered type is a live
        // companion to anything that scans for one, including the next case's `CompanionNPC.Instance`.
        OpenTheRecorderOnACompanion.Clear();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
