#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.SharedMovementSystem;

/// <summary>
/// A move toward a point from the current state alone, with no plan and no memory beyond a
/// reversal timer: the complete reactive layer every vanilla walker has and this project did not.
///
/// Why it exists is architectural rather than a fix for one defect. Vanilla has no route planner
/// at all — a zombie cannot reach a rooftop and is never *frozen*, because every tick, from
/// whatever state it is in, its AI produces a move. This project has the opposite shape: a real
/// planner over a floor that only jumped at a wall, so when a step's traversal could not be
/// performed there was nothing that made progress, and the body stood. The answer is not a better
/// planner; it is a floor good enough that a failed plan degrades to a zombie rather than to a
/// statue, with the planner on top for the routes a zombie cannot do — the long way round, the
/// shaft, the player's platforms.
///
/// The rule is the fighter AI's, read from the decompile (<c>AI_003_Fighters</c>) and kept in its
/// own shape rather than reinvented: a one-column feeler a fixed distance ahead, sampled at four
/// rows, mapping the height of what it finds to a jump; and a gap leap when there is no floor
/// ahead and none diagonally ahead either. Vanilla's obstacle test is a full solid block that is
/// neither a platform nor a top slope, which is <see cref="NavGrid.IsSolid"/> here, so a slope is
/// walked rather than jumped at exactly as it is in the game.
///
/// Two deliberate departures. Vanilla's gap leap is gated on <c>directionY &lt; 0</c>, upward
/// intent, and the equivalent here is the target not being below the body, because leaping a gap
/// toward something underneath you is how a body ends up in the pit rather than past it. And the
/// reversal has no vanilla counterpart: a fighter that cannot make progress simply keeps pressing,
/// which is acceptable for a zombie and not for a companion, so a floor that has got nowhere for a
/// while tries the other direction for a stretch. That is the one piece of state here, and it is
/// what makes "always produces a move" mean "always produces a move that might work".
///
/// It runs where there is no path, or the path is finished, and never underneath a live step: a
/// step that faults is already priced, struck and replanned, and a second owner of the same tick
/// steering somewhere else would fight that and can misland into a fresh fault. One owner per
/// tick is the rule; this is the owner when the planner has nothing to say.
/// </summary>
public sealed class ReactiveWalk
{
    /// <summary>How far ahead of the body's centre the feeler column sits; the fighter's own width/2 + 16.</summary>
    private static readonly float FeelerAhead = BodyPhysics.Width / 2f + 16f;

    /// <summary>Close enough to the target that asking for speed would only overshoot it.</summary>
    private const float ArriveSlack = 4f;

    /// <summary>How long the body may make no headway toward the target before the floor tries the other way.</summary>
    private const int StalledTicksToReverse = 45;

    /// <summary>How long a reversal runs once started; long enough to clear whatever the direct line was caught on.</summary>
    private const int ReverseTicks = 40;

    /// <summary>How much closer the body must get to count as headway, so a body jittering in place is stalled.</summary>
    private const float HeadwayPx = 12f;

    /// <summary>The speed multiplier a gap leap carries, the fighter's own 1.5, so the arc reaches across.</summary>
    private const float LeapBoost = 1.5f;

    private int stalled;
    private int reversing;
    private float bestDistance = float.MaxValue;
    private int lastDir = 1;

    /// <summary>Forget the stall: a fresh path, an arrival, or a new session starts the floor clean.</summary>
    public void Reset()
    {
        stalled = 0;
        reversing = 0;
        bestDistance = float.MaxValue;
    }

    /// <summary>The controls that move the body toward <paramref name="target"/> from where it is now.</summary>
    public Controls Toward(BodyState live, Vector2 target)
    {
        float dx = target.X - live.CentreX;
        float distance = MathF.Abs(dx);
        Track(live, target);
        if (distance < ArriveSlack && reversing == 0)
            return Controls.None;

        int toward = MathF.Sign(dx) == 0 ? lastDir : MathF.Sign(dx);
        int dir = reversing > 0 ? -toward : toward;
        lastDir = dir;

        if (!live.OnGround)
            return new Controls(dir * BodyPhysics.WalkSpeed);

        // The obstacle ladder: how high what is ahead reaches decides the jump, so a kerb is
        // stepped by the motor, a two-tile rise is hopped and a four-tile wall gets the full jump.
        // Read outward from the tallest, because a wall that fills rows -2 and -3 also fills -1 and
        // the tallest match is the one that has to clear.
        Point feet = live.FeetTile;
        int column = (int)MathF.Floor((live.CentreX + dir * FeelerAhead) / 16f);
        int? rise = null;
        if (NavGrid.IsSolid(column, feet.Y - 2) && NavGrid.IsSolid(column, feet.Y - 3))
            rise = 4;
        else if (NavGrid.IsSolid(column, feet.Y - 2))
            rise = 3;
        else if (NavGrid.IsSolid(column, feet.Y - 1))
            rise = 2;
        else if (NavGrid.IsSolid(column, feet.Y))
            rise = 2;
        if (rise is int tiles)
            return new Controls(dir * BodyPhysics.WalkSpeed, Jump: true, BodyPhysics.JumpScaleForTiles(tiles));

        // The gap leap: nothing to stand on ahead and nothing one row down either, so walking on
        // is walking off. Only while the target is not below the body — a gap on the way down is a
        // route rather than an obstacle, and leaping it is how a body clears the ledge it meant to
        // descend from and lands in whatever is past it.
        bool targetBelow = target.Y > live.Bottom + 8f;
        bool noFloorAhead = !NavGrid.IsSupport(column, feet.Y + 1) && !NavGrid.IsSupport(column, feet.Y + 2);
        if (!targetBelow && noFloorAhead)
            return new Controls(dir * BodyPhysics.WalkSpeed * LeapBoost, Jump: true);

        return new Controls(dir * BodyPhysics.WalkSpeed);
    }

    /// <summary>
    /// Whether the body is getting anywhere, measured as the closest it has ever been to the
    /// target rather than as movement: a body walking a circle moves every tick and arrives never,
    /// which is exactly the failure a displacement test cannot see.
    /// </summary>
    private void Track(BodyState live, Vector2 target)
    {
        if (reversing > 0)
        {
            reversing--;
            if (reversing == 0)
            {
                stalled = 0;
                bestDistance = float.MaxValue;
            }
            return;
        }
        float distance = Vector2.Distance(live.Feet, target);
        if (distance < bestDistance - HeadwayPx)
        {
            bestDistance = distance;
            stalled = 0;
            return;
        }
        if (++stalled >= StalledTicksToReverse)
            reversing = ReverseTicks;
    }
}
