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
    /// Which registered activity performs each opportunity domain, built from the activities' own
    /// <see cref="CompanionAction.CourseDomains"/> rather than written out a second time here.
    ///
    /// <para>**There is one source for "who performs this kind of work", and holding it is the whole
    /// reason this map is derived.** Until 23 September 2026 this was a hand-written table keyed by
    /// purpose, beside the activities' own declarations, and the two disagreed about pots:
    /// `CollectNearbyItems` declared `pot-target` and carried the whole pot method, while the table left
    /// the purpose out. A course that bound a pot therefore reached the tick with no activity, and the
    /// body flew thirty ticks to a pot with nothing to do on arrival; the repair of 22 September refused
    /// every pot rather than asking why the performer and the table disagreed. Derived, a domain nobody
    /// declares cannot be bound and a domain somebody declares cannot be refused, so the two cannot
    /// drift apart again.</para>
    ///
    /// <para>It is keyed by domain rather than purpose because the domain is what a census publishes and
    /// an activity declares, while the purpose is what the hand does, and one activity may do more than
    /// one thing. It is built from `RegisterActivities.All()`, the production set, so a fixture that
    /// registers a narrower list on its brain still reads the same performer for a domain;
    /// `CoordinateBrainTick` then finds no instance and runs nothing, which is what a restricted scene
    /// means.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Executors = BuildExecutors();

    private static IReadOnlyDictionary<string, string> BuildExecutors()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (CompanionAction activity in RegisterActivities.All())
            foreach (string domain in activity.CourseDomains)
            {
                // Two performers for one domain would make the binding's owner a matter of list order,
                // which is the silent fallback this map replaced; it is refused where the map is built.
                if (map.TryGetValue(domain, out string? other))
                    throw new InvalidOperationException(
                        $"The domain '{domain}' is declared by both '{other}' and '{activity.Name}'; one domain has one performer.");
                map[domain] = activity.Name;
            }
        return map;
    }

    /// <summary>Whether a domain has a performer at all, which is what makes an unexecutable step a
    /// *proven* refusal rather than an unanswered question: the registered activities are a fact of this
    /// tree and nothing about the world can change the answer.</summary>
    public static bool HasExecutor(string domain) => Executors.ContainsKey(domain);

    /// <summary>Every domain a course may bind, for the row that checks the producers against it.</summary>
    public static IEnumerable<string> ExecutableDomains => Executors.Keys;

    /// <summary>
    /// The activity that performs a bound opportunity's domain. A silent fallback here would route
    /// unknown work to whichever activity sorted first, so an undeclared domain throws — a new domain
    /// must be declared by the activity that performs it, and a domain the search should have refused
    /// must not reach the body wearing a blank activity name.
    /// </summary>
    public static string ActivityFor(string domain) => Executors.TryGetValue(domain, out string? activity)
        ? activity
        : throw new ArgumentOutOfRangeException(nameof(domain), domain,
            "A bound domain must be declared by the activity that performs it (CompanionAction.CourseDomains); "
            + "SearchCourseOrders refuses an undeclared domain before it can be ordered.");

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
        // A pot is tile work like a vein or a torch site: the hand acts on a tile, so the request names it
        // and the positioner applies the same tool-reach proof. A drop is not, because pickup is by contact.
        if (binding.Opportunity.Purpose is not ("mine" or "chop" or "light" or OpportunityPurposes.BreakPot))
            return PositionRequest.ExactAt(pose);
        Point work = WorkTileOf(binding);
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
    internal static Point WorkTileOf(StepBinding step)
    {
        // A gathering step's hand strikes the tile its use names, and that can differ from the tile the
        // opportunity's identity names. The ore census keys a vein by its first sorted tile but binds the
        // use at the first tile with a proven approach, so on a vein whose first sorted tile is sealed the
        // identity names a tile the pickaxe never touches (measured by lane B on 23 September 2026: use
        // 24,59 against identity ore:7:23,59). The position request's tool-reach proof and the effect
        // audit both have to aim where the hand does, so a gathering step is read through its use.
        if (step.Opportunity.Domain is "mine-target" or "chop-target"
            && Activities.Gathering.GatheringOpportunityBinder.TryReadUse(step, step.Opportunity.Domain, out Point used, out _))
            return used;
        return WorkTileOf(step.Opportunity);
    }

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
