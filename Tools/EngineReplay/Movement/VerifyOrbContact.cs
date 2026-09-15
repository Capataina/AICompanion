extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Tools.Ledger;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using Contact = live::AICompanion.Companion.Brain.Infrastructure.Movement.CircleContact;
using World = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;

/// <summary>
/// The orb's body, proved on the live motor and the engine's own advance: the size rule — it fits
/// every two-by-two gap in every direction and no one-by-one gap in any — the one-tile diagonal
/// step the circle was chosen for, and the contact itself: push-out, the velocity into a wall
/// killed, the slide along it kept, and a body wedged inside a tile ejected along the least
/// penetration. Every travelling row also asserts that no tick ever left the circle overlapping
/// a solid tile, because a body that passes a gap by tunnelling has not passed it.
/// </summary>
internal static class VerifyOrbContact
{
    private const float R = Contact.Radius;

    public static int SizeRule()
    {
        var companion = Scene();
        // A two-tall corridor is passable end to end.
        Carve(10, 50, 20, 21);
        float minimum = Drive(companion, Corner(11, 21), Corner(48, 21), 900, out int ticks);
        Require(ticks < 900, $"the two-tall corridor must be crossed; after 900 ticks the body sat at {companion.NPC.Center}");
        EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "Movement", "orb-two-tall-corridor-ticks", ticks, "ticks", "down");
        Require(minimum >= -0.01f, $"crossing the two-tall corridor overlapped a wall by {-minimum:0.00}px");

        // A one-tall corridor off a two-tall antechamber is a wall: the body never enters it.
        Fill();
        Carve(10, 14, 20, 21);
        Carve(15, 50, 20, 20);
        Drive(companion, Corner(12, 21), new Vector2(48 * 16f, 20 * 16f + 8f), 400, out _);
        // The circle may lean a few pixels into a mouth it cannot enter, diagonally past the
        // corner tiles, so the bound is the mouth's own column: the centre never reaches it.
        Require(companion.NPC.Center.X < 15 * 16f,
            $"the one-tall corridor must refuse the body; its centre reached x={companion.NPC.Center.X:0.0}, inside the corridor's first column at {15 * 16f:0}");

        // A two-wide shaft is passable top to bottom.
        Fill();
        Carve(20, 21, 10, 45);
        minimum = Drive(companion, Corner(21, 12), Corner(21, 43), 900, out ticks);
        Require(ticks < 900, $"the two-wide shaft must be descended; after 900 ticks the body sat at {companion.NPC.Center}");
        Require(minimum >= -0.01f, $"descending the two-wide shaft overlapped a wall by {-minimum:0.00}px");

        // A one-wide shaft under a two-by-two antechamber is a wall.
        Fill();
        Carve(30, 31, 10, 11);
        Carve(30, 30, 12, 45);
        Drive(companion, Corner(31, 11), new Vector2(30 * 16f + 8f, 43 * 16f), 400, out _);
        Require(companion.NPC.Center.Y < 12 * 16f + R,
            $"the one-wide shaft must refuse the body; its centre reached y={companion.NPC.Center.Y:0.0}, a radius into the shaft's first row at {12 * 16f:0}");

        // A one-by-one diagonal staircase off a two-by-two antechamber is a wall in the diagonal too.
        Fill();
        Carve(8, 9, 28, 29);
        for (int s = 0; s <= 12; s++) Carve(10 + s, 10 + s, 30 + s, 30 + s);
        Drive(companion, Corner(9, 29), Corner(23, 43), 400, out _);
        Require(companion.NPC.Center.X < 10 * 16f && companion.NPC.Center.Y < 30 * 16f,
            $"the one-by-one diagonal must refuse the body; its centre reached {companion.NPC.Center}, inside the staircase's first tile at {10 * 16f:0},{30 * 16f:0}");
        return 0;
    }

    public static int DiagonalStep()
    {
        var companion = Scene();
        // A staircase of two-by-two openings each offset one tile down and right. The squeeze at
        // every step is between two solid tiles whose nearest corners are a tile's diagonal apart,
        // 22.6 pixels, so a twenty-pixel circle passes with 2.6 to spare and a twenty-pixel box's
        // corner catches; this is the geometry the circle was chosen for.
        for (int s = 0; s <= 12; s++) Carve(10 + s, 11 + s, 10 + s, 11 + s);
        float minimum = Drive(companion, Corner(11, 11), Corner(23, 23), 1500, out int ticks);
        Require(ticks < 1500, $"the diagonal staircase must be descended; after 1500 ticks the body sat at {companion.NPC.Center}");
        Require(minimum >= -0.01f, $"descending the diagonal staircase overlapped a wall by {-minimum:0.00}px");
        EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "Movement", "orb-diagonal-step-ticks", ticks, "ticks", "down");
        EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "Movement", "orb-diagonal-step-minimum-clearance", minimum, "px", "up",
            message: "the least clearance the body kept through the squeezes; zero is touching, negative would be tunnelling");
        return 0;
    }

    public static int PushOutAndSlide()
    {
        var companion = Scene();
        var world = World.World;
        // A one-tile-thick wall at column 40 with free space either side, and a floor at row 45.
        Fill();
        Carve(10, 39, 10, 44);
        Carve(41, 60, 10, 44);

        // Push-out: a body overlapping the wall's left face by four pixels, moving into it and
        // down, is pushed out to touching, loses the velocity into the wall and keeps the slide.
        Vector2 centre = new(40 * 16f - 6f, 30 * 16f), velocity = new(3f, 2f);
        var result = Contact.Resolve(world, ref centre, ref velocity);
        Require(result.Touched && !result.Wedged, $"the overlapping body must be reported touched and not wedged: {result}");
        Require(MathF.Abs(centre.X - (40 * 16f - R)) < 0.01f && centre.Y == 30 * 16f, $"push-out must land the centre exactly a radius off the face; got {centre}");
        Require(velocity.X == 0f && velocity.Y == 2f, $"the velocity into the wall must die and the slide survive; got {velocity}");
        Require(result.Normal == new Vector2(-1f, 0f), $"the normal must point away from the face; got {result.Normal}");

        // Wedged: a centre inside the wall tile is ejected along the least penetration and ends clear.
        centre = new Vector2(40 * 16f + 8f, 30 * 16f); velocity = Vector2.Zero;
        result = Contact.Resolve(world, ref centre, ref velocity);
        Require(!Contact.Overlaps(world, centre), $"a body inside a tile must be ejected clear; it ended at {centre} overlapping");
        Require(!result.Wedged, "a one-tile wall with free space on both sides is not a wedge");

        // Through the motor and the engine's advance: steering into the wall ends touching it, never inside it.
        companion.NPC.Center = new Vector2(30 * 16f, 30 * 16f);
        float minimum = Drive(companion, companion.NPC.Center, new Vector2(60 * 16f, 30 * 16f), 120, out _);
        Require(minimum >= -0.01f, $"steering into a wall overlapped it by {-minimum:0.00}px on some tick");
        Require(MathF.Abs(companion.NPC.Center.X - (40 * 16f - R)) < 0.05f, $"the body must come to rest touching the wall; centre {companion.NPC.Center}");
        Require(companion.Motor.TouchedWall && companion.Motor.State.Velocity.X == 0f && companion.Motor.DesiredVelocity.X > 0f,
            $"at the wall the motor must report the touch, a dead velocity into it and a live desire toward it: touched={companion.Motor.TouchedWall} momentum={companion.Motor.State.Velocity} desired={companion.Motor.DesiredVelocity}");

        // Slide: steering diagonally into the floor keeps the horizontal motion while the floor holds the vertical.
        companion.NPC.Center = new Vector2(15 * 16f, 44 * 16f);
        float startX = companion.NPC.Center.X;
        minimum = Drive(companion, companion.NPC.Center, new Vector2(35 * 16f, 60 * 16f), 120, out _);
        Require(minimum >= -0.01f, $"sliding along the floor overlapped it by {-minimum:0.00}px on some tick");
        Require(companion.NPC.Center.X > startX + 64f, $"the slide must carry the body along the floor; it moved from x={startX:0} to {companion.NPC.Center.X:0}");
        Require(MathF.Abs(companion.NPC.Center.Y - (45 * 16f - R)) < 0.05f, $"the floor must hold the body a radius above it; centre {companion.NPC.Center}");
        return 0;
    }

    /// <summary>Steer the live motor toward a target every tick, advancing the body the way the engine
    /// does, and return the least clearance seen; <paramref name="ticks"/> becomes how many ticks the
    /// arrival took, or the budget when it never arrived.</summary>
    private static float Drive(CompanionNPC companion, Vector2 start, Vector2 target, int budget, out int ticks)
    {
        companion.NPC.Center = start;
        companion.NPC.velocity = Vector2.Zero;
        float speed = companion.Motor.LiveMaxSpeed;
        float minimum = float.PositiveInfinity;
        for (ticks = 0; ticks < budget; ticks++)
        {
            Vector2 toTarget = target - companion.NPC.Center;
            if (toTarget.Length() <= 4f) return minimum;
            companion.Motor.Track();
            companion.Motor.Steer(Vector2.Normalize(toTarget) * speed, "fixture");
            VerifyResponsiveFollowing.AdvanceNative(companion);
            minimum = MathF.Min(minimum, Contact.Clearance(World.World, companion.NPC.Center));
        }
        return minimum;
    }

    private static Vector2 Corner(int x, int y) => new(x * 16f, y * 16f);

    private static CompanionNPC Scene()
    {
        var companion = VerifyCompanionLifecycle.Create();
        // The pace the motor reads is the player's; a headless player carries zeros until the game's
        // own reset runs, so the base figures are set by hand: run speed three, acceleration 0.08.
        Main.player[0].maxRunSpeed = Main.player[0].accRunSpeed = 3f;
        Main.player[0].runAcceleration = 0.08f;
        Fill();
        return companion;
    }

    /// <summary>Everything solid inside the scene's box; each case carves what it needs out of it.</summary>
    private static void Fill()
    {
        for (int x = 5; x <= 62; x++)
            for (int y = 5; y <= 47; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.ClearEverything();
                tile.HasTile = true;
                tile.TileType = 1;
            }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
    }

    private static void Carve(int x0, int x1, int y0, int y1)
    {
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                Main.tile[x, y].ClearEverything();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
