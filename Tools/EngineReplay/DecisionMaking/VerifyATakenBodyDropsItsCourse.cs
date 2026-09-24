extern alias live;

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>
/// Downing and recovery flight take the body away from the course, and the course goes with it.
///
/// Both suspended the activity and left the course standing, so the tick after the body was handed back
/// resumed a step chosen from where the companion used to be — and a decision in flight kept its
/// observation frozen from before the body was carried somewhere else. `DecideCourseEachTick.Interrupt`
/// drops both, and the two owners of the body call it on every tick they hold it. This row drives the
/// downed path, because it needs no scene beyond a published course; recovery flight calls the same
/// method on the same terms.
/// </summary>
internal static class VerifyATakenBodyDropsItsCourse
{
    public static int Run()
    {
        int red = 0;
        red += Row("downing releases the course the body was performing", DowningReleasesTheCourse);
        red += Row("an ordinary tick keeps the course it published", AnOrdinaryTickKeepsIt);
        return red;
    }

    private static int Row(string name, Action test)
    {
        try { return RunOneRow.GreenOrRed(name, test); }
        finally { VerifyAdmittedOpportunitiesBind.ClearTheScene(); }
    }

    private static void DowningReleasesTheCourse()
    {
        ActionContext ctx = PublishACollectionCourse();
        ctx.Companion.Brain.ApplyDownedControls(ctx.Companion);
        var retained = ctx.Companion.Brain.Course.Course;
        Require(retained.Current == null,
            $"the companion was downed and its course still stands with {retained.Current?.Projection.Steps.Count} step(s), "
            + "so the tick it is revived on resumes a step chosen before it went down");
        Require(retained.ReleaseReason == "downed",
            $"the course was released but not by downing: reason '{retained.ReleaseReason}'");
    }

    /// <summary>The control: without it the first row passes against a course that was never kept at all.</summary>
    private static void AnOrdinaryTickKeepsIt()
    {
        ActionContext ctx = PublishACollectionCourse();
        Tick(ctx);
        Require(ctx.Companion.Brain.Course.Course.Current is { } course && course.Projection.Steps.Count > 0,
            "premise: a published collection course did not survive one ordinary tick, so the downing row proves nothing");
    }

    private static ActionContext PublishACollectionCourse()
    {
        ActionContext ctx = VerifyCollectionContracts.SetUpFloor();
        ctx.Player.Bottom = new Vector2(50 * 16, 60 * 16);
        ctx.Player.velocity = Vector2.Zero;
        ctx.Npc.Bottom = new Vector2(50 * 16, 59 * 16);
        Item drop = VerifyCollectionContracts.Drop(ItemID.CopperOre, 5, new Vector2(54 * 16 + 8, 60 * 16), slot: 10);
        drop.playerIndexTheItemIsReservedFor = Main.myPlayer;
        ctx.Senses.Update(ctx.Npc, ctx.Player);
        for (int tick = 0; tick < 60; tick++)
        {
            Tick(ctx);
            if (ctx.Companion.Brain.Course.Course.Current is { } course && course.Projection.Steps.Count > 0) return ctx;
        }
        throw new InvalidOperationException(
            "premise: sixty ticks beside one reachable drop published no course with a step, so there is nothing for downing to release");
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
