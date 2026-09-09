#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.SharedMovementSystem;

namespace AICompanion.Companion;

/// <summary>
/// Applies one resolved control packet to the NPC. Terraria owns integration and collision;
/// ability impulses come from the same rule used by planning. Prediction is captured before
/// helpers move the body and compared at the next AI entry, including every control source.
/// </summary>
public sealed class CompanionMotor
{
    public const float WalkSpeed = BodyPhysics.WalkSpeed;
    public const float JumpVelocity = BodyPhysics.JumpVelocity;
    public const float Acceleration = BodyPhysics.Acceleration;
    public const float Slowdown = BodyPhysics.Slowdown;
    private readonly NPC npc;
    private BodyState? expected;
    private int predictionTerrainRevision;
    private Vector2? previousPosition;
    private MobilityState mobility;
    private bool externalChange;
    private int predictedLife;

    public CompanionMotor(NPC npc) => this.npc = npc;
    public MovementCapabilities Capabilities { get; set; } = MovementCapabilities.Basic;
    public bool OnGround => npc.velocity.Y == 0f;
    public bool WantsFallThrough { get; set; }
    public bool Descending { get; private set; }
    public int PinnedTicks { get; private set; }
    public float Divergence { get; private set; }
    public bool DivergenceValid { get; private set; }
    public string DivergenceInvalidReason { get; private set; } = "no-prediction";
    public Controls AppliedControls { get; private set; }
    public BodyState ObservedState { get; private set; }
    public BodyState? PredictedState => expected;
    public string ControlSource { get; private set; } = "uninitialised";
    public BodyState State => new(npc.position.X, npc.Bottom.Y, npc.velocity.X,
        npc.velocity.Y, OnGround, npc.collideX, Mobility: mobility,
        Pinned: PinnedTicks >= 15, Wet: npc.wet, Capabilities: Capabilities,
        StairFall: npc.stairFall, LiquidKind: npc.shimmerWet ? 3 : npc.honeyWet ? 2 : npc.lavaWet ? 1 : 0);

    public void Apply(Controls controls, string source = "navigation")
    {
        BodyState before = State;
        AppliedControls = controls;
        ControlSource = source;
        // Capture against the world used by this decision. Re-reading it next tick could
        // silently compare a prediction with terrain modified after the decision.
        expected = BodyMotion.Step(MovementQueries.World, before, controls);
        externalChange = false;
        predictedLife = npc.life;
        predictionTerrainRevision = TerrainChanges.Revision;
        BodyState driven = MovementAbilities.ApplyControls(before, controls, Capabilities);
        npc.velocity = new Vector2(driven.Vx, driven.Vy);
        mobility = driven.Mobility;
        if (controls.MoveX != 0f)
            npc.direction = npc.spriteDirection = controls.MoveX > 0 ? 1 : -1;
        WantsFallThrough = controls.FallThrough;
        Descending = controls.Descend;
        ApplySteps();
    }

    public void Track()
    {
        Vector2 position = npc.position;
        bool stationary = previousPosition is Vector2 previous && Vector2.DistanceSquared(previous, position) < 0.01f;
        PinnedTicks = stationary && npc.velocity.LengthSquared() > 0.25f ? PinnedTicks + 1 : 0;
        previousPosition = position;
        DivergenceInvalidReason = !expected.HasValue ? "no-prediction"
            : externalChange || npc.life != predictedLife ? "external-hit-or-life-change"
            : predictionTerrainRevision != TerrainChanges.Revision ? "terrain-changed" : "none";
        DivergenceValid = DivergenceInvalidReason == "none";
        Divergence = expected is BodyState prediction
            ? Vector2.Distance(new Vector2(prediction.Left, prediction.Bottom), npc.Bottom - new Vector2(npc.width / 2f, 0))
            : 0f;
        expected = null;
        if (OnGround)
            mobility = mobility with { AirJumpsLeft = Capabilities.AirJumpCount };
        ObservedState = State;
    }

    public void Stop() => Apply(Controls.None, "idle");
    public void NotifyExternalHit() => externalChange = true;
    public void EnterDowned()
    {
        npc.velocity.X = 0f;
        externalChange = true;
        ControlSource = "downed";
    }
    public void Face(float worldX) => npc.direction = npc.spriteDirection = worldX >= npc.Center.X ? 1 : -1;
    public static float StepVelocity(float v, float target) => BodyPhysics.StepVelocity(v, target);
    public static float JumpScaleForTiles(int tiles) => BodyPhysics.JumpScaleForTiles(tiles);
    public static float JumpOffsetAt(int ticks) => BodyPhysics.JumpOffsetAt(ticks);

    /// <summary>The same pre-integration engine helpers used by the prediction backend.</summary>
    private void ApplySteps()
    {
        if (npc.velocity.Y == 0f && !WantsFallThrough)
            Collision.StepDown(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY);
        if (npc.velocity.Y >= 0f)
            Collision.StepUp(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY, 1, !Descending, 1);
    }
}
