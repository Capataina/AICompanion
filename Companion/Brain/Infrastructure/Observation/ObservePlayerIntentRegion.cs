#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// Where the player is going, as a place rather than a point: a box carried ahead of his feet by
/// his own observed pace, which every consumer measures "how far from the player" against.
///
/// <para>The shape it replaces was a symmetric box on the player's current feet, and its defect was
/// not its size. It had no gradient: inside it the reunion pull was exactly zero, so a moving player
/// was never itself a reason to move, and the body coasted to whichever edge it happened to enter by
/// and stayed there while any rival offer won. No comfort size fixes that, because the quantity that
/// was missing is velocity and the box did not carry any.</para>
///
/// <para>Being one object also settles an argument five callers were having separately. The reunion
/// pull, the work radius around the player, the meeting place's fallback anchor, the lighting
/// region's search centre and the collect radius each read the player's position with a radius of
/// their own, so work "near the player" was work near where he was standing, behind him, while he
/// walked away from it. They now all measure to this, which is why it is a sense and not a helper
/// inside following.</para>
/// </summary>
public readonly record struct PlayerIntentRegion(Vector2 Centre, Vector2 HalfSize, Vector2 Lead, bool IsTravelling)
{
    /// <summary>Whether a body standing here counts as being in the region. Both axes, never a radius:
    /// a point on a nearby but different cave floor is not being with the player.</summary>
    public bool Contains(Vector2 feet) => Pull(feet) <= 1f;

    /// <summary>
    /// How strongly this place is pulled towards the region, as a continuous scalar: zero at the
    /// centre, one at the edge, and rising beyond it with no step anywhere. The normalised Chebyshev
    /// distance rather than a radial one, because the region's two axes are deliberately different
    /// sizes and a radial measure would call a body a comfortable width away and a body directly
    /// below on the next floor down the same number.
    /// </summary>
    public float Pull(Vector2 feet) => MathF.Max(
        MathF.Abs(feet.X - Centre.X) / MathF.Max(1f, HalfSize.X),
        MathF.Abs(feet.Y - Centre.Y) / MathF.Max(1f, HalfSize.Y));

    /// <summary>
    /// The point on the region's edge in the direction of travel: where a travelling player is
    /// followed toward, and the anchor reunion aims at when nothing has been priced. A still player
    /// has no direction, so the leading edge is the centre and following aims at him.
    /// </summary>
    public Vector2 LeadingEdge
    {
        get
        {
            if (Lead.LengthSquared() < 1f) return Centre;
            Vector2 unit = Vector2.Normalize(Lead);
            // The scale that puts the unit vector on the box boundary: the smaller of the two
            // axis crossings, so the point lies on the edge rather than beyond a corner.
            float scale = float.PositiveInfinity;
            if (MathF.Abs(unit.X) > 1e-4f) scale = MathF.Min(scale, HalfSize.X / MathF.Abs(unit.X));
            if (MathF.Abs(unit.Y) > 1e-4f) scale = MathF.Min(scale, HalfSize.Y / MathF.Abs(unit.Y));
            return float.IsInfinity(scale) ? Centre : Centre + unit * scale;
        }
    }

    /// <summary>
    /// Whether a destination here still satisfies the request once the body has stopped at it. The
    /// navigator accepts any grounded pose within its arrival radius, so that radius is reserved
    /// inside the region: a candidate on the boundary is valid while the body stops just outside it,
    /// which leaves an Arrived navigator and an unsatisfied objective for ever.
    /// </summary>
    public bool Accepts(Vector2 feet, float arrivalSlack)
        => MathF.Abs(feet.X - Centre.X) <= MathF.Max(0f, HalfSize.X - arrivalSlack)
            && MathF.Abs(feet.Y - Centre.Y) <= MathF.Max(0f, HalfSize.Y - arrivalSlack);

}

/// <summary>
/// Rebuilds the region once per brain tick, and owns the one piece of state a region cannot carry:
/// how long the body has been grounded inside it.
/// </summary>
public sealed class PlayerIntentRegionSense
{
    public PlayerIntentRegion Region { get; private set; }

    /// <summary>
    /// Whether the body has been on the ground inside the region for a whole rescore. Following
    /// reads this rather than geometry alone, because a body in the air is passing through: at tick
    /// 8127 of the 2026-09-14 capture both bodies were mid-jump and momentarily inside the box, the
    /// objective read satisfied, keeping company took that as arrival and issued a Hold, and the
    /// Hold cancelled the jump one tick after take-off. Entering costs a rescore; leaving is
    /// immediate, because a body that has left is gone now and not in a rescore's time.
    /// </summary>
    public bool Settled { get; private set; }

    /// <summary>How many consecutive ticks the body has been grounded inside the region. Exposed so
    /// the recorder can say why a tick that looks satisfied is not.</summary>
    public int GroundedInsideTicks { get; private set; }

    /// <summary>Whether the companion was on the ground on this tick. Published beside the streak
    /// rather than derived by each reader, because the streak already computes it and a second
    /// grounded test elsewhere is the disagreement the one expression below exists to prevent. It
    /// changes no decision; it is what lets the follow reason name an airborne tick apart from a
    /// grounded one that is still standing out its rescore.</summary>
    public bool Grounded { get; private set; }

    private Vector2 lead;

    /// <summary>The follow objective every consumer shares, anchored on the region's own centre.
    /// A caller with an anchor of its own — a priced meeting place, a request's anchor — refines it
    /// with <see cref="FollowPlayerObjective.At"/> rather than building a second objective.</summary>
    public Position.FollowPlayerObjective Objective => new(Region, Region.Centre, Settled, Grounded);

    public void Update(NPC companion, PlayerSense player)
    {
        // A live interference footprint deliberately does not suppress the lead, and that was
        // measured rather than assumed. Suppressing it was tried, on the hypothesis that a lead
        // carries the companion into the very tiles the player is asking it to vacate; the courtesy
        // contract stayed red with it in and went green without it, because what was actually
        // holding the body in the passage was keeping company waiting a rescore before it could
        // change method. With that wait exempted for an overlapped footprint, the suppression's only
        // measured effect was on the open-floor walk, where it made courtesy worse — 44 stationary
        // ticks in the player's way against 28 — since a region pulled back onto his feet is a region
        // that asks the companion to stand where he is walking. Courtesy is a positioning problem and
        // is answered in the positioner's occupancy share, not by blinding the region.
        Vector2 target = player.Intent * Weights.IntentRegionLeadTicks;
        // The same discontinuities that clear the intent history clear the lead: a death, a
        // teleport or an unobserved interval leaves a filtered lead pointing at where the player
        // was going before he stopped being there, and a companion sent towards a corpse.
        // InferPlayerActivity.Observe is the authority — it resets its sample count on exactly
        // those three — so the count reaching its first sample is the signal rather than a second
        // copy of the test.
        if (player.IsDead || player.Activity.Samples <= 1)
            lead = Vector2.Zero;
        else
            // One-pole low pass. It matters at the stop rather than the start: a player who halts
            // has his intent fall to nothing in one observation, and without this the region would
            // snap back onto his feet and drag the destination with it.
            lead += (target - lead) / MathF.Max(1f, Weights.IntentRegionFilterTicks);

        float comfortScale = PlayerIntegration.CompanionPreferences.Current.FollowComfortScale;
        float growth = 1f + Weights.IntentRegionGrowthCap
            * MathF.Min(1f, lead.Length() / MathF.Max(1f, Weights.IntentRegionFullGrowthLead));
        Vector2 half = new(Weights.FollowHorizontalComfort * comfortScale * growth,
            Weights.FollowVerticalComfort * comfortScale * growth);

        // The region may drift up to the edge of the player's own half-screen and no further, so
        // the companion is somewhere he can see rather than somewhere he has to catch up with.
        // Headless there is no screen, so the clamp falls back to a fixed neighbourhood; without
        // that floor a zero screen pins the region to his feet and the whole lead is lost in
        // every fixture. Main.screenWidth is in pixels at the game's own zoom.
        float clampX = MathF.Max(Main.screenWidth / 2f, Weights.IntentRegionMinimumClampX) - half.X;
        float clampY = MathF.Max(Main.screenHeight / 2f, Weights.IntentRegionMinimumClampY) - half.Y;
        Vector2 applied = new(Math.Clamp(lead.X, -MathF.Max(0f, clampX), MathF.Max(0f, clampX)),
            Math.Clamp(lead.Y, -MathF.Max(0f, clampY), MathF.Max(0f, clampY)));

        Region = new PlayerIntentRegion(player.Bottom + applied, half, applied, player.IsTravelling);

        // The companion's own grounded test, which is velocity.Y being exactly zero, and it is the
        // motor's: ApplyControlsToCompanion.OnGround is the same expression on the same body. A
        // second definition here would let the brain and the motor disagree about whether a body
        // is standing, which is the disagreement this rule exists to settle.
        bool grounded = companion.velocity.Y == 0f;
        bool inside = Region.Contains(companion.Bottom);
        Grounded = grounded;
        GroundedInsideTicks = grounded && inside ? GroundedInsideTicks + 1 : 0;
        Settled = GroundedInsideTicks >= Weights.PositionRescoreTicks;
    }
}
