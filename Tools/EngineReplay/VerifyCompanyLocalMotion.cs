extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AStar = live::AICompanion.Companion.Brain.SharedMovementSystem.AStar;
using ActionContext = live::AICompanion.Companion.Brain.Behaviours.ActionContext;
using KeepCompany = live::AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance.KeepCompany;
using LimitPlanningWork = live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork;
using MovementQueries = live::AICompanion.Companion.Brain.SharedMovementSystem.MovementQueries;
using Policy = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicy;
using Reach = live::AICompanion.Companion.Brain.SharedMovementSystem.Reachability.Reach;
using RequestKind = live::AICompanion.Companion.Brain.PositionSelection.RequestKind;
using TerrainChanges = live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges;

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
    {
        var ctx = BuildNeighbourhood(hazards: false);
        VerifyUsefulAssistance.ClearMeasuredLight();
        var brain = ctx.Companion.Brain;
        var company = brain.Chooser.Actions.OfType<KeepCompany>().Single();
        int jumps = 0, restTicks = 0, otherActivity = 0;
        bool wasJumping = false;
        var goals = new HashSet<Point>();
        const int Ticks = 10800;
        for (int tick = 0; tick < Ticks; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.LastAction?.Name != "keep-company") otherActivity++;
            bool jumping = ctx.Companion.Motor.AppliedControls.Jump;
            if (jumping && !wasJumping) jumps++;
            wasJumping = jumping;
            if (brain.LastRequest.Kind == RequestKind.Hold) restTicks++;
            if (Walking(company) is Point goal) goals.Add(goal);
        }
        float rest = restTicks / (float)Ticks;
        string ledger = $"jumps started={jumps} rest share={rest:0.00} distinct goals={goals.Count} ticks not keeping company={otherActivity}";
        Require(otherActivity == 0, $"the idle premise needs keeping company to be the only activity chosen; {ledger}");
        Require(jumps == 0, $"an idle companion on a flat floor must not jump for show; {ledger}");
        Require(rest is >= .25f and <= .75f, $"idle company must neither always walk nor always stand; {ledger}");
        Require(goals.Count >= 2, $"idle company must still stroll; {ledger}");
        Console.WriteLine($"idle company: {ledger}");
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

    internal static ActionContext BuildNeighbourhood(bool hazards)
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
