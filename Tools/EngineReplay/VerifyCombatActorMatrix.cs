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

    private readonly record struct Row(string Actor, bool Blocked, float PlayerDanger, float CompanionDanger,
        Dictionary<int, (bool CanEngage, string Verdict, float Access, float Value)> Enemies, int Aim, float Guard,
        float Removal, float Intervention, float Urgency, string Evidence);

    public static void Run()
    {
        live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = true;
        var rows = new List<Row>();
        try
        {
            foreach (var (actor, player, companion) in new[] { ("neither", false, false), ("player", true, false), ("companion", false, true), ("both", true, true) })
                foreach (bool blocked in new[] { false, true })
                    rows.Add(Scene(actor, player, companion, blocked));
        }
        finally { live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = false; }

        foreach (Row row in rows)
            Console.WriteLine($"  actor matrix {row.Actor,-9} {(row.Blocked ? "blocked" : "clear  ")}: danger player {row.PlayerDanger:0.000} companion {row.CompanionDanger:0.000}; aim {row.Aim}; guard {row.Guard:0.000} removal {row.Removal:0.0} intervention {row.Intervention:0.0} urgency {row.Urgency:0.000}; evidence {row.Evidence}; "
                + string.Join(", ", row.Enemies.Select(e => $"{e.Key}: engage={e.Value.CanEngage} {e.Value.Verdict} wait {e.Value.Access:0.0} value {e.Value.Value:0.000}")));

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
    }

    private static Row Scene(string actor, bool threatenPlayer, bool threatenCompanion, bool blocked)
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
        if (threatenPlayer) Zombie(PlayerThreatSlot, NearPlayerX);

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
            brain.Senses.Threats.ProtectionUrgency, hunt.PursuitEvidence);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("actor matrix: " + message);
    }
}
