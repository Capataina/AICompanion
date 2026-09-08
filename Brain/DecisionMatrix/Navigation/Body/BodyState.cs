#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// The stateful part of what the body can do, carried on a body state and on a planner node
/// alike so a move whose availability depends on what came before it (a second jump in the air,
/// a dash on cooldown, a wall the body is holding) can be planned without the search being
/// rewritten: two nodes on the same tile with different counters are two nodes. Every counter
/// is zero today, because the body has one jump, no dash and no latch, and the mastery tree
/// fills them in later; the search keys on the whole record so that the day one is non-zero the
/// planner already tells the states apart.
/// </summary>
public readonly record struct MobilityState(int AirJumpsLeft = 0, bool Latched = false, int DashCooldown = 0);

/// <summary>
/// The body at one tick, in the terms the motion rule and the follower both read: where it is
/// (its left edge and the pixel row its feet rest on), how fast it moves, whether the ground
/// holds it, whether the last sideways move met a shape, and the counters of the moves it has
/// left. The motor builds one from the NPC every tick and the replay builds one from a pose,
/// which is what lets the navigator run against either without knowing which.
/// <see cref="Stuck"/> is the motion rule's own admission: the body is inside a shape it cannot
/// resolve, which a simulation reads as "this move does not exist" and a follower as a fault.
/// </summary>
public readonly record struct BodyState(float Left, float Bottom, float Vx, float Vy, bool OnGround, bool CollideX = false, bool Stuck = false, MobilityState Mobility = default)
{
    public float CentreX => Left + BodyPhysics.Width / 2f;

    /// <summary>The bottom-centre point, which is what the navigator measures distances from.</summary>
    public Vector2 Feet => new(CentreX, Bottom);

    /// <summary>The tile the feet are in: the column of the centre, the row of the last pixel above the bottom.</summary>
    public Point FeetTile => new((int)MathF.Floor(CentreX / 16f), BodyPhysics.FeetRow(Bottom));

    public BodyPhysics.Pose Pose => new(Left, Bottom);

    /// <summary>A body standing still in a pose, the state every simulated move starts from.</summary>
    public static BodyState Standing(BodyPhysics.Pose pose) => new(pose.Left, pose.Bottom, 0f, 0f, true);
}
