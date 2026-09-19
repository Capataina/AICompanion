extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using live::AICompanion.Companion.Brain.Infrastructure.Movement;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

internal static class VerifyCourseTravelScheduling
{
    public static int Run()
        => RunOneRow.Case("G11 native travel queries share one-operation turns without evicting work", FairNativeQueries)
        + RunOneRow.Case("G11 native model completion resumes a frozen companionship forecast", ModelOwner)
        + RunOneRow.Case("G08 deferred queries cannot certify terrain edited since observation", DeferredTerrainEdit)
        + RunOneRow.Case("G11 course search drives missing native models with one shared operation", SearchOwner)
        + RunOneRow.Case("G08 captured enemy motion records native collision read bounds", MotionTerrainReads)
        + RunOneRow.Case("G08 enemy motion publication rejects local edits and retains distant edits", MotionPublication)
        + RunOneRow.Case("G11 enemy motion and travel share native query turns", MixedNativeQueries)
        + RunOneRow.Case("G15 captured melee shapes match native victim-dependent geometry", NativeMeleeShapes)
        + RunOneRow.Case("G15 captured motion and native defence produce timed contact harm", NativeContactPipeline);

    private static void NativeContactPipeline()
    {
        var (_, context) = VerifyOreWork.SetUp(live::AICompanion.Companion.Brain.Activities.WorkPolicy.Opportunistic,
            Terraria.ID.TileID.Copper, new Microsoft.Xna.Framework.Point(25, 59));
        context.Player.statDefense = Terraria.Player.DefenseStat.Default; context.Player.endurance = 0;
        context.Npc.defense = 0; context.Npc.takenDamageMultiplier = 1;
        var playerDefence = EstimateEffectiveDamage.Capture(context.Player);
        var npcDefence = EstimateEffectiveDamage.Capture(context.Npc);
        float playerExpected = EstimateEffectiveDamage.ToPlayer(context.Player, 27);
        float npcExpected = EstimateEffectiveDamage.ToNpc(context.Npc, 20);
        context.Player.statDefense = Terraria.Player.DefenseStat.Default + 10000; context.Npc.defense = 10000;
        var enemy = new CapturedMeleeEnemy(460, new(400, 800), 40, 120, 1, 1, 0, 0, 0, 0);
        var motion = new CapturedEnemyCourseMotion(1, 1, 460, 40, 120, 1,
            new[] { new CoursePoint(420, 860), new CoursePoint(420, 860) }, "fixture");
        var victims = new[] { new ContactBox(425, 900, 20, 20), new ContactBox(100, 100, 20, 20) };
        var player = new ProjectMeleeContactGeometry(enemy, motion, victims, playerDefence, 20, -1, 0, Array.Empty<int>(), true);
        Require(player.Continue(new(double.PositiveInfinity, 1)) == null, "a partial contact trajectory was published");
        var playerShape = player.Continue(new(double.PositiveInfinity, 1))!;
        var npcShape = new ProjectMeleeContactGeometry(enemy, motion, victims, npcDefence, 20, 1, 0,
            Array.Empty<int>(), true).Continue(new(double.PositiveInfinity))!;
        Require(playerShape.Samples[0].Damage == playerExpected && playerShape.Samples[1].Damage == 20
            && npcShape.Samples[0].Damage == npcExpected,
            "contact geometry lost its per-sample multiplier, native actor distinction or frozen defence");
        var harm = new ForecastContactHarm(new[] { new ContactActor(HarmActor.Player, 100, 0, victims),
            new ContactActor(HarmActor.Companion, 100, 0, victims) },
            new[] { new ContactThreat(1, 1, playerShape, npcShape) }, 1, true).Continue(new(double.PositiveInfinity));
        Require(harm!.Harm.Count == 2 && harm.Harm.All(hit => hit.Tick == 0) && harm.TailUnresolved,
            "native contact geometry did not reach timed harm or concealed its post-hit uncertainty");
    }

    private static void NativeMeleeShapes()
    {
        _ = VerifyOreWork.SetUp(live::AICompanion.Companion.Brain.Activities.WorkPolicy.Opportunistic,
            Terraria.ID.TileID.Copper, new Microsoft.Xna.Framework.Point(25, 59));
        var previous = Terraria.Main.npc[154];
        var enemy = new Terraria.NPC { whoAmI = 154, position = new(400.5f, 800.5f), width = 40, height = 120 };
        Terraria.Main.npc[154] = enemy;
        int checkedShapes = 0;
        try
        {
            foreach (int type in new[] { 1, 430, 436, 591, 494, 495, 460, 417, 466, 576, 577, 552, 553, 554, 668 })
            foreach (int direction in new[] { -1, 1 })
            foreach (int frame in new[] { 0, 15, 16, 17, 18 })
            foreach (int state in new[] { 0, 4, 6, 24 })
            {
                enemy.type = type; enemy.direction = direction; enemy.spriteDirection = direction;
                enemy.frame.Y = frame; enemy.ai[0] = state; enemy.ai[2] = state; enemy.ai[3] = state == 6 ? 2 : 0;
                var captured = CapturedMeleeEnemy.From(enemy);
                foreach (var victim in new[] { new Microsoft.Xna.Framework.Rectangle(350, 810, 20, 100),
                    new Microsoft.Xna.Framework.Rectangle(425, 840, 30, 100), new Microsoft.Xna.Framework.Rectangle(100, 100, 20, 20) })
                {
                    var box = enemy.Hitbox; float damage = 1; int channel = -1;
                    Terraria.NPC.GetMeleeCollisionData(victim, 154, ref channel, ref damage, ref box);
                    var predicted = ResolveCapturedMeleeShape.Resolve(captured, victim, -1);
                    Require(predicted.Box == box && predicted.DamageMultiplier == damage && predicted.HitChannel == channel,
                        $"captured melee disagrees with native type={type}, direction={direction}, frame={frame}, state={state}");
                    checkedShapes++;
                }
            }
        }
        finally { Terraria.Main.npc[154] = previous; }
        Require(checkedShapes == 1800, "native melee comparison lost input coverage");
    }

    private static void MixedNativeQueries()
    {
        var world = World();
        var scheduler = new ScheduleCourseModels(world, 1, 2);
        var route = new CourseTravelRequest(new(48, 80), default, new(272, 80));
        var npc = new Terraria.NPC { whoAmI = 153, type = 1, position = new(48, 80), velocity = new(2, 0),
            width = 20, height = 20, noGravity = true, noTileCollide = true };
        var enemy = new CaptureEnemyCourseMotion(npc, 1, world, 1, 1);
        Require(scheduler.Request(route) && scheduler.RequestEnemyMotion(enemy), "mixed native queries failed admission");
        var results = new List<DecisionFact>();
        for (int tick = 0; tick < 10000 && scheduler.PendingCount > 0; tick++)
        {
            var allowance = new DecisionWorkBudget(double.PositiveInfinity, 1);
            results.AddRange(scheduler.Continue(allowance));
            Require(allowance.OperationsUsed <= 1, "mixed native models spent independent allowances");
        }
        Require(results.Count == 2 && results[0].Key == enemy.Key && results[1].Key == route.Key,
            "travel starved the enemy query or either frontier lost its continuation");
        PredictObservedMotion.Forget(153);
    }

    private static void MotionPublication()
    {
        _ = VerifyOreWork.SetUp(live::AICompanion.Companion.Brain.Activities.WorkPolicy.Opportunistic,
            Terraria.ID.TileID.Copper, new Microsoft.Xna.Framework.Point(25, 59));
        var npc = new Terraria.NPC { whoAmI = 152, type = 1, position = new(400, 880), velocity = new(2, 0),
            width = 20, height = 20, noGravity = true, noTileCollide = false };
        var edits = new TextTileWorld(0, 0, Enumerable.Repeat(new string('.', 100), 100).ToArray());
        var query = new CaptureEnemyCourseMotion(npc, 7, edits, 2, 1);
        Require(query.Continue(new(double.PositiveInfinity, 1)) == null, "partial enemy motion escaped as a completed model");
        edits.Set(80, 80, '#');
        var result = query.Continue(new(double.PositiveInfinity, 1));
        Require(result?.Evidence == FactEvidence.Modelled && query.CoveredTicks == 2,
            "an unrelated terrain edit discarded retained enemy motion");
        var changed = new CaptureEnemyCourseMotion(npc, 7, edits, 2, 1);
        edits.Set(23, 54, '#');
        Require(changed.Continue(new(double.PositiveInfinity, 1))?.Evidence == FactEvidence.Unresolved,
            "an edit to a newly read native collision neighbour certified the old observation");
        PredictObservedMotion.Forget(152);
    }

    private static void MotionTerrainReads()
    {
        _ = VerifyOreWork.SetUp(live::AICompanion.Companion.Brain.Activities.WorkPolicy.Opportunistic,
            Terraria.ID.TileID.Copper, new Microsoft.Xna.Framework.Point(25, 59));
        var npc = new Terraria.NPC { whoAmI = 151, type = 1, position = new(400, 880), velocity = new(2, 0),
            width = 20, height = 20, noGravity = true, noTileCollide = false };
        npc.velocity.Y = npc.gravity;
        var motion = PredictObservedMotion.Capture(npc);
        Require(!motion.Continue(1, new(double.PositiveInfinity, 0)) && !motion.ReadContains(25, 55),
            "an unanswered motion query claimed terrain reads");
        Require(motion.Continue(1, new(double.PositiveInfinity, 1)) && motion.ReadContains(23, 54)
            && motion.ReadContains(28, 57) && !motion.ReadContains(40, 55),
            "native motion footprint omitted tile-neighbour or foot-row reads, or included distant terrain");
        PredictObservedMotion.Forget(151);
    }

    private static void SearchOwner()
    {
        var original = Snapshot();
        var owner = new RetainCourseModelQueries(original, World(), 1, 1);
        var projector = new AwaitTravel();
        var search = new SearchCourseOrders(1);
        search.Begin(original, new(1, 1, 10, Array.Empty<UsefulNeed>(), true, false, "fixture"),
            Array.Empty<Opportunity>(), Array.Empty<OpportunityKey>(), projector);
        for (int tick = 0; tick < 1000 && !search.Exhausted; tick++)
        {
            var budget = new DecisionWorkBudget(double.PositiveInfinity, 1);
            owner.ContinueSearch(search, budget);
            Require(budget.OperationsUsed <= 1, "search and native model invented separate operation allowances");
        }
        Require(search.Exhausted && search.RejectedOrders == 1 && projector.Answered
            && owner.CompletedCount == 1 && search.RequiredTravel.Count == 0,
            "the pending order failed to request, resume or retire its native query");
        Require(!original.TryRead(projector.Query.Key, out _), "search completion mutated its original observation");
    }

    private sealed class AwaitTravel : ICourseProjector
    {
        public readonly CourseTravelRequest Query = new(new(48, 80), default, new(49, 80));
        public bool Answered;
        public CourseProjectionResult Continue(IReadOnlyList<OpportunityKey> order, DecisionFactSnapshot facts,
            CourseComparisonEpisode episode, DecisionWorkCursor cursor, DecisionWorkBudget budget)
        {
            if (!budget.TrySpend("fixture-projector")) return new(ProjectionStatus.Pending, null, "budget-cut");
            if (!facts.TryRead(Query.Key, out var answer))
                return new(ProjectionStatus.Pending, null, "native-travel-pending", new[] { Query });
            Answered = answer.Evidence == FactEvidence.Modelled;
            // This fixture proves transport and resumption, not a complete consequence model.
            return new(ProjectionStatus.Rejected, null, "fixture-transport-complete");
        }
    }

    private static TextTileWorld World() => new(0, 0, new[]
    {
        "####################", "#..................#", "#..................#", "#..................#",
        "#..................#", "#..................#", "#..................#", "#..................#",
        "#..................#", "####################"
    });
    private static DecisionFactSnapshot Snapshot() => new(1, 1, 100, 1, 0, new[]
    {
        new DecisionFact(CapturedCompanionshipRegion.Key, 1, new(Text: JsonSerializer.Serialize(
            new CapturedCompanionshipRegion(new(48, 80), new(20, 20), default, 100, 100, true))), FactEvidence.Observed)
    });

    private static void ModelOwner()
    {
        var original = Snapshot();
        var owner = new RetainCourseModelQueries(original, World(), 1, 2);
        var forecast = new ForecastCourseCompanionship(original, Array.Empty<StepBinding>(), new(48, 80), default, new(49, 80));
        Require(forecast.Continue(original, new(double.PositiveInfinity)).Status == ProjectionStatus.Pending
            && forecast.MissingTravel.HasValue, "the empty course did not request its native return model");
        var query = forecast.MissingTravel!.Value;
        Require(owner.RequestTravel(query), "model owner refused a free pending slot");
        for (int i = 0; i < 1000 && owner.PendingCount > 0; i++) owner.Continue(new(double.PositiveInfinity, 1));
        Require(owner.CompletedCount == 1 && owner.Snapshot.IsModelExtensionOf(original)
            && !original.TryRead(query.Key, out _), "native completion mutated the old snapshot or failed to append its model");
        Require(forecast.Continue(owner.Snapshot, new(double.PositiveInfinity)) is
            { Status: ProjectionStatus.Complete, NominallyRejoined: true }, "native query completion failed to resume consequence costing");
        Require(owner.RequestTravel(query) && owner.PendingCount == 0 && !owner.Continue(new(double.PositiveInfinity)),
            "an already captured query was rescheduled or republished");
        owner.Abandon();
        bool refused = false;
        try { owner.RequestTravel(query); } catch (InvalidOperationException) { refused = true; }
        Require(refused, "an abandoned observation accepted more native work");
    }

    private static void DeferredTerrainEdit()
    {
        var world = World(); var owner = new RetainCourseModelQueries(Snapshot(), world, 1, 1);
        world.Set(3, 5, '#');
        var query = new CourseTravelRequest(new(48, 80), default, new(49, 80));
        Require(owner.RequestTravel(query), "the deferred terrain fixture needs a pending query");
        for (int i = 0; i < 1000 && owner.PendingCount > 0; i++) owner.Continue(new(double.PositiveInfinity, 1));
        Require(owner.Snapshot.TryRead(query.Key, out var fact) && fact.Evidence == FactEvidence.Unresolved
            && fact.Value.Text.Contains("travel-observation-terrain-changed", StringComparison.Ordinal),
            "a query created after a terrain edit certified the new world as the earlier observation");
    }

    private static void FairNativeQueries()
    {
        var world = new TextTileWorld(0, 0, new[]
        {
            "####################", "#..................#", "#..................#", "#........##........#",
            "#........##........#", "#........##........#", "#........##........#", "#..................#",
            "#..................#", "####################"
        });
        var far = new CourseTravelRequest(new(48, 80), default, new(272, 80));
        var near = new CourseTravelRequest(new(48, 80), default, new(49, 80));
        var scheduler = new ScheduleCourseModels(world, 1, 2);
        Require(scheduler.Request(far) && scheduler.Request(near) && scheduler.Request(far) && scheduler.PendingCount == 2,
            "duplicate pending requests reset or duplicate work");
        Require(!scheduler.Request(new(new(48, 80), default, new(48, 96))) && scheduler.CapacityRefusals == 1,
            "capacity pressure silently displaced an unfinished route");
        Require(scheduler.Continue(new(double.PositiveInfinity, 0)).Count == 0 && scheduler.PendingCount == 2,
            "an empty allowance performed or erased pending work");
        var completed = new List<DecisionFact>();
        for (int slice = 0; slice < 20000 && scheduler.PendingCount > 0; slice++)
        {
            var budget = new DecisionWorkBudget(double.PositiveInfinity, 1);
            completed.AddRange(scheduler.Continue(budget));
            Require(budget.OperationsUsed <= 1, "a query invented an allowance outside the shared budget");
        }
        Require(completed.Count == 2 && completed[0].Key == near.Key && completed[1].Key == far.Key
            && completed.All(fact => fact.Evidence == FactEvidence.Modelled) && scheduler.CompletedCount == 2,
            "the long first query starved the short query or lost a retained route");
        Require(scheduler.Request(far), "a released pending slot remained permanently occupied");
        scheduler.Clear();
        Require(scheduler.PendingCount == 0 && scheduler.Continue(new(double.PositiveInfinity)).Count == 0,
            "world reset retained a queued native query");
    }
    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }
}
