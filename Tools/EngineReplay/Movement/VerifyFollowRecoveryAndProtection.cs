extern alias live;
using Microsoft.Xna.Framework;
using Terraria;
using Recovery = live::AICompanion.Companion.Brain.SharedBehaviours.Recovery.RecoverDistantCompanion;
using Combat = live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies;
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
        Vector2 start = new(100, 400), goal = new(2200, 400);
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
        // The engine's own flags are not the observable any more, and asserting on them was the walker's
        // question. This body sets `noGravity` and `noTileCollide` once when it spawns and never clears them —
        // the engine does nothing to it but add its velocity, and contact is the mod's own circle test — so both
        // flags read true in flight and true out of it, and the row said nothing either way. What flight
        // actually is here is `RecoveryFlight`, which makes the motor commit its move phasing; leaving it is
        // what puts the move back through `CircleContact`.
        Require(companion.Motor.RecoveryFlight, "the motor must be in recovery flight");
        companion.CheckDead();
        Require(companion.NPC.velocity == Vector2.Zero, "downed flight stops immediately");
        companion.Motor.Apply(live::AICompanion.Companion.Brain.Infrastructure.Movement.Controls.None, "downed");
        Require(!companion.Motor.RecoveryFlight, "a clear downed body leaves flight, so its moves run through contact again");
        VerifyGuard();
        VerifyRecoveryThroughBrain();
        VerifyRecoveryAdmissionUsesReunionPurpose();
        VerifyCancelledFlightClearsTerrain();
        Console.WriteLine($"follow recovery and protection: flight arrival in {ticks} ticks; exclusions, collision, downing, threat continuation and expiry pass");
        return 0;
    }

    /// <summary>
    /// Recovery flight may be started by an explicit reunion and by nothing else — not by an executor's
    /// class, not by a work destination that happens to sit near the player, not by a tick that is still
    /// deciding.
    ///
    /// This used to sweep the four request kinds by installing a stub activity in the chooser's list and
    /// reading back what the brain asked for. The course brain took that lever away: the request comes
    /// from the published course now, and an empty course asks for companionship, so every arm of that
    /// sweep read as reunion whatever it installed and the row failed on `request=Exact, active=True` —
    /// the fixture speaking for a mechanism that no longer decides anything.
    ///
    /// So the rule is driven where it lives, as `RecoverDistantCompanion.ReunionRequested`, across every
    /// combination of its three inputs rather than the four kinds alone; and one whole-brain arm still
    /// crosses the seam, because a pure predicate nobody calls would pass while the tick read something
    /// else. The whole-brain arm is the reunion case because that is the one an empty course produces,
    /// and the negative cases are the predicate's — which is the honest split, since a scene that cannot
    /// produce a non-reunion request cannot witness one being refused.
    /// </summary>
    private static void VerifyRecoveryAdmissionUsesReunionPurpose()
    {
        var kinds = new[] { live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer,
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.Exact,
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.FireFrom,
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.Hold };
        foreach (var kind in kinds)
            foreach (bool handsBusy in new[] { false, true })
                foreach (bool settled in new[] { false, true })
                {
                    bool reunion = kind == live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer
                        && !handsBusy && settled;
                    bool admitted = live::AICompanion.Companion.Brain.SharedBehaviours.Recovery.RecoverDistantCompanion
                        .ReunionRequested(kind, handsBusy, settled);
                    Require(admitted == reunion,
                        $"recovery admission must follow explicit reunion rather than executor class or shared "
                        + $"destination; request={kind} handsBusy={handsBusy} settled={settled} admitted={admitted}");
                }

        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null, new object[] { (ushort)200, (ushort)100 }, null)!;
        TerrainChanges.Reset();
        var companion = VerifyCompanionLifecycle.Create();
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.Bottom = new Vector2(2200, 1200);
        companion.NPC.Bottom = new Vector2(100, 1200);
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
        Require(companion.Brain.LastRequest.Kind == live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer,
            $"a companion two thousand pixels from its player with nothing else to do must ask for companionship, "
            + $"or this arm proves nothing about what admits flight; asked for {companion.Brain.LastRequest.Kind}");
        Require(companion.Brain.FollowRecovery.Active,
            "an explicit reunion request far beyond the recovery radius must start recovery flight, "
            + "so the predicate above is the one the tick actually reads");
    }

    /// <summary>
    /// Protection under the one combat stance: immediate danger offers a plan that interrupts commitment,
    /// entry commits the prepared plan's enemy rather than a later urgent one, execution retains the scored
    /// stand and target, and a dead target is handed on as no target. Irrelevance collapses the offer's value
    /// without ending its membership — the committed plan is kept while it still describes the world, and the
    /// chooser's comparison, not a release timer, sends the body back to work. A disappeared threat releases.
    /// </summary>
    private static void VerifyGuard()
    {
        BuildGuardFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Main.player[0].dead = false;
        Main.player[0].position = new Vector2(450, 900);
        companion.NPC.position = new Vector2(400, 900);
        for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
        var senses = companion.Brain.Senses;
        var npc = new NPC { whoAmI = 4, active = true, life = 100, lifeMax = 100, damage = 20, position = new Vector2(450, 880) };
        Main.npc[4] = npc;
        senses.Update(companion.NPC, Main.player[0]);
        senses.SetInterventionEstimate(float.PositiveInfinity);
        var context = new Context(companion, senses);
        var combat = new Combat();
        // Prime the reach flood to completion, then prepare once: the search's finished answer rather than
        // what the first flood slice happened to reach. WithPlayer pumps the flood; a firing request without
        // a flight profile early-outs before it refreshes anything.
        var primeRequest = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer, Main.player[0].Bottom);
        for (int i = 0; i < 3000 && !companion.Brain.Positioner.ReachComplete; i++)
            companion.Brain.Positioner.Resolve(primeRequest, senses);
        Require(companion.Brain.Positioner.ReachComplete, "the guard scene needs a completed flood before the plan can be read");
        VerifyPreparedActivities.PrepareAndScore(combat, context);
        Require(combat.OfferedPlan != null, $"the guard scene must offer a plan; reason={combat.EligibilityReason}");
        float entry = combat.Score();
        float entryWeighted = combat.OfferedPlan.Weighted;
        Require(entry > Weights.Commitment, "immediate danger can interrupt committed following even nearby");
        Require(combat.OfferedPlan.PrimaryTarget == npc.whoAmI, "combat must bind its prepared offer to the protected enemy");
        Threat threat = senses.Threats.Threats.Find(t => t.Npc == npc)
            ?? throw new InvalidOperationException("the guard scene must observe its planted hostile as a threat");
        combat.Enter(context);
        var preparedRequest = combat.Execute(context);
        var unrelated = new Threat { Npc = new NPC { whoAmI = 6, active = true, life = 100,
            position = new Vector2(800, 900) }, CanReachPlayer = true, Urgency = 1f };
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatSense).GetProperty("MostUrgent")!.SetValue(senses.Threats, unrelated);
        Require(combat.Score() == entry && combat.Execute(context) == preparedRequest,
            "combat execution must retain the scored target and anchor until preparation refreshes them");
        Require(combat.CommittedPlan != null && combat.CommittedPlan.PrimaryTarget == npc.whoAmI,
            "combat entry must commit the prepared enemy, not the later urgent enemy");
        companion.Brain.Activity.Select(combat, context);
        long firstProtection = companion.Brain.Activity.Id;
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatSense).GetProperty("MostUrgent")!.SetValue(senses.Threats, threat);
        threat.Urgency = .15f;
        threat.EffectiveTicksToPlayer = 150;
        senses.SetInterventionEstimate(1);
        var held = combat.CommittedPlan;
        float retreat = VerifyPreparedActivities.PrepareAndScore(combat, context);
        Require(retreat > 0 && ReferenceEquals(combat.CommittedPlan, held),
            "a small retreat retains the committed protection of the same relevant threat");
        // Genuinely irrelevant, to both bodies: zeroing only the player's side leaves the
        // companion-side danger pricing the fight, which is self-defense honestly kept. There is
        // no relevance timer to run down — the old guard's clear-ticks are gone with it, and the
        // new planner reprices irrelevance the tick it sees it — so no tick is advanced: advancing
        // past the stall window without firing would honestly stall, which is progress mechanics,
        // not relevance, and would end the membership this row asserts is kept.
        threat.CanReachPlayer = false;
        threat.Urgency = 0;
        threat.CanReachCompanion = false;
        threat.UrgencyToCompanion = 0;
        senses.SetInterventionEstimate(float.PositiveInfinity);
        float cold = VerifyPreparedActivities.PrepareAndScore(combat, context);
        // The collapse is read on the plans' unclamped value, not the scores: the offer maps the
        // plan through the specified saturate, so a good fight still reads the clamp after losing
        // its threat, and only the plans show the reprice. The matrix reads it the same way.
        float coldWeighted = combat.OfferedPlan?.Weighted ?? 0f;
        Require(cold > 0 && coldWeighted < entryWeighted && ReferenceEquals(combat.CommittedPlan, held),
            $"irrelevance collapses the offer's value without ending its membership; entry={entryWeighted} ({entry}) cold={coldWeighted} ({cold})");
        threat.CanReachPlayer = true;
        threat.Urgency = 1;
        VerifyPreparedActivities.PrepareAndScore(combat, context);
        combat.Enter(context);
        npc.active = false;
        Require(combat.Execute(context).Target == null,
            "a dead committed target must be handed to the positioner as no target rather than pursued");
        senses.Threats.Threats.Clear();
        senses.SetInterventionEstimate(float.PositiveInfinity);
        Require(VerifyPreparedActivities.PrepareAndScore(combat, context) == 0 && combat.CommittedPlan == null, "disappeared threat releases commitment");
        companion.Brain.Activity.Select(combat, context);
        var replacement = new NPC { whoAmI = 5, active = true, life = 100, lifeMax = 100, damage = 20, position = new Vector2(500, 880) };
        Main.npc[5] = replacement;
        var second = new Threat { Npc = replacement, CanReachPlayer = true, Urgency = 1f, EffectiveTicksToPlayer = 0 };
        senses.Threats.Threats.Add(second);
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatSense).GetProperty("MostUrgent")!.SetValue(senses.Threats, second);
        // On the next tick, the way the brain would ask it: the enemy forecast is cached per tick,
        // so planning the replacement on the cleared tick aims its sim at the old threat's forecast
        // and the new body is invisible to every use.
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.Senses).GetProperty("Tick")!.GetSetMethod(true)!.Invoke(senses,
            new object[] { senses.Tick + 1 });
        float renewed = VerifyPreparedActivities.PrepareAndScore(combat, context);
        Require(renewed > 0 && combat.CommittedPlan != null && combat.CommittedPlan.PrimaryTarget == 5,
            $"the replacement threat must be offered and committed a fresh plan; renewed={renewed} reason={combat.EligibilityReason}");
        companion.Brain.Activity.Select(combat, context);
        Require(companion.Brain.Activity.Id != firstProtection,
            "guarding a different enemy must start a distinct protection activity in the shared owner");
        second.Urgency = .15f;
        var secondHeld = combat.CommittedPlan;
        Require(VerifyPreparedActivities.PrepareAndScore(combat, context) > 0 && ReferenceEquals(combat.CommittedPlan, secondHeld),
            "a second threat must inherit commitment while guarding stays selected");

        // Attempt boundaries: the first threat's release above happened before this attempt opened,
        // so it cannot conclude it; the committed second threat vanishing before any attack invalidates
        // the attempt rather than completing it, because nothing the plan did removed it.
        combat.BeginAttempt();
        Require(combat.ConcludeAttempt(0).Status == live::AICompanion.Companion.Brain.Activities.AttemptStatus.Attempted,
            $"an attempt opened after an earlier release must not conclude from it; got {combat.ConcludeAttempt(0)}");
        replacement.active = false;
        senses.Threats.Threats.Clear();
        VerifyPreparedActivities.PrepareAndScore(combat, context);
        Require(combat.ConcludeAttempt(0) is { Status: live::AICompanion.Companion.Brain.Activities.AttemptStatus.Invalid,
                Cause: "planned-targets-gone-before-attack" },
            $"a committed threat gone before any attack invalidates the attempt; got {combat.ConcludeAttempt(0)}");
    }

    private static void BuildGuardFloor()
    {
        Main.maxTilesX = Main.maxTilesY = 140;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null, new object[] { (ushort)140, (ushort)140 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = 80; y <= 82; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.HasTile = true;
                tile.TileType = 1;
            }
        TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

    private static void VerifyRecoveryThroughBrain()
    {
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null, new object[] { (ushort)200, (ushort)100 }, null)!;
        // The scene's own dimensions, stated rather than inherited. The tilemap is two hundred tiles wide because
        // the flight is two thousand pixels long, but `Main.maxTilesX` is a static holding whatever the previous
        // fixture left — a hundred, here — and everything that asks the world about a tile past that answers
        // "solid". So the body flew the whole way, arrived within a pixel of the player, and `ClearOfTerrain` read
        // false forever: recovery never landed, in an empty world, because the world said the empty half of it was
        // rock. A rebuilt map is also a new world to every chunk and retained query, which compare it by
        // reference, so the reference is replaced with it.
        Main.maxTilesX = 200;
        Main.maxTilesY = 100;
        TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World
            = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
        var companion = VerifyCompanionLifecycle.Create();
        Main.player[0].dead = false;
        Main.player[0].Bottom = new Vector2(2200, 1200);
        companion.NPC.Center = new Vector2(100, 1200);
        bool started = false, landed = false;
        bool quietHands = false;
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
                quietHands = companion.Brain.EngageTarget == null && companion.Combat.LastFireOutcome != "fired";
                Main.npc[1].active = false;
            }
            started |= companion.Brain.FollowRecovery.Active;
            if (started && !companion.Brain.FollowRecovery.Active) { landed = true; break; }
            // `noGravity` and `noTileCollide` are permanently on for this body and cannot witness anything, so
            // what says the brain sent this through the flight motor is the motor's own flight state.
            Require(!started || companion.Motor.RecoveryFlight,
                "the real brain must send distant following through the flight motor");
            Require(companion.NPC.velocity.Length() <= Weights.FollowRecoverySpeed + .01f,
                "brain recovery must remain continuous bounded motion");
            // The engine's no-collision translation, with the real brain/motor supplying it.
            companion.NPC.position += companion.NPC.velocity;
        }
        Require(started && landed,
            $"the production brain must enter and complete distant recovery; started={started} landed={landed} "
            + $"centre={companion.NPC.Center} player={Main.player[0].Bottom} gap={Vector2.Distance(companion.NPC.Center, Main.player[0].Bottom):0} "
            + $"world={Main.maxTilesX}x{Main.maxTilesY} recoveryActive={companion.Brain.FollowRecovery.Active} "
            + $"flight={companion.Motor.RecoveryFlight} action={companion.Brain.LastAction?.Name} request={companion.Brain.LastRequest.Kind} "
            + $"nav={companion.Brain.Navigator.Status}");
        Require(quietHands, "recovery flight is not combat, so an enemy appearing mid-flight must neither be targeted nor fired at");
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
        // Only the position. The two engine flags this used to read are permanently on for this body, so they
        // could never have witnessed clearance ending; `RecoveryFlight` above is what does, and it is already
        // asserted on the line before.
        Require(companion.NPC.position.X < 448,
            $"clearance must return the body to the side it entered from; position={companion.NPC.position}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
