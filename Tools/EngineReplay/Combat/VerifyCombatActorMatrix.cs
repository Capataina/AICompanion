extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Combat = live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using PositionRequest = live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// Proposal 1's actor matrix repeated with a clear shot against a blocked one. Four scenes differ in who a
/// damageable zombie threatens — nobody, the player, the companion or both — and each is run twice on the
/// same floor, once open and once with a tall pillar beside the companion that blocks the line to every
/// enemy without sealing the floor. Who is threatened must follow placement alone, and whether the companion
/// can shoot from where it hovers must follow the pillar alone. The offered plan is worth more clear than
/// blocked, because the pillar forces a reposition the clear arm does not pay; guarding's kill and threat
/// removal are printed for both arms rather than asserted, because they are the measurement of whether
/// protection reads the same attack evidence.
/// </summary>
internal static class VerifyCombatActorMatrix
{
    private const int FloorY = 80, CompanionX = 38, PillarX = 40, PlayerX = 70, NearCompanionX = 43, NearPlayerX = 67;
    // The pillar was three tiles, which was the walking body's own height and so exactly enough to seal the
    // line from a walker's chest to a zombie's. It seals nothing for this body: the shot is fired by the orb's
    // own mechanism from a centre one radius off the floor and it arcs, so a three-tile column dropped the
    // companion-side value without ever making the enemy unshootable from here. It is tall enough now that no
    // arc clears it at these few tiles of range, and deliberately still short of the world margin, because the
    // row wants a line that is blocked *and* a way around it: a column run to row 0 would seal the detour too
    // and the verdict would become an absence rather than the priced reposition both blocked arms are about.
    //
    // Twelve rather than anything taller, and the ceiling is as real as the floor. At twenty-four the arc was
    // dead but the flight around the column cost more than the evaluator's 180-tick horizon, so both blocked
    // arms priced the detour at exactly zero — and any positive clear value beat zero, which kept the
    // comparison honest but vacuous. Twelve puts the waits inside the horizon, so the clear-against-blocked
    // rows compare two priced fights the way they read. The guard margin discriminates at either height
    // because it reads the reposition directly; the plan value does not, which is why the height is chosen
    // against it.
    private const int PillarHeightTiles = 12;
    private const int CompanionThreatSlot = 30, PlayerThreatSlot = 31;

    // The guarded zombie's life in the guard pairs. An ordinary zombie dies inside the plan's horizon, so its
    // removal is a finished kill in both arms; this life survives the horizon on starting weapons, so the
    // length is saturated and a guard difference between the arms is the reposition rather than the kill. The
    // premise checks below fail by name if weapons or walk pricing move it back inside.
    private const int GuardedLife = 400;

    private readonly record struct Row(string Actor, bool Blocked, float PlayerDanger, float CompanionDanger,
        Dictionary<int, (bool SolvesFromHere, bool Pursued, float Travel, float Value)> Enemies, float Guard,
        float ThreatRemoved, float KillIn, float Intervention, float Urgency, string Evidence,
        int GuardTarget, string GuardAccess, float GuardAccessTicks, float TopUrgency);

    public static void Run()
    {
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        var rows = new List<Row>();
        var guardRows = new List<Row>();
        try
        {
            foreach (var (actor, player, companion) in new[] { ("neither", false, false), ("player", true, false), ("companion", false, true), ("both", true, true) })
                foreach (bool blocked in new[] { false, true })
                    rows.Add(Scene(actor, player, companion, blocked));
            foreach (var (actor, companion) in new[] { ("player", false), ("both", true) })
                foreach (bool blocked in new[] { false, true })
                    guardRows.Add(Scene(actor, true, companion, blocked, GuardedLife));
        }
        finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false; }

        foreach (Row row in rows)
            Console.WriteLine($"  actor matrix {row.Actor,-9} {(row.Blocked ? "blocked" : "clear  ")}: danger player {row.PlayerDanger:0.000} companion {row.CompanionDanger:0.000}; guard {row.Guard:0.000} target {row.GuardTarget} access {row.GuardAccess} {row.GuardAccessTicks:0.0} threat-removed {row.ThreatRemoved:0.000} kill-in {row.KillIn:0.0} intervention {row.Intervention:0.0} urgency {row.Urgency:0.000}; evidence {row.Evidence}; "
                + string.Join(", ", row.Enemies.Select(e => $"{e.Key}: solves-here={e.Value.SolvesFromHere} pursued={e.Value.Pursued} travel {e.Value.Travel:0.0} value {e.Value.Value:0.000}")));
        foreach (Row row in guardRows)
            Console.WriteLine($"  actor matrix guard {row.Actor,-9} {(row.Blocked ? "blocked" : "clear  ")} (life {GuardedLife}): guard {row.Guard:0.000} target {row.GuardTarget} access {row.GuardAccess} {row.GuardAccessTicks:0.0} threat-removed {row.ThreatRemoved:0.000} kill-in {row.KillIn:0.0} urgency {row.Urgency:0.000} top {row.TopUrgency:0.000} danger player {row.PlayerDanger:0.000}");

        foreach (Row row in rows)
        {
            bool player = row.Actor is "player" or "both", companion = row.Actor is "companion" or "both";
            Require((row.PlayerDanger > 0) == player && (row.CompanionDanger > 0) == companion,
                $"{row.Actor}/{(row.Blocked ? "blocked" : "clear")} must threaten exactly the actors it names, or the matrix tests nothing; player={row.PlayerDanger} companion={row.CompanionDanger}");
            foreach (var (slot, enemy) in row.Enemies)
            {
                Require(enemy.SolvesFromHere == !row.Blocked,
                    $"{row.Actor}/{(row.Blocked ? "blocked" : "clear")}: the pillar alone must decide whether zombie {slot} can be shot from here; solves={enemy.SolvesFromHere}");
                if (enemy.Pursued)
                    Require((enemy.Travel > 0f) == row.Blocked,
                        $"{row.Actor}/{(row.Blocked ? "blocked" : "clear")}: the pursued zombie {slot} must need a reposition exactly when the pillar blocks the shot from here; travel={enemy.Travel}; evidence={row.Evidence}");
            }
        }
        // One zombie, one pursuit: the clear arm shoots from here and the blocked arm walks around the
        // pillar for the same kill, so the clear fight is worth more. The "both" scenes pursue whichever
        // zombie the valuation prefers per arm, which need not be the same zombie, so no per-slot value
        // comparison is well-defined for them; the single-zombie pairs carry the ordering.
        foreach (string actor in new[] { "player", "companion" })
        {
            Row clear = rows.Single(r => r.Actor == actor && !r.Blocked), blocked = rows.Single(r => r.Actor == actor && r.Blocked);
            int slot = clear.Enemies.Keys.Single();
            Require(blocked.Enemies.Keys.Single() == slot && clear.Enemies[slot].Pursued && blocked.Enemies[slot].Pursued,
                $"{actor}: both arms must pursue the one zombie, or the value comparison has no subject; clear={clear.Evidence}, blocked={blocked.Evidence}");
            Require(clear.Enemies[slot].Value > blocked.Enemies[slot].Value,
                $"{actor}: pursuing zombie {slot} must be worth more when it can be shot from here than after walking around the pillar; clear={clear.Enemies[slot].Value} blocked={blocked.Enemies[slot].Value}");
        }
        Row neitherClear = rows.Single(r => r.Actor == "neither" && !r.Blocked);
        Require(neitherClear.Enemies.Count == 0 && neitherClear.PlayerDanger == 0 && neitherClear.CompanionDanger == 0
            && neitherClear.Guard == 0f && neitherClear.Evidence.Contains("no-eligible-target", StringComparison.Ordinal),
            $"the neither scene must hold no hostile at all and offer no fight; evidence={neitherClear.Evidence}");

        // Guarding counts the time to reach a place it can shoot from as well as the length the weapons then
        // need (Proposal 1's P03). The ordinary arms die inside the horizon, so their guard difference is the
        // reposition's time to first damage; the guard pairs survive it, so the length is saturated and their
        // difference is the reposition too, against a longer fight. The old share formula — a saturation at one
        // inside a useful length and an exact access-over-access-plus-removal past it — is gone with the linear
        // guard side: the evaluator prices time inside a nonlinear outcome, so both pairs pin the direction.
        foreach (string actor in new[] { "player", "both" })
        {
            Row clear = rows.Single(r => r.Actor == actor && !r.Blocked), blocked = rows.Single(r => r.Actor == actor && r.Blocked);
            Require(clear.GuardTarget == PlayerThreatSlot && blocked.GuardTarget == PlayerThreatSlot
                && clear.GuardAccess == "FromHere" && blocked.GuardAccess == "AfterMoving" && blocked.GuardAccessTicks > 0f,
                $"{actor}: guarding must pursue the player's zombie from here clear and after moving blocked, or the pair tests nothing; clear={clear.GuardTarget}/{clear.GuardAccess}, blocked={blocked.GuardTarget}/{blocked.GuardAccess}/{blocked.GuardAccessTicks}");
            Require(blocked.Guard < clear.Guard,
                $"{actor}: an ordinary zombie must be worth less guard blocked than clear, by the walk around the pillar; clear={clear.Guard}, blocked={blocked.Guard}");

            Row tankClear = guardRows.Single(r => r.Actor == actor && !r.Blocked), tankBlocked = guardRows.Single(r => r.Actor == actor && r.Blocked);
            Require(tankClear.GuardTarget == PlayerThreatSlot && tankBlocked.GuardTarget == PlayerThreatSlot
                && tankClear.GuardAccess == "FromHere" && tankBlocked.GuardAccess == "AfterMoving"
                && float.IsFinite(tankBlocked.GuardAccessTicks) && tankBlocked.GuardAccessTicks > 0f,
                $"{actor} guard pair: the guarded zombie must be pursued from here clear and after a finite walk blocked; clear={tankClear.GuardTarget}/{tankClear.GuardAccess}, blocked={tankBlocked.GuardTarget}/{tankBlocked.GuardAccess}/{tankBlocked.GuardAccessTicks}");
            Require(float.IsPositiveInfinity(tankClear.KillIn) && float.IsPositiveInfinity(tankBlocked.KillIn)
                && tankClear.ThreatRemoved > 0f && tankBlocked.ThreatRemoved > 0f,
                $"{actor} guard pair: the horizon must finish neither arm's fight, so the guard difference is the reposition rather than the kill; clear kill={tankClear.KillIn}, blocked kill={tankBlocked.KillIn}");
            Require(MathF.Abs(tankClear.Urgency - tankBlocked.Urgency) < 1e-5f && MathF.Abs(tankClear.TopUrgency - tankBlocked.TopUrgency) < 1e-5f
                && MathF.Abs(tankClear.PlayerDanger - tankBlocked.PlayerDanger) < 1e-5f,
                $"{actor} guard pair: the threat to the player must be identical in both arms, or a guard difference could be danger rather than access; urgency {tankClear.Urgency}/{tankBlocked.Urgency}, top {tankClear.TopUrgency}/{tankBlocked.TopUrgency}, player danger {tankClear.PlayerDanger}/{tankBlocked.PlayerDanger}");
            float margin = 1f - tankBlocked.Guard / tankClear.Guard;
            Console.WriteLine($"  actor matrix guard {actor}: blocked guard {tankBlocked.Guard:0.0000} below clear {tankClear.Guard:0.0000} by {margin:P2}");
            Require(tankBlocked.Guard < tankClear.Guard,
                $"{actor} guard pair: a threat the companion must walk around the pillar to shoot must be worth less protection than the same threat it can shoot now; clear={tankClear.Guard}, blocked={tankBlocked.Guard}");
        }
    }

    private static Row Scene(string actor, bool threatenPlayer, bool threatenCompanion, bool blocked, int guardedLife = 0)
    {
        Main.maxTilesX = Main.maxTilesY = 140;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, null, new object[] { (ushort)140, (ushort)140 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= FloorY + 2; y++) { Tile rock = Main.tile[x, y]; rock.HasTile = true; rock.TileType = 1; }
        if (blocked)
            for (int y = FloorY - PillarHeightTiles; y < FloorY; y++) { Tile rock = Main.tile[PillarX, y]; rock.HasTile = true; rock.TileType = 1; }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();

        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        // The bow alone. The premise of every blocked row is that the pillar decides whether the near
        // zombie can be shot from here, and a thrown knife lobs over the pillar onto a body five tiles
        // away — which is a correct answer, and not the one this matrix varies. With an arrow's slow
        // drop no lob both clears the pillar and lands that close.
        player.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear.Slots[1] = new Item();
        player.dead = false;
        player.statLife = player.statLifeMax2;
        player.DefenseEffectiveness = MultipliableFloat.One * .5f;
        player.velocity = Vector2.Zero;
        player.position = new Vector2(PlayerX * 16f, FloorY * 16f - player.height);
        companion.NPC.position = new Vector2(CompanionX * 16f, FloorY * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;

        // The scene owns the threat list: every hostile slot starts inactive, so earlier scenes' zombies
        // are never scanned in and the "neither" arm truly holds no hostile at all.
        for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
        var placed = new List<NPC>();
        NPC Zombie(int slot, int tileX)
        {
            NPC npc = Main.npc[slot];
            npc.SetDefaults(NPCID.Zombie);
            npc.whoAmI = slot; npc.active = true; npc.velocity = Vector2.Zero; npc.damage = 100;
            npc.Bottom = new Vector2(tileX * 16f + 8f, FloorY * 16f);
            placed.Add(npc);
            return npc;
        }
        if (threatenCompanion) Zombie(CompanionThreatSlot, NearCompanionX);
        if (threatenPlayer)
        {
            NPC guarded = Zombie(PlayerThreatSlot, NearPlayerX);
            // Only the guard pairs raise it: life lengthens the fight and leaves the zombie's hit, and so its
            // danger to the player, unchanged.
            if (guardedLife > 0) guarded.lifeMax = guarded.life = guardedLife;
        }

        var brain = companion.Brain;
        brain.Senses.Update(companion.NPC, player);
        var ctx = new ActionContext(companion, brain.Senses);
        brain.Senses.SetInterventionEstimate(companion.Combat.EstimateInterventionTicks(ctx));
        var combat = brain.Chooser.Actions.OfType<Combat>().Single();
        // Settled before preparing: the resolves pump the reach flood to completion, so the offer is the
        // search's finished answer rather than what the first flood slice happened to reach. Preparing on a
        // growing flood reads whatever is reachable so far — usually the shot from here — and rows comparing
        // values across arms would then compare flood budgets, not fights.
        // WithPlayer, not a firing request: LineOfFire without a flight profile early-outs before it
        // refreshes the flood, so three thousand of those prime nothing. The assistance matrix primes the
        // same way, because four hundred no longer completes the flood.
        var primeRequest = new PositionRequest(RequestKind.WithPlayer, player.Bottom);
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
            brain.Positioner.Resolve(primeRequest, brain.Senses, null);
        Require(brain.Positioner.ReachComplete, "the reach flood must complete before the offer can be read as the search's answer");
        VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(combat.Eligibility != live::AICompanion.Companion.Brain.Activities.OfferEligibility.Unresolved,
            $"the stand search must decide on a completed flood; still {combat.EligibilityReason}");

        var plan = combat.OfferedPlan;
        Vector2 muzzle = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.CompanionCombat.Muzzle(companion.NPC);
        var enemies = new Dictionary<int, (bool SolvesFromHere, bool Pursued, float Travel, float Value)>();
        foreach (NPC npc in placed)
        {
            bool pursued = plan != null && plan.PrimaryTarget == npc.whoAmI;
            enemies[npc.whoAmI] = (companion.Combat.ShotSolves(ctx, muzzle, npc), pursued,
                pursued ? plan!.Current(brain.Senses.Tick).Verdict.TravelTicks : float.NaN,
                pursued ? plan!.Weighted : 0f);
        }
        float killIn = float.PositiveInfinity;
        if (plan?.TargetKillTicks != null)
            foreach ((int slot, int at) in plan.TargetKillTicks)
                if (slot == plan.PrimaryTarget)
                    killIn = at - plan.Validity.LastProgressTick;
        float travel = plan?.Current(brain.Senses.Tick).Verdict.TravelTicks ?? float.NaN;
        return new Row(actor, blocked, brain.Senses.Threats.PlayerDanger, brain.Senses.Threats.CompanionDanger, enemies,
            combat.Score(), plan?.Outcome.ThreatRemoved ?? 0f, killIn, brain.Senses.Threats.InterventionTicks,
            brain.Senses.Threats.ProtectionUrgency,
            $"offer={combat.Eligibility}/{combat.EligibilityReason} funnel={combat.Funnel.Describe()}",
            plan?.PrimaryTarget ?? -1, plan == null ? "unasked" : travel > 0f ? "AfterMoving" : "FromHere", travel,
            brain.Senses.Threats.MostUrgent?.Urgency ?? 0f);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("actor matrix: " + message);
    }
}
