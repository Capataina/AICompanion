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
/// Keeping company's local method through the whole brain on native collision: where it chooses to stroll when the neighbourhood holds
/// hazards, and what it does over a long idle window when nothing else is on offer.
///
/// <para>Two of this file's cases were about a body that fell. The rim case stood both actors on a slab with open air twelve rows down
/// on either side and required that the slab's end tiles were never strolled to; the hazard case's third hazard was a twelve-row pit
/// with a way down and none back. Neither is a hazard to a body that flies — it hovers off the edge and hovers back — and the rim rule
/// they tested is gone from the picker with the walker that needed it. The hazards that remain are the ones that still hurt this body,
/// which are the liquids: lava and water are walls to every flood and damage on contact, and they are what the stroll picker still has
/// to keep clear of. The "no jump for show" row went with them, because the body has no jump to start.</para>
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
        Each("J06/X01 stroll goals avoid lava and deep water", StrollGoalsAvoidHazards);
        Each("J06 an idle window neither collapses to one method nor stops strolling", IdleCompanyNeitherHopsNorFreezes);
        Each("J06 an idle window on a 1:1 block staircase keeps the flat floor's envelope", () => IdleCompanyOnStairs(StairStyle.Blocks));
        Each("empty-world reunion: nothing on offer and the player walks away", AnEmptyWorldReunitesWithAWalkingPlayer);
        if (red == 0) Console.WriteLine("company local motion: hazard-free reachable stroll goals and bounded idle movement pass");
        return red;
    }

    private const int FloorRow = 60, PlayerColumn = 40;
    // Hazard footprints, in tile columns, both inside the calm band either side of the player. Lava sits before the water pit, and
    // both are walls to every flood this body runs as well as damage on contact, so a goal beyond one of them is reached around it.
    private const int LavaLeft = 45, LavaRight = 47, WaterLeft = 51, WaterRight = 57;

    /// <summary>
    /// A floor with two hazards within stroll range of a standing player: a lava pool two rows deep and a water pit three rows deep.
    /// Over a long seeded run with keeping company the only activity, no stroll goal it holds, no destination the positioner resolves
    /// for it and no place the body reaches may lie inside a hazard or outside the region it can come back from, the body must never
    /// read as touching a liquid that hurts it, and nothing it does may earn productive-work credit. The scene asserts each hazard is
    /// what it claims before the run.
    /// </summary>
    private static void StrollGoalsAvoidHazards()
    {
        var ctx = BuildNeighbourhood(hazards: true);
        var brain = ctx.Companion.Brain;
        brain.Chooser.Actions.RemoveAll(a => a.Name != "keep-company");
        var company = brain.Chooser.Actions.OfType<KeepCompany>().Single();
        var violations = new List<string>();
        var counts = new SortedDictionary<string, int>();
        void Count(string what) => counts[what] = counts.TryGetValue(what, out int n) ? n + 1 : 1;
        var goals = new HashSet<Point>();
        int restTicks = 0;
        const int Ticks = 6000;
        for (int tick = 0; tick < Ticks; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.LastRequest.Kind == RequestKind.Hold) restTicks++;
            if (Walking(company) is Point goal)
            {
                goals.Add(goal);
                if (Hazard(goal) is string why) { Note(violations, $"t{tick} held goal {goal} {why}"); Count($"goal {why}"); }
            }
            if (brain.LastRequest.Kind == RequestKind.Exact && brain.Positioner.Chosen is Vector2 chosen)
            {
                Point tile = MovementQueries.Tile(chosen);
                if (Hazard(tile) is string why) { Note(violations, $"t{tick} resolved destination {tile} {why}"); Count($"destination {why}"); }
                if (!brain.Positioner.ChosenReturnable) { Note(violations, $"t{tick} resolved destination {tile} outside the returnable region"); Count("destination outside the returnable region"); }
            }
            Vector2 centre = ctx.Npc.Center;
            // The motor's own reading, not a tile lookup: the circle touching a hurting liquid at all is the exposure
            // for this body, and a tile test at its centre would miss a body half in the pool.
            if (ctx.Companion.Motor.InHurtingLiquid) { Note(violations, $"t{tick} body touching {(ctx.Companion.Motor.LiquidKind == 1 ? "lava" : "water")} at {centre}"); Count("body in a hurting liquid"); }
            if (brain.Chooser.IsCollectingWork(centre)) { Note(violations, $"t{tick} movement recorded as productive work"); Count("productive credit"); }
        }
        string ledger = $"distinct goals={goals.Count} rest share={restTicks / (float)Ticks:0.00}; violation ticks by kind: "
            + $"{string.Join(", ", counts.Select(c => $"{c.Key}={c.Value}"))}; first violations: {string.Join("; ", violations)}";
        Require(violations.Count == 0, $"keeping company must never choose, resolve or reach a hazard or a place it cannot come back from; {ledger}");
        Require(goals.Count >= 3, $"a safe neighbourhood must still be strolled, not only rested in; {ledger}");
        Console.WriteLine($"stroll hazards: {goals.Count} distinct goals over {Ticks} ticks, rest share {restTicks / (float)Ticks:0.00}, no hazard chosen, resolved or reached");
    }

    /// <summary>
    /// A flat, empty world with a standing player and every activity registered: no ore, no drops, no pots, light unmeasured, no
    /// enemies. The bound is chosen before the run, from what the behaviour is for rather than from what the code does. Keeping
    /// company must start no jump at all, because nothing on a flat floor needs one and a hop in place is movement for show; it must
    /// rest for between a quarter and three quarters of the window, a sanity envelope around the one-in-three rest picks of the local
    /// method that fails only if it collapses to always walking or always standing; it must hold more than one goal; and no other
    /// activity may be chosen, which is the premise that the world offers nothing else. The window is three game minutes: the local
    /// method's earlier random hop fired on one rest pick in twenty and one stroll pick in twelve, about six hops expected over this
    /// window, while a one-minute window drew none on this seed and could not tell a rule with no hop from a rule that was lucky.
    /// </summary>
    private static void IdleCompanyNeitherHopsNorFreezes()
        => IdleEnvelope(BuildNeighbourhood(hazards: false), "a flat floor");

    /// <summary>The idle window's envelope, on whatever floor <paramref name="ctx"/> stands over: keeping company the only activity
    /// chosen, a rest share between a quarter and three quarters, and more than one goal. The walker's "no jump started" row is gone
    /// with the jump; the body drifts and brakes, and there is no impulse left that could be movement for show.</summary>
    private static void IdleEnvelope(ActionContext ctx, string floor)
    {
        VerifyUsefulAssistance.ClearMeasuredLight();
        var brain = ctx.Companion.Brain;
        var company = brain.Chooser.Actions.OfType<KeepCompany>().Single();
        int restTicks = 0, otherActivity = 0;
        var goals = new HashSet<Point>();
        const int Ticks = 10800;
        for (int tick = 0; tick < Ticks; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.LastAction?.Name != "keep-company") otherActivity++;
            if (brain.LastRequest.Kind == RequestKind.Hold) restTicks++;
            if (Walking(company) is Point goal) goals.Add(goal);
            if (StrollTrace && tick % 600 == 0) Console.WriteLine($"  TRACE {floor} t{tick}: {DescribeStrollCandidates(ctx)}");
        }
        float rest = restTicks / (float)Ticks;
        string ledger = $"rest share={rest:0.00} distinct goals={goals.Count} ticks not keeping company={otherActivity}";
        Require(otherActivity == 0, $"the idle premise needs keeping company to be the only activity chosen on {floor}; {ledger}");
        Require(rest is >= .25f and <= .75f, $"idle company on {floor} must neither always travel nor always hold; {ledger}");
        Require(goals.Count >= 2, $"idle company on {floor} must still stroll; {ledger}");
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
        for (int x = PlayerColumn - Weights.StrollRowsFromPlayer; x <= PlayerColumn + Weights.StrollRowsFromPlayer; x++)
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
            if (!walking && brain.Senses.Intent.Objective.IsSatisfied(ctx.Npc.Center,
                    Collision.CanHitLine(ctx.Npc.position, ctx.Npc.width, ctx.Npc.height, player.position, player.width, player.height)))
                arrivedAt = tick;
        }
        string ledger = $"player stopped at tick {stoppedAt}, companion arrived at tick {arrivedAt}; centre={ctx.Npc.Center} player={player.Bottom}; "
            + $"reunion requested={askedForReunion}; ticks not keeping company={otherActivity}; recovery={brain.FollowRecovery.Active}";
        Require(otherActivity == 0, $"with nothing on offer keeping company must be the only activity; {ledger}");
        Require(askedForReunion && arrivedAt >= 0, $"a player walking away in an empty world must be met where they stop; {ledger}");
        Console.WriteLine($"empty-world reunion: {ledger}");
    }

    /// <summary>Probe switch: every six hundred ticks of an idle window, prints why each tile the walk from the feet reaches is or is not a
    /// stroll goal, by the production predicates, so a companion that rests with goals apparently available can be read.</summary>
    private static readonly bool StrollTrace = Environment.GetEnvironmentVariable("AIC_STROLL_TRACE") == "1";

    private static string DescribeStrollCandidates(ActionContext ctx)
    {
        const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        var clearOfLiquid = typeof(KeepCompany).GetMethod("ClearOfLiquidHazards", Static)!;
        Point player = MovementQueries.Tile(ctx.Senses.Player.Bottom), body = MovementQueries.Tile(ctx.Npc.Center);
        int span = (int)(Weights.CalmBandFar * 0.7f / 16f);
        var parts = new List<string> { $"body={body} player={player} request={ctx.Companion.Brain.LastRequest.Kind} clear={ctx.Companion.Motor.ClearOfTerrain}" };
        foreach (int direction in new[] { -1, 1 })
        {
            var line = new System.Text.StringBuilder(direction < 0 ? " left:" : " right:");
            for (int x = body.X + direction; Math.Abs(x - player.X) <= span; x += direction)
            {
                Point tile = new(x, body.Y);
                string verdict = Math.Abs(x - body.X) < Weights.StrollMinimumTiles ? "near"
                    : Math.Abs(tile.Y - player.Y) > Weights.StrollRowsFromPlayer ? "rows"
                    : !MovementQueries.IsHoverable(tile) ? "no-fit"
                    : !(bool)clearOfLiquid.Invoke(null, new object[] { tile })! ? "liquid"
                    : live::AICompanion.Companion.Brain.Infrastructure.Position.Positioner.PredictedExposureAt(MovementQueries.HoverPoint(tile), ctx.Senses) > Weights.StrollExposureLimit ? "exposed"
                    : !ctx.Companion.Brain.Positioner.IsReturnable(ctx.Senses, tile) ? "unreturnable"
                    : "OK";
                line.Append($" {x},{tile.Y}:{verdict}");
            }
            parts.Add(line.ToString());
        }
        return string.Join(";", parts);
    }

    private static Point? Walking(KeepCompany company)
    {
        const BindingFlags Field = BindingFlags.NonPublic | BindingFlags.Instance;
        bool walking = (bool)typeof(KeepCompany).GetField("walking", Field)!.GetValue(company)!;
        return walking ? MovementQueries.Tile((Vector2)typeof(KeepCompany).GetField("goal", Field)!.GetValue(company)!) : null;
    }

    /// <summary>What is wrong with hovering in this tile, by the scene's own geometry: no room for the body, or inside a hazard's
    /// footprint. Deliberately not the production predicate, so the two can disagree.</summary>
    private static string? Hazard(Point tile)
    {
        if (!MovementQueries.IsHoverable(tile)) return "is not a place the body fits";
        if (tile.X is >= LavaLeft and <= LavaRight && tile.Y >= FloorRow) return "is inside the lava pool";
        if (tile.X is >= WaterLeft and <= WaterRight && tile.Y >= FloorRow) return "is inside the water pit";
        return null;
    }

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
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Motor);
        if (hazards)
        {
            // Each pool must actually hold its liquid, and the body must have somewhere to hover beside it — otherwise
            // "it never went in" would be satisfied by a scene the body could not approach in the first place.
            Require(MovementQueries.IsLava(LavaLeft + 1, FloorRow) && MovementQueries.IsHoverable(new Point(LavaLeft - 2, FloorRow - 1)),
                "the lava pool premise needs lava below the floor line and room to hover beside it");
            Require(MovementQueries.IsWet(WaterLeft + 3, FloorRow + 1) && MovementQueries.IsHoverable(new Point(WaterRight + 2, FloorRow - 1)),
                "the water pit premise needs water in it and room to hover beside it");
            // Both pools are walls to every flood the body runs, which is what makes them hazards it routes around
            // rather than places it may pass through.
            Require(!MovementQueries.IsFreeForOrb(LavaLeft + 1, FloorRow) && !MovementQueries.IsFreeForOrb(WaterLeft + 3, FloorRow + 1),
                "the hazard premise needs both liquids to be walls to the flood under this body's immunities");
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
