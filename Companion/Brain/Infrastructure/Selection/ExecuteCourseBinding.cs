#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

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
/// Nothing calls this yet: `CoordinateBrainTick` still runs `Chooser.Choose`. It is the third of the
/// three pieces the tick switch needs, landed and checkable on its own.
/// </summary>
public static class ExecuteCourseBinding
{
    /// <summary>
    /// The activity that performs a bound purpose, by the name it registers under.
    ///
    /// The mapping is explicit rather than derived from the domain string because the two vocabularies
    /// genuinely differ and each difference is a decision: combat's opportunities are minted per *use*
    /// with the purpose "fire" while the activity is "combat", and lighting's activity is
    /// "place-torches" while its domain is "light-target". A silent fallback here would route unknown
    /// work to whichever activity sorted first, so an unmapped purpose throws instead — a new domain
    /// must name its executor rather than inherit one.
    /// </summary>
    public static string ActivityFor(string purpose) => purpose switch
    {
        "fire" => "combat",
        "mine" => "mine",
        "chop" => "chop",
        "collect" => "collect",
        "light" => "place-torches",
        // A pot is broken in passing by whatever activity is already travelling, through the shared
        // nearby-work adapter, so it names no activity of its own. The course may still bind it as a
        // purpose; the caller treats a null executor as "no activity change", not as a refusal.
        "break-pot" => "",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose,
            "A bound purpose must name the activity that performs it."),
    };

    /// <summary>
    /// Where the body is asked to be for this binding.
    ///
    /// A firing stand is a <see cref="RequestKind.FireFrom"/> because the combat stance's admission and
    /// its rock fallback live behind that kind, and asking for the same point as an Exact hover would
    /// route around both. Everything else is the point the binding was priced from, carried as Exact,
    /// with tile work naming its work tile so the tool-reach proof the positioner applies is the one
    /// that admitted the pose in the first place.
    /// </summary>
    public static PositionRequest RequestFor(StepBinding binding)
    {
        var pose = new Vector2((float)binding.Pose.X, (float)binding.Pose.Y);
        if (binding.Opportunity.Purpose == "fire")
            return new PositionRequest(RequestKind.FireFrom, pose);
        return binding.Opportunity.Purpose is "mine" or "chop" or "light" or "break-pot"
            ? PositionRequest.ExactAt(pose, new Point((int)(binding.Pose.X / 16), (int)(binding.Pose.Y / 16)))
            : PositionRequest.ExactAt(pose);
    }

    /// <summary>
    /// What an empty course asks for. The plan makes an empty order *be* companionship with its own
    /// projected costs rather than an artificial idle, so the body keeps the player company rather than
    /// holding still — a Hold here would make "the course found nothing worth doing" look identical to
    /// "the course told the body to freeze", which is the stillness the orb rewrite exists to prevent.
    /// </summary>
    public static PositionRequest Companionship => new(RequestKind.WithPlayer, Vector2.Zero);
}
