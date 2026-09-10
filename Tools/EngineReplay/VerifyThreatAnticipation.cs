extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;

internal static class VerifyThreatAnticipation
{
    public static int Run()
    {
        VerifyObservedMotion.SetTick(1);
        var player = new Player { active = true, statLifeMax2 = 100, position = new Vector2(400, 900) };
        var companion = new NPC { active = true, whoAmI = 0, width = 20, height = 40, lifeMax = 100, position = new Vector2(600, 900) };
        Main.player[0] = player;
        for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
        // A harmful hostile that cannot be selected by Terraria's chase predicate remains a
        // hazard observation; the production Arsenal filters that predicate later.
        var hazard = new NPC { active = true, whoAmI = 1, width = 20, height = 40, damage = 20, life = 50, lifeMax = 50, friendly = false, position = new Vector2(430, 900) };
        Main.npc[1] = hazard;
        hazard.dontTakeDamage = true;
        Require(!hazard.CanBeChasedBy(), "fixture must contain a genuinely unattackable hazard");
        var sense = new live::AICompanion.Companion.Brain.WorldObservation.ThreatSense();
        sense.Update(player, companion);
        Require(sense.Threats.Count == 1, "harmful nonchaseable hostile disappeared before threat observation");
        Require(sense.PlayerDanger > 0f, "near harmful hostile did not create player danger");
        sense.SetInterventionEstimate(0f);
        float immediateHorizon = sense.Horizon;
        sense.SetInterventionEstimate(float.PositiveInfinity);
        Require(sense.Horizon == 0f && sense.ProtectionUrgency > 0f && sense.Horizon <= immediateHorizon,
            "a threat with no demonstrated timely intervention cannot leave a safe excursion horizon");
        player.dead = true;
        hazard.position = companion.position + new Vector2(10, 0);
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        sense.Update(player, companion);
        Require(sense.PlayerDanger == 0f && sense.CompanionDanger > 0f, "player death did not clear only player danger");

        // Attribution is engine evidence, never a proximity guess: a source NPC with zero melee
        // damage remains dangerous when it owns a positive-damage hostile projectile, while a
        // nearby un-attributed projectile must not be filed under it.
        var source = new NPC { active = true, whoAmI = 2, damage = 0, life = 50, lifeMax = 50, friendly = false };
        Main.npc[2] = source;
        live::AICompanion.Companion.Brain.WorldObservation.HostileAttackSources.Clear();
        live::AICompanion.Companion.Brain.WorldObservation.HostileAttackSources.Spawn(source);
        var shot = new Projectile { hostile = true, damage = 37 };
        live::AICompanion.Companion.Brain.WorldObservation.HostileAttackSources.Observe(shot, new EntitySource_Parent(source));
        Require(live::AICompanion.Companion.Brain.WorldObservation.HostileAttackSources.RecentDamage(source) == 37,
            "parent-sourced hostile projectile was not attributed to its zero-melee-damage source NPC");
        live::AICompanion.Companion.Brain.WorldObservation.HostileAttackSources.Spawn(source);
        Require(live::AICompanion.Companion.Brain.WorldObservation.HostileAttackSources.RecentDamage(source) == 0,
            "NPC generation reuse retained a prior projectile attack attribution");

        // The generic predictor records a measured continuation miss and reduces confidence;
        // it does not require a type-specific enemy script.
        player.dead = false;
        live::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.Clear();
        hazard.velocity = new Vector2(1, 0);
        hazard.noGravity = true;
        hazard.noTileCollide = true;
        live::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.Observe(hazard);
        hazard.position += hazard.velocity;
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        live::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.Observe(hazard);
        float confidenceBefore = live::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.Confidence(hazard, 1);
        hazard.position += new Vector2(40, 0);
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        live::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.Observe(hazard);
        Require(live::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.ErrorSamples(hazard) > 0,
            "observation must measure its own forecast without an aiming consumer");
        Require(live::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.Confidence(hazard, 1) < confidenceBefore,
            "measured forecast miss did not lower generic prediction confidence");
        live::AICompanion.Companion.Brain.WorldObservation.HostileAttackSources.Spawn(hazard);
        Require(live::AICompanion.Companion.Brain.WorldObservation.PredictObservedMotion.ErrorSamples(hazard) == 0,
            "a recycled NPC slot must not inherit the previous occupant's confidence");
        VerifyAttackability();
        Console.WriteLine("threat anticipation: harmful nonchaseable hazards, player-death isolation and measured forecast confidence pass");
        return 0;
    }

    private static void VerifyAttackability()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.player[0].dead = false;
        companion.NPC.position = new Vector2(400, 800);
        Main.player[0].position = new Vector2(500, 800);
        var target = new NPC { whoAmI = 1, active = true, life = 100, lifeMax = 100, damage = 20,
            width = 30, height = 40, position = new Vector2(510, 800), noGravity = true };
        Main.npc[1] = target;
        companion.Brain.Senses.Update(companion.NPC, Main.player[0], companion.Breath);
        var context = new live::AICompanion.Companion.Brain.Behaviours.ActionContext(companion, companion.Brain.Senses);
        Require(companion.Arsenal.CanEngage(context, target), "open fixture must initially have an attackable shot");
        float estimate = companion.Arsenal.EstimateInterventionTicks(context);
        Require(float.IsFinite(estimate) && estimate > 0f,
            "first intervention estimate must include flight time rather than overflow its uninitialised cache");
        target.dontTakeDamage = true;
        Require(!companion.Arsenal.CanEngage(context, target) && !companion.Arsenal.TryFire(context, target)
            && float.IsPositiveInfinity(companion.Arsenal.EstimateInterventionTicks(context)),
            "current attackability must override a previously cached clear shot");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
