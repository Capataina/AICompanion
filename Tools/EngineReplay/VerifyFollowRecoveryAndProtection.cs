extern alias live;
using Microsoft.Xna.Framework;
using Terraria;
using Recovery = live::AICompanion.Companion.Brain.Behaviours.Companionship.RecoverDistantFollowing;
using Guard = live::AICompanion.Companion.Brain.Behaviours.Companionship.GuardAction;
using Context = live::AICompanion.Companion.Brain.Behaviours.ActionContext;
using Threat = live::AICompanion.Companion.Brain.WorldObservation.ThreatRecord;
using Weights = live::AICompanion.Companion.Brain.BehaviourSelection.Weights;

internal static class VerifyFollowRecoveryAndProtection
{
    public static int Run()
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
        companion.Motor.Apply(live::AICompanion.Companion.Brain.SharedMovementSystem.Controls.None, "downed");
        Require(!companion.NPC.noGravity && !companion.NPC.noTileCollide, "clear downed body restores ordinary collision");
        VerifyGuard();
        VerifyRecoveryThroughBrain();
        VerifyCancelledFlightClearsTerrain();
        Console.WriteLine($"follow recovery and protection: flight arrival in {ticks} ticks; exclusions, collision, downing, threat continuation and expiry pass");
        return 0;
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
        typeof(live::AICompanion.Companion.Brain.WorldObservation.ThreatSense).GetProperty("MostUrgent")!.SetValue(senses.Threats, threat);
        senses.SetInterventionEstimate(float.PositiveInfinity);
        var context = new Context(companion, senses);
        var guard = new Guard();
        float entry = guard.Score(context);
        Require(entry > Weights.Commitment, "immediate danger can interrupt committed following even nearby");
        guard.Enter(context);
        threat.Urgency = .15f;
        threat.EffectiveTicksToPlayer = 150;
        senses.SetInterventionEstimate(1);
        Require(guard.Score(context) >= entry, "small retreat retains protection of the same relevant threat");
        Require(guard.Score(context) * Weights.Commitment < Weights.SurviveUrgency,
            "maximum survival can interrupt committed protection");
        threat.CanReachPlayer = false;
        threat.Urgency = 0;
        senses.SetInterventionEstimate(float.PositiveInfinity);
        int firstClear = senses.Tick;
        Require(guard.Score(context) > 0, "one safe frame must not abandon protection");
        typeof(live::AICompanion.Companion.Brain.WorldObservation.Senses).GetProperty("Tick")!.SetValue(senses,
            firstClear + Weights.GuardClearTicks);
        Require(guard.Score(context) == 0 && guard.ProtectedThreatId == -1,
            "sustained irrelevance releases a living threat");
        threat.CanReachPlayer = true;
        threat.Urgency = 1;
        guard.Enter(context);
        npc.active = false;
        senses.Threats.Threats.Clear();
        senses.SetInterventionEstimate(float.PositiveInfinity);
        Require(guard.Score(context) == 0 && guard.ProtectedThreatId == -1, "disappeared threat releases commitment");
        typeof(live::AICompanion.Companion.Brain.BehaviourSelection.Chooser).GetProperty("Current")!.SetValue(companion.Brain.Chooser, guard);
        var replacement = new NPC { whoAmI = 5, active = true, life = 100, damage = 20 };
        var second = new Threat { Npc = replacement, CanReachPlayer = true, Urgency = 1f, EffectiveTicksToPlayer = 0 };
        senses.Threats.Threats.Add(second);
        typeof(live::AICompanion.Companion.Brain.WorldObservation.ThreatSense).GetProperty("MostUrgent")!.SetValue(senses.Threats, second);
        float renewed = guard.Score(context);
        second.Urgency = .15f;
        Require(guard.Score(context) >= renewed && guard.ProtectedThreatId == 5,
            "a second threat must inherit commitment while guarding stays selected");
    }

    private static void VerifyRecoveryThroughBrain()
    {
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        var companion = VerifyCompanionLifecycle.Create();
        Main.player[0].dead = false;
        Main.player[0].Bottom = new Vector2(1400, 1200);
        companion.NPC.Bottom = new Vector2(100, 1200);
        var memory = live::AICompanion.Companion.Brain.SharedMovementSystem.RememberExecutedRoutes.World;
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
            companion.AI();
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
        companion.NPC.position = new Vector2(480, 900);
        Require(!companion.Motor.ClearOfTerrain, "cancellation fixture must be inside a thick wall");
        companion.CheckDead();
        int ticks = 0;
        while (companion.Motor.RecoveryFlight && ticks++ < 100)
        {
            companion.Motor.Apply(live::AICompanion.Companion.Brain.SharedMovementSystem.Controls.None, "downed");
            companion.NPC.position += companion.NPC.velocity;
        }
        Require(companion.IsDowned && companion.Motor.ClearOfTerrain && !companion.Motor.RecoveryFlight,
            "a downed interrupted flight must leave the wall without travelling to the owner");
        Require(live::AICompanion.Companion.Brain.SharedMovementSystem.RememberExecutedRoutes.World.Count == 0,
            "cancelled-flight clearance must never teach world route memory");
        Require(companion.NPC.position.X < 448 && !companion.NPC.noTileCollide && !companion.NPC.noGravity,
            "clearance returns to the entry side and restores native collision");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
