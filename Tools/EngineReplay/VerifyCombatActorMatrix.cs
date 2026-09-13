extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Hunt = live::AICompanion.Companion.Brain.PurposeFamilies.Combat.PursueAttackOpportunity;
using Guard = live::AICompanion.Companion.Brain.PurposeFamilies.Combat.ProtectPlayer;
using ActionContext = live::AICompanion.Companion.Brain.Behaviours.ActionContext;
using PositionRequest = live::AICompanion.Companion.Brain.PositionSelection.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.PositionSelection.RequestKind;
using Weights = live::AICompanion.Companion.Brain.BehaviourSelection.Weights;

/// <summary>
/// Proposal 1's actor matrix repeated with a clear shot against a blocked one. Four scenes differ in who a
/// damageable zombie threatens — nobody, the player, the companion or both — and each is run twice on the
/// same floor, once open and once with a three-tile pillar beside the companion that blocks the line to
/// every enemy without sealing the floor. Who is threatened must follow placement alone, and whether the
/// companion can shoot, how the hunt reads the opportunity and what the pursuit is worth must follow the
/// pillar alone. Guarding's removal and intervention estimates are printed for both arms rather than
/// asserted, because they are the measurement of whether protection reads the same attack evidence.
/// </summary>
internal static class VerifyCombatActorMatrix
{
    private const int FloorY = 80, CompanionX = 38, PillarX = 40, PlayerX = 70, NearCompanionX = 43, NearPlayerX = 67;
    private const int CompanionThreatSlot = 30, PlayerThreatSlot = 31;

    // The guarded zombie's life in the guard pairs. Guarding's share is GuardUsefulRemovalTicks over access plus
    // removal, one while that sum is inside the length, so an ordinary zombie — about a hundred and seventy
    // ticks to remove and a hundred-odd to reach a firing spot around the pillar — is guarded identically in
    // both arms, and that equality is the rule's correct answer rather than a missing one. A threat whose
    // removal already exceeds the length is where access shows: clear reads length/removal, blocked
    // length/(removal + access). This life puts removal a little past the length on starting weapons, so the
    // walk around the pillar is a few percent of the whole rather than a rounding error; the premise checks
    // below fail by name if weapons or walk pricing move it back inside.
    private const int GuardedLife = 600;

    private readonly record struct Row(string Actor, bool Blocked, float PlayerDanger, float CompanionDanger,
        Dictionary<int, (bool CanEngage, string Verdict, float Access, float Value)> Enemies, int Aim, float Guard,
        float Removal, float Intervention, float Urgency, string Evidence,
        int GuardTarget, string GuardAccess, float GuardAccessTicks, float Usefulness, float TopUrgency);

    public static void Run()
    {
        live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = true;
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
        finally { live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = false; }

        foreach (Row row in rows)
            Console.WriteLine($"  actor matrix {row.Actor,-9} {(row.Blocked ? "blocked" : "clear  ")}: danger player {row.PlayerDanger:0.000} companion {row.CompanionDanger:0.000}; aim {row.Aim}; guard {row.Guard:0.000} access {row.GuardAccess} {row.GuardAccessTicks:0.0} removal {row.Removal:0.0} share {row.Usefulness:0.000} intervention {row.Intervention:0.0} urgency {row.Urgency:0.000}; evidence {row.Evidence}; "
                + string.Join(", ", row.Enemies.Select(e => $"{e.Key}: engage={e.Value.CanEngage} {e.Value.Verdict} wait {e.Value.Access:0.0} value {e.Value.Value:0.000}")));
        foreach (Row row in guardRows)
            Console.WriteLine($"  actor matrix guard {row.Actor,-9} {(row.Blocked ? "blocked" : "clear  ")} (life {GuardedLife}): guard {row.Guard:0.000} target {row.GuardTarget} access {row.GuardAccess} {row.GuardAccessTicks:0.0} removal {row.Removal:0.0} access+removal {(row.Removal + (float.IsFinite(row.GuardAccessTicks) ? row.GuardAccessTicks : 0f)):0.0} share {row.Usefulness:0.0000} urgency {row.Urgency:0.000} top {row.TopUrgency:0.000} danger player {row.PlayerDanger:0.000}");

        foreach (Row row in rows)
        {
            bool player = row.Actor is "player" or "both", companion = row.Actor is "companion" or "both";
            Require((row.PlayerDanger > 0) == player && (row.CompanionDanger > 0) == companion,
                $"{row.Actor}/{(row.Blocked ? "blocked" : "clear")} must threaten exactly the actors it names, or the matrix tests nothing; player={row.PlayerDanger} companion={row.CompanionDanger}");
            foreach (var (slot, enemy) in row.Enemies)
            {
                Require(enemy.CanEngage == !row.Blocked,
                    $"{row.Actor}/{(row.Blocked ? "blocked" : "clear")}: the pillar alone must decide whether zombie {slot} can be shot from here; canEngage={enemy.CanEngage}");
                Require(enemy.Verdict == (row.Blocked ? "AfterMoving" : "FromHere"),
                    $"{row.Actor}/{(row.Blocked ? "blocked" : "clear")}: the hunt must read zombie {slot} as {(row.Blocked ? "reachable after moving" : "shootable from here")}; got {enemy.Verdict}; evidence={row.Evidence}");
            }
            Require(row.Blocked ? row.Aim < 0 : row.Enemies.Count == 0 ? row.Aim < 0 : row.Aim >= 0,
                $"{row.Actor}/{(row.Blocked ? "blocked" : "clear")}: the hands aim only at an enemy they can hit from here; aim={row.Aim}");
        }
        foreach (string actor in new[] { "player", "companion", "both" })
        {
            Row clear = rows.Single(r => r.Actor == actor && !r.Blocked), blocked = rows.Single(r => r.Actor == actor && r.Blocked);
            foreach (int slot in clear.Enemies.Keys)
                Require(clear.Enemies[slot].Value > blocked.Enemies[slot].Value,
                    $"{actor}: pursuing zombie {slot} must be worth more when it can be shot from here than after walking around the pillar; clear={clear.Enemies[slot].Value} blocked={blocked.Enemies[slot].Value}");
        }
        Row neitherClear = rows.Single(r => r.Actor == "neither" && !r.Blocked);
        Require(neitherClear.Evidence.Length == 0 && neitherClear.PlayerDanger == 0 && neitherClear.CompanionDanger == 0,
            $"the neither scene must hold no hostile at all; evidence={neitherClear.Evidence}");

        // Guarding counts the time to reach a place it can shoot from as well as the time the weapons then need
        // (Proposal 1's P03). The ordinary arms stay inside the useful length, so their guard must not move —
        // a share that charged any access at all would fail here. The guard pairs sit past the length, so the
        // walk around the pillar must lower guard by exactly its part of access plus removal and nothing else.
        foreach (string actor in new[] { "player", "both" })
        {
            Row clear = rows.Single(r => r.Actor == actor && !r.Blocked), blocked = rows.Single(r => r.Actor == actor && r.Blocked);
            Require(clear.GuardTarget == PlayerThreatSlot && blocked.GuardTarget == PlayerThreatSlot
                && clear.GuardAccess == "FromHere" && blocked.GuardAccess == "AfterMoving" && blocked.GuardAccessTicks > 0f,
                $"{actor}: guarding must read the player's zombie as shootable from here clear and after moving blocked, or the pair tests nothing; clear={clear.GuardTarget}/{clear.GuardAccess}, blocked={blocked.GuardTarget}/{blocked.GuardAccess}/{blocked.GuardAccessTicks}");
            Require(blocked.Removal + blocked.GuardAccessTicks < Weights.GuardUsefulRemovalTicks && MathF.Abs(clear.Guard - blocked.Guard) < 1e-5f,
                $"{actor}: an ordinary zombie whose access plus removal stays inside the useful length must be guarded identically in both arms; access+removal={blocked.Removal + blocked.GuardAccessTicks}, clear={clear.Guard}, blocked={blocked.Guard}");

            Row tankClear = guardRows.Single(r => r.Actor == actor && !r.Blocked), tankBlocked = guardRows.Single(r => r.Actor == actor && r.Blocked);
            Require(tankClear.GuardTarget == PlayerThreatSlot && tankBlocked.GuardTarget == PlayerThreatSlot
                && tankClear.GuardAccess == "FromHere" && tankBlocked.GuardAccess == "AfterMoving"
                && float.IsFinite(tankBlocked.GuardAccessTicks) && tankBlocked.GuardAccessTicks > 0f,
                $"{actor} guard pair: the guarded zombie must be shootable from here clear and after a finite walk blocked; clear={tankClear.GuardTarget}/{tankClear.GuardAccess}, blocked={tankBlocked.GuardTarget}/{tankBlocked.GuardAccess}/{tankBlocked.GuardAccessTicks}");
            Require(MathF.Abs(tankClear.Removal - tankBlocked.Removal) < 0.01f && tankClear.Removal > Weights.GuardUsefulRemovalTicks,
                $"{actor} guard pair: removal must be identical in both arms and already past the useful length, or access cannot show in the share; clear={tankClear.Removal}, blocked={tankBlocked.Removal}, length={Weights.GuardUsefulRemovalTicks}");
            Require(MathF.Abs(tankClear.Urgency - tankBlocked.Urgency) < 1e-5f && MathF.Abs(tankClear.TopUrgency - tankBlocked.TopUrgency) < 1e-5f
                && MathF.Abs(tankClear.PlayerDanger - tankBlocked.PlayerDanger) < 1e-5f,
                $"{actor} guard pair: the threat to the player must be identical in both arms, or a guard difference could be danger rather than access; urgency {tankClear.Urgency}/{tankBlocked.Urgency}, top {tankClear.TopUrgency}/{tankBlocked.TopUrgency}, player danger {tankClear.PlayerDanger}/{tankBlocked.PlayerDanger}");
            float expectedClear = Weights.GuardUsefulRemovalTicks / tankClear.Removal;
            float expectedBlocked = Weights.GuardUsefulRemovalTicks / (tankBlocked.Removal + tankBlocked.GuardAccessTicks);
            Require(MathF.Abs(tankClear.Usefulness - expectedClear) < 1e-4f && MathF.Abs(tankBlocked.Usefulness - expectedBlocked) < 1e-4f,
                $"{actor} guard pair: the share must be the useful length over access plus removal; clear={tankClear.Usefulness} expected {expectedClear}, blocked={tankBlocked.Usefulness} expected {expectedBlocked}");
            float margin = 1f - tankBlocked.Guard / tankClear.Guard;
            float expectedMargin = tankBlocked.GuardAccessTicks / (tankBlocked.Removal + tankBlocked.GuardAccessTicks);
            Console.WriteLine($"  actor matrix guard {actor}: blocked guard {tankBlocked.Guard:0.0000} below clear {tankClear.Guard:0.0000} by {margin:P2}; access/(access+removal) {expectedMargin:P2}");
            Require(tankBlocked.Guard < tankClear.Guard && margin >= 0.02f && MathF.Abs(margin - expectedMargin) < 1e-3f,
                $"{actor} guard pair: a threat the companion must walk around the pillar to shoot must be worth less protection than the same threat it can shoot now, by the walk's part of access plus removal (at least 2%); clear={tankClear.Guard}, blocked={tankBlocked.Guard}, margin={margin}, expected={expectedMargin}");
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
            for (int y = FloorY - 3; y < FloorY; y++) { Tile rock = Main.tile[PillarX, y]; rock.HasTile = true; rock.TileType = 1; }
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
        live::AICompanion.Companion.Brain.SharedMovementSystem.AStar.InvalidateEdges();

        var companion = VerifyCompanionLifecycle.Create();
        live::AICompanion.Companion.Brain.SharedMovementSystem.AStar.MsBudget = 0;
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2;
        player.DefenseEffectiveness = MultipliableFloat.One * .5f;
        player.velocity = Vector2.Zero;
        player.position = new Vector2(PlayerX * 16f, FloorY * 16f - player.height);
        companion.NPC.position = new Vector2(CompanionX * 16f, FloorY * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;

        Main.npc[CompanionThreatSlot] = new NPC();
        Main.npc[PlayerThreatSlot] = new NPC();
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
            // Only the guard pairs raise it: life lengthens removal and leaves the zombie's hit, and so its
            // danger to the player, unchanged.
            if (guardedLife > 0) guarded.lifeMax = guarded.life = guardedLife;
        }

        var brain = companion.Brain;
        brain.Senses.Update(companion.NPC, player, companion.Breath);
        var ctx = new ActionContext(companion, brain.Senses);
        foreach (NPC npc in placed)
        {
            var request = new PositionRequest(RequestKind.LineOfFire, npc.Center, npc);
            var profile = companion.Arsenal.ProfileFor(ctx, npc);
            for (int i = 0; i < 400; i++) brain.Positioner.Resolve(request, brain.Senses, profile);
        }
        brain.Senses.SetInterventionEstimate(companion.Arsenal.EstimateInterventionTicks(ctx));
        NPC? aim = companion.Arsenal.BestTarget(ctx);
        var hunt = brain.Chooser.Actions.OfType<Hunt>().Single();
        VerifyPreparedActivities.PrepareAndScore(hunt, ctx);
        var guard = brain.Chooser.Actions.OfType<Guard>().Single();
        float guardValue = VerifyPreparedActivities.PrepareAndScore(guard, ctx);

        var enemies = new Dictionary<int, (bool, string, float, float)>();
        foreach (NPC npc in placed) enemies[npc.whoAmI] = (companion.Arsenal.CanEngage(ctx, npc), "unexamined", float.NaN, float.NaN);
        foreach (string entry in hunt.PursuitEvidence.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] field = entry.Split(':');
            int slot = int.Parse(field[0]);
            if (!enemies.ContainsKey(slot)) continue;
            enemies[slot] = (enemies[slot].Item1, field[2],
                float.Parse(field[3], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(field[4], System.Globalization.CultureInfo.InvariantCulture));
        }
        return new Row(actor, blocked, brain.Senses.Threats.PlayerDanger, brain.Senses.Threats.CompanionDanger,
            enemies.ToDictionary(e => e.Key, e => ((bool CanEngage, string Verdict, float Access, float Value))e.Value),
            aim?.whoAmI ?? -1, guardValue, guard.RemovalTicks, brain.Senses.Threats.InterventionTicks,
            brain.Senses.Threats.ProtectionUrgency, hunt.PursuitEvidence,
            (guard.ActivityIdentity as NPC)?.whoAmI ?? -1, guard.Access?.ToString() ?? "unasked", guard.AccessTicks,
            guard.InterventionUsefulness, brain.Senses.Threats.MostUrgent?.Urgency ?? 0f);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("actor matrix: " + message);
    }
}
