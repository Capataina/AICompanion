namespace AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>
/// Applies intent before gravity and collision. Both the simulation and the Terraria adapter
/// call this seam, which keeps a spent air jump or a rejected dash from becoming planner-only
/// state.
/// </summary>
public static class MovementAbilities
{
    public static BodyState ApplyControls(BodyState state, Controls controls, MovementCapabilities capabilities)
    {
        float vx = BodyPhysics.StepVelocity(state.Vx, controls.MoveX);
        float vy = state.Vy;
        bool grounded = state.OnGround;
        MobilityState mobility = state.Mobility;

        // Dash is deliberately not applied yet. The capability shape carries its cooldown so a
        // future mastery move can be simulated and executed through this one seam, but no dash
        // distance, duration or recovery has been designed; treating a walk-speed assignment as
        // a dash would make the planner claim an ability the companion does not have.

        if (controls.Jump)
        {
            if (grounded)
            {
                vy = BodyPhysics.JumpVelocity * controls.JumpScale;
                grounded = false;
                mobility = mobility with { AirJumpsLeft = capabilities.AirJumpCount };
            }
            else if (mobility.AirJumpsLeft > 0)
            {
                vy = BodyPhysics.JumpVelocity * controls.JumpScale;
                mobility = mobility with { AirJumpsLeft = mobility.AirJumpsLeft - 1 };
            }
        }

        return state with { Vx = vx, Vy = vy, OnGround = grounded, Mobility = mobility, Capabilities = capabilities };
    }
}
