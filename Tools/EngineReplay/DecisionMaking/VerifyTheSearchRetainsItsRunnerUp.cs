extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Infrastructure.Selection;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>
/// What the winning order beat, kept rather than discarded.
///
/// The search prices orders and keeps the best; everything else went out as it went. That leaves the
/// recorder's `task_order_runner_up` column with nothing to write but
/// `unavailable:the-search-retains-only-its-best-order`, and a reader unable to tell a fight that lost
/// narrowly from one that was never on the board — which is precisely the question the play of 0.38.13
/// left open, with combat admitted usable on every tick and the empty course winning every one.
///
/// The discriminator is the *first step* rather than the whole order, because the first step is the only
/// part of an order the tick performs: two orders that start the same way are one answer to "what would
/// it have done instead" however they differ afterwards.
/// </summary>
internal static class VerifyTheSearchRetainsItsRunnerUp
{
    public static int Run()
    {
        int red = 0;
        red += Row("two priced orders leave a runner-up that starts somewhere else", TwoOrdersLeaveARunnerUp);
        red += Row("one priced order leaves no runner-up rather than an unreadable one", OneOrderLeavesNone);
        return red;
    }

    private static int Row(string name, Action test)
    {
        try { test(); Console.WriteLine("  GREEN " + name); return 0; }
        catch (Exception error) { Console.WriteLine("  RED " + name + ": " + error.Message); return 1; }
    }

    /// <summary>
    /// Two drops on the floor is two single-step orders plus the empty one, so the search prices at least
    /// three distinct first steps and the runner-up is a real alternative rather than a reordering of the
    /// winner. The assertion is on the first step differing, not on the value, because which of two
    /// equally reachable drops wins is the objective's business.
    /// </summary>
    private static void TwoOrdersLeaveARunnerUp()
    {
        ActionContext ctx = FloorWithDrops(2);
        DecideCourseEachTick owner = ctx.Companion.Brain.Course;
        CourseProjection? best = null, runnerUp = null;
        for (int tick = 0; tick < 60; tick++)
        {
            Tick(ctx);
            if (owner.LastSearch.Evaluated < 2) continue;
            best = BestOf(owner);
            runnerUp = RunnerUpOf(owner);
            if (best != null && runnerUp != null) break;
        }
        Require(best != null,
            $"premise: the search never priced two orders, so there is nothing to be runner-up to; "
            + $"priced={owner.LastSearch.Evaluated} refused={owner.LastSearch.Rejected} "
            + $"admitted {Admitted(owner)}");
        Require(runnerUp != null,
            $"the search priced {owner.LastSearch.Evaluated} orders and kept no runner-up, so the recorder can "
            + $"only write that the alternative is unreadable; best starts "
            + $"{FirstStep(best!)}, admitted {Admitted(owner)}");
        Require(FirstStep(best!) != FirstStep(runnerUp!),
            $"the runner-up starts where the winner starts, so it is the same answer to what the tick would "
            + $"have done: both begin {FirstStep(best!)}");
        Require(owner.LastRunnerUpOrder is { } surfaced && surfaced.Purposes.Count == runnerUp!.Steps.Count,
            $"the owner's accessor disagrees with the search it read; search runner-up has "
            + $"{runnerUp!.Steps.Count} step(s), accessor carries "
            + $"{owner.LastRunnerUpOrder?.Purposes.Count.ToString() ?? "null"}");
        Console.WriteLine($"  runner-up: winner starts {FirstStep(best!)}, runner-up starts {FirstStep(runnerUp!)} "
            + $"worth {owner.LastRunnerUpOrder!.Value.Value:0.0000} over "
            + $"[{string.Join(">", owner.LastRunnerUpOrder!.Value.Purposes)}]; {owner.LastSearch.Evaluated} orders priced");
    }

    /// <summary>
    /// An empty floor prices exactly one order, the empty one, and the honest answer is that there was no
    /// alternative — not an alternative nobody recorded. A runner-up invented here would be the winner
    /// under another name and would make the recorder's column always non-empty, which is the failure
    /// mode that makes a column stop being read.
    /// </summary>
    private static void OneOrderLeavesNone()
    {
        ActionContext ctx = FloorWithDrops(0);
        DecideCourseEachTick owner = ctx.Companion.Brain.Course;
        for (int tick = 0; tick < 20; tick++) Tick(ctx);
        Require(owner.LastSearch.Evaluated == 1,
            $"premise: an empty floor must price exactly the empty order; priced={owner.LastSearch.Evaluated}, "
            + $"admitted {Admitted(owner)}");
        Require(owner.LastRunnerUpOrder == null,
            $"a search with one priced order reported a runner-up, which can only be the winner wearing "
            + $"another name; purposes [{string.Join(">", owner.LastRunnerUpOrder?.Purposes ?? Array.Empty<string>())}]");
        Console.WriteLine($"  runner-up: one order priced on an empty floor and no runner-up claimed");
    }

    private static CourseProjection? BestOf(DecideCourseEachTick owner) => Search(owner)?.Best;
    private static CourseProjection? RunnerUpOf(DecideCourseEachTick owner) => Search(owner)?.RunnerUp;

    /// <summary>The owner's own search, read through the field it keeps it in. The accessor under test is
    /// a projection of this, so the row checks both: the search retained something, and the owner
    /// surfaced the same thing.</summary>
    private static SearchCourseOrders? Search(DecideCourseEachTick owner)
        => (SearchCourseOrders?)typeof(DecideCourseEachTick)
            .GetField("search", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(owner);

    private static string FirstStep(CourseProjection order)
        => order.Steps.Count == 0 ? "(idle)" : order.Steps[0].Opportunity.ToString();

    private static string Admitted(DecideCourseEachTick owner)
        => string.Join(" ", owner.Admitted.Select(a => $"{a.Domain}={a.Usable}ok/{a.Unresolved}?/{a.Unusable}x"));

    private static ActionContext FloorWithDrops(int drops)
    {
        ActionContext ctx = VerifyCollectionContracts.SetUpFloor();
        ctx.Player.Bottom = new Vector2(50 * 16, 60 * 16);
        ctx.Player.velocity = Vector2.Zero;
        ctx.Npc.Bottom = new Vector2(50 * 16, 59 * 16);
        for (int i = 0; i < drops; i++)
        {
            Item drop = VerifyCollectionContracts.Drop(ItemID.CopperOre, 5, new Vector2((52 + i * 2) * 16 + 8, 60 * 16), slot: 10 + i);
            drop.playerIndexTheItemIsReservedFor = Main.myPlayer;
        }
        ctx.Senses.Update(ctx.Npc, ctx.Player);
        return ctx;
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
