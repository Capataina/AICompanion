#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// How the companion gets from one node to the next: walk (a step up counts as a walk),
/// jump, drop off an edge, or fall through the platform it stands on, which is a drop that
/// needs the body told to pass the platform for that tick.
/// </summary>
public enum MoveKind { Walk, Jump, Drop, FallThrough }

/// <summary>
/// One move of a path: the feet tile it arrives at, how, and for a jump the take-off the planner
/// proved the arc from, which the follower reproduces rather than re-deriving. <paramref name="From"/>
/// is the take-off tile and <paramref name="JumpScale"/> the share of the full jump velocity.
///
/// The next three describe <em>the state the arc was proven from</em>, and they are the whole of
/// what the performer has to reproduce. <paramref name="LaunchVx"/> is the horizontal velocity the
/// body carried on the tick the proof asked for the jump, signed; it is not the profile's nominal
/// speed and there is deliberately nowhere on a step to put that nominal, because a step carrying
/// a speed the proof did not use is a step the performer cannot reproduce — a one-tile runway
/// behind a take-off delivers about 1.7 px/tick against a 3.5 nominal, and every reader that
/// treated 3.5 as the entry (the commit test, the runway, the allowance, the walk before it)
/// meant a body that never existed. It doubles as the run-up's own throttle: the body's only
/// horizontal rule is <see cref="BodyPhysics.StepVelocity"/>, which accelerates toward the target
/// and clamps there, so running in at the proven speed climbs the identical ramp and then holds
/// it, which makes extra runway harmless instead of a faster take-off.
/// <paramref name="RunUpBack"/> is how far behind the take-off, in pixels, that run-up starts;
/// zero is a jump made where the body stands. <paramref name="LaunchAlong"/> and
/// <paramref name="LaunchRise"/> are where the proof's launch pose sat relative to the node's
/// pose — along the jump's direction and in pixels below it — because a run-in over a kerb or a
/// slope takes off from a different height than the tile it started on, and an arc compared
/// against the node's own bottom is a different arc. Both are offsets rather than absolutes so a
/// step stays readable against any pose the grid produces for that tile.
///
/// For a drop or a fall-through <paramref name="SteerX"/>
/// is the world X the body's centre falls along, because in a shaft wider than the body what it
/// lands on depends on which wall it hugs; zero when the move has no steer point.
/// <paramref name="Ticks"/> is how long the move took when it was proven (a jump's flight, a
/// fall's duration), which the follower's allowance and the executed-edge trace are read against.
/// <paramref name="FromRest"/> marks a walk that was proven from rest and lands elsewhere at the
/// walk speed (a ledge onto a narrow lip), so the step before it coasts to a stop the way it
/// does before a descent; a drop, a fall-through and a standing jump start from rest by kind.
/// </summary>
public readonly record struct NavStep(Point Tile, MoveKind Kind, Point From = default, float JumpScale = 1f, float LaunchVx = 0f, float SteerX = 0f, int Ticks = 0, bool FromRest = false, MobilityState Mobility = default, float RunUpBack = 0f, float LaunchAlong = 0f, float LaunchRise = 0f);

/// <summary>
/// What the search plans over: a feet tile and the body's mobility state on arriving there (air
/// jumps left, a latch, a dash cooldown), so two states on one tile are two nodes and a move
/// that spends or restores a counter plans as an ordinary edge. Every mobility is zero today,
/// so a node is its tile; the key exists so an air jump or a dash is a traversal and not a
/// rewrite of the search. The public seams stay tiles: a search is asked from a tile to a tile
/// and a flood returns tiles, because the brain, the positioner and the replay reason in tiles.
/// </summary>
public readonly record struct NavNode(Point Tile, MobilityState Mobility)
{
    public static NavNode At(Point tile) => new(tile, default);
}

/// <summary>
/// A planned route as feet tiles, first step first. Goal is the tile the search was asked
/// for; a partial path ends at the reachable tile nearest it instead, because a search that
/// ran out is still worth walking to the closest point it found.
/// </summary>
public sealed class NavPath
{
    public readonly List<NavStep> Steps;
    public readonly Point Goal;
    public readonly bool Partial;
    public int Index;

    public NavPath(List<NavStep> steps, Point goal, bool partial = false)
    {
        Steps = steps;
        Goal = goal;
        Partial = partial;
    }

    public bool Finished => Index >= Steps.Count;
    public NavStep Current => Steps[Index];
}
