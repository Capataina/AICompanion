extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Activities.Combat;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;

/// <summary>
/// The census and the binder answer one question about one target, so they must answer it the same way
/// inside one frozen observation.
///
/// This is the scene the owner's play of 0.38.13 ended in and the state it never left: a player standing
/// still, hostiles in reach, drops on the floor, and every decision reading
/// <c>combat=usable:3, collect-target=usable:4</c> and then refusing all twenty-eight orders built from
/// them with <c>target-capture-missing</c> and <c>assistance-target-unresolved</c>. Both of those strings
/// are one test — the target fact's evidence is not <c>Observed</c> — so seven usable opportunities and
/// twenty-eight refusals is not a preference between them. It is a census and a binder reading two
/// different worlds, and because the only order left to price is the empty one, and an empty order is
/// companionship, it reads in play as a companion that has stopped doing anything.
///
/// The scene is dynamic where a static one proves nothing: bodies leave the world while others stay,
/// which is what the store outliving an observation needs in order to be wrong.
/// </summary>
internal static class VerifyAdmittedOpportunitiesBind
{
    public static int Run()
    {
        int red = 0;
        red += Row("an admitted opportunity's own evidence is observed in the snapshot that admitted it", AdmittedEvidenceIsObserved);
        red += Row("the tail scene binds a collect step and weighs the fight rather than refusing it", TheTailSceneStillBinds);
        return red;
    }

    /// <summary>Every row leaves the world it built empty again. `ResetProcessState.BeforeCase` rebuilds
    /// the tile map between cases and puts the process statics back, but it does not deactivate a slot in
    /// `Main.item` or `Main.npc` — so a scene that seeds drops and hostiles hands them to whatever runs
    /// next, and a fixture whose subject is a companion following an empty floor then finds work to do
    /// instead and never opens the journey it came to measure.</summary>
    private static int Row(string name, Action test)
    {
        try { return RunOneRow.GreenOrRed(name, test); }
        finally { ClearTheScene(); }
    }

    internal static void ClearTheScene()
    {
        for (int slot = 10; slot <= 12; slot++) { Main.item[slot] = new Item(); Main.item[slot].active = false; }
        for (int slot = 30; slot <= 36; slot++) { Main.npc[slot].active = false; Main.npc[slot].life = 0; }
    }

    /// <summary>
    /// The property, checked on every decision of the run rather than at the end: a candidate the search
    /// is allowed to order carries an admission this observation still supports.
    ///
    /// Asserting it per decision rather than on a refusal tally is deliberate. A refusal count says the
    /// search threw orders away and nothing about why; this says which candidate, which key and what the
    /// observation actually holds for it, which is the difference between a number nobody can act on and
    /// a named first wrong node.
    /// </summary>
    private static void AdmittedEvidenceIsObserved()
    {
        ActionContext ctx = TailScene();
        DecideCourseEachTick owner = ctx.Companion.Brain.Course;
        int checkedDecisions = 0;

        foreach (int tick in Timeline(ctx))
        {
            DecisionFactSnapshot? facts = owner.Facts;
            if (facts == null) continue;
            checkedDecisions++;
            foreach (Opportunity candidate in owner.Candidates)
            {
                if (candidate.Admission != OpportunityAdmission.KnownUsable) continue;
                if (string.IsNullOrEmpty(candidate.AdmissionEvidence.Kind)) continue;
                bool present = facts.TryRead(candidate.AdmissionEvidence, out DecisionFact evidence);
                Require(present && evidence.Evidence == FactEvidence.Observed,
                    $"t{tick}: the census serves {candidate.Key} as {candidate.Admission}:{candidate.Reason} while the "
                    + $"binder's own key {Describe(candidate.AdmissionEvidence)} reads "
                    + $"{(present ? "evidence=" + evidence.Evidence : "ABSENT")} in snapshot {facts.Id} — "
                    + $"the refusal the play saw is {Refusals(owner)}, and the funnel still reports {Admitted(owner)}");
            }
        }
        Require(checkedDecisions > 400, $"the scene must actually run; decisions checked={checkedDecisions}");
        Console.WriteLine($"  admitted evidence: {checkedDecisions} decisions checked, final admission {Admitted(owner)}, "
            + $"refusals {(Refusals(owner).Length == 0 ? "none" : Refusals(owner))}");
    }

    /// <summary>
    /// The other half, and the half a retirement sweep alone would not give: the scene still produces
    /// work, and the fight is still weighed. A store that answered "nothing is usable" on every tick
    /// would satisfy the row above and be the same silence wearing an honest reason.
    ///
    /// So this asks for two outcomes rather than one. A drop on the floor becomes a bound collect step,
    /// which is a course actually published. And combat, once the drops are gone, reaches *pricing* —
    /// a real order beside the empty one, with nothing refused for a capture the census claimed to have.
    /// Pricing rather than winning is the honest bar: whether a fight beats standing still is the
    /// objective's answer and `AIC-439`'s open question, and this scene's five zombies round a standing
    /// player is not the place to settle it. What the seam owes is that the comparison happens.
    /// </summary>
    private static void TheTailSceneStillBinds()
    {
        ActionContext ctx = TailScene();
        DecideCourseEachTick owner = ctx.Companion.Brain.Course;
        var bound = new HashSet<string>(StringComparer.Ordinal);
        var captureRefusals = new List<string>();
        long combatOnlyPriced = 0;
        foreach (int tick in Timeline(ctx))
        {
            foreach (StepBinding step in owner.Course.Current?.Projection.Steps ?? Array.Empty<StepBinding>())
                bound.Add(step.Opportunity.Purpose);
            foreach (var refusal in owner.LastRefusals)
                if (refusal.Key is "target-capture-missing" or "assistance-target-unresolved")
                    captureRefusals.Add($"t{tick} {refusal.Key}={refusal.Value} while admitted {Admitted(owner)}");
            // After the drops go the only candidates left are combat's, so this is combat's own number.
            if (tick > 410) combatOnlyPriced = Math.Max(combatOnlyPriced, owner.LastSearch.Evaluated);
        }
        Require(bound.Contains("collect"),
            $"three drops on the floor and no collect step was ever bound; purposes bound={string.Join(",", bound.OrderBy(p => p))}; "
            + $"admission {Admitted(owner)}; refusals {Refusals(owner)}");
        Require(captureRefusals.Count == 0,
            $"{captureRefusals.Count} decisions refused an order for a capture the census said it had; first three: "
            + string.Join(" | ", captureRefusals.Take(3)));
        Require(combatOnlyPriced > 1,
            $"with the drops gone and {Admitted(owner)}, the search priced {combatOnlyPriced} order(s) — only the empty one — "
            + $"so no fight was ever weighed; refusals {Refusals(owner)}; body={ctx.Npc.Center.X:0},{ctx.Npc.Center.Y:0}; "
            + $"uses {CombatUses(owner)}");
        Console.WriteLine($"  tail scene: bound {string.Join(", ", bound.OrderBy(p => p))}; "
            + $"combat orders priced with nothing else on the board {combatOnlyPriced}; no capture refusals");
    }

    /// <summary>
    /// The run both rows share: five hundred ticks over which two hostiles die with three still present,
    /// a drop is taken off the floor, and two fresh hostiles arrive. The deaths are the point — the
    /// capture's onset is the tick a committed target died with others still there — and they are placed
    /// rather than fought for, because enemy AI does not run headless.
    /// </summary>
    private static IEnumerable<int> Timeline(ActionContext ctx)
    {
        for (int tick = 0; tick < 500; tick++)
        {
            if (tick == 150) { Kill(31); Kill(34); Main.item[10].active = false; }
            if (tick == 250) { Spawn(35, new Vector2(44 * 16, 60 * 16)); Spawn(36, new Vector2(56 * 16, 60 * 16)); }
            // The last two drops leave at 400, so the only work left is the fight: without that, collection
            // wins every comparison in this scene and the firing half of the row below is never reached.
            if (tick == 400) { Main.item[11].active = false; Main.item[12].active = false; }
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            ctx.Companion.Brain.Tick(ctx.Companion, ctx.Player);
            yield return tick;
        }
    }

    private static string Admitted(DecideCourseEachTick owner)
        => string.Join(" ", owner.Admitted.Select(a =>
            $"{a.Domain}={a.Usable}ok/{a.Unresolved}?/{a.Unusable}x{(a.Reason.Length == 0 ? "" : ":" + a.Reason)}"));

    private static string Refusals(DecideCourseEachTick owner)
        => string.Join(" ", owner.LastRefusals.OrderByDescending(r => r.Value).Select(r => $"{r.Key}={r.Value}"));

    private static string Describe(FactKey key) => $"{key.Kind}/{key.Identity}#{key.Generation}";

    /// <summary>The priced shots the observation is carrying, because "no use with captured travel and
    /// target impact" is a refusal whose cause is entirely in the uses and entirely invisible without
    /// them: a use with no impact tick was never solved, and a use whose stand is nowhere near the body
    /// belongs to a search that stopped being refreshed.</summary>
    private static string CombatUses(DecideCourseEachTick owner)
    {
        DecisionFactSnapshot? facts = owner.Facts;
        if (facts == null) return "none";
        return string.Join(" ", facts.Facts.Where(f => f.Key.Kind == "combat-use")
            .Select(f => System.Text.Json.JsonSerializer.Deserialize<CombatCourseFacts.Use>(f.Value.Text ?? "null"))
            .Where(use => use != null)
            .Select(use => $"[npc{use!.TargetSlot}.{use.TargetGeneration} stand={use.StandX:0},{use.StandY:0} "
                + $"dmg={use.ExpectedTargetDamage:0.0} impact={use.TargetImpactTicks} fire={use.FireTick}]"));
    }

    /// <summary>A player standing still with five hostiles inside his region and three drops on the
    /// floor: the last twenty-five seconds of capture <c>2026-09-22_10-05-56-125</c>, reduced to what
    /// the brain reads.</summary>
    private static ActionContext TailScene()
    {
        ActionContext ctx = VerifyCollectionContracts.SetUpFloor();
        ctx.Player.Bottom = new Vector2(50 * 16, 60 * 16);
        ctx.Player.velocity = Vector2.Zero;
        ctx.Npc.Bottom = new Vector2(50 * 16, 59 * 16);
        ctx.Player.GetModPlayer<CompanionPlayer>().Gear.Slots[0].SetDefaults(ItemID.WoodenBow);
        ctx.Player.GetModPlayer<CompanionPlayer>().Gear.Slots[1] = new Item();
        for (int i = 0; i < 5; i++) Spawn(30 + i, new Vector2((46 + i * 2) * 16, 60 * 16));
        for (int i = 0; i < 3; i++)
        {
            Item drop = VerifyCollectionContracts.Drop(ItemID.CopperOre, 5, new Vector2((52 + i) * 16 + 8, 60 * 16), slot: 10 + i);
            drop.playerIndexTheItemIsReservedFor = Main.myPlayer;
        }
        ctx.Senses.Update(ctx.Npc, ctx.Player);
        return ctx;
    }

    /// <summary>A hostile leaves the world the way the game leaves one: life gone, slot inactive.</summary>
    private static void Kill(int slot)
    {
        Main.npc[slot].life = 0;
        Main.npc[slot].active = false;
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
