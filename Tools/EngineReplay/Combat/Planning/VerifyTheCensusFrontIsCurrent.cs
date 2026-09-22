extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Activities.Combat;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;
using WorkPolicies = live::AICompanion.Companion.Brain.Activities.WorkPolicies;

/// <summary>
/// Combat is the one domain whose opportunities are a tactical search rather than a world scan, so the
/// front it publishes is the whole of what the course can ever know about a fight. This is whether that
/// front describes the hostiles that are there now.
///
/// In the play of 0.38.13 it did not. From tick 1,816 combat was never the activity, so every plan it
/// re-priced was a *prepared* plan — an offer nobody took — and the `CheckPrepared` short-circuit
/// returns before the line that refreshes what the census publishes. The funnel read `offered plan=237`
/// unchanged for five hundred ticks while `combat-plan` events, which are commitments, stopped at 1,819;
/// the census reported `combat=usable:3` frozen through one death and five spawns; and not one of those
/// five new hostiles was ever minted a use, so the course could not weigh a single one of them.
/// </summary>
internal static class VerifyTheCensusFrontIsCurrent
{
    public static int Run()
    {
        int red = 0;
        red += Row("a hostile that arrives after the search is still minted a use", ANewHostileIsPublished);
        red += Row("a hostile flickering in and out of the set cannot buy a search a tick", ChurnIsRateLimited);
        return red;
    }

    /// <summary>
    /// The row puts back the world it built, and that is not enough, and **this case is not the carrier
    /// of the in-suite red it was once filed against.**
    ///
    /// What was observed is that registering it eighth in the default table coincided with three
    /// `VerifyAttackPlanning` scenes and `VerifyTravelEpisodes` going red in-suite while all four passed
    /// standalone, and that skipping this row's body turned that subset green. The conclusion drawn from
    /// that — a residue this row leaks, wide enough to reach its neighbours — was wrong in both of its
    /// load-bearing halves, and a review on 22 September 2026 showed why: this case runs *after* the
    /// journey row rather than before it, so it cannot be that row's cause at all, and one brain tick of
    /// `an opportunity the census admits usable is one the binder can still read` reddens `a spread
    /// weapon closes at full life` on an otherwise pristine tree. The class is therefore a process static
    /// written by the non-combat half of a single brain tick, and it belongs to whatever
    /// `ResetProcessState.BeforeCase` does not restore rather than to anything this file does. A separate
    /// harness lane owns naming it; the case stays registered last because the ordering costs nothing and
    /// the cause is still open, not because that position is evidence of anything.
    ///
    /// What remains here is the scene teardown, which is correct on its own terms. The mining policy was
    /// saved and restored alongside it and is gone: `WorkPolicies.Mining` proxies
    /// `CompanionPreferences.Current`, which `BeforeCase` replaces wholesale with a fresh instance, so
    /// reading it before the row and writing it after moved nothing — a restore that cannot fail is
    /// indistinguishable from one that is not needed, and leaving it standing reads as evidence that the
    /// policy was a suspect somebody eliminated.
    /// </summary>
    private static int Row(string name, Action test)
    {
        try { test(); Console.WriteLine("  GREEN " + name); return 0; }
        catch (Exception error) { Console.WriteLine("  RED " + name + ": " + error.Message); return 1; }
        finally { ClearTheScene(); }
    }

    private static void ANewHostileIsPublished()
    {
        ActionContext ctx = FloorWhereCollectingWins();
        Brain brain = ctx.Companion.Brain;
        FightEnemies fight = brain.Actions.OfType<FightEnemies>().Single();

        // Settle until combat is holding an offer it never got to commit, which is the state the play
        // sat in for five hundred ticks: a prepared plan, with the body doing something else.
        for (int tick = 0; tick < 120; tick++) Tick(ctx);
        Require(fight.OfferedPlan != null,
            $"premise: combat must be holding a prepared plan before a hostile arrives, or there is no "
            + $"stale front to be wrong about; offer={fight.Eligibility}/{fight.EligibilityReason}");
        Require(ctx.Companion.Combat.Planner.Committed == null,
            $"premise: combat must never have taken the body — this row is about the prepared path, and a "
            + $"committed plan is a different contract; committed={ctx.Companion.Combat.Planner.Committed?.Id}");
        Require(brain.Activity.Current?.Name != "combat",
            $"premise: something else must own the body; activity={brain.Activity.Current?.Name ?? "none"}");
        int before = PublishedTargets(brain).Count;

        // Placed away from the line of fire on purpose. A hostile that walks into the shot invalidates
        // the held offer on its own and forces a search without any of this — two earlier geometries for
        // this row came back green for exactly that reason, and the defect only bites on an arrival the
        // offer survives, which is the one a scene has to arrange rather than stumble into.
        Spawn(NewHostile, new Vector2(24 * 16, 60 * 16));
        int searchesAfterArrival = 0;
        for (int tick = 0; tick < 60; tick++)
        {
            Tick(ctx);
            if (fight.OfferedFrontSize > 1) searchesAfterArrival++;
        }

        IReadOnlyCollection<int> published = PublishedTargets(brain);
        int present = LiveHostiles();
        Require(ctx.Companion.Combat.Planner.Committed == null,
            $"the arrival made combat commit, so the row is no longer about the prepared path; "
            + $"committed={ctx.Companion.Combat.Planner.Committed?.Id}");
        Require(published.Contains(NewHostile),
            $"a hostile that arrived after the last search was never minted a use, so the course cannot "
            + $"weigh it at all: uses published for {published.Count} of {present} hostiles present "
            + $"({before} before it arrived), covering slots [{string.Join(",", published.OrderBy(s => s))}] "
            + $"with npc{NewHostile} absent; offer={fight.Eligibility}/{fight.EligibilityReason}, "
            + $"plan={fight.OfferedPlan?.Id.ToString() ?? "none"}");
        // Deliberately not `published.Count == present`. Combat publishes *priced shots*, and a hostile
        // no plan solves against — out of range, no line, the budget ran out — legitimately has no use.
        // The property is that the front is not older than the hostiles, which is the containment above
        // plus a search having actually run below; a coverage equality would fail on a planner doing its
        // job and would be a row about the search rather than about the census.
        Require(searchesAfterArrival >= 1,
            $"no search ran in the sixty ticks after a hostile arrived, so whatever the census published "
            + $"came from a front searched before it existed; published [{string.Join(",", published.OrderBy(s => s))}]");
        // And one search on the change rather than a search a tick. A front wider than the single
        // re-priced plan is what a fresh search looks like from outside.
        Require(searchesAfterArrival <= 2,
            $"the arrival forced {searchesAfterArrival} searches in sixty ticks, so the offer is being "
            + $"re-searched rather than re-priced and the gate costs a search a tick");

        // A departure is the other half of the same set. `CheckPrepared` already refuses an offer whose
        // own target died, so this is the case it does not cover: a hostile that leaves without being the
        // one the offer was about.
        Main.npc[FirstHostile + 1].active = false;
        Main.npc[FirstHostile + 1].life = 0;
        int searchesAfterDeparture = 0;
        for (int tick = 0; tick < 40; tick++)
        {
            Tick(ctx);
            if (fight.OfferedFrontSize > 1) searchesAfterDeparture++;
        }
        IReadOnlyCollection<int> after = PublishedTargets(brain);
        Require(searchesAfterDeparture >= 1,
            $"no search ran after npc{FirstHostile + 1} left, so the offer outlived the set it was searched "
            + $"against in the other direction; published [{string.Join(",", after.OrderBy(s => s))}]");
        Require(!after.Contains(FirstHostile + 1),
            $"a use is still published for npc{FirstHostile + 1}, which is no longer in the world; "
            + $"slots [{string.Join(",", after.OrderBy(s => s))}]");

        Console.WriteLine($"  census front: uses published for {published.Count} of {present} hostiles present "
            + $"after an arrival ({before} before npc{NewHostile} arrived), in {searchesAfterArrival} search(es) "
            + $"over sixty ticks; after a departure, {after.Count} of {LiveHostiles()} present in "
            + $"{searchesAfterDeparture} search(es), slots [{string.Join(",", after.OrderBy(s => s))}]");
    }

    /// <summary>
    /// The other side of the row above: making the front current must not cost a search a frame.
    ///
    /// The gate re-searches when the admissible set changes, and a hostile sitting on the admission
    /// boundary changes it on every tick — a review measured sixty searches over sixty ticks against zero
    /// over sixty quiet ones, which is one full `SearchAttackPlans.Search` per frame against a 12 ms
    /// decide allowance. The scene reproduces that directly by taking a hostile out of the world and
    /// putting it back on alternate ticks, which is the cheapest thing that changes the set without
    /// changing anything else about it.
    ///
    /// The pass line is stated as a rate rather than a count, and the bound is the interval the gate
    /// declares rather than a number written here twice: sixty ticks of alternation can force at most one
    /// search every ten, so anything above eight is the limit not holding. It is deliberately loose at the
    /// top, because the row is about the *class* — a search a tick — and pinning it to exactly six would
    /// go red on a scene where the planner happens to widen the front for its own reasons.
    /// </summary>
    private static void ChurnIsRateLimited()
    {
        ActionContext ctx = FloorWhereCollectingWins();
        Brain brain = ctx.Companion.Brain;
        FightEnemies fight = brain.Actions.OfType<FightEnemies>().Single();
        for (int tick = 0; tick < 120; tick++) Tick(ctx);
        Require(fight.OfferedPlan != null,
            $"premise: combat must be holding a prepared plan, or there is no gate to churn against; "
            + $"offer={fight.Eligibility}/{fight.EligibilityReason}");

        // The quiet control first, because "few searches" means nothing without knowing the gate would
        // otherwise be silent: a quiet stretch must force none at all.
        int quiet = 0;
        for (int tick = 0; tick < 60; tick++) { Tick(ctx); if (fight.OfferedFrontSize > 1) quiet++; }
        Require(quiet == 0,
            $"premise: a quiet sixty ticks forced {quiet} search(es), so this scene cannot tell churn's "
            + "cost from the planner's ordinary behaviour");

        Spawn(NewHostile, new Vector2(24 * 16, 60 * 16));
        int churned = 0, cut = 0;
        for (int tick = 0; tick < 60; tick++)
        {
            bool present = tick % 2 == 0;
            Main.npc[NewHostile].active = present;
            Main.npc[NewHostile].life = present ? Main.npc[NewHostile].lifeMax : 0;
            Tick(ctx);
            if (fight.OfferedFrontSize > 1) churned++;
            if (fight.OfferedCut) cut++;
        }
        Require(churned <= 8,
            $"a hostile entering and leaving the set on alternate ticks forced {churned} searches over "
            + "sixty ticks, so the gate is unbounded and a body on the admission boundary spends the whole "
            + "planning allowance re-searching the same fight");
        Require(cut >= 1,
            "no tick published a cut front, so a suppressed search is being published as a current one — "
            + "the rate limit is only honest while the front it holds back says it is not finished");
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"  churn cost: {churned} search(es) over sixty ticks "
            + $"of a hostile entering and leaving the set each tick, against {quiet} over sixty quiet ticks; "
            + $"{cut} of the sixty published a cut front");
    }

    /// <summary>Every distinct hostile the frozen observation currently carries a priced use for. This is
    /// the census's own input: `CombatOpportunitySource` walks these facts and mints one opportunity per
    /// distinct target, so a hostile absent here is a hostile the course cannot order a shot at.</summary>
    private static IReadOnlyCollection<int> PublishedTargets(Brain brain)
    {
        DecisionFactSnapshot? facts = brain.Course.Facts;
        if (facts == null) return Array.Empty<int>();
        var slots = new HashSet<int>();
        foreach (DecisionFact fact in facts.Facts)
        {
            if (fact.Key.Kind != "combat-use") continue;
            var use = JsonSerializer.Deserialize<CombatCourseFacts.Use>(fact.Value.Text ?? "null");
            if (use != null) slots.Add(use.TargetSlot);
        }
        return slots;
    }

    private static int LiveHostiles()
    {
        int count = 0;
        for (int slot = FirstHostile; slot <= NewHostile; slot++)
            if (Main.npc[slot].active && Main.npc[slot].life > 0) count++;
        return count;
    }

    private const int FirstHostile = 30, NewHostile = 32;

    /// <summary>Two drops beside a standing player and two hostiles in reach: collecting wins the body, so
    /// combat prepares a plan every tick and never commits one, which is the state the capture recorded.</summary>
    private static ActionContext FloorWhereCollectingWins()
    {
        ActionContext ctx = VerifyCollectionContracts.SetUpFloor();
        ctx.Player.Bottom = new Vector2(50 * 16, 60 * 16);
        ctx.Player.velocity = Vector2.Zero;
        ctx.Npc.Bottom = new Vector2(50 * 16, 59 * 16);
        ctx.Player.GetModPlayer<CompanionPlayer>().Gear.Slots[0].SetDefaults(ItemID.WoodenBow);
        ctx.Player.GetModPlayer<CompanionPlayer>().Gear.Slots[1] = new Item();
        Spawn(30, new Vector2(46 * 16, 60 * 16));
        Spawn(31, new Vector2(48 * 16, 60 * 16));
        for (int i = 0; i < 2; i++)
        {
            Item drop = VerifyCollectionContracts.Drop(ItemID.CopperOre, 5, new Vector2((52 + i * 2) * 16 + 8, 60 * 16), slot: 10 + i);
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

    /// <summary>The per-case reset does not deactivate a seeded `Main.item` or `Main.npc` slot, so this
    /// row empties what it built rather than handing it to whatever runs next.</summary>
    private static void ClearTheScene()
    {
        foreach (Projectile projectile in Main.projectile) projectile.active = false;
        for (int slot = 10; slot <= 11; slot++) { Main.item[slot] = new Item(); Main.item[slot].active = false; }
        for (int slot = FirstHostile; slot <= NewHostile; slot++) { Main.npc[slot].active = false; Main.npc[slot].life = 0; }
    }

    private static void Tick(ActionContext ctx)
    {
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        ctx.Companion.Brain.Tick(ctx.Companion, ctx.Player);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
