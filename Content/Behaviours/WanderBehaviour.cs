#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Content.Behaviours;

/// <summary>
/// Idle movement that reads like a person: stand for a while, stroll one way,
/// occasionally hop, and never drift past the leash, which is most of the visible
/// screen so the companion can be across the screen from the player and still be
/// its own presence. Runs only when nothing else wants the companion.
/// </summary>
public class WanderBehaviour
{
    /// <summary>
    /// Furthest the companion strolls from the player, in world pixels: a little under
    /// half the visible width at the current zoom, so it stays on screen.
    /// </summary>
    public static float Leash => Main.screenWidth / Main.GameViewMatrix.Zoom.X * 0.42f;

    private enum Mode { Standing, Walking }

    private Mode mode = Mode.Standing;
    private int ticksLeft = 60;
    private int direction = 1;
    private float speed = 1.5f;
    private bool hopQueued;

    /// <summary>Returns the horizontal speed the companion should move at this tick (0 to stand) and whether to hop.</summary>
    public (float speedX, bool hop) Update(NPC npc, Player player)
    {
        if (--ticksLeft <= 0)
            PickNext(npc, player);

        bool hop = hopQueued;
        hopQueued = false;

        if (mode == Mode.Standing)
            return (0f, hop);

        // Turn back if the stroll would leave the leash.
        float offset = npc.Center.X - player.Center.X;
        if (System.MathF.Abs(offset) > Leash && System.MathF.Sign(offset) == direction)
            direction = -direction;

        return (direction * speed, hop);
    }

    private void PickNext(NPC npc, Player player)
    {
        if (mode == Mode.Walking || Main.rand.NextBool(3))
        {
            mode = Mode.Standing;
            ticksLeft = Main.rand.Next(60, 240);
            hopQueued = Main.rand.NextBool(20);
            return;
        }

        mode = Mode.Walking;
        ticksLeft = Main.rand.Next(60, 240);
        speed = Main.rand.NextFloat(1.2f, 2.6f);
        float offset = npc.Center.X - player.Center.X;
        direction = System.MathF.Abs(offset) > Leash * 0.7f ? -System.MathF.Sign(offset) : (Main.rand.NextBool() ? 1 : -1);
        if (direction == 0) direction = 1;
        hopQueued = Main.rand.NextBool(12);
    }
}
