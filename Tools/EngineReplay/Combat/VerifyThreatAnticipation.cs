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
        var sense = new live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatSense();
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
        live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.Clear();
        live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.Spawn(source);
        var shot = new Projectile { hostile = true, damage = 37 };
        live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.Observe(shot, new EntitySource_Parent(source));
        Require(live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.RecentDamage(source) == 37,
            "parent-sourced hostile projectile was not attributed to its zero-melee-damage source NPC");
        live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.Spawn(source);
        Require(live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.RecentDamage(source) == 0,
            "NPC generation reuse retained a prior projectile attack attribution");

        // The generic predictor records a measured continuation miss and reduces confidence;
        // it does not require a type-specific enemy script.
        player.dead = false;
        live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Clear();
        hazard.velocity = new Vector2(1, 0);
        hazard.noGravity = true;
        hazard.noTileCollide = true;
        live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Observe(hazard);
        hazard.position += hazard.velocity;
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Observe(hazard);
        float confidenceBefore = live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Confidence(hazard, 1);
        hazard.position += new Vector2(40, 0);
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Observe(hazard);
        Require(live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.ErrorSamples(hazard) > 0,
            "observation must measure its own forecast without an aiming consumer");
        Require(live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Confidence(hazard, 1) < confidenceBefore,
            "measured forecast miss did not lower generic prediction confidence");
        live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.Spawn(hazard);
        Require(live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.ErrorSamples(hazard) == 0,
            "a recycled NPC slot must not inherit the previous occupant's confidence");
        VerifyAttackability();
        AWalkerBelowAHoveringOrbReachesItOnlyInsideItsJump();
        Console.WriteLine("threat anticipation: harmful nonchaseable hazards, player-death isolation, measured forecast confidence and the walker's jump envelope pass");
        return 0;
    }

    /// <summary>
    /// A walking enemy on a floor under a hovering orb reaches it only inside its own height plus the fighter AI's highest
    /// jump, in three matched arms that differ only in how high the orb hovers. Low, the walker reaches it. High, it does not,
    /// and the orb reads no danger from it — which is the direction the first play of the orb got wrong, reading danger at nine
    /// tenths and more with zombies underneath while it took no hit, and the direction no row guarded: an envelope that always
    /// answered yes left the whole native suite green. The middle arm sits above the fighter AI's ordinary jump and inside its
    /// tallest-step jump, so it fails if the envelope is ever put back to the ordinary hop, which under-read a walker at the foot
    /// of a tall step.
    /// </summary>
    private static void AWalkerBelowAHoveringOrbReachesItOnlyInsideItsJump()
    {
        const int floorRow = 80;
        Main.tileSolid[Terraria.ID.TileID.Stone] = true;
        for (int x = 0; x < 100; x++)
        {
            Tile tile = Main.tile[x, floorRow];
            tile.HasTile = true;
            tile.TileType = Terraria.ID.TileID.Stone;
        }
        float floorTop = floorRow * 16f;
        const float walkerHeight = 40f, orbX = 800f;

        (bool Reaches, float Danger) Arm(float orbBottomAboveFloor)
        {
            for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
            var player = new Player { active = true, statLifeMax2 = 100, position = new Vector2(200, floorTop - 42) };
            Main.player[0] = player;
            var companion = new NPC { active = true, whoAmI = 0, width = 20, height = 20, life = 100, lifeMax = 100,
                position = new Vector2(orbX - 10, floorTop - orbBottomAboveFloor - 20) };
            var walker = new NPC { active = true, whoAmI = 1, width = 18, height = (int)walkerHeight, damage = 20, life = 50, lifeMax = 50,
                friendly = false, position = new Vector2(orbX + 24, floorTop - walkerHeight) };
            Main.npc[1] = walker;
            var sense = new live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatSense();
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            sense.Update(player, companion);
            var record = sense.Threats.Find(t => t.Npc == walker);
            Require(record != null, $"premise: the walker must be observed as a threat at every height; none at {orbBottomAboveFloor} px");
            return (record!.CanReachCompanion, sense.CompanionDanger);
        }

        // Heights of the orb's bottom above the floor, against the walker's 40 px body plus a jump apex: 107 px for the fighter
        // AI's ordinary -8 jump, 202 px for its tallest-step -11 jump.
        var low = Arm(20f);
        var middle = Arm(40f + 150f);
        var high = Arm(40f + 400f);
        Require(low.Reaches && low.Danger > 0f,
            $"a walker beside an orb hovering just above its floor must reach it and read as danger to it; reaches={low.Reaches} danger={low.Danger}");
        Require(middle.Reaches,
            $"a walker must reach an orb above its ordinary jump but inside its tallest-step jump; reaches={middle.Reaches} danger={middle.Danger}");
        Require(!high.Reaches && high.Danger == 0f,
            $"a walker must not reach an orb hovering far above its highest jump, and the orb must read no danger from it; reaches={high.Reaches} danger={high.Danger}");
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
        companion.Brain.Senses.Update(companion.NPC, Main.player[0]);
        var context = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, companion.Brain.Senses);
        Require(companion.Arsenal.CanEngage(context, target), "open fixture must initially have an attackable shot");
        float estimate = companion.Arsenal.EstimateInterventionTicks(context);
        Require(float.IsFinite(estimate) && estimate > 0f,
            "first intervention estimate must include flight time rather than overflow its uninitialised cache");
        target.life = 1;
        float singleHit = companion.Arsenal.EstimateInterventionTicks(context);
        Require(singleHit < estimate, "protection must budget repeat hits to remove a healthy threat, not only first impact");
        companion.Arsenal.NoteHandsBusy();
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.Senses).GetProperty("Tick")!.SetValue(
            companion.Brain.Senses, companion.Brain.Senses.Tick + 5);
        Require(companion.Arsenal.EstimateInterventionTicks(context) == singleHit,
            "time holding a tool must not count down a projectile that was never fired");
        target.life = 100;
        Vector2 openPosition = target.position;
        for (int x = 39; x <= 43; x++)
        for (int y = 46; y <= 54; y++)
        {
            Tile tile = Main.tile[x, y];
            tile.HasTile = true;
            tile.TileType = 1;
        }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Changed(40, 50);
        target.position = new Vector2(40 * 16, 50 * 16);
        Require(!companion.Arsenal.CanEngage(context, target), "target inside solid terrain must invalidate a cached clear shot");
        target.position = openPosition;
        Require(companion.Arsenal.CanEngage(context, target),
            "an opening firing window must invalidate a negative answer immediately without waiting for cache age");
        Require(companion.Arsenal.BestTarget(context) == target, "single attackable target must initially win retention");
        typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.Senses).GetProperty("Tick")!.SetValue(
            companion.Brain.Senses, companion.Brain.Senses.Tick + 1);
        live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.Spawn(target);
        companion.Arsenal.BestTarget(context);
        Require(companion.Arsenal.TargetEvidenceTick == companion.Brain.Senses.Tick,
            "a reused hostile slot must be reranked instead of inheriting prior target retention");
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
