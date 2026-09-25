#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Interactions;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Mining;
using AICompanion.Companion.Brain.Infrastructure.Interactions.WorldProtection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Activities.Gathering;

/// <summary>One immutable native observation of a mine or chop purpose.  The source below only
/// deserializes this value; live tiles, policies and reachability are deliberately absent there.</summary>
public sealed record GatheringOpportunityFact(string Domain, string Target, long Generation, int TileX, int TileY,
    int Material, string Purpose, double RemainingAmount, double CensusAmount, string Admission, string Reason,
    string Detail, double StandX = 0, double StandY = 0, CapturedToolWork? Work = null);

/// <summary>The native mechanism's next physical application, separate from the complete vein census.</summary>
public sealed record CapturedToolWork(int ItemType, int Prefix, int Power, int UseTime, int DamagePerHit, int DamageRemaining);

/// <summary>Capture coverage is a fact in its own right.  A bounded native scan has observed a
/// prefix, never proved the rest of the rectangle empty.</summary>
public sealed record GatheringCoverageFact(string Domain, long Generation, long Examined, bool Complete, string Bounds);

/// <summary>Native gathering observation, before MineOre and ChopTree choose a retained target.
/// It carries the actual tool, policy, home-protection and approach verdict that existed during
/// capture.  It never borrows TerrainChanges.Revision as an entity generation: a reappearing
/// coordinate after an unobserved absence gets a new observer generation and explicit gap reason.</summary>
public sealed class CaptureGatheringOpportunities
{
    private sealed record Seen(int Material, long Generation);
    private readonly Dictionary<string, Seen> seen = new(StringComparer.Ordinal);
    private readonly Dictionary<FactKey, DecisionFact> factCache = new();
    private readonly Dictionary<FactKey, (GatheringOpportunityFact Value, string Text)> serialised = new();
    private HashSet<string> visibleLastCapture = new(StringComparer.Ordinal);
    private readonly HashSet<string> visibleThisCapture = new(StringComparer.Ordinal);
    /// <summary><paramref name="Tiles"/> and <paramref name="VeinComplete"/> are the flood this site came from,
    /// kept so a change of *geometry* — the body moved, a new flood is answering — can re-answer this one
    /// vein's approach instead of rescanning the whole window to find it again. Finding a vein is a sweep
    /// over the search box; re-approaching a known one is a handful of reach queries.</summary>
    private sealed record ObservedOre(string Identity, long Generation, Point Tile, int Material, double Remaining,
        string Admission, string Reason, string Detail, Vector2 Stand, bool Complete, CapturedToolWork? Work,
        Point[] Tiles, bool VeinComplete, bool ReplacementGap);
    private Rectangle? oreArea;
    private long oreOffset;
    /// <summary>How far through re-answering the known veins against a changed body or flood this capture
    /// has got. It is a cursor rather than a loop because the re-answer spends the decision's own allowance.</summary>
    private int oreReanswer;
    /// <summary>The round's site keys, taken once when it opens. Re-deriving them per iteration sorted the
    /// whole census for every site.</summary>
    private string[]? oreReanswerKeys;
    /// <summary>The offset every block up to which has already been charged. A block that is paid stays
    /// paid across captures, so reaching a vein with no allowance left costs nothing to retry.</summary>
    private long oreBlockPaidThrough;
    /// <summary>The swept rectangle unioned with every tile the census actually holds, which is what a
    /// terrain edit is tested against. A vein flood is not bounded by the window it was seeded in.</summary>
    private Rectangle oreHeld;
    private readonly Dictionary<string, ObservedOre> observedOres = new(StringComparer.Ordinal);
    private readonly HashSet<Point> oreVisited = new();
    private int terrainRevision;
    /// <summary>The pick and the player's tool reach the admissions were proved under. Reach belongs here beside the pick
    /// because every stand this census publishes is a cell within reach of its tile: a smaller reach strands the
    /// published stand out of range, and a larger one leaves ore it now reaches refused. Mining's private search
    /// keyed its own approach on reach and held that property until 23 September 2026, when it went and the census
    /// became the only discovery.</summary>
    private (int Type, int Prefix, int Power, (int X, int Y) Reach) pickSignature;
    private (WorkPolicy Policy, object List, int Revision, MimicGate Mimic) policySignature;
    /// <summary>
    /// What Mimic allows right now: whether the player's ore contact is live, and the ore he last hit.
    ///
    /// <para>The census is the only discovery, so it is the only place Mimic can be honoured. Until 23 September
    /// 2026 it admitted ore under Mimic exactly as under Opportunistic, and Mimic was enforced by mining's own
    /// private search declining to start — so the course bound the ore, the body flew to it, and the hand did
    /// nothing, which is the "flew to a pot and hovered" shape one domain over. The ore type is remembered here
    /// rather than read from the watcher, because the watcher's recent-hit answer lasts three quarters of a second
    /// while the contact that keeps Mimic live lasts <see cref="MineOre.MimicContactTicks"/>.</para>
    /// </summary>
    private readonly record struct MimicGate(bool Live, int OreType);
    private int lastPlayerOreType = -1;
    /// <summary>The gate of the capture in progress, read by <see cref="Admit"/> from the sweep and the re-answer alike.</summary>
    private MimicGate currentMimic = new(true, -1);
    private long minerAttempt = -1;
    /// <summary>
    /// The reach the ore admissions were proved under. An admission of "no approach" is a claim about the
    /// flood that answered it, and the sense says so itself: a refusal proved from one finished flood
    /// stays true only while that flood is the one answering, which is what <c>FloodGeneration</c> exists
    /// to key on. Without it, an ore read during a young flood keeps its refusal for as long as nothing
    /// else in the signature moves — and nothing else does, because the rest of the signature is the pick,
    /// the policy, the area, a terrain edit and the miner's last attempt, none of which a companion that
    /// is not mining ever changes. The tree capture beside this one has keyed on it since it was written.
    /// </summary>
    private (int Generation, bool Complete, Point Body) oreGeometry;
    private long nextGeneration;
    private long version;

    public IReadOnlyList<DecisionFact> Capture(in ActionContext context, DecisionWorkBudget budget)
    {
        var facts = new List<DecisionFact>();
        using (Infrastructure.Diagnostics.BrainSections.Enter(OresSection)) CaptureOres(context, budget, facts, visibleThisCapture);
        facts.Add(new(GatheringOpportunityBinder.ReadyKey("mine-target"), 0,
            new(Amount: context.Companion.Miner.CooldownTicks > 0 ? (double)Main.GameUpdateCount + context.Companion.Miner.CooldownTicks : 0), FactEvidence.Observed));
        // Gathering is two domains and this capture published one of them. CaptureTreeOpportunities
        // was driven by nothing but its own fixture, so a wired brain would have seen no chop-target
        // site and no chop-coverage at all — not an empty forest, an unanswered question, which under
        // the rule that optional work does not start on an unanswered search means never chopping.
        // The tree census owns its own slicing and cursor and shares the same borrowed allowance, so
        // it composes here rather than needing a second caller.
        using (Infrastructure.Diagnostics.BrainSections.Enter(TreesSection)) facts.AddRange(trees.Capture(context, budget));
        return facts.OrderBy(fact => fact.Key).ToArray();
    }

    // The two censuses this capture composes, timed apart: a vein flood and a trunk scan grow with different parts
    // of the world.
    private static readonly int OresSection = Infrastructure.Diagnostics.BrainSections.Register("ores");
    private static readonly int TreesSection = Infrastructure.Diagnostics.BrainSections.Register("trees");

    /// <summary>The trunk census this capture composes. It owns its own cursor and slicing and is
    /// reset with the rest, because a world reload must not leave it holding the old world's trunks.</summary>
    private readonly CaptureTreeOpportunities trees = new();

    public void ResetWorld()
    {
        seen.Clear(); factCache.Clear(); serialised.Clear(); visibleLastCapture.Clear(); visibleThisCapture.Clear(); observedOres.Clear(); oreVisited.Clear(); oreArea = null;
        oreOffset = 0; oreReanswer = 0; oreReanswerKeys = null; oreBlockPaidThrough = 0; oreHeld = default;
        nextGeneration = 0; version = 0; lastPlayerOreType = -1;
        trees.ResetWorld();
    }

    private void CaptureOres(in ActionContext context, DecisionWorkBudget budget, List<DecisionFact> facts, HashSet<string> visible)
    {
        Rectangle area = SearchArea(context.Senses.Intent.Region.Heading,
            PlayerIntegration.CompanionPreferences.Current.WorkCensusRadiusTiles);
        Item pick = TileMiner.PickaxeFor(context.Player);
        if (context.Senses.Player.MinedOre is (Point _, int hitType)) lastPlayerOreType = hitType;
        // Outside Mimic the gate is one constant value, so turning Mimic contact on and off while mining is
        // Opportunistic never reopens the sweep.
        MimicGate mimic = WorkPolicies.Mining == WorkPolicy.Mimic
            ? new(context.Senses.Player.MinedOre != null || Infrastructure.Observation.TileDamageWatcher.TicksSinceOreHit <= MineOre.MimicContactTicks,
                lastPlayerOreType)
            : new(true, -1);
        var policy = (WorkPolicies.Mining, WorkPolicies.MiningListVersion.List, WorkPolicies.MiningListVersion.Revision, mimic);
        currentMimic = mimic;
        // The edits that matter are the ones touching tiles this census *holds*, which is not the same
        // rectangle it swept. `OreFinder.Vein` floods a connected component with no spatial bound — only
        // a 400-tile cap — so a vein seeded a cell inside the window's edge can reach well outside it,
        // and the census then carries tiles no edit inside the rectangle would ever cover. Watching the
        // swept rectangle alone let such a tile be mined away unnoticed: nothing reopened, the re-answer
        // replayed the stale tile list, `EstimateRemaining` answered null for a tile that is no longer
        // ore, and the whole vein flipped to `native-remaining-unresolved` and stayed worthless until the
        // window moved. The full rescan this commit's predecessor did on every body move was hiding that
        // by re-censusing from live tiles constantly, so retiring the treadmill is what exposed it.
        //
        // `oreHeld` is therefore the swept rectangle unioned with every tile of every vein in the census,
        // which is exact rather than a margin: those tiles are precisely what `Admit` re-reads.
        bool spatialEdit = oreArea == area && TerrainChanges.Edits.ChangedSince(terrainRevision,
            (x, y) => x >= oreHeld.Left && x < oreHeld.Right && y >= oreHeld.Top && y < oreHeld.Bottom) != TerrainEditVerdict.Unchanged;
        bool changedInputs = pickSignature != (pick.type, pick.prefix, pick.pick, FindToolAccess.Reach) || policySignature != policy
            || minerAttempt != (context.Companion.Miner.LastOutcome?.Attempt ?? -1);
        long cells = (long)area.Width * area.Height;
        var geometry = (context.Senses.Reach.FloodGeneration, context.Senses.Reach.Complete,
            context.Npc.Center.ToTileCoordinates());
        // A moved body or a new flood invalidates every *admission* in a finished scan, because each was
        // proved against the flood and the body of the moment it was read — but it invalidates none of the
        // *discoveries*, because where the ore is has nothing to do with where the companion is. Those are
        // two questions and reopening the scan answered the cheap one by re-paying for the expensive one:
        // a sweep of the whole window to find veins it already knew about, on every tick the body moved,
        // which is most ticks. The re-answer below asks the reach question of each known vein instead —
        // a handful of queries against a window of sixteen thousand cells — and only a terrain edit inside
        // the window, a changed pick or list, or the window itself moving reopens the sweep.
        //
        // It is deliberately not done mid-scan: a change while the cursor is still running would erase it
        // every tick and the capture would never publish anything at all, and the binder revalidates each
        // site at its own boundary in any case.
        bool changedGeometry = oreOffset == cells && oreGeometry != geometry;
        // An edit nobody announced — another mod writing tiles, a direct world write — never reaches the
        // edit record, so the sweep would keep publishing a vein at a tile that is no longer that ore. The
        // hand refuses such a step by name, but a refusal does not reach the course, so the course re-bound
        // the same stale tile every tick: measured by the lane B review on 23 September 2026, 111 invalid
        // attempts in 115 ticks and no strike on the four good tiles beside it. The tile each usable vein is
        // bound through is re-read every capture instead — one tile per vein — and a mismatch is treated as
        // the edit it is. The private search mining used to run re-read live tiles every preparation, which
        // is what covered for this before the census became the only discovery.
        bool silentEdit = oreOffset == cells && observedOres.Values.Any(ore => ore.Admission == "usable"
            && !OreFinder.IsOreOfType(ore.Tile.X, ore.Tile.Y, ore.Material));
        if (oreOffset > 0 && (spatialEdit || changedInputs || silentEdit)) oreArea = null;
        else if (changedGeometry)
        {
            // Resumable, because the re-answer shares the decision's allowance like everything else, and
            // a round that a cut interrupts resumes where it stopped rather than restarting.
            //
            // **A round can therefore span several ticks, and the sites in it are proved against the body
            // as it was when each was read rather than against one pose.** That is worth stating plainly
            // because the obvious reading of a cursor is the opposite one. It is not a defect introduced
            // here: the full sweep this replaced had exactly the same property and more of it — sixteen
            // thousand cells cannot be read in one tick either, so a site censused early in a sweep was
            // always proved against an older body than one censused late. What the re-answer changes is
            // the size of the spread, from a whole sweep to a handful of sites. The admission terms that
            // move with the body — the reach verdict and the allowance test — both change slowly against
            // a body travelling at a few pixels a tick, and the binder revalidates each site at its own
            // native boundary regardless, which is where an admission has to be right.
            //
            // The keys are snapshotted once per round rather than re-sorted per iteration, because
            // `Keys.OrderBy(...).ElementAt(n)` inside the loop is a fresh sort and a fresh enumeration
            // for every site — quadratic in the census for no gain, and charged one unit whatever it did.
            oreReanswerKeys ??= observedOres.Keys.OrderBy(identity => identity, StringComparer.Ordinal).ToArray();
            while (oreReanswer < oreReanswerKeys.Length && budget.TrySpend("gathering-native-reanswer"))
            {
                string key = oreReanswerKeys[oreReanswer];
                // A site the sweep has since dropped is simply skipped; the round is about what is held.
                if (!observedOres.TryGetValue(key, out ObservedOre? stale)) { oreReanswer++; continue; }
                observedOres[key] = Admit(context, pick, currentMimic, stale.Identity, stale.Material, stale.Generation,
                    stale.Tiles, stale.VeinComplete, stale.ReplacementGap);
                oreReanswer++;
            }
            if (oreReanswer >= oreReanswerKeys.Length) { oreGeometry = geometry; oreReanswer = 0; oreReanswerKeys = null; }
        }
        if (oreArea != area)
        {
            oreArea = area; oreOffset = 0; oreReanswer = 0; oreReanswerKeys = null; oreBlockPaidThrough = 0;
            oreHeld = area;
            observedOres.Clear(); oreVisited.Clear(); visibleThisCapture.Clear();
            terrainRevision = TerrainChanges.Revision;
            pickSignature = (pick.type, pick.prefix, pick.pick, FindToolAccess.Reach);
            policySignature = policy;
            minerAttempt = context.Companion.Miner.LastOutcome?.Attempt ?? -1;
            oreGeometry = geometry;
        }
        while (oreOffset < cells)
        {
            // One budget unit buys a block of empty cells or one vein, because those are the two kinds of
            // work this loop does and they differ by four orders of magnitude: a cell that is not ore is a
            // tile-type read, while a cell that is pays for a flood census of its vein and a reach query per
            // tile. Charged per cell, the sweep's accounted cost was its *area* rather than its work, so
            // widening the window to the admission radius (8,281 cells to 16,129) doubled the charge and the
            // census stopped finishing inside a decision: measured through `--crowd-cost`, decide's median
            // went 4.26 ms to 11.3 ms against its own 12 ms allowance, which is a brain spending every tick
            // of its thinking on a tile scan. Per block it is 64 units for the whole window.
            // The block is charged once and stays paid, which is the difference between a cursor that
            // resumes and one that cannot. Charging on `offset % Block == 0` looked equivalent and was
            // not: a vein sitting exactly on a block boundary spent the block unit, failed the vein
            // spend, rewound the offset onto the multiple and charged the same block again next capture
            // — one unit for zero cells of progress, every time. With an allowance of exactly one
            // operation, which `VerifyTreeOpportunityCapture` drives the census with deliberately, that
            // is not a slow path but a livelock: the block takes the only unit, the site spend can never
            // succeed, and the offset never moves. It passes today only because no site in that scene
            // happens to land on a multiple of 256.
            if (oreOffset >= oreBlockPaidThrough)
            {
                if (!budget.TrySpend("gathering-native-capture-block")) break;
                oreBlockPaidThrough = oreOffset + ScanBlockCells;
            }
            int x = area.Left + (int)(oreOffset % area.Width);
            int y = area.Top + (int)(oreOffset / area.Width);
            oreOffset++;
            Point seed = new(x, y);
            // The sweep is a square and the allowance is a disc, so the square's corners hold cells the
            // admission rule refuses by construction — about a quarter of the window once it was widened
            // to bound the disc. Publishing them cost a site unit at discovery, a re-answer unit on every
            // geometry round and a serialisation per tick, and worse, it inflated the census denominator
            // that scales every site's worth, because the census sum is over what was observed rather
            // than over what was admitted. Skipping them is free rather than a saving traded for
            // accuracy: nothing outside the disc can ever be admitted, and the skip is bounded by the
            // block that has already been paid for, so it cannot spin.
            if (!WithinAllowanceOfHeading(context, seed)) continue;
            if (!OreFinder.IsOre(x, y) || oreVisited.Contains(seed)) continue;
            if (!budget.TrySpend("gathering-native-capture-vein")) { oreOffset--; break; }
            int material = Main.tile[x, y].TileType;
            OreFinder.VeinCensus vein = OreFinder.CensusVein(seed, material);
            foreach (Point tile in vein.Tiles) oreVisited.Add(tile);
            Point[] tiles = vein.Tiles.OrderBy(tile => tile.X).ThenBy(tile => tile.Y).ToArray();
            // Every tile this vein puts in the census widens what an edit is tested against, because a
            // flood is not bounded by the window that seeded it.
            foreach (Point tile in tiles) oreHeld = Rectangle.Union(oreHeld, new Rectangle(tile.X, tile.Y, 1, 1));
            string identity = $"ore:{material}:{tiles[0].X},{tiles[0].Y}";
            long generation = Generation(identity, material, visible, out bool replacementGap);
            observedOres[identity] = Admit(context, pick, currentMimic, identity, material, generation, tiles, vein.Complete, replacementGap);
        }
        bool complete = oreOffset == cells;
        // A partial scan proves no denominator.  It publishes only coverage, so it cannot rescale
        // already compared work or manufacture an absence for cells it has not reached.
        if (complete)
        {
            visibleLastCapture = new HashSet<string>(visibleThisCapture, StringComparer.Ordinal);
            foreach (ObservedOre ore in observedOres.Values.OrderBy(ore => ore.Identity))
            {
                double census = observedOres.Values.Where(other => other.Material == ore.Material).Sum(other => other.Remaining);
                var value = new GatheringOpportunityFact("mine-target", ore.Identity, ore.Generation, ore.Tile.X, ore.Tile.Y, ore.Material, OpportunityPurposes.Mine,
                    ore.Remaining, census, ore.Admission, ore.Reason, ore.Detail, ore.Stand.X, ore.Stand.Y, ore.Work);
                facts.Add(Fact(value, ore.Complete ? FactEvidence.Observed : FactEvidence.Unresolved));
            }
        }
        facts.Add(Coverage("mine-coverage", oreOffset, complete, area));
    }

    /// <summary>Starts a new native census after an observed tile change while retaining the
    /// generation ledger; the next completed scan can distinguish a continuously observed purpose
    /// from one that disappeared between observations.</summary>
    public void Invalidate() => oreArea = null;

    /// <summary>
    /// What one known vein is worth right now: its best approach, its admission and its remaining native
    /// work, against the body and the flood of this moment. It is one function because it is asked from two
    /// places — the sweep that discovers a vein, and the re-answer that a moved body forces on a vein
    /// already discovered — and two copies of an admission ladder is how the two answers drift into
    /// disagreeing about the same ore.
    /// </summary>
    private static ObservedOre Admit(in ActionContext context, Item pick, MimicGate mimic, string identity, int material,
        long generation, Point[] tiles, bool veinComplete, bool replacementGap)
    {
        var miner = context.Companion.Miner;
        // The first tile with a proven approach, else the first whose approach is not yet known, else none.
        // Tracked by index and flag, never by a default tuple: `Reach.Yes` is the enum's zero, so a default
        // read as "nothing found" *reads as Yes*, and until 23 September 2026 a vein with no proven approach
        // skipped the Unknown fallback and was published as `approach-unreachable` — a proven refusal —
        // whenever its tiles were merely not yet reached by the flood. The re-answer on a new flood hid it by
        // correcting the fact once the flood finished; mining's own private search, which answered Unknown
        // correctly, hid it from every whole-brain scene until that search was deleted.
        //
        // The loop stops at the first Yes, because nothing after it can be chosen and `Approach` is a pure
        // read of the reach sense. It used to rank every tile of the vein, up to four hundred, on every
        // re-answer — and a re-answer runs whenever the body moves a tile.
        int chosen = -1;
        Reachability.Reach reach = Reachability.Reach.No;
        Vector2 stand = default;
        for (int i = 0; i < tiles.Length; i++)
        {
            Reachability.Reach approach = FindToolAccess.Approach(tiles[i], context.Npc.Center, context.Senses.Reach, out Vector2 pose);
            if (approach == Reachability.Reach.Yes) { chosen = i; reach = approach; stand = pose; break; }
            if (approach == Reachability.Reach.Unknown && reach != Reachability.Reach.Unknown) { chosen = i; reach = approach; stand = pose; }
        }
        Point target = chosen < 0 ? tiles[0] : tiles[chosen];
        bool listed = WorkPolicies.MinesOre(material);
        bool policyEnabled = WorkPolicies.Mining != WorkPolicy.Disabled;
        // Mimic helps with the ore the player is mining, while he is mining it: his contact is live and the
        // vein is his ore type. Opportunistic passes a gate that admits everything.
        bool mimicked = mimic.Live && (mimic.OreType < 0 && WorkPolicies.Mining != WorkPolicy.Mimic || mimic.OreType == material);
        bool allowed = InNewActivityAllowance(context, target) && !ProtectCompanionHomes.IsProtected(target);
        bool mineable = miner.CanMine(target, pick.pick);
        string admission = !policyEnabled || !listed || !mimicked || !allowed || !mineable ? "unusable"
            : reach == Reachability.Reach.Unknown ? "unknown" : reach == Reachability.Reach.Yes ? "usable" : "unusable";
        string reason = !policyEnabled ? "mining-disabled" : !listed ? "mining-list"
            : !mimicked ? (mimic.Live && mimic.OreType >= 0 ? "mimic-other-ore-type" : "mimic-awaiting-player-ore-contact")
            : !allowed ? "outside-allowance-or-protected"
            : !mineable ? "pickaxe-cannot-damage" : reach == Reachability.Reach.Unknown ? "approach-not-yet"
            : reach == Reachability.Reach.Yes ? "observed-native-ore" : "approach-unreachable";
        if (replacementGap) reason += ";replacement-observation-gap";
        // A tile with no remaining estimate is a proven fact, not an unanswered question, and reading it as
        // the second is what made every ore in the 22 September 2026 capture read
        // `native-remaining-unresolved` for all 2,340 ticks — ten to thirteen veins discovered, none ever
        // usable and none ever refused. `TileMiner.EstimateRemaining` answers null on five conditions and
        // every one of them is observed: outside the world, no longer ore, inside a protected home, a tile
        // the game refuses to kill, or a pickaxe with no power against it. None of those is a bounded search
        // running out, which is the only thing `unknown` is for in this tree — and because optional work does
        // not start on an unanswered search, calling a proven refusal unknown leaves the vein permanently
        // unstartable *and* permanently unrefusable, so the census can never resolve it by looking again.
        //
        // The old shape made that worse in two further ways, and both are why this is a rewrite of the block
        // rather than a renamed string. It required *every* tile of the vein to answer before any of them
        // counted, so one dead cell in a four-hundred-tile flood zeroed the whole vein's remaining work; and
        // the override ran after the ladder above had already computed the exact refusal, discarding
        // `pickaxe-cannot-damage` and `outside-allowance-or-protected` in favour of a string naming neither
        // the tile nor the condition. What the record needs from a refusal is which of the five it was.
        RemainingToolWork?[] estimates = tiles.Select(tile => miner.EstimateRemaining(tile, pick)).ToArray();
        int answered = estimates.Count(estimate => estimate != null);
        double remaining = estimates.Where(estimate => estimate != null).Sum(estimate => estimate!.Value.DamageRemaining);
        RemainingToolWork? targetWork = miner.EstimateRemaining(target, pick);
        // The target is what the binding will actually swing at, so it alone decides the admission; the
        // other tiles decide only how much work the vein is still worth.
        bool targetSettled = true;
        if (targetWork is null)
        {
            string why = WhyNoRemainingWork(miner, pick, target);
            targetSettled = why != UnexplainedRemaining;
            admission = targetSettled ? "unusable" : "unknown";
            reason = why;
        }
        CapturedToolWork? work = targetWork is { } next
            ? new(pick.type, pick.prefix, pick.pick, pick.useTime, next.DamagePerHit, next.DamageRemaining) : null;
        return new(identity, generation, target, material, remaining, admission, reason,
            $"vein-complete={veinComplete};tiles-with-work={answered}/{tiles.Length};pick={pick.pick};listed={listed};policy={WorkPolicies.Mining};reach={reach}",
            stand, veinComplete && targetSettled, work, tiles, veinComplete, replacementGap);
    }

    /// <summary>The one refusal that is genuinely an open question rather than an observed fact.</summary>
    private const string UnexplainedRemaining = "native-remaining-unresolved";

    /// <summary>
    /// Which of <see cref="TileMiner.EstimateRemaining"/>'s five refusals a tile met, asked in that
    /// method's own order so the answer is the condition it actually returned on rather than the first one
    /// that happens to hold. The conditions are re-read here rather than returned from the miner because
    /// each is a public predicate this file already consults for the admission above, and a second return
    /// channel through `Interactions/` would put the same ladder in two places for one caller.
    ///
    /// <para>The last arm is deliberately a live possibility rather than an unreachable default: a pickaxe
    /// with a zero <c>useTime</c> refuses with no condition above it holding, and a fact that claimed a
    /// proven refusal there would be asserting something nobody established. That is the one case that stays
    /// <c>unknown</c>, which is what <c>unknown</c> is for.</para>
    ///
    /// <para><b>Only one of the five names is pinned by a fixture, and the reason is a property of where this
    /// is called rather than a gap somebody can close with another scene.</b> A sentinel collapsed the whole
    /// ladder to <c>=> "pickaxe-cannot-damage"</c> and both vein rows stayed green, which is true and reads
    /// worse than it is. The target this is asked about came out of the census, and the census publishes a
    /// site only for a tile that is inside the world and is ore — so on a *fresh* capture
    /// <c>tile-outside-the-world</c> and <c>tile-is-no-longer-ore</c> cannot be produced at all, and
    /// <c>the-game-refuses-to-kill-this-tile</c> needs an ore type the game refuses to kill, which vanilla's
    /// ore set does not contain. All three are reachable only on the re-answer path, where a frozen tile list
    /// outlives the tiles in it. And <c>outside-allowance-or-protected</c> is reachable but not
    /// *distinguishable*: the admission ladder above reaches the identical string first through
    /// <c>allowed</c>, so no observation can tell that arm from this one.</para>
    ///
    /// <para>So what a scene can pin today is the pickaxe arm, which the meteorite row does. The value of the
    /// other four is in the record rather than in the branch — a re-answer that outlives its tiles says which
    /// tile and why instead of saying nothing — and the row that would pin them is a re-answer scene, which
    /// is where the stale-tile history in this folder's guide already lives.</para>
    /// </summary>
    private static string WhyNoRemainingWork(TileMiner miner, Item pick, Point tile)
        => !Terraria.WorldGen.InWorld(tile.X, tile.Y, 5) ? "tile-outside-the-world"
            : !OreFinder.IsOre(tile.X, tile.Y) ? "tile-is-no-longer-ore"
            : ProtectCompanionHomes.IsProtected(tile) ? "outside-allowance-or-protected"
            // Split from the pickaxe arm on purpose: a tile the game itself refuses to kill is not a tool
            // problem, and handing a player a stronger pickaxe would not change it.
            : !Terraria.WorldGen.CanKillTile(tile.X, tile.Y) ? "the-game-refuses-to-kill-this-tile"
            : !miner.CanMine(tile, pick.pick) ? "pickaxe-cannot-damage"
            : UnexplainedRemaining;

    /// <summary>How many cells of the sweep one budget unit buys. It is a block rather than a cell because a
    /// cell that holds no ore costs a tile-type read; see the charge at the sweep for what charging per cell
    /// did to the decision allowance. The tree census beside this one uses the same constant.</summary>
    internal const int ScanBlockCells = 256;

    private static Rectangle SearchArea(Vector2 centre, int radius) => new((int)(centre.X / 16f) - radius, (int)(centre.Y / 16f) - radius,
        2 * radius + 1, 2 * radius + 1);
    private static bool InNewActivityAllowance(in ActionContext context, Point point)
    {
        float radius = PlayerIntegration.CompanionPreferences.Current.NewActivityRadius;
        return WithinAllowanceOfHeading(context, point)
            && Vector2.DistanceSquared(context.Npc.Bottom, context.Senses.Intent.Region.Heading) <= radius * radius;
    }

    /// <summary>
    /// The half of the allowance rule that is about the *site* rather than about the companion, which is
    /// the half a sweep may skip on. The other clause asks whether the companion itself is near enough
    /// to take new work, and a sweep must not read it: a body that has wandered out of range would then
    /// discover nothing at all, and the census would report a finished search over an empty world rather
    /// than a full one whose sites are currently refused. Discovery answers where the work is; admission
    /// answers whether it may be taken, and only the second is allowed to depend on where the body is.
    /// </summary>
    private static bool WithinAllowanceOfHeading(in ActionContext context, Point point)
    {
        float radius = PlayerIntegration.CompanionPreferences.Current.NewActivityRadius;
        return Vector2.DistanceSquared(point.ToWorldCoordinates(), context.Senses.Intent.Region.Heading) <= radius * radius;
    }
    private long Generation(string identity, int material, HashSet<string> visible, out bool replacementGap)
    {
        replacementGap = false;
        if (seen.TryGetValue(identity, out Seen? prior) && prior.Material == material && visibleLastCapture.Contains(identity))
        { visible.Add(identity); return prior.Generation; }
        replacementGap = seen.ContainsKey(identity) && !visibleLastCapture.Contains(identity);
        long generation = ++nextGeneration;
        seen[identity] = new(material, generation); visible.Add(identity);
        return generation;
    }
    private DecisionFact Fact(GatheringOpportunityFact value, FactEvidence evidence = FactEvidence.Observed)
    {
        FactKey key = new(value.Domain, value.Target, value.Generation);
        // An equal record serialises to the same text, so a site whose value held still reuses its JSON.
        string text = serialised.TryGetValue(key, out var last) && last.Value == value ? last.Text : JsonSerializer.Serialize(value);
        serialised[key] = (value, text);
        FactValue factValue = new(value.RemainingAmount, value.TileX * 16 + 8, value.TileY * 16 + 8, text);
        if (factCache.TryGetValue(key, out DecisionFact? prior) && prior.Value == factValue && prior.Evidence == evidence) return prior;
        return factCache[key] = new DecisionFact(key, ++version, factValue, evidence);
    }
    private DecisionFact Coverage(string domain, long examined, bool complete, Rectangle area)
    {
        var value = new GatheringCoverageFact(domain, 0, examined, complete, $"{area.Left},{area.Top}:{area.Width}x{area.Height}");
        FactKey key = new(domain, "native-census");
        FactValue factValue = new(examined, Text: JsonSerializer.Serialize(value));
        FactEvidence evidence = complete ? FactEvidence.Observed : FactEvidence.Unresolved;
        if (factCache.TryGetValue(key, out DecisionFact? prior) && prior.Value == factValue && prior.Evidence == evidence) return prior;
        return factCache[key] = new DecisionFact(key, ++version, factValue, evidence);
    }
}

/// <summary>Pure gathering census reader.  It cannot see Terraria: every candidate, verdict and
/// denominator comes from a serialized DecisionFact captured before activities select a winner.</summary>
public sealed class GatheringOpportunitySource : IOpportunitySource
{
    private sealed class SnapshotCursor { public long Id = long.MinValue; public FactKey[] Prefix = Array.Empty<FactKey>(); }
    private readonly string domain;
    private readonly ConditionalWeakTable<DecisionWorkCursor, SnapshotCursor> snapshots = new();
    public GatheringOpportunitySource(string domain)
    {
        if (domain is not "mine-target" and not "chop-target") throw new ArgumentOutOfRangeException(nameof(domain));
        this.domain = domain;
    }
    public string Name => domain;

    public OpportunitySlice Continue(DecisionFactSnapshot facts, DecisionWorkCursor cursor, DecisionWorkBudget budget)
    {
        SnapshotCursor state = snapshots.GetValue(cursor, static _ => new SnapshotCursor());
        if (state.Id != facts.Id)
        {
            // Snapshots advance every brain tick.  A new envelope with the same consumed immutable
            // prefix must not turn a one-operation source into perpetual first-candidate churn.
            DecisionFact[] current = facts.Facts.Where(fact => fact.Key.Kind == domain).OrderBy(fact => fact.Key).ToArray();
            bool retainsPrefix = cursor.Offset <= current.Length && state.Prefix.Length == cursor.Offset
                && state.Prefix.SequenceEqual(current.Take((int)cursor.Offset).Select(fact => fact.Key));
            if (!retainsPrefix) cursor.Rescan();
            state.Prefix = current.Take((int)cursor.Offset).Select(fact => fact.Key).ToArray();
            state.Id = facts.Id;
        }
        DecisionFact[] sites = facts.Facts.Where(fact => fact.Key.Kind == domain).OrderBy(fact => fact.Key).ToArray();
        string coverageKind = domain == "mine-target" ? "mine-coverage" : "chop-coverage";
        DecisionFact coverageFact = facts.Track().Read(new FactKey(coverageKind, "native-census"));
        GatheringCoverageFact? coverage = string.IsNullOrEmpty(coverageFact.Value.Text) ? null
            : JsonSerializer.Deserialize<GatheringCoverageFact>(coverageFact.Value.Text);
        bool captureComplete = coverage?.Complete == true && coverageFact.Evidence == FactEvidence.Observed;
        var examined = new List<Opportunity>();
        while (cursor.Offset < sites.Length && budget.TrySpend(Name))
        {
            DecisionFact raw = sites[(int)cursor.Offset]; cursor.Advance();
            TrackedFactReader reader = facts.Track();
            DecisionFact observed = reader.Read(raw.Key);
            reader.Read(new FactKey(coverageKind, "native-census"));
            GatheringOpportunityFact? site = JsonSerializer.Deserialize<GatheringOpportunityFact>(observed.Value.Text);
            if (site == null || site.Domain != domain || site.Target != raw.Key.Identity || site.Generation != raw.Key.Generation)
                throw new InvalidOperationException("Captured gathering fact did not retain its stable identity.");
            OpportunityAdmission admission = site.Admission switch
            {
                "usable" => OpportunityAdmission.KnownUsable, "unusable" => OpportunityAdmission.KnownUnusable,
                "unknown" => OpportunityAdmission.Unresolved, _ => throw new InvalidOperationException("Unknown gathering admission: " + site.Admission)
            };
            if (observed.Evidence == FactEvidence.Unresolved || !captureComplete)
                admission = OpportunityAdmission.Unresolved;
            var key = new OpportunityKey(domain, site.Purpose, site.Target, site.Generation);
            double amount = Math.Max(0, site.Work?.DamageRemaining ?? site.RemainingAmount);
            double census = Math.Max(1, site.CensusAmount);
            CoursePoint workingPose = site.StandX != 0 || site.StandY != 0 ? new(site.StandX, site.StandY) : new(site.TileX * 16 + 8, site.TileY * 16 + 8);
            // A site with no captured tool work offers no need, whatever the refusal was called. This read the
            // reason string until 22 September 2026, which made it a second place the admission ladder's
            // vocabulary had to be kept in step: the day that string became five specific refusals, a site the
            // pickaxe provably cannot damage would have started publishing a need worth zero rather than no
            // need at all. `Work` is exactly the quantity the string was standing in for — it is null on
            // precisely the sites whose target has no estimate — so the test is on the fact rather than on
            // what somebody named it.
            IEnumerable<UsefulNeed> needs = site.Work == null
                ? Array.Empty<UsefulNeed>()
                : new[] { new UsefulNeed(GatheringOpportunityBinder.Need(site), amount, census,
                    admission == OpportunityAdmission.KnownUsable ? 1 : 0) };
            examined.Add(new Opportunity(key, observed.Version, workingPose, admission, site.Reason,
                needs, new[] { site.Purpose }, reader.Manifest(), raw.Key));
        }
        if (captureComplete && cursor.Offset == sites.Length) cursor.Complete();
        state.Prefix = sites.Take((int)cursor.Offset).Select(fact => fact.Key).ToArray();
        return new(examined, new(Name, facts.WorldEpoch, cursor.Offset, sites.Length, cursor.Exhausted && captureComplete,
            budget.Cut && cursor.Offset < sites.Length, "snapshot-facts;capture-complete=" + captureComplete + ";cursor=" + cursor.Offset));
    }
}
