extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using KeepCompany = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using NavGrid = live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;

/// <summary>
/// A01, moving out of the player's way, through the whole brain on native tiles with only keeping company offered. Evidence of
/// interference comes from what the player is already doing: a solid block aimed at the tile the companion's body covers, or
/// walking down a one-body-tall passage the companion stands in. The same scenes with a weapon or a torch aimed at the
/// companion, or with the passage open above, are the negative cases. Every scene is deterministic with the planning allowances
/// lifted, so the negative cases compare whole trajectories tick for tick against an empty hand. The player is moved by the
/// fixture rather than by Terraria's player physics, so these scenes show the companion leaving the player's way; they do not
/// show that the companion's body would have blocked a real player.
/// </summary>
internal static class VerifyCourtesy
{
    private const int FloorRow = 80;
    /// <summary>Probe switch: prints the passage scene's decision chain every few ticks.</summary>
    private static readonly bool Trace = Environment.GetEnvironmentVariable("AIC_COURTESY_TRACE") == "1";

    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        int red = 0;
        int targetX = Player.tileTargetX, targetY = Player.tileTargetY, reachX = Player.tileRangeX, reachY = Player.tileRangeY;
        void Each(string name, Action fixture)
        {
            LimitPlanningWork.Unbounded = true;
            try { fixture(); Console.WriteLine($"GREEN courtesy {name}"); }
            catch (InvalidOperationException e) { red++; Console.WriteLine($"RED courtesy {name}: {e.Message}"); }
            finally
            {
                LimitPlanningWork.Unbounded = false;
                Player.tileTargetX = targetX; Player.tileTargetY = targetY;
                Player.tileRangeX = reachX; Player.tileRangeY = reachY;
            }
        }
        Each("a block aimed at the companion's tile moves it to another spot before the placement", ABlockAimedAtTheCompanionMovesIt);
        Each("a weapon or a torch aimed at the companion changes nothing", AWeaponOrTorchAimedAtTheCompanionChangesNothing);
        Each("a player walking a one-body-tall passage gets the passage back", APassageIsGivenBack);
        MeasureTheSameWalkOnOpenFloor();
        Console.WriteLine(red == 0
            ? "courtesy: placement moves the companion, a weapon or torch does not, and a passage is given back"
            : $"courtesy: {red} case(s) failed");
        return red;
    }

    // ── cases ────────────────────────────────────────────────────────────────────────────────

    private static void ABlockAimedAtTheCompanionMovesIt()
    {
        var rest = Placement(ItemID.None);
        Require(rest.EndCoversTarget, $"premise: with an empty hand the resting companion stays on its tile; {rest}");
        var block = Placement(ItemID.DirtBlock);
        Require(block.FirstClearTick >= 0 && !block.EndCoversTarget,
            $"a block aimed at the companion's tile must move it off that tile and keep it off; empty hand {rest}; block {block}");
        Require(block.EndDistanceToPlayer <= 6 * 16,
            $"moving aside must stay with the player rather than wander off; block {block}");
    }

    private static void AWeaponOrTorchAimedAtTheCompanionChangesNothing()
    {
        var rest = Placement(ItemID.None);
        var bow = Placement(ItemID.WoodenBow);
        var torch = Placement(ItemID.Torch);
        Require(bow.Trace.SequenceEqual(rest.Trace),
            $"a weapon aimed at the companion must change nothing, tick for tick; first difference at {FirstDifference(rest.Trace, bow.Trace)}");
        Require(torch.Trace.SequenceEqual(rest.Trace),
            $"a torch aimed at the companion must change nothing, tick for tick; first difference at {FirstDifference(rest.Trace, torch.Trace)}");
    }

    /// <summary>
    /// Giving a passage back is not a place: leading the player through it out of reach gives it back as surely as stepping
    /// behind. What must not happen is the companion standing still in the player's own body or the two columns ahead once it
    /// has had time to react, which is what a resting body or a meeting place on the journey does; a body crossing past is not
    /// counted.
    /// </summary>
    private static void APassageIsGivenBack()
    {
        var walk = Passage(roofed: true);
        Require(walk.BlockingTicks == 0,
            $"a companion in a passage the player cannot jump past must not stand in the player's way; {walk}");
    }

    /// <summary>Printed and not asserted: what the same walk does on an open floor, where the player could jump past and no
    /// passage evidence exists.</summary>
    private static void MeasureTheSameWalkOnOpenFloor()
    {
        var walk = Passage(roofed: false);
        Console.WriteLine($"MEASURE courtesy the same walk with no roof: {walk}");
    }

    // ── scenes ───────────────────────────────────────────────────────────────────────────────

    private readonly record struct PlacementRun(int FirstClearTick, bool EndCoversTarget, float EndDistanceToPlayer, List<Vector2> Trace)
    {
        public override string ToString()
            => $"firstClear={FirstClearTick} endCoversTarget={EndCoversTarget} endDistance={EndDistanceToPlayer:0} endFeet={Trace[^1]}";
    }

    /// <summary>The companion rests on open floor with the player four tiles to its left holding <paramref name="held"/>, aimed at
    /// the companion's own feet-row tile and not swinging. Only keeping company is offered and its resting method is held.</summary>
    private static PlacementRun Placement(int held)
    {
        var (companion, player) = Scene(roofed: false, companionColumn: 50, playerColumn: 46);
        Item item = player.inventory[player.selectedItem];
        if (held == ItemID.None) item.TurnToAir(); else item.SetDefaults(held);
        player.itemAnimation = 0;
        Point target = new(50, FloorRow - 1);
        var trace = new List<Vector2>();
        int firstClear = -1;
        for (int tick = 0; tick < 180; tick++)
        {
            Player.tileTargetX = target.X;
            Player.tileTargetY = target.Y;
            Step(companion);
            trace.Add(companion.NPC.Bottom);
            if (firstClear < 0 && !Covers(companion, target)) firstClear = tick;
        }
        return new PlacementRun(firstClear, Covers(companion, target), Vector2.Distance(companion.NPC.Bottom, player.Bottom), trace);
    }

    private readonly record struct PassageRun(int BlockingTicks, int StartColumn, float EndPlayerColumn, float EndCompanionColumn)
    {
        public override string ToString()
            => $"companion started at column {StartColumn}; stationary ticks in the player's way after the grace: {BlockingTicks}; "
                + $"at the end the player was at column {EndPlayerColumn:0.0} and the companion at {EndCompanionColumn:0.0}";
    }

    private const int ReactionGraceTicks = 30;

    /// <summary>The player walks right at a steady pace for 160 ticks from eight tiles left of the resting companion, along a
    /// floor with a roof directly over a standing body when <paramref name="roofed"/>.</summary>
    private static PassageRun Passage(bool roofed)
    {
        const int start = 46;
        var (companion, player) = Scene(roofed, companionColumn: start, playerColumn: start - 8);
        player.inventory[player.selectedItem].TurnToAir();
        int blocking = 0;
        for (int tick = 0; tick < 160; tick++)
        {
            player.velocity = new Vector2(1.5f, 0f);
            player.position += player.velocity;
            Step(companion);
            if (Trace && tick % 6 == 0)
            {
                object? evidence = companion.Brain.Senses.Player.GetType().GetProperty("Interference")?.GetValue(companion.Brain.Senses.Player);
                Console.WriteLine($"  TRACE roofed={roofed} t={tick} playerCol={player.Center.X / 16f:0.0} companionCol={companion.NPC.Center.X / 16f:0.0} "
                    + $"request={companion.Brain.LastRequest.Kind} chosen={companion.Brain.Positioner.Chosen} reason={companion.Brain.Positioner.ChoiceReason} "
                    + $"satisfied={companion.Brain.Positioner.FollowObjectiveSatisfied} nav={companion.Brain.Navigator.Status} evidence={evidence ?? "none"} "
                    + $"travelling={companion.Brain.Senses.Player.IsTravelling}");
            }
            // The player's own body and the two columns ahead of it: a companion parked where the player is walking, which is where
            // reunion's meeting place put it with no courtesy, stands in the player's own column rather than ahead of it.
            Rectangle way = new((int)player.position.X, (FloorRow - 3) * 16, player.width + 32, 48);
            if (tick >= ReactionGraceTicks && companion.NPC.Hitbox.Intersects(way) && MathF.Abs(companion.NPC.velocity.X) < .5f)
                blocking++;
        }
        return new PassageRun(blocking, start, player.Center.X / 16f, companion.NPC.Center.X / 16f);
    }

    private static (CompanionNPC Companion, Player Player) Scene(bool roofed, int companionColumn, int playerColumn)
    {
        Main.maxTilesX = Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        // The live game's values for the tiles and placeables used here; the headless table seeds almost nothing.
        Main.tileSolid[TileID.Stone] = true;
        Main.tileSolid[TileID.Dirt] = true;
        Main.tileSolidTop[TileID.Dirt] = false;
        Main.tileSolid[TileID.Torches] = false;
        for (int x = 5; x < 95; x++) Solid(x, FloorRow);
        if (roofed)
            for (int x = 20; x < 80; x++) Solid(x, FloorRow - 4);
        var companion = VerifyCompanionLifecycle.Create();
        TerrainChanges.Reset();
        NavGrid.World = new GameTileWorld();
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.velocity = Vector2.Zero;
        player.selectedItem = 0;
        for (int i = 0; i < player.inventory.Length; i++) player.inventory[i] ??= new Item();
        player.position = new Vector2(playerColumn * 16 + 8 - player.width / 2f, FloorRow * 16 - player.height);
        Player.tileRangeX = 5;
        Player.tileRangeY = 4;
        companion.NPC.position = new Vector2(companionColumn * 16 + 8 - companion.NPC.width / 2f, FloorRow * 16 - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;
        Player.tileTargetX = Player.tileTargetY = 0;
        var brain = companion.Brain;
        var company = brain.Chooser.Actions.OfType<KeepCompany>().Single();
        brain.Chooser.Actions.RemoveAll(a => !ReferenceEquals(a, company));
        // One tick first: activating the activity calls Enter, which resets its local movement. Then hold the resting method,
        // so a random stroll cannot move the companion in the reference scene and pass a case for it.
        Step(companion);
        Set(company, "walking", false);
        Set(company, "ticksLeft", 1_000_000);
        return (companion, player);
    }

    private static void Step(CompanionNPC companion)
    {
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        companion.AI();
        VerifyResponsiveFollowing.AdvanceNative(companion);
    }

    private static bool Covers(CompanionNPC companion, Point tile)
    {
        Rectangle body = companion.NPC.Hitbox;
        Rectangle cell = new(tile.X * 16, tile.Y * 16, 16, 16);
        return body.Intersects(cell);
    }

    private static int FirstDifference(List<Vector2> a, List<Vector2> b)
    {
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            if (a[i] != b[i]) return i;
        return a.Count == b.Count ? -1 : Math.Min(a.Count, b.Count);
    }

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.ClearEverything();
        tile.HasTile = true;
        tile.TileType = TileID.Stone;
    }

    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
