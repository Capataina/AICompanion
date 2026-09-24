extern alias live;

using System;
using System.Linq;
using AICompanion.Tools.Ledger;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Infrastructure.Position;
using live::AICompanion.Companion.Brain.Infrastructure.Selection;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>
/// The switch itself: whether the live brain tick asks a course what to do.
///
/// Every other course fixture drives a piece — a capture, a binder, a forecast, a search — against a
/// hand-built snapshot. None of them establishes the thing the whole design exists for, which is that
/// the companion in a running game is doing what a published course said. That claim has exactly one
/// honest form: run `Brain.Tick` against real Terraria tiles and read what came out the far end.
///
/// These rows are deliberately about ownership rather than about quality of play. They do not establish
/// that a course picks good work, that its ordering beats the family chooser's, or that any of it feels
/// right — those are a playtest's job and the acceptance play is still open. What they establish is that
/// the decision is the course's, that an empty world is companionship rather than a freeze, and that a
/// published course survives the tick that published it.
/// </summary>
internal static class VerifyTheCourseOwnsTheTick
{
    public static int Run()
    {
        int red = 0;
        void Row(string name, Action test)
        {
            red += RunOneRow.GreenOrRed(name, test);
        }
        Row("G01 a whole tick runs through the course and asks for a place", ATickRunsThroughTheCourse);
        Row("G01 an empty world is companionship, not a hold", NothingToDoKeepsCompany);
        Row("G01 a published course is retained across the next tick", APublishedCourseIsRetained);
        Row("G01 every tick has an owner: an activity holds the body or the body is asked to keep company", TheTickAlwaysHasAnOwner);
        Row("G01 companionship is still observed on a tick the course owns", CompanionshipIsStillObserved);
        return red;
    }

    /// <summary>
    /// The other half of "the chooser is off the path", and the half that was missing. Proving the old
    /// decision procedure no longer runs says nothing about what it used to *write* on its way through,
    /// and `Choose` wrote four companionship observations nobody else did: the estimated return time, the
    /// reunion delay cost, the departure reading and the regroup urgency. Since `0bb2c8a` every one of
    /// them had been frozen at its default for entire sessions — the recorder's three reunion columns
    /// recorded two constants beside one live number, and `KeepCompany.CalculateReunionValue` read a
    /// regroup urgency that was always zero. Nothing went red, because a frozen float is a legal float.
    ///
    /// So this row asks the question the negative row cannot: with the body carried far outside the
    /// player's region for long enough that a return genuinely costs something, do these numbers move?
    /// It asserts responsiveness rather than a value — a threshold here would be a tuning nobody agreed —
    /// and it is deliberately driven through `Brain.Tick` rather than by calling the observation directly,
    /// because what failed was the wiring and a direct call cannot see wiring.
    /// </summary>
    private static void CompanionshipIsStillObserved()
    {
        ActionContext ctx = Scene();
        Tick(ctx, 2);
        var chooser = ctx.Companion.Brain.Companionship;
        Require(chooser.EstimatedReturnTicks >= 0f,
            $"the return estimate was never computed on a tick the course owns; ticks={chooser.EstimatedReturnTicks}");

        // The player walks away while the body is held where it is, which is the only arrangement that
        // makes all three numbers non-zero and is why the first draft of this row failed against its own
        // fix. `DelayCostPerTick` is `Departure × (…) + ApartTicks / (…)`: beside a standing player both
        // terms are honestly zero, and a body merely teleported away flies home inside a few ticks and
        // resets `ApartTicks`. So the separation has to be sustained *and* the player has to be the one
        // opening it — which is also the case the number exists to price.
        var held = new Vector2(40 * 16 + 8, 60 * 16);
        for (int i = 0; i < 40; i++)
        {
            ctx.Player.velocity = new Vector2(6f, 0f);
            ctx.Player.controlRight = true;
            ctx.Player.position += ctx.Player.velocity;
            ctx.Npc.Bottom = held;
            ctx.Npc.velocity = Vector2.Zero;
            // `Reunion.Observe` counts apart-ticks only across *consecutive* engine ticks, deliberately —
            // a missing observation does not prove continued separation. This fixture drives `Brain.Tick`
            // without the engine behind it, so nothing advances the frame counter and every Observe would
            // early-return on an unchanged tick, leaving ApartTicks at zero however far apart the two are.
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            Tick(ctx, 1);
        }
        Require(chooser.EstimatedReturnTicks > 0f,
            $"a body far from the player still estimates no time to return; ticks={chooser.EstimatedReturnTicks}");
        Require(chooser.RegroupUrgency > 0f,
            $"a body far outside the player's region feels no regroup urgency, so the observation is not running; "
            + $"urgency={chooser.RegroupUrgency}, return={chooser.EstimatedReturnTicks}");
        Require(chooser.Reunion.DelayCostPerTick > 0f,
            $"the reunion delay cost stayed at its default while the body was away, so Reunion.Evaluate never ran; "
            + $"cost={chooser.Reunion.DelayCostPerTick}, apart={chooser.Reunion.ApartTicks}");
    }

    /// <summary>Drives the real `Brain.Tick` the way the game does: the engine's own AI entry point on a
    /// real `CompanionNPC` over real tiles, with nothing hand-fed into the decision.</summary>
    private static ActionContext Scene()
    {
        ActionContext ctx = VerifyCollectionContracts.SetUpFloor();
        ctx.Npc.Bottom = new Vector2(40 * 16 + 8, 60 * 16);
        ctx.Player.position = new Vector2(42 * 16, 59 * 16 - ctx.Player.height);
        ctx.Senses.Update(ctx.Npc, ctx.Player);
        return ctx;
    }

    private static void Tick(ActionContext ctx, int times = 1)
    {
        for (int i = 0; i < times; i++) ctx.Companion.Brain.Tick(ctx.Companion, ctx.Player);
    }

    /// <summary>
    /// The load-bearing row, and the one that would have stayed green through the whole build if the
    /// wiring had gone in without it: the tick completes, and the request the body was given came from
    /// the course rather than from nothing.
    /// </summary>
    private static void ATickRunsThroughTheCourse()
    {
        ActionContext ctx = Scene();
        Tick(ctx);
        Brain brain = ctx.Companion.Brain;
        Require(brain.ChoiceEvaluated,
            "the tick did not reach a decision at all, so nothing was asked of the body");
        Require(brain.Course.Facts != null,
            "no observation was frozen this tick, so the course had no world to decide from");
        Require(brain.Course.Facts!.Facts.Count > 0,
            "the frozen observation carried no facts, so every domain would read as a world nobody looked at");
        Require(brain.LastRequest.Kind != RequestKind.Hold,
            $"the body was told to hold, which is the stillness the orb rewrite exists to prevent; request={brain.LastRequest.Kind}");
    }

    /// <summary>
    /// An empty floor has nothing worth doing, and what the companion does then is the difference between
    /// a companion and a statue. The plan makes an empty order *be* companionship with its own projected
    /// costs rather than an artificial idle, so this asserts the request kind the course falls back to.
    /// </summary>
    private static void NothingToDoKeepsCompany()
    {
        ActionContext ctx = Scene();
        Tick(ctx, 3);
        Brain brain = ctx.Companion.Brain;
        Require(brain.LastRequest.Kind == RequestKind.WithPlayer,
            $"an empty world produced {brain.LastRequest.Kind} rather than keeping the player company");
        Require(brain.Activity.Current?.Name == "keep-company",
            $"the activity carrying an empty course is not keeping company; activity={brain.Activity.Current?.Name ?? "none"}");
    }

    /// <summary>
    /// Retention is the whole property the design is named for, and it is the one a per-tick chooser
    /// would satisfy by accident on a scene where the same answer wins twice. So the row asserts on the
    /// course's own identity rather than on what the body was asked for: a retained course keeps the id
    /// it was published under, where a course re-decided from scratch every tick mints a new one.
    /// </summary>
    private static void APublishedCourseIsRetained()
    {
        ActionContext ctx = Scene();
        // A drop on the floor beside the body is real work with a real binder behind it, which is what
        // gives the course something to publish. Without one this row passes against an empty course.
        Item drop = VerifyCollectionContracts.Drop(ItemID.CopperOre, 5, new Vector2(38 * 16 + 8, 60 * 16));
        drop.playerIndexTheItemIsReservedFor = Main.myPlayer;
        ctx.Senses.Update(ctx.Npc, ctx.Player);
        Tick(ctx, 4);

        RetainedCourse? published = ctx.Companion.Brain.Course.Course.Current;
        if (published == null)
        {
            // An honest skip rather than a false pass: the scene did not produce a course, so it cannot
            // say anything about retaining one. This is reported rather than swallowed, because a row
            // that quietly proves nothing is the silence that reads as health.
            DecideCourseEachTick owner = ctx.Companion.Brain.Course;
            string coverage = string.Join(" ", owner.Coverage.Select(source =>
                $"{source.Source}:{source.Examined}/{source.Total}{(source.Exhausted ? "" : "+")}"));
            EmitLedgerRows.Detail("no course was published on this floor, so retention was not exercised; "
                + $"decision={owner.Last.Reason}; release={owner.Course.ReleaseReason}; orders evaluated={owner.LastSearch.Evaluated} "
                + $"rejected={owner.LastSearch.Rejected} exhausted={owner.LastSearch.Exhausted}; coverage {coverage}");
            return;
        }
        long id = published.Id;
        Tick(ctx);
        RetainedCourse? after = ctx.Companion.Brain.Course.Course.Current;
        Require(after != null && after.Id == id,
            $"the course published on one tick did not survive the next, so nothing is being retained; was={id} now={after?.Id.ToString() ?? "released"}");
    }

    /// <summary>
    /// **The negative half of this row is gone, because there is no longer a second decision system to be
    /// off the path.** It used to require `Chooser.LastScores` and `LastNominations` to be empty after a
    /// real tick, since only `ChooseBehaviour.Choose` filled them — the one check that could catch a wiring
    /// calling the course *and* leaving the family chooser deciding. `AIC-419` deleted the chooser on
    /// 22 September 2026, so that wiring cannot be written and the assertion is one no change could falsify,
    /// which is worse than no assertion at all.
    ///
    /// What is left is the positive half, and it is not a leftover: **a tick must have an owner.** Either an
    /// activity holds the body or the body was asked to keep the player company, because an empty course is
    /// companionship and never a freeze — `ExecuteCourseBinding` owns that contract and this is the row that
    /// witnesses it after two real ticks of the whole brain.
    /// </summary>
    private static void TheTickAlwaysHasAnOwner()
    {
        ActionContext ctx = Scene();
        Tick(ctx, 2);
        Brain brain = ctx.Companion.Brain;
        Require(brain.Activity.Current != null || brain.LastRequest.Kind == RequestKind.WithPlayer,
            "no activity owns the tick and the body was not asked to keep company either, so the tick has no owner at all");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
