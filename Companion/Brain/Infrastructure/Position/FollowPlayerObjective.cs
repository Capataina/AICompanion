#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Infrastructure.Position;

/// <summary>
/// What counts as being with the player, read off the player's intent region rather than computed
/// here. This type is a view over <see cref="PlayerIntentRegion"/> plus the two things a region
/// cannot know: which anchor this particular request is refining towards, and whether the body was
/// inside the region on the tick before, which is what lets the edge have a width.
///
/// <para><paramref name="Anchor"/> is a preference and no longer a second acceptance box. It is the
/// request's own aim — a priced meeting place, the region's centre — and it decides which of the
/// acceptable tiles is preferred, never which tiles are acceptable. It used to widen the region by a
/// growth factor of its own, which made a far-off meeting place enlarge the arrival test it was
/// supposed to be aiming inside, so the body could satisfy following by standing anywhere along a
/// line to a place it had not reached.</para>
///
/// <para><paramref name="Inside"/> comes from the sense, because it is a latch and a struct rebuilt
/// nine times a tick cannot hold one. Being with the player is being inside his region, and nothing
/// else: the owner ruled on 15 September 2026 that the companion lives and moves about inside it, so
/// there is no arrival to wait out, no rest to stand still for and no follow spot to reach. What the
/// latch adds is width at the edge. A body that entered stays with the player until it is more than
/// the settle radius beyond the edge, so a body carried along the boundary by its own drift does not
/// leave and re-enter the region on alternate ticks. Losing sight of the player is not a condition:
/// a companion behind a pillar inside his region is with him.</para>
///
/// <para><paramref name="CutOff"/> also comes from the sense, and it is a fact about the body the sense saw rather than about
/// any point, which is why every caller asks <see cref="IsSatisfied"/> about the live body. The owner ruled on 15 September
/// 2026 that being with the player requires the companion can reach him: a body inside the box on the far side of a sealed
/// wall is outside, so rejoining sends it round. It is true only for a proof, and it defaults to false, which is the answer while
/// nothing proves the body cut off, so an objective built by hand from a region and a latch reads as it always did.</para>
/// </summary>
public readonly record struct FollowPlayerObjective(PlayerIntentRegion Region, Vector2 Anchor, bool Inside, bool CutOff = false)
{
    /// <summary>The same objective aimed at a different place. Every consumer starts from the sense's
    /// own objective and refines it, so there is one region and one inside latch in the brain.</summary>
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
    /// that it starts at zero where the region ends; the region's own paragraph carries why.</summary>
    public float GapBeyond(Vector2 feet) => Region.GapBeyond(feet);

    /// <summary>
    /// A destination outside the region is useful only inside it, less the navigator's settle radius. The
    /// reservation is not optional: a destination on the boundary is legal while the body drifts around it,
    /// so a reservation of nothing would let the drift carry the body out of the region on every orbit.
    /// </summary>
    public bool AcceptsDestination(Vector2 centre, bool locallyConnected)
        => locallyConnected && Region.Accepts(centre, Movement.Navigator.SettleRadius);

    /// <summary>
    /// Being with the player: a way to him inside his region, and inside the region, or still within the
    /// settle radius beyond its edge for a body that was inside on the tick before.
    /// </summary>
    public bool IsSatisfied(Vector2 centre)
        => !CutOff && (Region.Contains(centre) || (Inside && Region.GapBeyond(centre) <= Movement.Navigator.SettleRadius));

    public string Reason(Vector2 centre)
        => IsSatisfied(centre) ? "follow-objective-satisfied"
            : VerticalGap(centre) > VerticalComfort ? "follow-vertical-gap"
            : "follow-horizontal-gap";
}
