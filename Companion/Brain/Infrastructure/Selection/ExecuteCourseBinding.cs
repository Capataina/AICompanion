#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Interactions;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>
/// Translates one published course binding into the two things the rest of the tick already knows how
/// to carry out: which activity owns the work, and where the body is asked to be.
///
/// This is deliberately a pure translation and not a second executor. The course owner decides *what*
/// to do; the activities below still perform it, the positioner still resolves the point, and the motor
/// is still the only writer to the live NPC. Adding a parallel execution path for course work would
/// give the companion two ways to mine and no single answer about what it is doing — which is the
/// post-grant second chooser the plan names for deletion, arriving again by the back door.
///
/// This said "nothing calls this yet" from the day it landed until 22 September 2026, and it had been
/// wrong since `0bb2c8a` the morning after: `CoordinateBrainTick` supplies every tick's position request
/// through it. Corrected while `AIC-419` deleted the chooser the old sentence named.
/// </summary>
public static class ExecuteCourseBinding
{
    /// <summary>
    /// The activity that performs each bound purpose, by the name it registers under, and the whole of
    /// what a course may bind.
    ///
    /// The mapping is explicit rather than derived from the domain string because the two vocabularies
    /// genuinely differ and each difference is a decision: combat's opportunities are minted per *use*
    /// with the purpose "fire" while the activity is "combat", and lighting's activity is
    /// "place-torches" while its domain is "light-target".
    ///
    /// <para>**`break-pot` is deliberately absent, and its absence is now load-bearing rather than a
    /// note.** A pot is broken in passing by whatever activity is already travelling, through the shared
    /// nearby-work adapter — that is the owner's ruling, `J08` asserts it, and the root guide states it.
    /// The purpose used to map to the empty string with the caller reading that as "no activity change",
    /// which made a pot bindable as a course step: the search would order a dedicated trip to a pot, the
    /// tick would carry it, and the body would fly there with no activity, no attempt, no credit and
    /// nothing in the record saying anything was running. Measured over a whole-tick probe, thirty ticks
    /// of `bound=pot-target:tile:27,58 action=none` before the lighting trip the row was about could
    /// start. So the map is the single fact of which purposes are executable, `SearchCourseOrders`
    /// refuses any opportunity whose purpose is not in it before enumeration, and this throws for a pot
    /// like any other unmapped purpose rather than answering with a blank.</para>
    ///
    /// <para>What a future pot executor would need, so nobody re-adds the blank: an activity registered
    /// under its own name that reserves the hand on arrival, opens and concludes an attempt so the work
    /// is credited to something, and declares `CourseDomains` for `pot-target` — at which point the
    /// entry goes in this map and the exemption in `VerifyCourseBindingExecution` goes out. That is a
    /// product decision about whether the companion should make trips for pots, not a wiring gap.</para>
    /// </summary>
    private static readonly Dictionary<string, string> Executors =
        new(StringComparer.Ordinal)
        {
            ["fire"] = "combat",
            ["mine"] = "mine",
            ["chop"] = "chop",
            ["collect"] = "collect",
            ["light"] = "place-torches",
        };

    /// <summary>Whether a purpose has an executor at all, which is what makes an unexecutable step a
    /// *proven* refusal rather than an unanswered question: the map is a fact of this tree and nothing
    /// about the world can change the answer.</summary>
    public static bool HasExecutor(string purpose) => Executors.ContainsKey(purpose);

    /// <summary>Every purpose a course may bind, for the row that checks the producers against it.</summary>
    public static IReadOnlyCollection<string> ExecutablePurposes => Executors.Keys;

    /// <summary>
    /// The activity that performs a bound purpose. A silent fallback here would route unknown work to
    /// whichever activity sorted first, so an unmapped purpose throws — a new domain must name its
    /// executor rather than inherit one, and a purpose the search should have refused must not reach
    /// the body wearing a blank activity name.
    /// </summary>
    public static string ActivityFor(string purpose) => Executors.TryGetValue(purpose, out string? activity)
        ? activity
        : throw new ArgumentOutOfRangeException(nameof(purpose), purpose,
            "A bound purpose must name the activity that performs it; SearchCourseOrders refuses a "
            + "purpose with no executor before it can be ordered.");

    /// <summary>
    /// Where the body is asked to be for this binding.
    ///
    /// A firing stand is a <see cref="RequestKind.FireFrom"/> because the combat stance's admission and
    /// its rock fallback live behind that kind, and asking for the same point as an Exact hover would
    /// route around both. Everything else is the point the binding was priced from, carried as Exact,
    /// with tile work naming its work tile so the tool-reach proof the positioner applies is the one
    /// that admitted the pose in the first place.
    /// </summary>
    public static PositionRequest RequestFor(StepBinding binding, Vector2? bodyCentre = null)
    {
        var pose = new Vector2((float)binding.Pose.X, (float)binding.Pose.Y);
        if (binding.Opportunity.Purpose == "fire")
            return new PositionRequest(RequestKind.FireFrom, pose);
        // `break-pot` was in this list and is orphaned by the executor map refusing it before a step can
        // exist: a pot can no longer be a binding, so a branch for one is a branch nothing reaches.
        if (binding.Opportunity.Purpose is not ("mine" or "chop" or "light"))
            return PositionRequest.ExactAt(pose);
        Point work = WorkTileOf(binding.Opportunity);
        // A stand the body has already satisfied is not a journey. The stand in the binding came from a
        // capture that scans its area in budgeted slices and restarts on every strike, so the tile the
        // body is about to hit is usually re-read several ticks after the strike that restarted the
        // scan — by which time the body has moved and `FindToolAccess.Approach` no longer short-circuits
        // on the body's own centre, ranking a cell beside the work instead. The stand is therefore a
        // body-relative answer published as a durable fact, and by the time it is bound it describes
        // where the body *was*.
        //
        // Measured on the maximum-reach mining scene: the first strike bound the body's own pose at 320,
        // the strike restarted the scan, and the next binding asked for 384 — four tiles in, toward ore
        // the body was already swinging at. The same shape appears on the neighbouring row's ore, 416
        // then 356.9, so it is the capture's cadence rather than one scene's arithmetic.
        //
        // Re-deriving here rather than in the binder is deliberate: the search prices orders against a
        // frozen observation and must not read the live world, while this is execution, which is exactly
        // the seam where a decided course becomes what the body is asked to do this tick and where the
        // live body is the thing being asked. It uses the same `InReach` the capture short-circuits on,
        // so the two can never disagree about what "already reaches it" means, and it can only ever
        // remove travel the course priced — never add any — so no forecast is made optimistic by it.
        if (bodyCentre is { } centre && FindToolAccess.InReach(centre, work))
            return PositionRequest.ExactAt(centre, work);
        return PositionRequest.ExactAt(pose, work);
    }

    /// <summary>
    /// The tile the hand acts on, read from the opportunity's own identity rather than from the pose.
    ///
    /// This used to be <c>new Point((int)(binding.Pose.X / 16), (int)(binding.Pose.Y / 16))</c>, which is
    /// the tile the *body* occupies. For every tile domain those are different points by construction:
    /// the pose is a hover beside the work, and a body hovering beside an ore tile is in air. Measured
    /// on the mining evidence scene, where the only copper is at 25,59 and the recorded work tile read
    /// 24,59 with <c>has-tile=False type=0</c> — a tool-reach success region declared over nothing, so
    /// every arrival verdict computed from it was judged against a tile the scene never seeded.
    ///
    /// It is the same confusion the torch census had, one layer further on: **a work site and a body
    /// destination are two different quantities, and a field that carries one cannot be asked for the
    /// other.** The opportunity key is the honest source because it is the identity the binder
    /// validated the site against and the recorder joins effects by.
    ///
    /// Two shapes exist and both are produced by this tree: <c>tile:x,y</c> from the light and pot
    /// captures, and <c>tile:material:x,y</c> from gathering, which carries the material so a replaced
    /// tile is a different opportunity. An unrecognised shape throws rather than guessing a tile,
    /// because a silently wrong work tile is what this replaced.
    /// </summary>
    internal static Point WorkTileOf(OpportunityKey opportunity)
    {
        string target = opportunity.Target;
        int comma = target.LastIndexOf(',');
        int colon = comma < 0 ? -1 : target.LastIndexOf(':', comma);
        if (colon >= 0 && comma > colon
            && int.TryParse(target.AsSpan(colon + 1, comma - colon - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            && int.TryParse(target.AsSpan(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            return new Point(x, y);
        throw new ArgumentOutOfRangeException(nameof(opportunity), target,
            "A tile-work opportunity must name its tile as 'tile:x,y' or 'tile:material:x,y'; the work "
            + "tile cannot be recovered from the body's pose, which is a hover beside the work.");
    }

    /// <summary>
    /// What an empty course asks for. The plan makes an empty order *be* companionship with its own
    /// projected costs rather than an artificial idle, so the body keeps the player company rather than
    /// holding still — a Hold here would make "the course found nothing worth doing" look identical to
    /// "the course told the body to freeze", which is the stillness the orb rewrite exists to prevent.
    ///
    /// <para><paramref name="regionCentre"/> is required rather than defaulted, and that is the fix for a
    /// defect this was: it shipped as a parameterless property anchored at <c>Vector2.Zero</c>, which is
    /// the world's top-left corner. An anchor is only read when the positioner admits no candidate — the
    /// body is cut off, or no usable corner inside the region exists — and then <c>SeekDestination</c>
    /// aims the navigator at it directly. So the zero anchor was invisible on every ordinary tick and, on
    /// the tick it mattered, flew the companion at the world origin: measured on the locked-door scene,
    /// the body climbed from row 79 to row 8 and column 38 to column 8, thirty tiles *away* from a player
    /// at column 80, until the straight-line distance finally admitted recovery flight, which then
    /// carried it through the sealed wall. The legacy <c>KeepCompany</c> passed the region's centre here;
    /// the wiring at <c>0bb2c8a</c> dropped it. Requiring the argument is what stops it being dropped
    /// again, because a value you must supply cannot be forgotten the way a default can.</para>
    /// </summary>
    public static PositionRequest Companionship(Vector2 regionCentre) => new(RequestKind.WithPlayer, regionCentre);
}
