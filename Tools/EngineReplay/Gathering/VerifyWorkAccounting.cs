extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using MineOre = live::AICompanion.Companion.Brain.Activities.Gathering.MineOre;
using TileMiner = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.TileMiner;
using WorkPolicies = live::AICompanion.Companion.Brain.Activities.WorkPolicies;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using AttemptStatus = live::AICompanion.Companion.Brain.Activities.AttemptStatus;
using AttemptOutcome = live::AICompanion.Companion.Brain.Activities.AttemptOutcome;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;

/// <summary>
/// What a mining job reports it did against what the world shows: a tracked portion cleared while the vein continues,
/// cancelled work with ore left in the ground, and equal remaining work treated equally whoever removed the earlier ore.
/// </summary>
internal static class VerifyWorkAccounting
{
    public static int Run()
    {
        WorkPolicy mining = WorkPolicies.Mining;
        int red = 0;
        void Each(string name, Action fixture)
        {
            LimitPlanningWork.Unbounded = true;
            try { red += RunOneRow.GreenOrRed(name, fixture); }
            finally { WorkPolicies.Mining = mining; LimitPlanningWork.Unbounded = false; }
        }
        Each("M05 cleared portion of a continuing vein", AClearedPortionOfAContinuingVeinIsPartial);
        Each("P05 equal remaining work", EqualRemainingWorkIsTreatedEquallyWhoeverRemovedTheRest);
        Each("cancelled work by range", WorkCancelledByRangeLeavesPartialWorkAndOreInPlace);
        if (red == 0) Console.WriteLine("work accounting: continuing veins, equal remaining work and cancelled work pass");
        return red;
    }

    /// <summary>
    /// The companion clears both tiles of a two-tile vein with its own native strikes while the player places a third copper
    /// tile touching it. Every tracked coordinate ends empty, but the vein did not: calling that the companion's complete work
    /// is a completion the world contradicts. The attempt must end partial and say the vein continues, and the next
    /// discovery must find the remaining tile as new work.
    /// </summary>
    private static void AClearedPortionOfAContinuingVeinIsPartial()
    {
        Point a = new(24, 59), b = new(25, 59), added = new(26, 59);
        var (mine, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, a, b);
        VerifyOreWork.AcceptBoundOre(ctx, mine, "the continuing-vein fixture needs a bound vein");
        Require(mine.RemainingTiles == 2, $"the continuing-vein fixture needs a two-tile job; remaining={mine.RemainingTiles}");
        int job = mine.JobId;
        mine.BeginAttempt();
        VerifyOreWork.Place(added, TileID.Copper);
        TerrainChanges.Changed(added.X, added.Y);
        int effects = MineUntilGone(mine, ctx, a, b);
        Require(effects > 0 && !Main.tile[a.X, a.Y].HasTile && !Main.tile[b.X, b.Y].HasTile && Main.tile[added.X, added.Y].HasTile,
            $"the companion must clear both tracked tiles with its own strikes and leave the added one; effects={effects}");
        Require(mine.LastConclusion is { JobId: var ended, ObservedClear: true, CompanionRemovals: 2 } && ended == job,
            $"the tracked portion must be observed clear and wholly the companion's removal; got {mine.LastConclusion}");
        var conclusion = mine.ConcludeAttempt(effects);
        Require(conclusion is { Status: AttemptStatus.Partial, Cause: "tracked-portion-clear-vein-continues" },
            $"a cleared tracked portion of a vein that continues is partial work, not a completion; got {conclusion}");
        VerifyOreWork.AcceptBoundOre(ctx, mine, "the tile the vein continues into must be bound");
        Require(mine.JobId > job && mine.TargetTile == added, $"the tile the vein continues into must become new work; job={mine.JobId} target={mine.TargetTile}");
    }

    /// <summary>
    /// A two-tile vein where one tile is removed, in one scene by the companion's own native strikes and in the other by the
    /// player, with the companion then standing on the same feet. Equal remaining work must yield the same offer: value,
    /// forecast, remaining tiles, target and native remaining hits. The companion's cooldown from its own last strike is
    /// allowed to finish first, because that is the companion's tool state rather than the work. A third scene shows what is
    /// not equal: the player's partial pick damage on the remaining tile lives in the player's own hit table, which Terraria
    /// keeps per hitter, so the companion's forecast for that tile is unchanged by it.
    /// </summary>
    private static void EqualRemainingWorkIsTreatedEquallyWhoeverRemovedTheRest()
    {
        Point remaining = new(24, 89), removed = new(25, 89);
        Vector2 feet = new(20 * 16 + 8, 90 * 16);
        // What the course offers for the remaining tile is compared, because the course is what offers work now:
        // its worth for mining, the step's travel and use, the job's remaining count, the bound tile and the
        // census's own remaining damage on it.
        (float Value, double Travel, double Use, int Tiles, Point? Target, int? Damage) Scene(string remover)
        {
            var (mine, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, remaining, removed);
            ctx.Npc.Bottom = feet;
            TerrainChanges.Reset();
            VerifyOreWork.AcceptBoundOre(ctx, mine, $"{remover}: the scene needs a bound vein");
            Require(mine.RemainingTiles == 2, $"{remover}: the scene needs a two-tile job; remaining={mine.RemainingTiles}");
            Item pick = TileMiner.PickaxeFor(ctx.Player);
            if (remover == "companion")
            {
                for (int guard = 0; guard < 20 && Main.tile[removed.X, removed.Y].HasTile; guard++)
                {
                    Require(ctx.Companion.Miner.Swing(removed, pick), "the companion scene needs admitted native strikes");
                    for (int tick = 0; tick < pick.useTime; tick++) ctx.Companion.Miner.Tick();
                }
                Require(ctx.Companion.Miner.Ready, "the companion's cooldown must have finished before comparing");
            }
            else
            {
                Main.tile[removed.X, removed.Y].ClearEverything();
                if (remover == "player-with-partial-damage")
                {
                    Player player = ctx.Player;
                    player.PickTile(remaining.X, remaining.Y, pick.pick);
                    Require(Main.tile[remaining.X, remaining.Y].HasTile && player.hitTile.TryFinding(remaining.X, remaining.Y, 1) >= 0,
                        "the partial-damage scene needs the player's own hit table to hold damage on a tile still present");
                }
            }
            Require(!Main.tile[removed.X, removed.Y].HasTile && Main.tile[remaining.X, remaining.Y].HasTile, $"{remover}: exactly one tile must be gone");
            TerrainChanges.Changed(removed.X, removed.Y);
            ctx.Npc.Bottom = feet;
            var step = DriveGatheringThroughTheCourse.Bind(ctx, "mine", $"{remover}: the remaining tile must be bound");
            ctx.Companion.Brain.Activity.Select(mine, ctx, step);
            float value = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.ReadCourseWorthPerActivity.Of(ctx.Companion.Brain, mine).Raw;
            int? damage = DriveGatheringThroughTheCourse.Site(ctx, "mine-target", remaining)?.Work?.DamageRemaining;
            return (value, step.TravelTicks, step.UseTicks, mine.RemainingTiles, mine.TargetTile, damage);
        }
        var byCompanion = Scene("companion");
        var byPlayer = Scene("player");
        Require(byCompanion == byPlayer,
            $"equal remaining work must receive equal treatment whoever removed the rest; companion={byCompanion} player={byPlayer}");
        Require(byCompanion.Value > 0 && byCompanion.Tiles == 1 && byCompanion.Target == remaining,
            $"the compared offer must be the remaining tile's; got {byCompanion}");
        var withPartial = Scene("player-with-partial-damage");
        Require(withPartial == byPlayer,
            $"the player's partial damage is in the player's hit table and must not change the companion's remaining work; partial={withPartial} untouched={byPlayer}");
    }

    /// <summary>
    /// The player leaves the activity range after the companion's first removal from a three-tile vein. No strike may land
    /// after that, the job must end with the remaining ore still present and not observed clear, and the attempt it ended
    /// must be partial with the companion's credited effects, never a completion.
    /// </summary>
    private static void WorkCancelledByRangeLeavesPartialWorkAndOreInPlace()
    {
        // An active job keeps a wider allowance than a new one (the work radius times the continuation factor, 125 tiles),
        // which no hundred-tile world can separate player from ore by, so this scene is 300 tiles wide: the vein near the
        // left, the player walking off to the far right.
        CancelByRange(live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current.ActiveActivityRadius);
    }

    private static void CancelByRange(float activeRadius)
    {
        // The vein sits right of SetUp's starting column so its native line probe sees the first ore's left face, and both
        // bodies then start beside it.
        Point[] vein = { new(85, 59), new(86, 59), new(87, 59) };
        var (_, ctx) = VerifyOreWork.SetUpWide(WorkPolicy.Opportunistic, TileID.Copper, 300, vein);
        ctx.Player.Bottom = new Vector2(80 * 16, 60 * 16);
        ctx.Npc.Bottom = new Vector2(80 * 16 + 8, 60 * 16);
        var mine = ctx.Companion.Brain.Actions.OfType<MineOre>().Single();
        TerrainChanges.Reset();
        for (int tick = 0; tick < 600 && vein.All(p => Main.tile[p.X, p.Y].HasTile); tick++) VerifyOreWork.AdvanceBrain(ctx);
        Require(vein.Count(p => !Main.tile[p.X, p.Y].HasTile) == 1 && mine.JobId > 0,
            $"the cancellation fixture needs exactly one tile removed by a live job; removed={vein.Count(p => !Main.tile[p.X, p.Y].HasTile)} job={mine.JobId}");
        int job = mine.JobId;
        ctx.Player.Bottom = new Vector2(290 * 16, 60 * 16);
        Require(vein.All(p => Vector2.Distance(p.ToWorldCoordinates(), ctx.Player.Bottom) > activeRadius),
            $"the remaining ore must lie outside the active job's allowance of {activeRadius} px, or nothing is cancelled");
        long strikes = ctx.Companion.Miner.LastOutcome?.Attempt ?? -1;
        for (int tick = 0; tick < 120; tick++) VerifyOreWork.AdvanceBrain(ctx);
        Require((ctx.Companion.Miner.LastOutcome?.Attempt ?? -1) == strikes && vein.Count(p => Main.tile[p.X, p.Y].HasTile) == 2,
            "no strike may land after the player leaves the activity range");
        Require(mine.LastConclusion is { JobId: var ended, ObservedClear: false, Present: 2, CompanionRemovals: 1 } && ended == job,
            $"the cancelled job must keep its remaining ore as present work and its one companion removal; got {mine.LastConclusion}");
        var attempt = ctx.Companion.Brain.Activity.RecentAttempts.LastOrDefault(a => a.Activity == "mine");
        Require(attempt is { Status: AttemptStatus.Partial, ProductiveEffects: > 0 },
            $"work cancelled by range after a removal is partial, never complete; got {attempt}");
    }

    private static int MineUntilGone(MineOre mine, ActionContext ctx, params Point[] tiles)
    {
        int effects = 0;
        // Each tick asks the course for its step and hands it to mining, as the tick does, with the body held still.
        for (int tick = 0; tick < 900 && tiles.Any(p => Main.tile[p.X, p.Y].HasTile); tick++)
        {
            if (DriveGatheringThroughTheCourse.Decide(ctx, "mine") is { } step) ctx.Companion.Brain.Activity.Select(mine, ctx, step);
            long before = ctx.Companion.Miner.LastOutcome?.Attempt ?? -1;
            mine.Execute(ctx);
            if (ctx.Companion.Miner.LastOutcome is { Productive: true } outcome && outcome.Attempt != before) effects++;
            ctx.Companion.Miner.Tick();
        }
        mine.Prepare(ctx);
        return effects;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
