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
        return red;
    }

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
        FightEnemies fight = brain.Chooser.Actions.OfType<FightEnemies>().Single();

        // Settle until combat is holding an offer it never got to commit, which is the state the play
        // sat in for five hundred ticks: a prepared plan, with the body doing something else.
        for (int tick = 0; tick < 120; tick++) Tick(ctx);
        Require(fight.OfferedPlan != null,
            $"premise: combat must be holding a prepared plan before a hostile arrives, or there is no "
            + $"stale front to be wrong about; offer={fight.Eligibility}/{fight.EligibilityReason}");
        Require(ctx.Companion.Combat.Planner.Committed == null,
            $"premise: combat must never have taken the body — this row is about the prepared path, and a "
            + $"committed plan is a different contract; committed={ctx.Companion.Combat.Planner.Committed?.Id}");
        Require(brain.Chooser.Current?.Name != "combat",
            $"premise: something else must own the body; activity={brain.Chooser.Current?.Name ?? "none"}");
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
