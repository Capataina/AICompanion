extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using KeepCompany = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
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
        Each("a block aimed at the tile ahead of a moving companion turns it off that tile", ABlockAimedAtTheTileAheadTurnsTheCompanionOff);
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
        // Restated on 15 September 2026. The premise was that with an empty hand the resting companion stays on its tile, and
        // nothing rests any more: a companion keeping the player company moves about his region and leaves any tile on its own.
        // What a block aimed at the tile adds is that it is refused as somewhere to move to, so the premise is now only that the
        // body began on the tile, and the verdict is that once it has had time to react it is never on the tile again.
        var rest = Placement(ItemID.None);
        Require(rest.FirstClearTick > 0, $"premise: the companion's body must begin on the tile the player aims at; {rest}");
        var block = Placement(ItemID.DirtBlock);
        Require(block.FirstClearTick >= 0 && block.CoveredAfterGrace == 0,
            $"a block aimed at the companion's tile must move it off that tile and keep it off; empty hand {rest}; block {block}");
        // Restated on 15 September 2026: "with the player" was six tiles, the old follow spot's distance from his feet. A companion
        // moving about his whole region is up to its grown half-width from him by design, so that is the bound.
        float regionReach = live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegion
            .BaseHalfSize().X
            * (1f + live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.IntentRegionGrowthCap);
        Require(block.EndDistanceToPlayer <= regionReach,
            $"moving aside must stay with the player rather than wander off; within {regionReach:0} px; block {block}");
    }

    /// <summary>
    /// The row above no longer proves courtesy on its own: with the footprint refusal switched off it still passed, because the
    /// wander carries the body off the floor tile within a quarter of a second in both arms. The refusal only does anything when
    /// the walk would carry the body into a tile the player is aiming at, so this scene aims at the tile the moving body is
    /// about to enter. The two arms are identical tick for tick until the body first covers that tile, because the footprint
    /// needs the body on it; from then on the block arm refuses the tile and the empty hand does not, so an accompanying motion
    /// that ignores the player's footprint gives two equal counts.
    /// </summary>
    private static void ABlockAimedAtTheTileAheadTurnsTheCompanionOff()
    {
        var rest = PlacementAhead(ItemID.None);
        Require(rest.Covered >= 8, $"premise: with an empty hand the moving companion must pass through the tile it was heading for; {rest}");
        var block = PlacementAhead(ItemID.DirtBlock);
        Console.WriteLine($"courtesy tile ahead: empty hand {rest}; block {block}");
        Require(block.Aim == rest.Aim, $"premise: both arms must aim at the same tile, or they compare different scenes; empty hand {rest}; block {block}");
        Require(block.Covered * 3 <= rest.Covered * 2,
            $"a block aimed at the tile ahead of a moving companion must turn it off that tile sooner than an empty hand does; empty hand {rest}; block {block}");
    }

    private readonly record struct AheadRun(Point Aim, int Covered, int FirstCover)
    {
        public override string ToString() => $"aim={Aim.X},{Aim.Y} coveredTicks={Covered} firstCover={FirstCover}";
    }

    /// <summary>The placement scene, aiming from the grace tick on at the tile the body's velocity would carry its centre into
    /// twelve ticks later, and counting the ticks the body covers that tile over the next ninety.</summary>
    private static AheadRun PlacementAhead(int held)
    {
        const int Lookahead = 12, Window = 90;
        var (companion, player) = Scene(roofed: false, companionColumn: 50, playerColumn: 46);
        Item item = player.inventory[player.selectedItem];
        if (held == ItemID.None) item.TurnToAir(); else item.SetDefaults(held);
        player.itemAnimation = 0;
        // Reach wide enough that the aimed tile is inside the player's placement range wherever the body is headed; the
        // untargeted ticks aim at the world's corner, which is outside it.
        Player.tileRangeX = Player.tileRangeY = 40;
        Player.tileTargetX = Player.tileTargetY = 0;
        Point aim = default;
        int covered = 0, firstCover = -1;
        for (int tick = 0; tick < ReactionGraceTicks + Window; tick++)
        {
            if (tick == ReactionGraceTicks)
            {
                Vector2 ahead = companion.NPC.Center + companion.NPC.velocity * Lookahead;
                aim = new Point((int)MathF.Floor(ahead.X / 16f), (int)MathF.Floor(ahead.Y / 16f));
            }
            if (tick >= ReactionGraceTicks)
            {
                Player.tileTargetX = aim.X;
                Player.tileTargetY = aim.Y;
            }
            Step(companion);
            if (tick >= ReactionGraceTicks && Covers(companion, aim))
            {
                covered++;
                if (firstCover < 0) firstCover = tick;
            }
        }
        return new AheadRun(aim, covered, firstCover);
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

    // DELETED on 15 September 2026: the pair asserting that a rejected combat query does not eat the rescore a new
    // interference footprint forces on keeping company's held destination. Its scene stood the companion's body on a
    // follow destination it held and aimed a block at that body, because a footprint is only the tiles the body covers.
    // A follow destination is admitted inside the player's region, and a body inside the region holds no destination —
    // it moves about the region, and the positioner answers it with nothing — so the scene the pair needs cannot be
    // built any more, and forcing it would assert a path the brain no longer takes. The restore of the footprint stamp
    // it protected is still in the refinement query, where it keeps a rejected query from spending a force it did not ask
    // for; nothing on the follow path reads that force now. Courtesy itself is held by the passage row above and by the
    // tile-ahead placement row, through the accompanying motion refusing the player's footprint.

    /// <summary>Printed and not asserted: what the same walk does on an open floor, where the player could jump past and no
    /// passage evidence exists.</summary>
    private static void MeasureTheSameWalkOnOpenFloor()
    {
        var walk = Passage(roofed: false);
        Console.WriteLine($"MEASURE courtesy the same walk with no roof: {walk}");
    }

    // ── scenes ───────────────────────────────────────────────────────────────────────────────

    private readonly record struct PlacementRun(int FirstClearTick, bool EndCoversTarget, float EndDistanceToPlayer, int CoveredAfterGrace, List<Vector2> Trace)
    {
        public override string ToString()
            => $"firstClear={FirstClearTick} endCoversTarget={EndCoversTarget} endDistance={EndDistanceToPlayer:0} coveredAfterGrace={CoveredAfterGrace} endFeet={Trace[^1]}";
    }

    /// <summary>The companion begins on open floor with the player four tiles to its left holding <paramref name="held"/>, aimed at
    /// the tile the companion's body began on and not swinging. Only keeping company is offered, so the companion is moving about
    /// the player's region throughout.</summary>
    private static PlacementRun Placement(int held)
    {
        var (companion, player) = Scene(roofed: false, companionColumn: 50, playerColumn: 46);
        Item item = player.inventory[player.selectedItem];
        if (held == ItemID.None) item.TurnToAir(); else item.SetDefaults(held);
        player.itemAnimation = 0;
        Point target = new(50, FloorRow - 1);
        var trace = new List<Vector2>();
        int firstClear = -1, coveredAfterGrace = 0;
        for (int tick = 0; tick < 180; tick++)
        {
            Player.tileTargetX = target.X;
            Player.tileTargetY = target.Y;
            Step(companion);
            trace.Add(companion.NPC.Bottom);
            if (firstClear < 0 && !Covers(companion, target)) firstClear = tick;
            if (tick >= ReactionGraceTicks && Covers(companion, target)) coveredAfterGrace++;
        }
        return new PlacementRun(firstClear, Covers(companion, target), Vector2.Distance(companion.NPC.Bottom, player.Bottom), coveredAfterGrace, trace);
    }

    private readonly record struct PassageRun(int BlockingTicks, int StartColumn, float EndPlayerColumn, float EndCompanionColumn, string BlockedTicks)
    {
        public override string ToString()
            => $"companion started at column {StartColumn}; stationary ticks in the player's way after the grace: {BlockingTicks} [{BlockedTicks}]; "
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
        // The ticks themselves, not only the count. A contiguous run at the start of the window is a body still
        // accelerating and reads as a threshold question; ticks scattered through the walk are a body that keeps
        // settling in front of the player, which is a behaviour finding and belongs in the failure message rather
        // than in whoever next reads the count.
        var blockedTicks = new List<int>();
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
            {
                blocking++;
                blockedTicks.Add(tick);
            }
        }
        return new PassageRun(blocking, start, player.Center.X / 16f, companion.NPC.Center.X / 16f,
            string.Join(",", blockedTicks));
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
        MovementQueries.World = new GameTileWorld();
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
        var company = brain.Actions.OfType<KeepCompany>().Single();
        brain.Actions.RemoveAll(a => !ReferenceEquals(a, company));
        // One tick first, so the activity is entered before the scene is read. Keeping company has no random stroll any more:
        // its local method is a seeded hover around the spot, so the reference scene is deterministic without holding anything.
        Step(companion);
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
