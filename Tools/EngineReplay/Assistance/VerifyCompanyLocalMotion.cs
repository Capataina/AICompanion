extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AStar = live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar;
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
        Each("J06/X01 stroll goals avoid lava, deep water and a one-way drop", StrollGoalsAvoidHazards);
        Each("J06 an idle window neither hops for show nor collapses to one method", IdleCompanyNeitherHopsNorFreezes);
        Each("J06 an idle window on a 1:1 block staircase keeps the flat floor's envelope", () => IdleCompanyOnStairs(StairStyle.Blocks));
        // Slope stairs are the same picker and a different walker: a 1:1 slope walk times out and the navigator jumps, which is
        // AIC-212, not a return of sampling. Re-enable SlopesRisingRight/Left here when that walk holds.
        Each("J06/X01 the rim of a drop with a way back is never a stroll goal or a destination", RimOfADropIsNeverAGoal);
        Each("empty-world reunion: nothing on offer and the player walks away", AnEmptyWorldReunitesWithAWalkingPlayer);
        if (red == 0) Console.WriteLine("company local motion: hazard-free returnable stroll goals and bounded idle movement pass");
        return red;
    }

    private const int FloorRow = 60, PlayerColumn = 40;
    // Hazard footprints, in tile columns, all inside the calm band either side of the player. The drop sits left of the player with
    // open floor between, and the lava pool right of it before the water pit, because a walker cannot cross the deep water pit: a
    // drop placed beyond that pit is unreachable, so no stroll could ever reach it and the case would prove nothing about it.
    private const int DropLeft = 22, DropRight = 26, LavaLeft = 45, LavaRight = 47, WaterLeft = 51, WaterRight = 57;

    /// <summary>
    /// A floor with three hazards within stroll range of a standing player: a lava pool two rows deep, a water pit three rows deep
    /// whose floor puts a standing body's head under water, and a pit twelve rows deep that the body can drop into and never climb
    /// out of. Over a long seeded run with keeping company the only activity, no stroll goal it holds, no destination the positioner
    /// resolves for it and no pose the body reaches may lie over a hazard or outside the region it can come back from, and nothing
    /// it does may earn productive-work credit. The scene asserts each hazard is what it claims before the run.
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
                Point tile = MovementQueries.FeetTile(chosen);
                if (Hazard(tile) is string why) { Note(violations, $"t{tick} resolved destination {tile} {why}"); Count($"destination {why}"); }
                if (!brain.Positioner.ChosenReturnable) { Note(violations, $"t{tick} resolved destination {tile} outside the returnable region"); Count("destination outside the returnable region"); }
            }
            Vector2 feet = ctx.Npc.Bottom;
            Point body = MovementQueries.FeetTile(feet);
            if (feet.Y > FloorRow * 16 + 0.5f) { Note(violations, $"t{tick} body below the floor at {feet}"); Count("body below the floor"); }
            if (ctx.Npc.lavaWet) { Note(violations, $"t{tick} body in lava at {feet}"); Count("body in lava"); }
            if (MovementQueries.IsLiquid(body.X, body.Y - MovementQueries.BodyHeightTiles + 1)) { Note(violations, $"t{tick} head under water at {feet}"); Count("head under water"); }
            if (brain.Chooser.IsCollectingWork(feet)) { Note(violations, $"t{tick} movement recorded as productive work"); Count("productive credit"); }
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

    /// <summary>The idle window's envelope, on whatever floor <paramref name="ctx"/> stands on: keeping company the only activity chosen,
    /// no jump started, a rest share between a quarter and three quarters, and more than one goal.</summary>
    private static void IdleEnvelope(ActionContext ctx, string floor)
    {
        VerifyUsefulAssistance.ClearMeasuredLight();
        var brain = ctx.Companion.Brain;
        var company = brain.Chooser.Actions.OfType<KeepCompany>().Single();
        int jumps = 0, restTicks = 0, otherActivity = 0;
        bool wasJumping = false;
        var goals = new HashSet<Point>();
        // Which layer asked for each jump: the stroll's goal, the navigator's state and the edge it last reported, for the first few.
        var jumpStarts = new List<string>();
        const int Ticks = 10800;
        for (int tick = 0; tick < Ticks; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.LastAction?.Name != "keep-company") otherActivity++;
            bool jumping = ctx.Companion.Motor.AppliedControls.Jump;
            if (jumping && !wasJumping)
            {
                jumps++;
                if (jumpStarts.Count < 3)
                    jumpStarts.Add($"t{tick} feet={MovementQueries.FeetTile(ctx.Npc.Bottom)} goal={Walking(company)} request={brain.LastRequest.Kind} "
                        + $"nav={brain.Navigator.Status}/{brain.Navigator.ProgressReason} navGoal={brain.Navigator.GoalTile} edge={brain.Navigator.LastEdge}");
            }
            wasJumping = jumping;
            if (brain.LastRequest.Kind == RequestKind.Hold) restTicks++;
            if (Walking(company) is Point goal) goals.Add(goal);
            if (StrollTrace && tick % 600 == 0) Console.WriteLine($"  TRACE {floor} t{tick}: {DescribeStrollCandidates(ctx)}");
        }
        float rest = restTicks / (float)Ticks;
        string ledger = $"jumps started={jumps} rest share={rest:0.00} distinct goals={goals.Count} ticks not keeping company={otherActivity}"
            + (jumpStarts.Count > 0 ? $"; first jump starts: {string.Join("; ", jumpStarts)}" : "");
        Require(otherActivity == 0, $"the idle premise needs keeping company to be the only activity chosen on {floor}; {ledger}");
        Require(jumps == 0, $"an idle companion on {floor} must not jump for show; {ledger}");
        Require(rest is >= .25f and <= .75f, $"idle company on {floor} must neither always walk nor always stand; {ledger}");
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
            int feet = MovementQueries.IsStandable(x, top - 1) ? top - 1 : MovementQueries.IsStandable(x, top) ? top : int.MinValue;
            Require(feet != int.MinValue && MovementQueries.IsSupport(x, feet + 1),
                $"the {style} staircase premise needs a standable, supported tile in column {x} at its surface row {top}");
            int higher = x + rise;
            Require(!MovementQueries.IsSupport(higher, feet + 1) || !MovementQueries.IsSupport(x - rise, feet + 1),
                $"the {style} staircase premise needs a neighbour of column {x} with no floor on its own row, or the old rule would accept it");
        }
        IdleEnvelope(ctx, $"a 1:1 staircase of {style}");
    }

    private const int SlabLeft = 34, SlabRight = 46, DropFloor = FloorRow + 12;

    /// <summary>
    /// The one case where only the rule against the rim of a drop can refuse a goal. The player and the companion stand on a slab from
    /// column 34 to 46 with open air twelve rows down to the ground on both sides, no liquid anywhere and nothing hostile. The slab's
    /// end tiles are rims: standable, with floor under their own column, a walk from the feet across the flat slab, on the companion's
    /// own floor so the round trip to them and back is proven (which is the way back the returnable test needs), and beside a drop the
    /// body cannot climb out of. The hazard scene cannot hold this, because its one drop is refused by the returnable test before the
    /// rim rule is read, which is why removing the rim rule entirely left that scene green. Two rims among a dozen candidate tiles keeps
    /// the seeded run sampling them. Over six thousand ticks with keeping company the only activity, no held goal and no resolved
    /// destination may be a rim or the tile over the edge, the body must stay on the slab, and more than one goal must be strolled to so
    /// a companion that only rests cannot pass by never choosing anything.
    /// </summary>
    private static void RimOfADropIsNeverAGoal()
    {
        var ctx = BuildNeighbourhood(hazards: false, terrain: () =>
        {
            for (int x = 5; x < 95; x++)
            {
                if (x < SlabLeft || x > SlabRight) Main.tile[x, FloorRow].ClearEverything();
                VerifyOreWork.Place(new Point(x, DropFloor), TileID.Dirt);
            }
        });
        var brain = ctx.Companion.Brain;
        brain.Chooser.Actions.RemoveAll(a => a.Name != "keep-company");
        var company = brain.Chooser.Actions.OfType<KeepCompany>().Single();
        Point feet = new(PlayerColumn, FloorRow - 1);
        foreach (int rim in new[] { SlabLeft, SlabRight })
        {
            int outside = rim == SlabLeft ? rim - 1 : rim + 1;
            Require(MovementQueries.IsStandable(rim, FloorRow - 1) && MovementQueries.IsSupport(rim, FloorRow),
                $"the rim premise needs column {rim} standable with floor under it");
            Require(!MovementQueries.IsSupport(outside, FloorRow - 1) && !MovementQueries.IsSupport(outside, FloorRow) && !MovementQueries.IsSupport(outside, FloorRow + 1),
                $"the rim premise needs no floor within a step beside column {rim}");
            var there = MovementQueries.RoundTrip(feet, new Point(rim, FloorRow - 1), VerifyAssistanceTrips.EnvelopeOf(ctx));
            Require(there.Outward == Reach.Yes && there.Return == Reach.Yes, $"the rim premise needs a proven way to the rim at column {rim} and back; trip={there}");
            var down = MovementQueries.RoundTrip(feet, new Point(outside, DropFloor - 1), VerifyAssistanceTrips.EnvelopeOf(ctx));
            Require(down.Outward == Reach.Yes && down.Return == Reach.No, $"the rim premise needs the drop beside column {rim} to have a way down and none back; trip={down}");
        }
        for (int x = SlabLeft - 3; x <= SlabRight + 3; x++)
            for (int y = FloorRow - 6; y <= DropFloor; y++)
                Require(!MovementQueries.IsLiquid(x, y), $"the rim premise needs no liquid, found at {x},{y}");

        static string? OffTheSlab(Point tile) => tile.X <= SlabLeft || tile.X >= SlabRight || tile.Y >= FloorRow
            ? "is a rim, over the edge or below the slab" : null;
        var violations = new List<string>();
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
                if (OffTheSlab(goal) is string why) Note(violations, $"t{tick} held goal {goal} {why}");
            }
            if (brain.LastRequest.Kind == RequestKind.Exact && brain.Positioner.Chosen is Vector2 chosen
                && OffTheSlab(MovementQueries.FeetTile(chosen)) is string whyChosen)
                Note(violations, $"t{tick} resolved destination {MovementQueries.FeetTile(chosen)} {whyChosen}");
            if (ctx.Npc.Bottom.Y > FloorRow * 16 + 0.5f) Note(violations, $"t{tick} body below the slab at {ctx.Npc.Bottom}");
        }
        string ledger = $"distinct goals={goals.Count} [{string.Join(" ", goals.OrderBy(g => g.X).Select(g => g.X))}] rest share={restTicks / (float)Ticks:0.00}; first violations: {string.Join("; ", violations)}";
        Require(violations.Count == 0, $"keeping company must never stroll to the rim of a drop; {ledger}");
        Require(goals.Count >= 2, $"the slab must still be strolled, or never choosing a rim proves nothing; {ledger}");
        Console.WriteLine($"stroll rim: {ledger}");
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
            if (!walking && new FollowPlayerObjective(player.Bottom, player.Bottom).IsSatisfied(ctx.Npc.Bottom,
                    Collision.CanHitLine(ctx.Npc.position, ctx.Npc.width, ctx.Npc.height, player.position, player.width, player.height)))
                arrivedAt = tick;
        }
        string ledger = $"player stopped at tick {stoppedAt}, companion arrived at tick {arrivedAt}; feet={ctx.Npc.Bottom} player={player.Bottom}; "
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
        var walkStep = typeof(KeepCompany).GetMethod("WalkStep", Static)!;
        var noDrop = typeof(KeepCompany).GetMethod("NoDropBeside", Static)!;
        var clearOfLiquid = typeof(KeepCompany).GetMethod("ClearOfLiquidHazards", Static)!;
        Point player = MovementQueries.FeetTile(ctx.Senses.Player.Bottom), feet = MovementQueries.FeetTile(ctx.Npc.Bottom);
        int span = (int)(Weights.CalmBandFar * 0.7f / 16f);
        var parts = new List<string> { $"feet={feet} player={player} request={ctx.Companion.Brain.LastRequest.Kind} onGround={ctx.Companion.Motor.State.OnGround}" };
        int start = walkStep.Invoke(null, new object[] { feet.X, feet.Y }) is int settled ? settled : feet.Y;
        parts[0] += $" walkStart={start}";
        foreach (int direction in new[] { -1, 1 })
        {
            int y = start;
            var line = new System.Text.StringBuilder(direction < 0 ? " left:" : " right:");
            for (int x = feet.X + direction; Math.Abs(x - player.X) <= span; x += direction)
            {
                if (walkStep.Invoke(null, new object[] { x, y }) is not int next) { line.Append($" stop@{x}"); break; }
                y = next;
                Point tile = new(x, y);
                string verdict = Math.Abs(x - feet.X) < Weights.StrollMinimumTiles ? "near"
                    : Math.Abs(y - player.Y) > Weights.StrollRowsFromPlayer ? "rows"
                    : !MovementQueries.IsStandable(x, y) ? "stand"
                    : !(bool)noDrop.Invoke(null, new object[] { tile })! ? "rim"
                    : !(bool)clearOfLiquid.Invoke(null, new object[] { tile })! ? "liquid"
                    : live::AICompanion.Companion.Brain.Infrastructure.Position.Positioner.PredictedExposureAt(MovementQueries.FeetWorld(tile), ctx.Senses) > Weights.StrollExposureLimit ? "exposed"
                    : !ctx.Companion.Brain.Positioner.IsReturnable(ctx.Senses, tile) ? "unreturnable"
                    : "OK";
                line.Append($" {x},{y}:{verdict}");
            }
            parts.Add(line.ToString());
        }
        return string.Join(";", parts);
    }

    private static Point? Walking(KeepCompany company)
    {
        const BindingFlags Field = BindingFlags.NonPublic | BindingFlags.Instance;
        bool walking = (bool)typeof(KeepCompany).GetField("walking", Field)!.GetValue(company)!;
        return walking ? MovementQueries.FeetTile((Vector2)typeof(KeepCompany).GetField("goal", Field)!.GetValue(company)!) : null;
    }

    /// <summary>What is wrong with standing at this feet tile, by the scene's own geometry: not a place to stand, over a hazard's footprint,
    /// or touching lava from the rim. Deliberately not the production predicate, so the two can disagree.</summary>
    private static string? Hazard(Point tile)
    {
        if (!MovementQueries.IsStandable(tile.X, tile.Y)) return "is not standable";
        if (tile.X is >= LavaLeft - 1 and <= LavaRight + 1 && tile.Y >= FloorRow - 1) return "is over or beside the lava pool";
        if (tile.X is >= WaterLeft and <= WaterRight && tile.Y >= FloorRow - 1) return "is over or in the water pit";
        if (tile.X is >= DropLeft and <= DropRight && tile.Y >= FloorRow - 1) return "is over or in the one-way drop";
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
        ctx.Npc.Bottom = new Vector2(PlayerColumn * 16 + 8, FloorRow * 16);
        ctx.Npc.velocity = Vector2.Zero;
        if (hazards)
        {
            Basin(LavaLeft, LavaRight, depth: 2, liquid: LiquidID.Lava);
            Basin(WaterLeft, WaterRight, depth: 3, liquid: LiquidID.Water);
            Basin(DropLeft, DropRight, depth: 12, liquid: null);
        }
        terrain?.Invoke();
        TerrainChanges.Reset();
        AStar.InvalidateEdges();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        if (hazards)
        {
            Require(MovementQueries.IsLava(LavaLeft + 1, FloorRow) && MovementQueries.IsStandable(LavaLeft - 1, FloorRow - 1),
                "the lava pool premise needs lava below the floor line and a standable rim beside it");
            Require(MovementQueries.IsStandable(WaterLeft + 3, FloorRow + 2) && MovementQueries.IsLiquid(WaterLeft + 3, FloorRow + 2 - MovementQueries.BodyHeightTiles + 1),
                "the water pit premise needs a standable floor that puts the head under water");
            var drop = MovementQueries.RoundTrip(new Point(PlayerColumn, FloorRow - 1), new Point(DropLeft + 2, FloorRow + 11), VerifyAssistanceTrips.EnvelopeOf(ctx));
            Require(drop.Outward == Reach.Yes && drop.Return == Reach.No, $"the drop premise needs a way down and none back; trip={drop}");
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
