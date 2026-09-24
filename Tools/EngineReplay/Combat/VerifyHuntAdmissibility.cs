extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;

using Combat = live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies;
using T = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatRecord;
using C = live::AICompanion.Companion.Brain.Activities.ActionContext;

/// <summary>
/// A combat target is admissible only if the planner can stand somewhere it can reach and shoot
/// from. The owner's rule: can I hit this enemy, if not can I move to hit it, and if neither then
/// it should not be hunted at all.
///
/// Both halves are checked, and the second is the one that matters. Applying the from-here shot
/// test to every target is the obvious change and rejects every enemy the companion would simply
/// have had to fly toward — worse than the behaviour it replaces. So a target with no shot from
/// where the companion floats, but a reachable place that can see it, must stay admissible.
/// </summary>
internal static class VerifyHuntAdmissibility
{
    public static int Run()
    {
        VerifyWalkableFiringPositionKeepsTheTarget();
        VerifySealedTargetIsRefused();
        VerifyUnfinishedSearchIsUndecidedNotRefused();
        MeasureTheCheckOnAHopelessCrowd();
        Console.WriteLine("combat admissibility: a repositionable target is kept, an unshootable target is refused, an unfinished search is undecided rather than refused, and the check's cost on a hopeless crowd is measured");
        return 0;
    }

    /// <summary>
    /// Establishing a firing opportunity runs inside the per-tick score of every combat, so it is
    /// exactly the kind of addition that has made this brain unplayable before. The worst case is a
    /// crowd where nothing is shootable and the companion is moving: every target is refused, the
    /// retry limit is reached each tick, and a body in motion keeps moving out from under the
    /// verdict cache.
    ///
    /// A measure and not a pass line. This row carried a 5 ms bound until 24 September 2026 and went
    /// red at 8.2 ms the day before because the machine slowed 4.8 times mid-run, with the parent
    /// commit reproducing it in the same minute, so the bound was testing the machine. The regression
    /// it stood guard over — a cache key that stops holding or a sample stride that collapses to one,
    /// which once cost fifty milliseconds a tick — is a tenfold step against this row's own history,
    /// and the scoreboard's comparison of each timing with its history is what catches it now.
    /// </summary>
    private static void MeasureTheCheckOnAHopelessCrowd()
    {
        BuildFloor();
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= FloorY + 30; y++)
                Solid(x, y);
        for (int x = 59; x <= 61; x++)
            for (int y = FloorY + 11; y <= FloorY + 12; y++)
                Open(x, y);
        Rebuild();

        var companion = Place(companionTileX: 58, enemyTileX: 60, enemyTileY: FloorY + 13, out NPC first, out C ctx);
        var threats = companion.Brain.Senses.Threats.Threats;
        for (int slot = 26; slot < 34; slot++)
        {
            var extra = new NPC();
            extra.SetDefaults(Terraria.ID.NPCID.Zombie);
            extra.whoAmI = slot;
            extra.active = true;
            extra.velocity = Vector2.Zero;
            extra.Bottom = first.Bottom + new Vector2((slot - 30) * 8f, 0f);
            Main.npc[slot] = extra;
            threats.Add(new T { Npc = extra, DistanceToCompanion = 210, DistanceToPlayer = 210 });
        }

        var combat = new Combat();
        VerifyPreparedActivities.PrepareAndScore(combat, ctx); // first call warms the terrain caches this is not trying to measure
        var clock = System.Diagnostics.Stopwatch.StartNew();
        const int Ticks = 240;
        for (int tick = 0; tick < Ticks; tick++)
        {
            // Moving, so the verdict cache cannot simply hold a single answer for the whole run.
            companion.NPC.position.X += 2f;
            VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        }
        double perTick = clock.Elapsed.TotalMilliseconds / Ticks;
        Console.WriteLine($"combat admissibility cost: {perTick:0.000} ms per score over {Ticks} ticks with {threats.Count} unshootable targets");
        // Two weapons, nine sealed bodies, a muzzle that walks two pixels a tick so the planned-sim
        // cache cannot hold a single answer. The measure's name is the ledger's key for its history,
        // so renaming it starts that history again.
        EmitTimingMeasures.Timing("combat admissibility: cost per score on a hopeless moving crowd", perTick,
            $"the combat stance's prepare-and-score, mean over {Ticks} ticks after one untimed warm-up, two weapons, "
            + $"{threats.Count} sealed targets, the companion moving two pixels a tick");
    }

    private const int FloorY = 80;

    /// <summary>
    /// The trap case. A pillar between the companion and the enemy blocks the shot from where it
    /// floats, while the open floor beyond the pillar's end has a clear line. Flying there is the
    /// whole point of hunting, so the target must survive selection.
    /// </summary>
    private static void VerifyWalkableFiringPositionKeepsTheTarget()
    {
        BuildFloor();
        // A short pillar beside the companion. It blocks the flat line from the companion's own
        // position without sealing anything: the floor continues past it in both directions.
        for (int y = FloorY - 3; y < FloorY; y++) Solid(40, y);
        Rebuild();

        var companion = Place(companionTileX: 38, enemyTileX: 60, enemyTileY: FloorY, out NPC enemy, out C ctx);

        // The first preparation may honestly be undecided — the stands past the pillar are an unanswered
        // search — so the row settles the senses the way the brain does and asserts on the verdict.
        var combat = new Combat();
        float score = SettleUntilDecided(companion, ctx, combat, out int passes);
        Console.WriteLine($"combat admissibility: a repositionable target settled to {combat.EligibilityReason} after priming ({passes} resolves)");
        Require(score > 0f,
            "an enemy with no shot from here but a reachable sighted floor beyond a pillar was refused: " +
            $"applying the from-here test to every target rejects exactly the enemies hunting exists to fly toward. reason={combat.EligibilityReason}");
        Require(combat.OfferedPlan != null && combat.OfferedPlan.PrimaryTarget == enemy.whoAmI,
            $"the repositionable enemy was not the offered plan's target; reason={combat.EligibilityReason}");
        var capturedPlan = combat.OfferedPlan;
        float capturedTrip = combat.ForecastTicks();
        ctx.Senses.Threats.Threats.Clear();
        enemy.position.X += 48;
        Require(combat.Score() == score && combat.ForecastTicks() == capturedTrip && ReferenceEquals(combat.OfferedPlan, capturedPlan),
            "combat comparison must retain its prepared values when live observation changes");
        // Committed, then the target dies: the request must carry no target, so the positioner holds
        // rather than pursuing a corpse.
        combat.Enter(ctx);
        Require(combat.CommittedPlan != null, "Enter must commit the offered plan");
        enemy.active = false;
        var request = combat.Execute(ctx);
        Require(request.Target == null,
            "a dead committed target must not be handed to the positioner as a pursuit target");
    }

    /// <summary>
    /// The playtest case. The enemy sits in a sealed chamber with solid rock all around its own
    /// level, so no place within weapon reach has a line to it. The 2026-09-11 session stood in
    /// combat mode at exactly this shape rather than refusing it.
    /// </summary>
    private static void VerifySealedTargetIsRefused()
    {
        BuildFloor();
        // A sealed chamber below the main floor: two tiles of air inside thick rock, with no opening
        // at all, so nothing outside it can see in. The companion floats almost directly above it,
        // because an enemy placed far away is refused on activity radius long before the shot
        // question is asked and the check would pass without testing anything it claims to.
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= FloorY + 30; y++)
                Solid(x, y);
        for (int x = 59; x <= 61; x++)
            for (int y = FloorY + 11; y <= FloorY + 12; y++)
                Open(x, y);
        Rebuild();

        var companion = Place(companionTileX: 58, enemyTileX: 60, enemyTileY: FloorY + 13, out NPC enemy, out C ctx);

        // The refusal must be earned against finished searches, not against planning that never
        // grew: an undecided stand is deliberately not a refusal, so a green result here on
        // unfinished searches would be measuring nothing. Each pass runs the senses the way the
        // brain does — the reach flood grows a slice per update — and re-prepares.
        var combat = new Combat();
        float score = SettleUntilDecided(companion, ctx, combat, out int passes);
        Console.WriteLine($"combat admissibility: a sealed enemy became a proven absence on a completed flood ({combat.EligibilityReason}, {passes} resolves)");
        Require(score == 0f,
            $"a sealed enemy no reachable position can shoot was still hunted: score={score}; plan={combat.OfferedPlan?.Id}; reason={combat.EligibilityReason}");
        Require(combat.Eligibility == live::AICompanion.Companion.Brain.Activities.OfferEligibility.KnownUnusable,
            $"a proven absence of firing stands is a known-unusable search, not an absent enemy; got {combat.Eligibility}/{combat.EligibilityReason}");
        // The offer names the search's refusal; the funnel names the threat it was about and what the planner
        // read, which is what thousands of rows of an undecided stand on the 15 September capture could not say.
        var furthest = combat.Funnel.Best;
        Require(furthest is { RefusedAt: "unplannable" } refused && refused.Identity.StartsWith($"npc{enemy.whoAmI}:", StringComparison.Ordinal)
            && refused.Readings.Contains("reason=" + combat.EligibilityReason, StringComparison.Ordinal),
            $"hunting's funnel names the sealed enemy as refused for want of a firing stand, with the planner's verdict; best={furthest} counts={combat.Funnel.Summary()}");
    }

    /// <summary>
    /// The tall-pillar case on the first preparation. Nothing solves from where the body hovers — a sword
    /// in hand, whose reach ends twenty tiles short of the enemy — and the stands past it are an unanswered
    /// search, so the offer is undecided rather than a refusal: a bound that ran out is a third value, never
    /// a negative. Once the search settles, the same scene offers the plan past the pillar. A bow would arc
    /// over this pillar from here under the honest simulator, so the bow no longer produces the premise.
    /// </summary>
    private static void VerifyUnfinishedSearchIsUndecidedNotRefused()
    {
        BuildFloor();
        for (int y = FloorY - 8; y < FloorY; y++) Solid(40, y);
        Rebuild();

        var companion = Place(companionTileX: 38, enemyTileX: 60, enemyTileY: FloorY, out NPC enemy, out C ctx);
        var gear = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;
        gear.Slots[0] = new Item();
        gear.Slots[1] = new Item();
        gear.Slots[0].SetDefaults(Terraria.ID.ItemID.CopperBroadsword);

        var combat = new Combat();
        float first = VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(combat.Eligibility == live::AICompanion.Companion.Brain.Activities.OfferEligibility.Unresolved
            && combat.EligibilityReason == "stands-undecided",
            $"an unanswered stand search is undecided, never a refusal and never a plan from here; got {combat.Eligibility}/{combat.EligibilityReason} score={first}");

        float score = SettleUntilDecided(companion, ctx, combat, out int passes);
        Require(score > 0f && combat.OfferedPlan != null && combat.OfferedPlan.PrimaryTarget == enemy.whoAmI,
            $"a solvable stand past a pillar was not hunted once the search settled: score={score}; reason={combat.EligibilityReason}");
        Require(combat.Eligibility == live::AICompanion.Companion.Brain.Activities.OfferEligibility.Usable,
            $"a settled plan past a pillar is a combat; got {combat.Eligibility}/{combat.EligibilityReason}");
    }

    /// <summary>
    /// Primes the reach flood to completion and prepares once, so the offer is the search's finished answer
    /// rather than what the first flood slice happened to reach. Updates alone grow nothing headlessly — the
    /// flood grows across resolves — so looping updates and preparations would read the shot from here for
    /// ever while the stands past the pillar stayed unexamined.
    /// </summary>
    private static float SettleUntilDecided(
        live::AICompanion.Companion.CharacterBody.CompanionNPC companion, C ctx, Combat combat, out int passes)
    {
        // WithPlayer, not a firing request: LineOfFire without a flight profile early-outs before it
        // refreshes the flood, so three thousand of those prime nothing.
        var request = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer, Main.player[0].Bottom);
        passes = 0;
        for (int i = 0; i < 3000 && !companion.Brain.Positioner.ReachComplete; i++)
        {
            companion.Brain.Positioner.Resolve(request, companion.Brain.Senses);
            passes++;
        }
        Require(companion.Brain.Positioner.ReachComplete, "the reach flood must complete before the offer can be read as the search's answer");
        float score = VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        passes++;
        Require(combat.Eligibility != live::AICompanion.Companion.Brain.Activities.OfferEligibility.Unresolved,
            $"the stand search must decide on a completed flood; still {combat.EligibilityReason}");
        return score;
    }

    private static live::AICompanion.Companion.CharacterBody.CompanionNPC Place(
        int companionTileX, int enemyTileX, int enemyTileY, out NPC enemy, out C ctx)
    {
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(companionTileX * 16f, FloorY * 16f - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.position = new Vector2(companionTileX * 16f, FloorY * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;

        enemy = new NPC();
        enemy.SetDefaults(Terraria.ID.NPCID.Zombie);
        enemy.whoAmI = 25;
        enemy.active = true;
        enemy.velocity = Vector2.Zero;
        enemy.Bottom = new Vector2(enemyTileX * 16f + 8f, enemyTileY * 16f);
        Main.npc[25] = enemy;

        companion.Brain.Senses.Update(companion.NPC, player);
        var threats = companion.Brain.Senses.Threats.Threats;
        threats.Clear();
        threats.Add(new T
        {
            Npc = enemy,
            DistanceToCompanion = Vector2.Distance(companion.NPC.Bottom, enemy.Bottom),
            DistanceToPlayer = Vector2.Distance(player.Bottom, enemy.Bottom),
        });
        ctx = new C(companion, companion.Brain.Senses);
        return companion;
    }

    private static void BuildFloor()
    {
        Main.maxTilesX = Main.maxTilesY = 140;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)140, (ushort)140 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= FloorY + 2; y++)
                Solid(x, y);
    }

    private static void Rebuild()
    {
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = true;
        tile.TileType = 1;
    }

    private static void Open(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = false;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
