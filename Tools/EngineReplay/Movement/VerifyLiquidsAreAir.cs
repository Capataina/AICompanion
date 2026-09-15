extern alias live;
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using CornerGraph = live::AICompanion.Companion.Brain.Infrastructure.Movement.CornerGraph;
using EvadeReason = live::AICompanion.Companion.Brain.Infrastructure.Movement.EvadeReason;
using FreeSpaceSearch = live::AICompanion.Companion.Brain.Infrastructure.Movement.FreeSpaceSearch;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using ReachVerdict = live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;

/// <summary>
/// Every liquid is air to the companion, by the owner's ruling of 15 September 2026: the orb flies through water, honey,
/// lava and shimmer exactly as through open air, is never hurt, slowed or transformed by any of them, and nothing that plans
/// for it treats a liquid as a wall. The ruling made the immunity built in rather than a mastery unlock, because a capability
/// switched on by an upgrade would have to be tested with and without it after every later change to the brain.
///
/// <para>Two scenes, on native tiles with the real <c>CompanionNPC</c>. The first drives the live motor straight through a
/// column of each liquid and compares every tick inside a liquid with a cruising tick in air, which is what proves the
/// motor applies no slowdown and no hurt. The second puts the player beyond a passage flooded with each liquid in turn, the
/// only way through a sealed wall, and asks the route search, the reach sense and then the whole brain whether he is reached
/// through it, which is what proves no search treats the liquid as a wall and no response takes the body for it.</para>
///
/// <para>What neither scene can reach: the engine's own NPC update. The headless tools finish a tick the way the engine
/// finishes it for a no-gravity, no-tile-collide body — the velocity added to the position — and never run
/// <c>NPC.UpdateNPC</c>, so the engine's inaction on a wet orb stands on the decompiled source (its wet slowdown, lava strike
/// and shimmer buff all live in <c>UpdateCollision</c>, which a body with tile collision off never reaches) rather than on a
/// run. The shimmer buff immunity and lava immunity the NPC declares are for any engine path outside that step.</para>
/// </summary>
internal static class VerifyLiquidsAreAir
{
    private static readonly (int Kind, string Name)[] Liquids =
        { (LiquidID.Water, "water"), (LiquidID.Honey, "honey"), (LiquidID.Lava, "lava"), (LiquidID.Shimmer, "shimmer") };

    /// <summary>
    /// How far a tick inside a liquid may move from a cruising tick in air, in pixels, declared before the first run. The motor
    /// is deterministic and the body flies a straight line at its cap, so the two are expected to be equal; the tolerance only
    /// absorbs float rounding, and every slowdown the engine or the motor ever applied to a liquid was a quarter of the step or
    /// more — two orders of magnitude above it.
    /// </summary>
    private const float DisplacementTolerance = 0.01f;

    private const int Height = 100;

    /// <summary>
    /// A corridor twelve tiles tall with a column of water, honey, lava and shimmer across its whole height, eight tiles wide
    /// each and twelve apart, and the body started well to the left so it reaches its cap before the first column. The motor
    /// is asked for the cap straight right every tick and the engine's step finishes the move. Pass line: every liquid is
    /// touched on at least ten ticks (the premise that the body was in it), every tick inside a liquid moves within the
    /// tolerance of the mean cruising tick in air, life never falls, and no shimmer buff or shimmer transparency appears.
    /// </summary>
    public static int FlightThroughEveryLiquid()
    {
        const int width = 170, ceiling = 57, floor = 70, firstColumn = 80, columnWidth = 8, columnGap = 20;
        BuildWorld(width, () =>
        {
            for (int x = 5; x < width - 5; x++) { Solid(x, ceiling); Solid(x, floor); }
            for (int y = ceiling; y <= floor; y++) { Solid(4, y); Solid(width - 5, y); }
            for (int i = 0; i < Liquids.Length; i++)
                Pour(firstColumn + i * columnGap, firstColumn + i * columnGap + columnWidth - 1, ceiling + 1, floor - 1, Liquids[i].Kind);
        });
        CompanionNPC companion = Spawn(new Vector2(8 * 16 + 8, 64 * 16), new Vector2(10 * 16 + 8, floor * 16));
        int type = companion.NPC.type, startLife = companion.NPC.life, lowestLife = startLife;
        bool shimmered = false;
        var steps = new List<(int Kind, float X, Vector2 Moved)>();
        for (int tick = 0; tick < 900 && companion.NPC.Center.X < (width - 15) * 16; tick++)
        {
            companion.Motor.Track();
            companion.Motor.Steer(new Vector2(companion.Motor.LiveMaxSpeed, 0f), "liquids-fixture");
            int kind = companion.Motor.LiquidKind;
            Vector2 before = companion.NPC.position;
            VerifyResponsiveFollowing.AdvanceNative(companion);
            steps.Add((kind, companion.NPC.Center.X, companion.NPC.position - before));
            lowestLife = Math.Min(lowestLife, companion.NPC.life);
            shimmered |= companion.NPC.HasBuff(BuffID.Shimmer) || companion.NPC.shimmerTransparency > 0f || companion.NPC.type != type;
        }

        int failures = 0;
        // Air after the easing and before the first column: the cruising step every liquid tick is compared against.
        var air = steps.Where(s => s.Kind < 0 && s.X > 40 * 16 && s.X < (firstColumn - 2) * 16).Select(s => s.Moved).ToList();
        float speed = companion.Motor.LiveMaxSpeed;
        if (air.Count < 10 || air.Any(m => MathF.Abs(m.X - speed) > DisplacementTolerance || MathF.Abs(m.Y) > DisplacementTolerance))
            return Detail($"premise: the body must cruise at its cap of {speed:0.00} px in air before the first column; air ticks {air.Count}, steps {string.Join(" ", air.Take(6).Select(m => $"{m.X:0.000},{m.Y:0.000}"))}");
        Vector2 cruise = new(air.Average(m => m.X), air.Average(m => m.Y));
        var report = new List<string>();
        foreach (var (kind, name) in Liquids)
        {
            var inside = steps.Where(s => s.Kind == kind).Select(s => s.Moved).ToList();
            float worst = inside.Count == 0 ? float.NaN : inside.Max(m => Vector2.Distance(m, cruise));
            report.Add($"{name} {inside.Count} ticks, worst deviation {worst:0.0000} px");
            if (inside.Count < 10)
                failures += Detail($"premise: the body must be in the {name} column for at least ten ticks; it was in it for {inside.Count}");
            else if (worst > DisplacementTolerance)
                failures += Detail($"a tick in {name} moved {worst:0.0000} px away from the cruising step {cruise.X:0.000},{cruise.Y:0.000}; the first: {string.Join(" ", inside.Where(m => Vector2.Distance(m, cruise) > DisplacementTolerance).Take(4).Select(m => $"{m.X:0.000},{m.Y:0.000}"))}");
        }
        if (lowestLife < startLife)
            failures += Detail($"the body lost life flying through the liquids: {startLife} -> lowest {lowestLife}; {string.Join("; ", report)}");
        if (shimmered)
            failures += Detail($"the body was shimmered: buff {companion.NPC.HasBuff(BuffID.Shimmer)}, transparency {companion.NPC.shimmerTransparency:0.00}, type {type} -> {companion.NPC.type}");
        if (failures == 0)
            Console.WriteLine($"liquids are air: cruising step {cruise.X:0.000},{cruise.Y:0.000} px; {string.Join("; ", report)}; life {startLife} throughout");
        return failures;
    }

    /// <summary>
    /// Each liquid in turn floods the only passage through a wall forty tiles thick, and the player stands still on the far side.
    /// Pass line, per liquid: the route search from the body to the player finds him through the passage in no more than
    /// 1.25 times the straight line; over the whole brain with keeping company its only activity, the reach sense reads his tile
    /// reachable on some tick and unreachable on none, the body touches the liquid on its way, arrives inside his region past
    /// the wall, never loses life, is never given to a suspending owner, and the evade layer never reads anything but off or kept.
    /// </summary>
    public static int AFloodedPassageIsReachedThroughIt()
    {
        int failures = 0;
        LimitPlanningWork.Unbounded = true;
        try
        {
            foreach (var (kind, name) in Liquids)
                failures += FloodedPassage(kind, name);
        }
        finally { LimitPlanningWork.Unbounded = false; }
        return failures;
    }

    private static int FloodedPassage(int kind, string name)
    {
        const int width = 170, ceiling = 40, floor = 70, wallLeft = 61, wallRight = 100, tunnelTop = 62, tunnelBottom = 67, right = 154;
        BuildWorld(width, () =>
        {
            for (int x = 5; x <= right; x++) { Solid(x, ceiling); Solid(x, floor); }
            for (int y = ceiling; y <= floor; y++) { Solid(5, y); Solid(right, y); }
            for (int x = wallLeft; x <= wallRight; x++)
                for (int y = ceiling + 1; y < floor; y++)
                    if (y < tunnelTop || y > tunnelBottom) Solid(x, y);
            Pour(wallLeft, wallRight, tunnelTop, tunnelBottom, kind);
        });
        Vector2 feet = new(125 * 16 + 8, floor * 16);
        CompanionNPC companion = Spawn(new Vector2(35 * 16 + 8, 64 * 16), feet);
        var world = MovementQueries.World;

        // The scene's own premise, asked of the tiles rather than of any rule under test: the wall is solid everywhere but
        // the passage, and every tile of the passage holds the liquid.
        for (int x = wallLeft; x <= wallRight; x++)
            for (int y = ceiling + 1; y < floor; y++)
            {
                bool passage = y >= tunnelTop && y <= tunnelBottom;
                if (passage != !MovementQueries.IsSolidForOrb(x, y) || passage != (world.LiquidAmount(x, y) > 0 && world.LiquidKind(x, y) == kind))
                    return Detail($"{name}: premise: tile {x},{y} must be {(passage ? "open and full of " + name : "solid and dry")}");
            }

        int failures = 0;
        Point? start = CornerGraph.NearestUsable(world, companion.NPC.Center, 2, requireSweep: false);
        Point? goal = CornerGraph.NearestUsable(world, new Vector2(feet.X, feet.Y - 24f), 2, requireSweep: false);
        if (start is not Point from || goal is not Point to)
            return Detail($"{name}: premise: the body and the player must each have a corner the body fits at; start {start} goal {goal}");
        var search = new FreeSpaceSearch(world, from, to);
        search.Advance(int.MaxValue);
        var corners = search.RouteCorners();
        float straight = Vector2.Distance(CornerGraph.ToWorld(from), CornerGraph.ToWorld(to)), length = 0f;
        if (corners != null)
            for (int i = 1; i < corners.Count; i++) length += Vector2.Distance(CornerGraph.ToWorld(corners[i - 1]), CornerGraph.ToWorld(corners[i]));
        bool throughPassage = corners?.Any(c => c.X > wallLeft && c.X <= wallRight) == true;
        if (search.Stop != FreeSpaceSearch.StopReason.Found || corners == null || !throughPassage || length > straight * 1.25f)
            failures += Detail($"{name}: the route search must find the player through the flooded passage within 1.25 times the straight line: stop {search.Stop}, {corners?.Count ?? 0} corners, {length:0} px against {straight:0} px, through the passage {throughPassage}");

        var brain = companion.Brain;
        brain.Chooser.Actions.RemoveAll(a => a.Name != "keep-company");
        Point playerTile = Main.player[0].Center.ToTileCoordinates();
        int reachableAt = -1, unreachableTicks = 0, wetTicks = 0, arrivedAt = -1, lowestLife = companion.NPC.life, startLife = companion.NPC.life;
        var owners = new SortedSet<string>(StringComparer.Ordinal);
        var evades = new SortedSet<string>(StringComparer.Ordinal);
        for (int tick = 0; tick < 1500 && arrivedAt < 0; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
            VerifyResponsiveFollowing.AdvanceNative(companion);
            ReachVerdict verdict = brain.Senses.Reach.Reachable(playerTile);
            if (verdict == ReachVerdict.Reachable && reachableAt < 0) reachableAt = tick;
            if (verdict == ReachVerdict.Unreachable) unreachableTicks++;
            if (companion.Motor.LiquidKind == kind) wetTicks++;
            lowestLife = Math.Min(lowestLife, companion.NPC.life);
            owners.Add(brain.ControlGrants.Last?.RequestedOwner ?? "-");
            evades.Add(brain.Movement.LastEvade.Reason.ToString());
            if (companion.NPC.Center.X > (wallRight + 1) * 16 && brain.Senses.Intent.Objective.IsSatisfied(companion.NPC.Center))
                arrivedAt = tick;
        }
        string ledger = $"reachable from tick {reachableAt}, unreachable on {unreachableTicks} ticks, in {name} on {wetTicks} ticks, arrived at tick {arrivedAt}, "
            + $"life {startLife} -> lowest {lowestLife}, owners {string.Join(",", owners)}, evade {string.Join(",", evades)}, body at {companion.NPC.Center.X:0},{companion.NPC.Center.Y:0}";
        var suspending = new[] { "survival-escape", "follow-recovery-flight", "downed" };
        if (reachableAt < 0 || unreachableTicks > 0)
            failures += Detail($"{name}: the reach sense must read the player's tile reachable through the passage and never unreachable; {ledger}");
        if (wetTicks == 0 || arrivedAt < 0)
            failures += Detail($"{name}: the companion must fly through the {name} to the player; {ledger}");
        if (lowestLife < startLife)
            failures += Detail($"{name}: the body must not lose life in the {name}; {ledger}");
        if (owners.Any(o => suspending.Contains(o)) || evades.Any(e => e != nameof(EvadeReason.Off) && e != nameof(EvadeReason.Kept)))
            failures += Detail($"{name}: nothing may take the body or bend it for the {name}; {ledger}");
        if (failures == 0)
            Console.WriteLine($"flooded passage of {name}: route {length:0} px against {straight:0} px straight; {ledger}");
        return failures;
    }

    private static CompanionNPC Spawn(Vector2 centre, Vector2 playerFeet)
    {
        Replug();
        CompanionNPC companion = VerifyCompanionLifecycle.Create();
        Replug();
        Main.player[0].dead = false;
        Main.player[0].velocity = Vector2.Zero;
        Main.player[0].Bottom = playerFeet;
        companion.NPC.Center = centre;
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.active = true;
        return companion;
    }

    private static void BuildWorld(int width, Action draw)
    {
        Main.maxTilesX = width;
        Main.maxTilesY = Height;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)width, (ushort)Height }, null)!;
        Main.tileSolid[1] = true;
        draw();
        Replug();
    }

    /// <summary>A rebuilt tile map is a different world to every clearance chunk and retained search, which compare it by reference.</summary>
    private static void Replug()
    {
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
    }

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = true;
        tile.TileType = 1;
    }

    private static void Pour(int left, int right, int top, int bottom, int kind)
    {
        for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.LiquidType = kind;
                tile.LiquidAmount = byte.MaxValue;
            }
    }

    private static int Detail(string message)
    {
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail(message);
        Console.WriteLine("   liquids are air: " + message);
        return 1;
    }
}
