extern alias live;
using Microsoft.Xna.Framework;
using Terraria;
using Recovery = live::AICompanion.Companion.Brain.SharedBehaviours.Recovery.RecoverDistantCompanion;
using Guard = live::AICompanion.Companion.Brain.Activities.Combat.ProtectPlayer;
using Context = live::AICompanion.Companion.Brain.Activities.ActionContext;
using Threat = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatRecord;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;

internal static class VerifyFollowRecoveryAndProtection
{
    public static int Run()
    {
        // The brain cases below run the live tick, whose searches stop at wall-clock allowances; lifted, each
        // verdict is about the brain rather than about how busy the machine was.
        LimitPlanningWork.Unbounded = true;
        try { return RunWithPlanningLifted(); }
        finally { LimitPlanningWork.Unbounded = false; }
    }

    private static int RunWithPlanningLifted()
    {
        var recovery = new Recovery();
        Vector2 start = new(100, 400), goal = new(1400, 400);
        Require(!recovery.Update(false, false, true, start, goal, true), "non-follow actions cannot start recovery");
        Require(!recovery.Update(true, true, true, start, goal, true), "downed cannot start recovery");
        Require(!recovery.Update(true, false, false, start, goal, true), "dead owner cannot start recovery");
        Require(recovery.Update(true, false, true, start, goal, true), "distant follow starts recovery");
        Require(recovery.Update(true, false, true, goal, goal, false), "cannot land inside solid terrain");
        Vector2 feet = start, velocity = Vector2.Zero;
        int ticks = 0;
        while (recovery.Update(true, false, true, feet, goal, true) && ticks++ < 400)
        {
            Vector2 next = recovery.Steer(feet, velocity, goal, Vector2.Zero);
            Require((next - velocity).Length() <= Weights.FollowRecoveryAcceleration + .001f, "flight acceleration is bounded");
            velocity = next;
            feet += velocity;
        }
        Require(!recovery.Active && ticks < 400 && Vector2.Distance(feet, goal) <= Weights.FollowRecoveryArrival,
            "visible bounded flight reaches clear owner neighbourhood");
        Require(!recovery.Update(true, false, true, feet, goal, true), "arrival hysteresis prevents immediate reentry");

        var companion = VerifyCompanionLifecycle.Create();
        companion.NPC.position = new Vector2(400, 900);
        companion.Motor.ApplyRecoveryFlight(new Vector2(3, -2));
        Require(companion.NPC.noGravity && companion.NPC.noTileCollide, "native motor enables flight flags");
        companion.CheckDead();
        Require(companion.NPC.velocity == Vector2.Zero, "downed flight stops immediately");
        companion.Motor.Apply(live::AICompanion.Companion.Brain.Infrastructure.Movement.Controls.None, "downed");
        Require(!companion.NPC.noGravity && !companion.NPC.noTileCollide, "clear downed body restores ordinary collision");
        VerifyGuard();
        VerifyRecoveryThroughBrain();
        VerifyRecoveryAdmissionUsesReunionPurpose();
        VerifyCancelledFlightClearsTerrain();
        Console.WriteLine($"follow recovery and protection: flight arrival in {ticks} ticks; exclusions, collision, downing, threat continuation and expiry pass");
        return 0;
    }

    private static void VerifyRecoveryAdmissionUsesReunionPurpose()
    {
        foreach (var kind in new[] { live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer,
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.Exact,
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.Guard,
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.Hold })
        {
            var companion = VerifyCompanionLifecycle.Create();
            Main.LocalPlayer.dead = false;
            Main.LocalPlayer.Bottom = new Vector2(1400, 1200);
            companion.NPC.Bottom = new Vector2(100, 1200);
            companion.Brain.Chooser.Actions.Clear();
            companion.Brain.Chooser.Actions.Add(new RequestedPurpose(kind));
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
            bool reunion = kind == live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer;
            Require(companion.Brain.FollowRecovery.Active == reunion,
                $"recovery admission must follow explicit reunion rather than executor class or shared destination; request={kind}, active={companion.Brain.FollowRecovery.Active}");
        }
    }

    private sealed class RequestedPurpose(live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind kind)
        : live::AICompanion.Companion.Brain.Activities.CompanionAction
    {
        public override string Name => "recovery-purpose-probe";
        public override live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily Family
            => live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily.NearbyAssistance;
        public override bool IsExcursion => false;
        // A positive score needs a classified offer, exactly as for a production activity.
        public override void Prepare(in Context ctx) => Classify(live::AICompanion.Companion.Brain.Activities.OfferEligibility.Usable, "probe");
        public override float Score() => 1f;
        public override live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest Execute(in Context ctx)
            => new(kind, ctx.Player.Bottom);
    }

    private static void VerifyGuard()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.player[0].dead = false;
        Main.player[0].position = new Vector2(450, 900);
        var senses = companion.Brain.Senses;
        senses.Update(companion.NPC, Main.player[0], companion.Breath);
        var npc = new NPC { whoAmI = 4, active = true, life = 100, damage = 20 };
        var threat = new Threat { Npc = npc, CanReachPlayer = true, Urgency = 1f, EffectiveTicksToPlayer = 0 };
        senses.Threats.Threats.Add(threat);
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatSense).GetProperty("MostUrgent")!.SetValue(senses.Threats, threat);
        senses.SetInterventionEstimate(float.PositiveInfinity);
        var context = new Context(companion, senses);
        var guard = new Guard();
        float entry = VerifyPreparedActivities.PrepareAndScore(guard, context);
        Require(entry > Weights.Commitment, "immediate danger can interrupt committed following even nearby");
        Require(ReferenceEquals(guard.ActivityIdentity, npc), "guard must bind its prepared offer to the protected enemy");
        var preparedRequest = guard.Execute(context);
        var unrelated = new Threat { Npc = new NPC { whoAmI = 6, active = true, life = 100,
            position = new Vector2(800, 900) }, CanReachPlayer = true, Urgency = 1f };
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatSense).GetProperty("MostUrgent")!.SetValue(senses.Threats, unrelated);
        Require(guard.Score() == entry && guard.Execute(context) == preparedRequest,
            "guard execution must retain the scored target and anchor until preparation refreshes them");
        guard.Enter(context);
        Require(guard.ProtectedThreatId == npc.whoAmI, "guard entry must commit the prepared enemy, not the later urgent enemy");
        companion.Brain.Chooser.Activity.Select(guard, context);
        long firstProtection = companion.Brain.Chooser.Activity.Id;
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatSense).GetProperty("MostUrgent")!.SetValue(senses.Threats, threat);
        threat.Urgency = .15f;
        threat.EffectiveTicksToPlayer = 150;
        senses.SetInterventionEstimate(1);
        Require(VerifyPreparedActivities.PrepareAndScore(guard, context) >= entry, "small retreat retains protection of the same relevant threat");
        threat.CanReachPlayer = false;
        threat.Urgency = 0;
        senses.SetInterventionEstimate(float.PositiveInfinity);
        int firstClear = senses.Tick;
        Require(VerifyPreparedActivities.PrepareAndScore(guard, context) > 0, "one safe frame must not abandon protection");
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.Senses).GetProperty("Tick")!.SetValue(senses,
            firstClear + Weights.GuardClearTicks);
        Require(VerifyPreparedActivities.PrepareAndScore(guard, context) == 0 && guard.ProtectedThreatId == -1,
            "sustained irrelevance releases a living threat");
        threat.CanReachPlayer = true;
        threat.Urgency = 1;
        VerifyPreparedActivities.PrepareAndScore(guard, context);
        guard.Enter(context);
        npc.active = false;
        Require(guard.Execute(context) == live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest.Hold,
            "an unavailable prepared guard target must be refused at execution");
        senses.Threats.Threats.Clear();
        senses.SetInterventionEstimate(float.PositiveInfinity);
        Require(VerifyPreparedActivities.PrepareAndScore(guard, context) == 0 && guard.ProtectedThreatId == -1, "disappeared threat releases commitment");
        companion.Brain.Chooser.Activity.Select(guard, context);
        var replacement = new NPC { whoAmI = 5, active = true, life = 100, damage = 20 };
        var second = new Threat { Npc = replacement, CanReachPlayer = true, Urgency = 1f, EffectiveTicksToPlayer = 0 };
        senses.Threats.Threats.Add(second);
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatSense).GetProperty("MostUrgent")!.SetValue(senses.Threats, second);
        float renewed = VerifyPreparedActivities.PrepareAndScore(guard, context);
        companion.Brain.Chooser.Activity.Select(guard, context);
        Require(companion.Brain.Chooser.Activity.Id != firstProtection,
            "guarding a different enemy must start a distinct protection activity in the shared owner");
        second.Urgency = .15f;
        Require(VerifyPreparedActivities.PrepareAndScore(guard, context) >= renewed && guard.ProtectedThreatId == 5,
            "a second threat must inherit commitment while guarding stays selected");

        // Attempt boundaries: the first threat's release above happened before this attempt opened,
        // so it cannot conclude it; the committed second threat vanishing while the player lives
        // completes protection without naming who removed it.
        guard.BeginAttempt();
        Require(guard.ConcludeAttempt(0).Status == live::AICompanion.Companion.Brain.Activities.AttemptStatus.Attempted,
            $"an attempt opened after an earlier release must not conclude from it; got {guard.ConcludeAttempt(0)}");
        replacement.active = false;
        senses.Threats.Threats.Clear();
        VerifyPreparedActivities.PrepareAndScore(guard, context);
        Require(guard.ConcludeAttempt(0) is { Status: live::AICompanion.Companion.Brain.Activities.AttemptStatus.Complete,
                Attribution: live::AICompanion.Companion.Brain.Activities.AttemptAttribution.Unattributed, Cause: "protected-threat-gone" },
            $"a committed threat gone while the player lives completes protection, unattributed; got {guard.ConcludeAttempt(0)}");
    }

    private static void VerifyRecoveryThroughBrain()
    {
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        TerrainChanges.Reset();
        var companion = VerifyCompanionLifecycle.Create();
        Main.player[0].dead = false;
        Main.player[0].Bottom = new Vector2(1400, 1200);
        companion.NPC.Bottom = new Vector2(100, 1200);
        var memory = live::AICompanion.Companion.Brain.Infrastructure.Movement.RememberExecutedRoutes.World;
        memory.Clear();
        bool started = false, landed = false;
        bool answeredThreat = false;
        for (int tick = 0; tick < 400; tick++)
        {
            if (tick == 15)
                Main.npc[1] = new NPC { whoAmI = 1, active = true, life = 100, lifeMax = 100,
                    damage = 20, width = 30, height = 40, noGravity = true,
                    position = companion.NPC.position + new Vector2(120, 0) };
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
            if (tick == 15)
            {
                answeredThreat = companion.Brain.EngageTarget == Main.npc[1];
                Main.npc[1].active = false;
            }
            started |= companion.Brain.FollowRecovery.Active;
            Require(memory.Count == 0, "recovery must not add any executed route to world memory");
            if (started && !companion.Brain.FollowRecovery.Active) { landed = true; break; }
            Require(companion.NPC.noGravity && companion.NPC.noTileCollide,
                "the real brain must send distant following through the flight motor");
            Require(companion.NPC.velocity.Length() <= Weights.FollowRecoverySpeed + .01f,
                "brain recovery must remain continuous bounded motion");
            // The engine's no-collision translation, with the real brain/motor supplying it.
            companion.NPC.position += companion.NPC.velocity;
        }
        Require(started && landed, "the production brain must enter and complete distant recovery");
        Require(answeredThreat, "recovery flight must keep independent weapon targeting active when an enemy appears");
        Require(Vector2.Distance(companion.NPC.Bottom, Main.player[0].Bottom) <= Weights.FollowRecoveryArrival,
            "flight may complete only inside the owner arrival region");
    }

    private static void VerifyCancelledFlightClearsTerrain()
    {
        var companion = VerifyCompanionLifecycle.Create();
        companion.NPC.position = new Vector2(400, 900);
        companion.Motor.ApplyRecoveryFlight(new Vector2(8, 0));
        for (int x = 28; x <= 35; x++)
        for (int y = 53; y <= 62; y++)
        {
            Tile tile = Main.tile[x, y]; tile.HasTile = true; tile.TileType = 1;
        }
        TerrainChanges.Reset();
        companion.NPC.position = new Vector2(480, 900);
        Require(!companion.Motor.ClearOfTerrain, "cancellation fixture must be inside a thick wall");
        companion.CheckDead();
        int ticks = 0;
        while (companion.Motor.RecoveryFlight && ticks++ < 100)
        {
            companion.Motor.Apply(live::AICompanion.Companion.Brain.Infrastructure.Movement.Controls.None, "downed");
            companion.NPC.position += companion.NPC.velocity;
        }
        Require(companion.IsDowned && companion.Motor.ClearOfTerrain && !companion.Motor.RecoveryFlight,
            "a downed interrupted flight must leave the wall without travelling to the owner");
        Require(live::AICompanion.Companion.Brain.Infrastructure.Movement.RememberExecutedRoutes.World.Count == 0,
            "cancelled-flight clearance must never teach world route memory");
        Require(companion.NPC.position.X < 448 && !companion.NPC.noTileCollide && !companion.NPC.noGravity,
            "clearance returns to the entry side and restores native collision");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
