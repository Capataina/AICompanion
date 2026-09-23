#nullable enable

extern alias live;

using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Activities.Gathering;
using live::AICompanion.Companion.Brain.Infrastructure.Movement;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using FindToolAccess = live::AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;

/// <summary>
/// The hand works the target the course bound, and nothing else.
///
/// <para>Until 23 September 2026 mining and chopping each ran a private nearest-first search and swung at what
/// it found, while the body flew to the pose of the step the course had bound. Two choosers agree only when both
/// pick the same nearest thing, so every scene here is built so that they would not: two targets both inside
/// tool reach of where the companion already hovers, the nearer one worth less to the course because its
/// material's census is larger. The old search strikes the nearer target; the course binds the other; the row
/// requires the strike to land on the bound one.</para>
///
/// <para>Each scene first asserts its premise — that the course really bound the target nearest-first would not
/// pick — because a scene where both choosers agree passes whichever of them the hand obeys.</para>
///
/// <para>The steps are real: the production census, the production binder and the course's own decision
/// produce the binding, and <c>OwnCurrentActivity.Select</c> hands it to the activity exactly as the tick does.
/// Only the body is held still, so the strike is about the hand rather than about where the body drifted.</para>
/// </summary>
internal static class VerifyGatheringActsOnTheBinding
{
    public static int Run()
        => RunOneRow.Case("the pickaxe strikes the ore the course bound, not the nearest ore", ThePickaxeStrikesTheBoundOre)
         + RunOneRow.Case("the axe strikes the trunk the course bound, not the nearest trunk", TheAxeStrikesTheBoundTrunk)
         + RunOneRow.Case("a bound ore mined out from under the step is refused by name and no neighbour is struck",
               AMinedOutBoundOreIsRefusedNotSubstituted)
         + RunOneRow.Case("a bound ore that became another ore is refused by name and not struck",
               AChangedBoundOreIsRefused)
         + RunOneRow.Case("a mine step strikes the tile its use names, which is not the vein's identity tile when that one is sealed",
               AMineStepStrikesItsUseTileNotTheVeinIdentity);

    /// <summary>
    /// A two-tile copper vein whose first tile in sorted order is sealed on every open side and whose second is
    /// exposed. The ore opportunity's identity names the vein by that first tile, while the census admits the vein
    /// through the second, so the step's use — and its stand — are about the exposed tile. The pickaxe must strike
    /// the use's tile.
    ///
    /// <para>It also prints the work tile the tick's position request carries for the same step, because
    /// `ExecuteCourseBinding.WorkTileOf` reads the opportunity's identity rather than its use: on this scene the two
    /// disagree, which is a property of that reader (outside this lane) and is printed here rather than asserted.</para>
    /// </summary>
    private static void AMineStepStrikesItsUseTileNotTheVeinIdentity()
    {
        Point sealedFirst = new(23, 59), exposed = new(24, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, exposed);
        WorkPolicies.Chopping = WorkPolicy.Disabled;
        VerifyOreWork.Place(sealedFirst, TileID.Copper);
        foreach (Point wall in new[] { new Point(22, 59), new Point(23, 58), new Point(22, 58), new Point(22, 57), new Point(23, 57) })
            VerifyOreWork.Place(wall, TileID.Dirt);
        // Hovering to the right of the exposed tile, whose right face is the one it shows.
        ctx.Npc.Center = new Vector2(27 * 16 + 8, ctx.Npc.Center.Y);
        TerrainChanges.Reset();
        VerifyOreWork.ResettleReach(ctx);
        Require(FindToolAccess.InReach(ctx.Npc.Center, exposed), $"premise: the exposed tile must be in reach from the hover; centre={ctx.Npc.Center}");
        Require(FindToolAccess.Approach(sealedFirst, ctx.Npc.Center, ctx.Senses.Reach, out _) != live::AICompanion.Companion.Brain.Infrastructure.Movement.Reachability.Reach.Yes,
            "premise: the vein's first tile must have no approach, or identity and use coincide and the row proves nothing");
        StepBinding step = BindThroughTheCourse(ctx, "mine");
        bool named = GatheringOpportunityBinder.TryReadUse(step, "mine-target", out Point use, out _);
        Require(step.Opportunity.Target.EndsWith($":{sealedFirst.X},{sealedFirst.Y}", StringComparison.Ordinal) && named && use == exposed,
            $"premise: the opportunity must be named by the sealed tile and its use must be the exposed one; opportunity '{step.Opportunity.Target}' use '{step.NativeUseId}'");
        var request = live::AICompanion.Companion.Brain.Infrastructure.Selection.ExecuteCourseBinding.RequestFor(step, new Vector2(0, 0));
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail(
            $"MEASURE mine step on a sealed-first vein: use tile {use}, position request work tile {request.WorkTile?.ToString() ?? "none"}, opportunity '{step.Opportunity.Target}'");
        var strike = PerformOnce(ctx, step);
        Require(strike == exposed, $"the pickaxe struck {Describe(strike)} while the step's use names {exposed}");
    }

    /// <summary>
    /// Copper one tile from the companion and tin three tiles away, both inside tool reach from where it
    /// hovers. A second copper vein far off in the census window makes the copper census four tiles, so one
    /// swing at the near copper is worth a quarter of what one swing at the tin is worth, and the course binds
    /// the tin while a nearest-first search takes the copper.
    /// </summary>
    private static void ThePickaxeStrikesTheBoundOre()
    {
        // The tin floats two rows up so the copper does not stand between it and the companion's eye.
        Point nearCopper = new(22, 59), tin = new(24, 57);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, nearCopper);
        WorkPolicies.Chopping = WorkPolicy.Disabled;
        Main.tileSolid[TileID.Tin] = true;
        VerifyOreWork.Place(tin, TileID.Tin);
        foreach (int x in new[] { 60, 61, 62 }) VerifyOreWork.Place(new Point(x, 59), TileID.Copper);
        TerrainChanges.Reset();
        VerifyOreWork.ResettleReach(ctx);
        Require(FindToolAccess.InReach(ctx.Npc.Center, nearCopper) && FindToolAccess.InReach(ctx.Npc.Center, tin),
            $"premise: both ores must be inside tool reach of where the companion hovers, so neither needs a flight; centre={ctx.Npc.Center}");
        Require(Vector2.DistanceSquared(ctx.Npc.Center, nearCopper.ToWorldCoordinates()) < Vector2.DistanceSquared(ctx.Npc.Center, tin.ToWorldCoordinates()),
            "premise: the copper must be the nearer ore, or a nearest-first search would pick the tin too");

        StepBinding step = BindThroughTheCourse(ctx, "mine");
        Require(GatheringOpportunityBinder.TryReadUse(step, "mine-target", out Point boundTile, out _) && boundTile == tin,
            $"premise: the course must bind the tin, which nearest-first would not pick; bound use '{step.NativeUseId}'");

        var strike = PerformOnce(ctx, step);
        Require(strike == tin,
            $"the pickaxe struck {Describe(strike)} while the course bound the tin at {tin}: the hand worked a target of its own choosing");
        Require(Main.tile[nearCopper.X, nearCopper.Y].HasTile, "the near copper the course did not bind must still stand");
    }

    /// <summary>
    /// The same shape for trees: an ordinary trunk one tile from the companion and a palm three tiles away, two
    /// more ordinary trunks far off, so the palm's census is its own and the course binds it while a nearest-first
    /// search takes the ordinary trunk.
    /// </summary>
    private static void TheAxeStrikesTheBoundTrunk()
    {
        Point nearTrunk = new(22, 59), palm = new(25, 59);
        Point placeholder = new(40, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        WorkPolicies.Chopping = WorkPolicy.Opportunistic;
        foreach (ushort type in new[] { TileID.Trees, TileID.PalmTree })
        {
            Main.tileAxe[type] = true;
            Main.tileSolid[type] = false;
        }
        TileID.Sets.IsATreeTrunk[TileID.Trees] = true;
        VerifyOreWork.Place(nearTrunk, TileID.Trees);
        VerifyOreWork.Place(palm, TileID.PalmTree);
        foreach (int x in new[] { 60, 64 }) VerifyOreWork.Place(new Point(x, 59), TileID.Trees);
        TerrainChanges.Reset();
        VerifyOreWork.ResettleReach(ctx);
        Require(FindToolAccess.InReach(ctx.Npc.Center, nearTrunk) && FindToolAccess.InReach(ctx.Npc.Center, palm),
            $"premise: both trunks must be inside tool reach of where the companion hovers; centre={ctx.Npc.Center}");

        StepBinding step = BindThroughTheCourse(ctx, "chop");
        Require(GatheringOpportunityBinder.TryReadUse(step, "chop-target", out Point boundTrunk, out _) && boundTrunk == palm,
            $"premise: the course must bind the palm, which nearest-first would not pick; bound use '{step.NativeUseId}'");

        ctx.Companion.Brain.Activity.Select(ctx.Companion.Brain.Actions.Single(a => a.Name == "chop"), ctx, step);
        ctx.Companion.Brain.Activity.BeginExecution();
        ctx.Companion.Brain.Actions.Single(a => a.Name == "chop").Execute(ctx);
        Point? struck = ctx.Companion.Chopper.LastOutcome?.Target;
        Require(struck == palm,
            $"the axe struck {Describe(struck)} while the course bound the palm at {palm}: the hand worked a trunk of its own choosing");
    }

    /// <summary>
    /// A two-tile copper vein, the course binds one tile, and the player mines that tile before the swing. The
    /// companion must refuse the step by name — twice, on two ticks carrying the same stale step — and must not
    /// strike the other tile of the vein, which is the substitution a private search makes by relocating within
    /// its vein. The attempt the refusal ended must close with that name.
    /// </summary>
    private static void AMinedOutBoundOreIsRefusedNotSubstituted()
    {
        // Stacked rather than side by side, so both tiles show a face to the companion hovering at their left
        // and a substitution would actually land from where it is.
        Point first = new(22, 59), second = new(22, 58);
        // Set up on the lower tile alone, because the scene's floor sits one row under the highest ore it is
        // given, and the upper tile placed afterwards.
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, first);
        VerifyOreWork.Place(second, TileID.Copper);
        TerrainChanges.Reset();
        VerifyOreWork.ResettleReach(ctx);
        WorkPolicies.Chopping = WorkPolicy.Disabled;
        StepBinding step = BindThroughTheCourse(ctx, "mine");
        Require(GatheringOpportunityBinder.TryReadUse(step, "mine-target", out Point boundTile, out _) && (boundTile == first || boundTile == second),
            $"premise: the course must bind a tile of the vein; bound use '{step.NativeUseId}'");
        Point other = boundTile == first ? second : first;
        Require(FindToolAccess.InReach(ctx.Npc.Center, other),
            "premise: the other tile of the vein must be inside reach, so a substitution would actually land");
        // The step is first performed as bound, so the job is under way on this vein when the player takes the
        // tile — the state play is in, where a step is bound, struck and then overtaken by the player's pick.
        Require(PerformOnce(ctx, step) == boundTile && Main.tile[boundTile.X, boundTile.Y].HasTile,
            $"premise: the first swing must land on the bound tile and leave it standing; the pickaxe struck {Describe(ctx.Companion.Miner.LastOutcome?.Target)}");
        long firstStrike = ctx.Companion.Miner.LastOutcome!.Value.Attempt;

        Tile taken = Main.tile[boundTile.X, boundTile.Y];
        taken.ClearEverything();
        TerrainChanges.Changed(boundTile.X, boundTile.Y);
        var mine = ctx.Companion.Brain.Actions.OfType<MineOre>().Single();
        for (int tick = 0; tick < 2; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            // The pick is ready on every one of these ticks, so a substitution has nothing stopping it but the rule.
            while (!ctx.Companion.Miner.Ready) ctx.Companion.Miner.Tick();
            foreach (CompanionAction activity in ctx.Companion.Brain.Actions) activity.Prepare(ctx);
            PerformOnce(ctx, step);
            Require(ctx.Companion.Miner.LastOutcome?.Attempt == firstStrike,
                $"tick {tick}: a bound ore the player mined out must not be replaced by another; the pickaxe struck {Describe(ctx.Companion.Miner.LastOutcome?.Target)}");
            Require(mine.Status == "bound tile no longer ore",
                $"tick {tick}: the refusal must say the bound tile is no longer ore; status '{mine.Status}'");
        }
        // The attempt that struck once and was then refused closes on the next selection with the refusal's
        // name, partial because its one strike was real work.
        var concluded = ctx.Companion.Brain.Activity.RecentAttempts.Where(a => a.Activity == "mine").ToList();
        Require(concluded.Any(a => a is { Status: AttemptStatus.Partial, Cause: "bound-tile-no-longer-ore", ProductiveEffects: 1 }),
            $"the attempt the refusal ended must close partial with the refusal's name; got [{string.Join("; ", concluded)}]");
        Require(Main.tile[other.X, other.Y].HasTile, "the rest of the vein must still stand");
    }

    /// <summary>
    /// A copper tile bound for a swing turns into tin before it. The pickaxe could damage tin, so without the
    /// material check the hand would strike ore nobody bound; the step is refused by name instead.
    /// </summary>
    private static void AChangedBoundOreIsRefused()
    {
        Point ore = new(22, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        WorkPolicies.Chopping = WorkPolicy.Disabled;
        Main.tileSolid[TileID.Tin] = true;
        StepBinding step = BindThroughTheCourse(ctx, "mine");
        Tile changed = Main.tile[ore.X, ore.Y];
        changed.TileType = TileID.Tin;
        TerrainChanges.Changed(ore.X, ore.Y);
        Require(ctx.Companion.Miner.CanMine(ore, live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.TileMiner.PickaxeFor(ctx.Player).pick),
            "premise: the pickaxe must be able to damage the replacement, or a missing check could not strike it anyway");
        var mine = ctx.Companion.Brain.Actions.OfType<MineOre>().Single();
        PerformOnce(ctx, step);
        Require(ctx.Companion.Miner.LastOutcome == null,
            $"a bound tile that became another ore must not be struck; the pickaxe struck {Describe(ctx.Companion.Miner.LastOutcome?.Target)}");
        Require(mine.Status == "bound tile material changed",
            $"the refusal must say the bound tile's material changed; status '{mine.Status}'");
    }

    /// <summary>The course's own decision through the production census and binder, with the body held still,
    /// required to have named <paramref name="activity"/> and bound a step for it.</summary>
    private static StepBinding BindThroughTheCourse(ActionContext ctx, string activity)
    {
        string chosen = VerifyOreWork.DecideThroughTheCourse(ctx);
        var brain = ctx.Companion.Brain;
        Require(chosen == activity && brain.Course.Last.Binding is not null,
            $"premise: the course must choose {activity} with a bound step; chose '{chosen}' reason '{brain.Course.Last.Reason}' "
            + $"admitted {string.Join(" | ", brain.Course.Admitted.Select(a => $"{a.Domain}:u{a.Usable}/n{a.Unresolved}/x{a.Unusable}:{a.Reason}"))}");
        return brain.Course.Last.Binding!;
    }

    /// <summary>Hand the step to its activity exactly as the tick does, execute once, and return the tile the
    /// pickaxe struck on this call, if it struck at all.</summary>
    private static Point? PerformOnce(ActionContext ctx, StepBinding step)
    {
        var brain = ctx.Companion.Brain;
        var mine = brain.Actions.Single(a => a.Name == "mine");
        long before = ctx.Companion.Miner.LastOutcome?.Attempt ?? -1;
        brain.Activity.Select(mine, ctx, step);
        brain.Activity.BeginExecution();
        mine.Execute(ctx);
        return ctx.Companion.Miner.LastOutcome is { } outcome && outcome.Attempt != before ? outcome.Target : null;
    }

    private static string Describe(Point? tile) => tile is { } t ? t.ToString() : "nothing";

    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
