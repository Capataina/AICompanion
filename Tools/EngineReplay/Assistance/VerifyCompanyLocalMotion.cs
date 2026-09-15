extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using FollowPlayerObjective = live::AICompanion.Companion.Brain.Infrastructure.Position.FollowPlayerObjective;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using KeepCompany = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using Policy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using Reach = live::AICompanion.Companion.Brain.Infrastructure.Movement.Reachability.Reach;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// Keeping company's local method through the whole brain on native collision: what it resolves when the neighbourhood holds pools,
/// and what it does over a long idle window when nothing else is on offer.
///
/// <para>Two of this file's cases were about a body that fell. The rim case stood both actors on a slab with open air twelve rows down
/// on either side and required that the slab's end tiles were never strolled to; the hazard case's third hazard was a twelve-row pit
/// with a way down and none back. Neither is a hazard to a body that flies — it hovers off the edge and hovers back — and the rim rule
/// they tested is gone from the picker with the walker that needed it. The liquids were the last hazards, and since the owner ruled on
/// 15 September 2026 that every liquid is air to the companion they are not hazards either: the pools stay in the scene as space the
/// body may use, and the row keeps what is still a property — only places it can come back from, and no work credit for moving about.
/// The "no jump for show" row went with the walker, because the body has no jump to start.</para>
/// </summary>
internal static class VerifyCompanyLocalMotion
{
    public static int Run()
    {
        int red = 0;
        void Each(string name, Action fixture)
        {
            // Whole-brain cases catch states by condition over thousands of ticks; under the wall-clock allowances machine load would
            // decide how far each flood and route got, and so what the case observed.
            LimitPlanningWork.Unbounded = true;
            try { fixture(); Console.WriteLine($"GREEN {name}"); }
            catch (Exception e) { red++; Console.WriteLine($"RED {name}: {e.Message}"); }
            finally { LimitPlanningWork.Unbounded = false; }
        }
        Each("J06/X01 hovering company over pools resolves only places it can come back from", HoveringCompanyOverPoolsStaysReturnable);
        Each("J06 an idle window keeps company without ever standing still", IdleCompanyNeverStandsStill);
        Each("J06 an idle window on a 1:1 block staircase keeps the flat floor's envelope", () => IdleCompanyOnStairs(StairStyle.Blocks));
        Each("empty-world reunion: nothing on offer and the player walks away", AnEmptyWorldReunitesWithAWalkingPlayer);
        if (red == 0) Console.WriteLine("company local motion: hovering company over pools stays returnable, never stands still, and meets a walking player where he stops");
        return red;
    }

    private const int FloorRow = 60, PlayerColumn = 40;
    // Pool footprints, in tile columns, both inside the calm band either side of the player. Lava sits before the water pit; both are
    // air to this body, so a place inside either is as free as any other and a goal beyond one is reached straight through it.
    private const int LavaLeft = 45, LavaRight = 47, WaterLeft = 51, WaterRight = 57;
    // "Still" is the session reader's own threshold, and a run this long is the stop the owner saw in play.
    private const float StillSpeed = 0.3f;
    private const int StillRunTicks = 10;

    /// <summary>
    /// A floor with two pools beside a standing player: a lava pool two rows deep and a water pit three rows deep. The row used to
    /// ask that nothing the hover could reach lay in a pool and that the body never touched one, because both hurt it and both were
    /// walls to every flood; since 15 September 2026 every liquid is air to the companion, so that half of the row asks for a
    /// property that no longer exists and is gone. What remains is still a property: over a long seeded run with keeping company the
    /// only activity, no destination the positioner resolves may lie where the body does not fit or outside the region the body can
    /// come back from, and nothing it does may earn productive-work credit. The scene asserts each pool holds its liquid, and that
    /// the liquid is free for the body, before the run.
    /// </summary>
    private static void HoveringCompanyOverPoolsStaysReturnable()
    {
        var ctx = BuildNeighbourhood(hazards: true);
        var brain = ctx.Companion.Brain;
        brain.Chooser.Actions.RemoveAll(a => a.Name != "keep-company");
        var violations = new List<string>();
        var counts = new SortedDictionary<string, int>();
        void Count(string what) => counts[what] = counts.TryGetValue(what, out int n) ? n + 1 : 1;
        const int Ticks = 6000;
        for (int tick = 0; tick < Ticks; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.Positioner.Chosen is Vector2 chosen)
            {
                Point tile = MovementQueries.Tile(chosen);
                if (Hazard(tile) is string why) { Note(violations, $"t{tick} resolved destination {tile} {why}"); Count($"destination {why}"); }
                if (!brain.Positioner.ChosenReturnable) { Note(violations, $"t{tick} resolved destination {tile} outside the returnable region"); Count("destination outside the returnable region"); }
            }
            Vector2 centre = ctx.Npc.Center;
            if (brain.Chooser.IsCollectingWork(centre)) { Note(violations, $"t{tick} movement recorded as productive work"); Count("productive credit"); }
        }
        string ledger = $"violation ticks by kind: {string.Join(", ", counts.Select(c => $"{c.Key}={c.Value}"))}; first violations: {string.Join("; ", violations)}";
        Require(violations.Count == 0, $"keeping company over pools must never resolve a place the body does not fit or cannot come back from; {ledger}");
        Console.WriteLine($"hovering over pools: every destination returnable over {Ticks} ticks");
    }

    /// <summary>
    /// A flat, empty world with a standing player and every activity registered: no ore, no drops, no pots, light unmeasured, no
    /// enemies. The bound is chosen before the run, from what the behaviour is for rather than from what the code does. Keeping company
    /// must be the only activity chosen, which is the premise that the world offers nothing else; the body must never sit under the still
    /// threshold for ten ticks running, which is the owner's ruling that the orb is never strictly standing still; and its mean speed must
    /// stay under the settled threshold, so the motion is a drift around where it keeps company rather than travel. The window is three
    /// game minutes, long enough for the wander's random walk and its reversals to have happened many times.
    /// </summary>
    private static void IdleCompanyNeverStandsStill()
        => IdleEnvelope(BuildNeighbourhood(hazards: false), "a flat floor");

    /// <summary>The idle window's envelope, on whatever floor <paramref name="ctx"/> stands over: keeping company the only activity
    /// chosen, no still run of ten ticks, and a mean speed that reads as a drift. The walker's rest share and stroll-goal count went
    /// with the stroll picker; a companion that hovers has no rests to count and no goals to reach.</summary>
    private static void IdleEnvelope(ActionContext ctx, string floor)
    {
        VerifyUsefulAssistance.ClearMeasuredLight();
        var brain = ctx.Companion.Brain;
        int otherActivity = 0, stillRun = 0, longestStill = 0;
        double speedSum = 0;
        const int Ticks = 10800;
        for (int tick = 0; tick < Ticks; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.LastAction?.Name != "keep-company") otherActivity++;
            float speed = ctx.Companion.Motor.State.Velocity.Length();
            speedSum += speed;
            stillRun = speed < StillSpeed ? stillRun + 1 : 0;
            longestStill = Math.Max(longestStill, stillRun);
        }
        float mean = (float)(speedSum / Ticks);
        string ledger = $"longest still run={longestStill} mean speed={mean:0.00} px/tick ticks not keeping company={otherActivity}";
        Require(otherActivity == 0, $"the idle premise needs keeping company to be the only activity chosen on {floor}; {ledger}");
        Require(longestStill < StillRunTicks, $"idle company on {floor} must never stand still for {StillRunTicks} ticks; {ledger}");
        Require(mean <= Weights.SettledSpeedPx, $"idle company on {floor} must drift rather than travel; {ledger}");
        Console.WriteLine($"idle company on {floor}: {ledger}");
    }

    internal enum StairStyle { Blocks, SlopesRisingRight, SlopesRisingLeft }
    // How far either side of the player the staircase keeps rising before the floor levels off; past the stroll band, so every
    // column a pick can sample is on the stair.
    private const int StairHalfSpan = 24;

    /// <summary>
    /// The idle scene on a floor rising one row per column across the whole stroll band, built of full blocks or of slopes in either
    /// direction, which is what most cave floors are made of. The earlier stroll rule asked for floor on the same row under both
    /// neighbours of a goal and so refused every tile here: rest share 1.00 with no goal in 400 picks, where the flat floor read 0.52
    /// with 19. The envelope is the flat floor's own, chosen for it before this scene existed, and the premise is asserted first:
    /// every column near the player has a standable, supported tile, and the neighbour on the rising side has no floor on that tile's
    /// row, so the scene is one the same-row rule refuses.
    /// </summary>
    private static void IdleCompanyOnStairs(StairStyle style)
    {
        int rise = style == StairStyle.SlopesRisingLeft ? -1 : 1;
        int SurfaceRow(int x) => FloorRow - Math.Clamp(rise * (x - PlayerColumn), -StairHalfSpan, StairHalfSpan);
        var ctx = BuildNeighbourhood(hazards: false, terrain: () =>
        {
            for (int x = 5; x < 95; x++)
                for (int y = FloorRow - StairHalfSpan - 6; y <= FloorRow + StairHalfSpan + 6; y++)
                    Main.tile[x, y].ClearEverything();
            for (int x = 5; x < 95; x++)
            {
                int top = SurfaceRow(x);
                for (int y = top; y <= top + 3; y++) VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
                bool onStair = Math.Abs(x - PlayerColumn) < StairHalfSpan;
                if (style != StairStyle.Blocks && onStair)
                {
                    // Tile is a view returned by value from the tile map's indexer, so it is held in a local before a field is written.
                    Tile step = Main.tile[x, top];
                    step.Slope = style == StairStyle.SlopesRisingRight ? (Terraria.ID.SlopeType)2 : (Terraria.ID.SlopeType)1;
                }
            }
        });
        // The premise columns either side of the player, the span the retired stroll picker's row bound once covered.
        const int PremiseColumns = 4;
        for (int x = PlayerColumn - PremiseColumns; x <= PlayerColumn + PremiseColumns; x++)
        {
            int top = SurfaceRow(x);
            // Somewhere in this column the body fits, above the step's own tile: a rising floor of full blocks is a
            // staircase of walls to this body, so what the premise needs is free space over it, not a place to stand.
            int hover = Enumerable.Range(1, 3).Select(d => top - d).FirstOrDefault(y => MovementQueries.IsHoverable(new Point(x, y)), int.MinValue);
            Require(hover != int.MinValue && MovementQueries.IsSupport(x, top),
                $"the {style} staircase premise needs a hoverable cell over the supported surface of column {x} at row {top}");
        }
        IdleEnvelope(ctx, $"a 1:1 staircase of {style}");
    }

    /// <summary>
    /// P10's "empty world reunion": every activity registered, nothing any of them can offer (no ore, drops, pots, measured darkness or
    /// enemies), and a player who walks forty tiles away along the floor and stops. Keeping company must be the only activity chosen
    /// throughout, must ask for reunion, and must end with the body inside the follow objective at the stopped player. The C-turn case
    /// keeps the full chooser in an empty world too, but its player never moves, and the meeting-place and company-method cases remove
    /// every other activity, so none of them is this scene.
    /// </summary>
    private static void AnEmptyWorldReunitesWithAWalkingPlayer()
    {
        var ctx = BuildNeighbourhood(hazards: false);
        VerifyUsefulAssistance.ClearMeasuredLight();
        var brain = ctx.Companion.Brain;
        Player player = ctx.Player;
        float stopAt = 80 * 16 + 8;
        int otherActivity = 0, stoppedAt = -1, arrivedAt = -1;
        bool askedForReunion = false;
        for (int tick = 0; tick < 1500 && arrivedAt < 0; tick++)
        {
            bool walking = player.Bottom.X < stopAt;
            player.velocity = walking ? new Vector2(3f, 0f) : Vector2.Zero;
            player.position += player.velocity;
            if (!walking && stoppedAt < 0) stoppedAt = tick;
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.LastAction?.Name != "keep-company") otherActivity++;
            askedForReunion |= brain.LastRequest.Kind == RequestKind.WithPlayer;
            // Met is inside the stopped player's region; sight of him stopped being a condition when losing it stopped being distance.
            if (!walking && brain.Senses.Intent.Objective.IsSatisfied(ctx.Npc.Center))
                arrivedAt = tick;
        }
        string ledger = $"player stopped at tick {stoppedAt}, companion arrived at tick {arrivedAt}; centre={ctx.Npc.Center} player={player.Bottom}; "
            + $"reunion requested={askedForReunion}; ticks not keeping company={otherActivity}; recovery={brain.FollowRecovery.Active}";
        Require(otherActivity == 0, $"with nothing on offer keeping company must be the only activity; {ledger}");
        Require(askedForReunion && arrivedAt >= 0, $"a player walking away in an empty world must be met where they stop; {ledger}");
        Console.WriteLine($"empty-world reunion: {ledger}");
    }

    /// <summary>What is wrong with hovering in this tile: no room for the body. A tile inside either pool is not wrong, because every
    /// liquid is air to the body.</summary>
    private static string? Hazard(Point tile)
        => MovementQueries.IsHoverable(tile) ? null : "is not a place the body fits";

    private static void Note(List<string> violations, string what)
    {
        if (violations.Count < 6) violations.Add(what);
        else if (violations.Count == 6) violations.Add("...");
    }

    internal static ActionContext BuildNeighbourhood(bool hazards, Action? terrain = null)
    {
        Point placeholder = new(80, FloorRow - 1);
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        ctx.Player.velocity = Vector2.Zero;
        ctx.Player.Bottom = new Vector2(PlayerColumn * 16 + 8, FloorRow * 16);
        // The body hovers a tile above the floor rather than standing on it.
        ctx.Npc.Center = MovementQueries.HoverPoint(new Point(PlayerColumn, FloorRow - 1));
        ctx.Npc.velocity = Vector2.Zero;
        if (hazards)
        {
            Basin(LavaLeft, LavaRight, depth: 2, liquid: LiquidID.Lava);
            Basin(WaterLeft, WaterRight, depth: 3, liquid: LiquidID.Water);
        }
        terrain?.Invoke();
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        if (hazards)
        {
            // Each pool must actually hold its liquid, and the body must have somewhere to hover beside it, so the scene is one
            // with pools in it rather than a dry floor that would pass the same row.
            Require(MovementQueries.IsLava(LavaLeft + 1, FloorRow) && MovementQueries.IsHoverable(new Point(LavaLeft - 2, FloorRow - 1)),
                "the lava pool premise needs lava below the floor line and room to hover beside it");
            Require(MovementQueries.IsWet(WaterLeft + 3, FloorRow + 1) && MovementQueries.IsHoverable(new Point(WaterRight + 2, FloorRow - 1)),
                "the water pit premise needs water in it and room to hover beside it");
            // Both pools are free space to every flood the body runs, because every liquid is air to it.
            Require(MovementQueries.IsFreeForOrb(LavaLeft + 1, FloorRow) && MovementQueries.IsFreeForOrb(WaterLeft + 3, FloorRow + 1),
                "the pool premise needs both liquids to be free space to the flood, because every liquid is air to the body");
        }
        return ctx;
    }

    /// <summary>Clears a basin <paramref name="depth"/> rows deep below the floor between two columns, walls and floors it, and fills
    /// it with the liquid if one is given.</summary>
    private static void Basin(int left, int right, int depth, int? liquid)
    {
        int bottom = FloorRow + depth;
        for (int x = left; x <= right; x++)
            for (int y = FloorRow; y < bottom; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.ClearEverything();
                if (liquid is int kind) { tile.LiquidType = kind; tile.LiquidAmount = 255; }
            }
        for (int x = left - 1; x <= right + 1; x++) VerifyOreWork.Place(new Point(x, bottom), TileID.Dirt);
        for (int y = FloorRow; y <= bottom; y++)
        {
            VerifyOreWork.Place(new Point(left - 1, y), TileID.Dirt);
            VerifyOreWork.Place(new Point(right + 1, y), TileID.Dirt);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
