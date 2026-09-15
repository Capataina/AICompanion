extern alias live;
#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using CircleContact = live::AICompanion.Companion.Brain.Infrastructure.Movement.CircleContact;

/// <summary>
/// Whether the whole brain gets the body out of water it is being hurt by, on two scenes taken from
/// play: the pool the walker drowned in at capture 23-50-24-14964, and a flooded low passage with a
/// wall at one end so the only way out is sideways.
///
/// <para>What went with the walker. This file used to hold two isolated searches beside the
/// full-brain scenes — a control-sequence search that chose jump-and-move inputs tick by tick, and a
/// native-control dump that printed where four ticks of each input landed. Both existed because the
/// walker's escape was a question about which sequence of jumps cleared an awning, and the orb has
/// no sequence to search: it asks for a velocity and the motor applies it. The head-dry test went
/// with them, because it was Terraria's own drowning rectangle over a forty-two-pixel body; the orb
/// has no head, and what hurts it is the circle touching water or lava at all, which the motor
/// already reports as <c>InHurtingLiquid</c>.</para>
///
/// <para>The awning is flooded to its ceiling here, where the walker's version left a dry row
/// between the water's surface and the roof. For a body that walks, that row was unreachable
/// without a jump the awning refused, so the scene was about sideways clearance; for a body that
/// flies, the same row is one tick upward and the scene would pass without ever going sideways. The
/// flood restores the scene's subject rather than preserving its tiles.</para>
///
/// <para>The pass line is deliberately sustained rather than instantaneous: a body that clips out of
/// the water for one tick on its way through has not escaped, so the dry reading has to hold for
/// sixty ticks with safety no longer claiming the body, and the companion has to be alive to the
/// end of it.</para>
/// </summary>
internal static class VerifyCapturedEscape
{
    /// <summary>How long the body must read dry before the escape counts, so a body crossing a
    /// surface on its way somewhere worse cannot satisfy it.</summary>
    private const int SustainedDryTicks = 60;

    public static int Run()
    {
        int failed = 0;
        // The whole-brain scenes lift the live tick's wall-clock planning allowances: under them, how far
        // each search got before its deadline decides the escape, so the verdict would follow machine load
        // rather than the brain. The default suite's own reset lifts them too; this keeps the standalone
        // --escape flag, which does not pass through that reset, running under the same regime.
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        try
        {
            failed += VerifyAwning(mirrored: false);
            failed += VerifyAwning(mirrored: true);
            failed += VerifyCapturedPool();
            failed += VerifyCapturedPool(emptyOffers: true);
        }
        finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false; }
        return failed;
    }

    /// <summary>
    /// The pool from the capture, unchanged terrain, with the body in the water and the player on the
    /// shore. The empty-offers variant clears every ordinary activity, so nothing but shared safety
    /// can move the body: an escape that only happens because following wanted to go that way anyway
    /// is not an escape the safety response produced.
    /// </summary>
    private static int VerifyCapturedPool(bool emptyOffers = false)
    {
        BuildCapturedPool();
        Replug();
        var companion = VerifyCompanionLifecycle.Create();
        Replug();
        if (emptyOffers) companion.Brain.Chooser.Actions.Clear();
        Main.player[0].dead = false;
        Main.player[0].Bottom = new Vector2(2024, 1376);
        // The captured NPC's own centre, translated by the fixture's five-tile origin.
        companion.NPC.Center = new Vector2(5 * 16f + (60540f - 3704 * 16f) + 10f, 5 * 16f + (9072f - 446 * 16f) - 10f);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.active = true;
        return RunEscape($"captured pool emptyOffers={emptyOffers}", companion, 900);
    }

    /// <summary>
    /// A flooded passage under a roof with a wall at one end: every free cell inside it is wet, so the
    /// body has to travel along the passage and out of its open end rather than rising out of the
    /// water where it stands.
    /// </summary>
    private static int VerifyAwning(bool mirrored)
    {
        BuildAwning(mirrored);
        Replug();
        var companion = VerifyCompanionLifecycle.Create();
        Replug();
        Main.player[0].dead = false;
        Main.player[0].position = new Vector2((mirrored ? 70 : 30) * 16, 70 * 16 - Main.player[0].height);
        // Well inside the flooded passage, a tile clear of the wall at its closed end.
        companion.NPC.Center = new Vector2((mirrored ? 46 : 53) * 16 + 8, 68 * 16 + 8);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.active = true;
        return RunEscape($"flooded awning mirrored={mirrored}", companion, 900);
    }

    /// <summary>
    /// Drive the whole brain until the body has read dry for <see cref="SustainedDryTicks"/> ticks
    /// running with safety no longer holding it, and assert the scene's own premise first, because a
    /// body that started dry would pass every row below without the escape ever running.
    /// </summary>
    private static int RunEscape(string name, live::AICompanion.Companion.CharacterBody.CompanionNPC companion, int limit)
    {
        companion.Motor.Track();
        // The premise is asked of the geometry rather than of the motor, because the motor only learns which
        // liquid it is touching inside an application — and driving one here to populate it would deal the
        // first tick of contact damage before the brain has run at all.
        var world = MovementQueries.World;
        bool startsWet = CircleContact.Touches(companion.NPC.Center,
            (x, y) => live::AICompanion.Companion.Brain.Infrastructure.Movement.OrbTerrain.WetWall(
                world, x, y, live::AICompanion.Companion.Brain.Infrastructure.Movement.LiquidImmunity.None));
        if (!startsWet)
        {
            AICompanion.Tools.Ledger.EmitLedgerRows.Detail(
                $"{name}: the scene must start the body in liquid that hurts it; centre={companion.NPC.Center}");
            return 1;
        }
        if (CircleContact.Overlaps(MovementQueries.World, companion.NPC.Center))
        {
            AICompanion.Tools.Ledger.EmitLedgerRows.Detail(
                $"{name}: the scene must start the body in free space, not inside terrain; centre={companion.NPC.Center}");
            return 1;
        }

        int dry = 0, startingLife = companion.NPC.life;
        for (int tick = 0; tick < limit; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
            VerifyResponsiveFollowing.AdvanceNative(companion);
            if (companion.IsDowned || companion.NPC.life <= 0)
            {
                AICompanion.Tools.Ledger.EmitLedgerRows.Detail(
                    $"{name}: the body died in the liquid at tick {tick}; centre={companion.NPC.Center} life={companion.NPC.life}");
                return 1;
            }
            dry = !companion.Motor.InHurtingLiquid && !companion.Brain.Safety.Active ? dry + 1 : 0;
            if (dry >= SustainedDryTicks)
            {
                Console.WriteLine($"{name}: sustained dry exit at tick {tick}, life {companion.NPC.life} of {startingLife}, "
                    + $"contact ticks {companion.Motor.LiquidContactTicks}");
                return 0;
            }
        }
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail(
            $"{name}: never held {SustainedDryTicks} dry ticks in {limit}; centre={companion.NPC.Center} liquid={companion.Motor.LiquidKind} "
            + $"life={companion.NPC.life} safety={companion.Brain.Safety.Active} stage={companion.Brain.Safety.Escape.EscapeStage} "
            + $"action={companion.Brain.LastAction?.Name} status={companion.Brain.Navigator.Status}");
        return 1;
    }

    /// <summary>A rebuilt tile map is a different world to every clearance chunk and retained search,
    /// and they compare it by reference, so the reference changes with it.</summary>
    private static void Replug()
    {
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
    }

    private static void BuildAwning(bool mirrored)
    {
        Main.maxTilesX = 100;
        Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        Main.tileSolid[1] = true;

        for (int x = 10; x < 90; x++)
            Solid(x, 70);
        // Flooded to the roof: every free cell between the roof at 62 and the floor at 70 is wet, so
        // there is no dry cell inside the passage for a body that flies to rise into.
        for (int x = 45; x <= 55; x++)
        for (int y = 63; y < 70; y++)
            Main.tile[x, y].LiquidAmount = byte.MaxValue;
        for (int x = 45; x <= 55; x++)
            Solid(x, 62);

        int wall = mirrored ? 44 : 56;
        for (int y = 62; y < 70; y++)
            Solid(wall, y);
    }

    internal static void BuildCapturedPool()
    {
        string[] lines = File.ReadAllLines(CapturePath());
        if (lines.Length != 204 || !lines[0].StartsWith("tick 14964 follow failure", StringComparison.Ordinal))
            throw new InvalidOperationException("captured-pool fixture has an unexpected header or dimensions");
        Main.maxTilesX = Main.maxTilesY = 212;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, null, new object[] { (ushort)212, (ushort)212 }, null)!;
        Main.tileSolid[1] = true;
        for (int y = 0; y < 201; y++)
        for (int x = 0; x < 202; x++)
            CapturedTile(5 + x, 5 + y, lines[3 + y][x]);
    }

    private static string CapturePath()
    {
        for (string? at = Directory.GetCurrentDirectory(); at != null; at = Directory.GetParent(at)?.FullName)
        {
            string path = Path.Combine(at, "Tools", "Scenarios", "captured-pool-23-50-24-14964.txt");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("captured-pool fixture", "Tools/Scenarios/captured-pool-23-50-24-14964.txt");
    }

    private static void CapturedTile(int x, int y, char glyph)
    {
        if (glyph == '~') { Main.tile[x, y].LiquidAmount = byte.MaxValue; return; }
        if (glyph is not ('#' or '/' or '\\' or '<' or '>' or '_')) return;
        Tile tile = Main.tile[x, y];
        tile.HasTile = true; tile.TileType = 1;
        tile.Slope = glyph switch { '\\' => (Terraria.ID.SlopeType)1, '/' => (Terraria.ID.SlopeType)2, '<' => (Terraria.ID.SlopeType)3, '>' => (Terraria.ID.SlopeType)4, _ => 0 };
        tile.IsHalfBlock = glyph == '_';
    }

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = true;
        tile.TileType = 1;
    }
}
