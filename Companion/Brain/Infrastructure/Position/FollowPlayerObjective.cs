#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Infrastructure.Position;

/// <summary>
/// What counts as being with the player, read off the player's intent region rather than computed
/// here. This type is now a view over <see cref="PlayerIntentRegion"/> plus the two things a region
/// cannot know: which anchor this particular request is refining towards, and whether the body has
/// been grounded inside long enough for arrival to mean anything.
///
/// <para><paramref name="Anchor"/> is a preference and no longer a second acceptance box. It is the
/// request's own aim — a priced meeting place, the leading edge — and it decides which of the
/// acceptable tiles is preferred, never which tiles are acceptable. It used to widen the region by a
/// growth factor of its own, which made a far-off meeting place enlarge the arrival test it was
/// supposed to be aiming inside, so the body could satisfy following by standing anywhere along a
/// line to a place it had not reached.</para>
///
/// <para><paramref name="Settled"/> comes from the sense, because it is a streak and a struct rebuilt
/// nine times a tick cannot hold one. Requiring it is what stops a tick at pace reading as an
/// arrival. <paramref name="AtRest"/> comes from the same place and changes no decision: it exists
/// so <see cref="Reason"/> can say which kind of unsettled tick this is, since a body crossing the
/// region and a body resting out its first ticks of a streak are the same geometry and different
/// situations, and the recorder's column is read to tell one from the other.</para>
/// </summary>
public readonly record struct FollowPlayerObjective(PlayerIntentRegion Region, Vector2 Anchor, bool Settled, bool AtRest)
{
    /// <summary>The same objective aimed at a different place. Every consumer starts from the sense's
    /// own objective and refines it, so there is one region and one settled streak in the brain.</summary>
    public FollowPlayerObjective At(Vector2 anchor) => this with { Anchor = anchor };

    public Vector2 Centre => Region.Centre;
    public float HorizontalComfort => Region.HalfSize.X;
    public float VerticalComfort => Region.HalfSize.Y;

    /// <summary>The gap on each axis, measured to the region's centre rather than to the player's
    /// feet: the whole point of the region is that "how far from the player" is one question with
    /// one answer, and it is asked about where he is going.</summary>
    public float HorizontalGap(Vector2 feet) => MathF.Abs(feet.X - Region.Centre.X);
    public float VerticalGap(Vector2 feet) => MathF.Abs(feet.Y - Region.Centre.Y);

    /// <summary>How far outside the region this place is, zero inside. What a reunion pull is priced on.</summary>
    public float Pull(Vector2 feet) => Region.Pull(feet);

    /// <summary>How far beyond the region's edge this place is, in pixels, zero inside. An
    /// outside-the-region slope measures on this rather than on a distance to the player's body, so
    /// that it starts where the inside gradient finishes; the region's own paragraph carries why.</summary>
    public float GapBeyond(Vector2 feet) => Region.GapBeyond(feet);

    /// <summary>
    /// A hover destination is useful inside the region, less the navigator's settle radius. The
    /// reservation is not optional: a candidate on the boundary is legal while the body drifts around
    /// it, so a reservation of the arrival radius alone would let the hover carry the body out of the
    /// region on every orbit, and following would flip between satisfied and not at a destination it
    /// had reached.
    /// </summary>
    public bool AcceptsDestination(Vector2 centre, bool locallyConnected)
        => locallyConnected && Region.Accepts(centre, Movement.Navigator.SettleRadius);

    /// <summary>
    /// Arrival: the body is in the region, has been at rest in it for a rescore, and is locally
    /// connected to it. The rest streak is what the symmetric box was missing — two bodies passing
    /// each other at pace are momentarily a few pixels apart and neither has arrived anywhere.
    /// </summary>
    public bool IsSatisfied(Vector2 centre, bool locallyConnected)
        => Settled && Region.Contains(centre) && locallyConnected;

    public string Reason(Vector2 centre, bool locallyConnected)
        => IsSatisfied(centre, locallyConnected) ? "follow-objective-satisfied"
            : VerticalGap(centre) > VerticalComfort ? "follow-vertical-gap"
            : HorizontalGap(centre) > HorizontalComfort ? "follow-horizontal-gap"
            : !locallyConnected ? "follow-local-connection"
            // Inside the region, connected, and not satisfied: the streak is what is missing, and the
            // two ways to be missing it are not one fact. Moving is the capture's own case — a body
            // passing through at pace, which must never read as arrival — while at rest is a body
            // that has arrived and is resting out the rescore before anyone may act on it.
            : !AtRest ? "follow-moving-deferred"
            : "follow-settling";
}
