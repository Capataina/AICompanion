#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Brain.DecisionMatrix.Navigation;

namespace AICompanion.Companion;

/// <summary>
/// Turns intentions into NPC velocity. The only place the companion's physics
/// constants live, so a change to how it moves is one edit. It knows nothing about
/// why it is moving.
///
/// Horizontal movement has the player's own shape: speed builds by a fixed amount per
/// tick up to the walk speed, and bleeds by a larger fixed amount when stopping or
/// reversing, so a turn costs the ticks it costs a player and cannot happen inside one
/// jump. The first version lerped toward the target speed, which reached full speed in
/// a handful of ticks and reversed in the air in the same handful; that is the
/// "no momentum, turns instantly mid-air" the first playtest saw.
/// </summary>
public sealed class CompanionMotor
{
    // The numbers live in the navigation core's BodyPhysics so the planner's simulated jumps,
    // the reflex simulation and the replay tool move the body exactly as this does.
    public const float WalkSpeed = BodyPhysics.WalkSpeed;
    public const float JumpVelocity = BodyPhysics.JumpVelocity;
    public const float Acceleration = BodyPhysics.Acceleration;
    public const float Slowdown = BodyPhysics.Slowdown;

    private readonly NPC npc;

    public CompanionMotor(NPC npc) => this.npc = npc;

    public bool OnGround => npc.velocity.Y == 0f;

    /// <summary>
    /// Set by the navigator for the tick it wants the body to fall through the platform it
    /// stands on; read by the NPC's fall-through hook and cleared every tick.
    /// </summary>
    public bool WantsFallThrough { get; set; }

    /// <summary>
    /// The move in hand is going down, so <see cref="ApplySteps"/> must not lift the body onto a
    /// platform at its knee. Set by <see cref="Apply"/> and consumed by <see cref="ApplySteps"/>,
    /// which gives it a one-tick life: anything that drives the body without going through the
    /// controls (a reflex's own step or jump) leaves it false, and false is the behaviour the
    /// motor always had.
    /// </summary>
    public bool Descending { get; private set; }

    /// <summary>
    /// Consecutive ticks the body has held a real velocity and not moved a pixel. That pair cannot
    /// both be true of a body the engine is integrating: <c>Collision_MoveWhileDry</c> does
    /// <c>position += velocity</c> unconditionally and only sets a collide flag when its tile
    /// collision changed the velocity it was handed, so a velocity nothing clipped beside a
    /// displacement of zero means something wrote the position back during the AI phase, where
    /// the engine's own displacement measure cannot see it.
    ///
    /// It is a separate signal from standing still on purpose, and it is why <c>OnGround</c> is
    /// left alone. Vanilla gates every jump on <c>velocity.Y == 0f</c> — the fighter's ladder, the
    /// town NPC's step block, all of it — so that test is the game's own path and replacing it
    /// with an invention to paper over a pin would trade a reused rule for a guess. The pin gets
    /// its own reading instead, and the navigator treats it as a body that cannot act.
    /// </summary>
    public int PinnedTicks { get; private set; }

    /// <summary>
    /// How far the offline body's rule and the engine disagreed about the last tick, in pixels:
    /// <see cref="BodyMotion.Step"/> run on last tick's state with last tick's controls, against
    /// where the engine actually left the body. Zero is the two agreeing.
    ///
    /// This is the divergence check for the boundary that has cost the most: every move the
    /// planner offers is proven with <c>BodyMotion</c> and performed by this motor, so the two
    /// being different rules is the failure mode that produces a body standing still holding a
    /// valid path. It is measured live, on the real world, every tick, because the alternative —
    /// replaying a recording offline — only ever tests the tiles the recording happened to cover.
    /// </summary>
    public float Divergence { get; private set; }

    private BodyState? previous;
    private Controls previousControls;

    /// <summary>The body as the navigator reads it, built from the NPC every tick; the seam that lets the replay run the same navigator over a simulated body.</summary>
    public BodyState State => new(npc.position.X, npc.Bottom.Y, npc.velocity.X, npc.velocity.Y, OnGround, npc.collideX, Stuck: false, Mobility: default, Pinned: PinnedTicks >= PinnedTicksToBelieve);

    /// <summary>
    /// How many consecutive pinned ticks before the follower acts on it. A single tick is
    /// ordinary: the engine clips a move to zero against a wall on the tick it is met, and a body
    /// resting on a slope's diagonal can read a hair of velocity with no displacement. A quarter
    /// of a second of it is a body that is being held.
    /// </summary>
    private const int PinnedTicksToBelieve = 15;

    /// <summary>Drive the NPC with one tick's controls: the speed to build toward, the jump, the platform to pass, whether the move is going down.</summary>
    public void Apply(Controls controls)
    {
        MoveX(controls.MoveX);
        if (controls.Jump)
            Jump(controls.JumpScale);
        WantsFallThrough = controls.FallThrough;
        Descending = controls.Descend;
        previousControls = controls;
    }

    /// <summary>
    /// Read what the engine did with the last tick before the brain acts on this one: whether the
    /// body is pinned, and how far the offline motion rule's prediction missed. Called once per
    /// tick from the NPC's AI, ahead of everything that reads either number.
    /// </summary>
    public void Track()
    {
        Vector2 moved = npc.position - npc.oldPosition;
        bool wantsToMove = npc.velocity.LengthSquared() > PinnedSpeed * PinnedSpeed;
        PinnedTicks = wantsToMove && moved.LengthSquared() < PinnedPixels * PinnedPixels ? PinnedTicks + 1 : 0;

        if (previous is BodyState was)
        {
            BodyState predicted = BodyMotion.Step(NavGrid.World, was, previousControls);
            Divergence = Vector2.Distance(new Vector2(predicted.Left, predicted.Bottom), new Vector2(npc.position.X, npc.Bottom.Y));
        }
        previous = State;
    }

    /// <summary>Slower than this and a body is settling rather than being held; half a pixel a tick.</summary>
    private const float PinnedSpeed = 0.5f;

    /// <summary>Closer than this to no displacement at all counts as none; the engine moves in floats.</summary>
    private const float PinnedPixels = 0.1f;

    /// <summary>Accelerate toward a horizontal speed; sign is direction, magnitude is pace.</summary>
    public void MoveX(float speedX)
    {
        npc.velocity.X = StepVelocity(npc.velocity.X, speedX);
        if (speedX != 0f)
            npc.direction = npc.spriteDirection = speedX > 0f ? 1 : -1;
    }

    public void Stop()
    {
        npc.velocity.X = StepVelocity(npc.velocity.X, 0f);
    }

    public void Face(float worldX)
    {
        npc.direction = npc.spriteDirection = worldX >= npc.Center.X ? 1 : -1;
    }

    /// <summary>
    /// One tick of horizontal physics: toward <paramref name="target"/> by the acceleration when
    /// the current speed is on the target's side, by the slowdown when it is against it or the
    /// target is zero, never overshooting. Shared with the reflex simulation so a simulated
    /// step-back moves exactly as the real one does.
    /// </summary>
    public static float StepVelocity(float v, float target) => BodyPhysics.StepVelocity(v, target);

    /// <summary>Jump if standing; returns whether it happened.</summary>
    public bool Jump(float scale = 1f)
    {
        if (!OnGround)
            return false;
        npc.velocity.Y = JumpVelocity * scale;
        return true;
    }

    /// <summary>
    /// Jump velocity for a rise of so many tiles, the heights the fighter AI uses (-6 for two
    /// tiles, -7 for three, -8 for four) and the full jump above that; one tile is a step, not a jump.
    /// </summary>
    public static float JumpScaleForTiles(int tiles) => BodyPhysics.JumpScaleForTiles(tiles);

    /// <summary>
    /// Walk one-tile steps and slopes the way every vanilla walker does: Collision.StepUp lifts
    /// the body over a one-tile rise ahead of it and StepDown keeps its feet on a one-tile fall,
    /// so neither ever registers as a wall. Custom-AI NPCs get none of this unless they call it,
    /// which is why the companion used to jump at every kerb. Called once per tick after the
    /// brain has set the velocity, in the same place the fighter AI calls it.
    ///
    /// StepUp's holdsMatching is the player's "holding up", and it is the only thing in the
    /// function that lets a body be lifted onto a platform: inside its flag4 the platform clause
    /// is the one guarded by it, while a solid block, a half block and a slope are accepted
    /// whatever it says. The companion needs it on to climb a platform staircase, which is the
    /// rule the planner's walk proof drives the body with, and a fighter passing false is why a
    /// zombie jumps at a platform instead of stepping onto it.
    ///
    /// It has to be off while the move in hand is going down, and hard-coding it on is the shaft
    /// freeze of 2026-09-09. StepUp writes npc.position by reference and never touches
    /// velocity.Y, so during a descent it lifted the body back onto the platform it was passing,
    /// every tick, while gravity accumulated to the fall cap — and it did so inside the AI phase,
    /// where the engine's own displacement measure (position - oldPosition) is blind to it, which
    /// is why the record showed a body with a large velocity, no collision and no movement. Every
    /// vanilla walker recomputes this permission per tick from its own vertical intent; the town
    /// NPC's flag18 is the same bit derived from whether it is above its home.
    ///
    /// StepDown keeps its original condition rather than becoming the mirror of StepUp the way
    /// vanilla pairs them, and the reason is that the offline body is the thing this has to agree
    /// with, not the town NPC: BodyMotion.Step runs its own step-down on every grounded tick that
    /// is not passing a platform, and the walk edges the planner offers are proven with it
    /// enabled — a slope or a short ledge lowering the feet a row is a walk and not a drop. Making
    /// it fire only while descending would refuse every one of those walks in the game while the
    /// planner kept proving them, which is the same class of disagreement in the other direction.
    /// It skips a fall-through for the reason the offline rule skips it: the platform underfoot is
    /// the thing being passed.
    /// </summary>
    public void ApplySteps()
    {
        bool descending = Descending;
        Descending = false;
        if (npc.velocity.Y == 0f && !WantsFallThrough)
            Collision.StepDown(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY);
        if (npc.velocity.Y >= 0f)
            Collision.StepUp(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY, 1, !descending, 1);
    }

    /// <summary>Vertical offset of a jump from standing after so many ticks (negative is up).</summary>
    public static float JumpOffsetAt(int ticks) => BodyPhysics.JumpOffsetAt(ticks);
}
