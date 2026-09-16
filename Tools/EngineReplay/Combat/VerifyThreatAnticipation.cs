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

    /// <summary>
    /// Attackability under the planner: a solving use from the body's muzzle, a committed plan kept by
    /// membership across ticks, an intervention estimate that counts its predicted kill down with the clock,
    /// a reused hostile slot re-planned rather than retained, and current unattackability ending the plan.
    /// The per-tick forecast cache is the freshness contract now — the arsenal's immediate invalidation is
    /// gone with it — so a moved target is re-asked on the next tick, the way the brain would ask it.
    /// </summary>
    private static void VerifyAttackability()
    {
        BuildOpenWorld();
        for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
        var companion = VerifyCompanionLifecycle.Create();
        Main.player[0].dead = false;
        companion.NPC.position = new Vector2(400, 800);
        Main.player[0].position = new Vector2(500, 800);
        var target = new NPC { whoAmI = 1, active = true, life = 100, lifeMax = 100, damage = 20,
            width = 30, height = 40, position = new Vector2(510, 800), noGravity = true };
        Main.npc[1] = target;
        companion.Brain.Senses.Update(companion.NPC, Main.player[0]);
        var context = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, companion.Brain.Senses);
        var combat = companion.Combat;
        Vector2 muzzle = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.CompanionCombat.Muzzle(companion.NPC);
        Require(combat.ShotSolves(context, muzzle, target), "open fixture must initially have an attackable shot");

        var fight = new live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies();
        companion.Brain.Chooser.Activity.Select(fight, context);
        Require(VerifyPreparedActivities.PrepareAndScore(fight, context) > 0f && fight.OfferedPlan != null,
            $"the attackable target must be offered a plan; reason={fight.EligibilityReason}");
        float estimate = combat.EstimateInterventionTicks(context);
        Require(float.IsFinite(estimate) && estimate > 0f,
            "the first intervention estimate must count down to a predicted kill rather than read infinite with a plan committed");

        // Protection budgets repeat hits: one life left dies sooner than a hundred.
        target.life = 1;
        combat.Planner.Release("fixture-replan");
        Require(VerifyPreparedActivities.PrepareAndScore(fight, context) > 0f && fight.OfferedPlan != null,
            $"the weakened target must be offered a fresh plan; reason={fight.EligibilityReason}");
        float singleHit = combat.EstimateInterventionTicks(context);
        Require(singleHit < estimate, "protection must budget repeat hits to remove a healthy threat, not only first impact");

        // The kill approaches with the clock; holding a tool does not freeze it, because the estimate is a
        // predicted tick rather than a projectile in flight.
        combat.NoteHandsBusy();
        SetTick(companion, companion.Brain.Senses.Tick + 5);
        Require(combat.EstimateInterventionTicks(context) == singleHit - 5,
            "the intervention estimate must count its predicted kill down with the clock, hands busy or not");

        // Inside solid rock nothing solves; back in the open the next tick solves again.
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
        SetTick(companion, companion.Brain.Senses.Tick + 1);
        Require(!combat.ShotSolves(context, muzzle, target), "a target inside solid terrain must have no solving use");
        target.position = openPosition;
        SetTick(companion, companion.Brain.Senses.Tick + 1);
        Require(combat.ShotSolves(context, muzzle, target),
            "an opening firing window must solve again on the next tick rather than inherit the negative answer");

        // Retention is membership: the committed plan survives re-preparation while it still describes the world.
        var held = combat.Planner.Committed;
        Require(held != null, "the attackable scene must hold a committed plan before the retention checks");
        VerifyPreparedActivities.PrepareAndScore(fight, context);
        Require(ReferenceEquals(combat.Planner.Committed, held) && fight.OfferedPlan != null && fight.OfferedPlan.PrimaryTarget == target.whoAmI,
            "a single attackable target's plan must be kept across preparations while it still describes the world");

        // A reused hostile slot is re-planned, never retained: the generation the plan was admitted against
        // is gone, so the commitment ends and the search offers the new occupant fresh.
        live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.Spawn(target);
        float score = VerifyPreparedActivities.PrepareAndScore(fight, context);
        Require(combat.Planner.LastInvalidation == "target-gone-unplanned",
            $"a reused slot must end the commitment admitted against its old occupant; invalidation={combat.Planner.LastInvalidation}");
        Require(score > 0f && fight.OfferedPlan != null && !ReferenceEquals(fight.OfferedPlan, held),
            $"the new occupant must be offered a fresh plan rather than inherit retention; score={score} reason={fight.EligibilityReason}");

        // Current unattackability ends the plan: no remaining use solves, so the commitment is released and
        // the search refuses the body rather than offering a fight that cannot happen.
        target.dontTakeDamage = true;
        score = VerifyPreparedActivities.PrepareAndScore(fight, context);
        Require(!combat.ShotSolves(context, muzzle, target),
            "an unattackable target must have no solving use whatever the plan once held");
        Require(score == 0f && combat.Planner.Committed == null
            && combat.Planner.LastInvalidation == "uses-stopped-solving"
            && float.IsPositiveInfinity(combat.EstimateInterventionTicks(context)),
            $"current unattackability must release the plan and read infinite protection; score={score} reason={fight.EligibilityReason} invalidation={combat.Planner.LastInvalidation}");
    }

    /// <summary>Open air where the shots fly, with a floor far below the sight line.</summary>
    private static void BuildOpenWorld()
    {
        Main.maxTilesX = Main.maxTilesY = 140;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            null, new object[] { (ushort)140, (ushort)140 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = 80; y <= 82; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.HasTile = true;
                tile.TileType = 1;
            }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

    private static void SetTick(live::AICompanion.Companion.CharacterBody.CompanionNPC companion, int tick)
    {
        var setTick = typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.Senses)
            .GetProperty("Tick")!.GetSetMethod(true)!;
        setTick.Invoke(companion.Brain.Senses, new object[] { tick });
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
