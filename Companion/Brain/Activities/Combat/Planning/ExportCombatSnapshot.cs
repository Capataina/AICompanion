#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;
using AICompanion.Companion.Inventory;
using AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>
/// One combat decision's complete input as JSON: the body, gear and knowledge, the enemy forecast with its
/// motion history, hostile projectiles, the player and his intent region, the terrain window, the stand
/// verdicts, the budget, the weights and the committed plan. The audit restores the world from this alone —
/// NPCs and projectiles into their slots, tiles into the window, knowledge and tracks over them — re-runs
/// the senses and the search, and grades what the live decision did. Terrain mirrors the chunk capture's
/// encoding (glyphs plus base64 arrays) without the frames, which no combat computation reads.
/// </summary>
public static class ExportCombatSnapshot
{
    public const int SchemaVersion = 1;

    /// <summary>Set by the inspector's mark key, consumed by the next combat preparation: "that moment", written.</summary>
    public static bool MarkRequested { get; set; }

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    public sealed record Vec(float X, float Y);
    public sealed record TrackDto(ulong Tick, Vec Position, Vec Velocity, Vec Acceleration, float Gravity,
        float MaxFall, float Water, float Lava, float Honey, float Shimmer, bool NoGravity, bool NoTileCollide,
        bool Wet, bool LavaWet, bool HoneyWet, bool ShimmerWet, float MeanError, int ErrorSamples);
    public sealed record BuffDto(string Name, int Time);
    public sealed record AddedByDto(string Buff, string Item);
    /// <summary>The threat sense's post-update memory for the slot: the peak speed urgencies divide by and the
    /// two reachability answers. Null on snapshots written before the audit restored it; the replay then runs
    /// the fresh-memory update and the verdict says the memory was missing.</summary>
    public sealed record ThreatDto(float PeakSpeed, bool CanReachPlayer, bool CanReachCompanion);
    public sealed record NpcDto(int Slot, string Type, Vec Position, Vec Velocity, int Life, int LifeMax,
        int Defense, int Damage, float KnockbackResist, int Width, int Height, List<BuffDto> Buffs, bool OnFire,
        bool NoGravity, bool NoTileCollide, TrackDto? Track, List<AddedByDto> AddedBy, ThreatDto? Threat = null);
    public sealed record DeferredDto(int Slot, int Generation, int Remaining, Vec Target, Vec Body);
    public sealed record ProjectileDto(int Slot, string Type, Vec Position, Vec Velocity, int Width, int Height,
        int Damage, int ExtraUpdates);
    public sealed record RegionDto(Vec Centre, Vec Half, Vec Lead, Vec Heading, Vec Velocity, bool Travelling);
    public sealed record PlayerDto(Vec Centre, Vec Velocity, int Life, int LifeMax, bool Dead, int ManaMax,
        RegionDto Region, float PlayerDanger, float CompanionDanger);
    public sealed record BodyDto(Vec Centre, Vec Velocity, int Life, int LifeMax, float Mana, float ManaMax,
        int ExtraProjectiles, int AddedPierce);
    public sealed record TerrainDto(int X, int Y, int Width, int Height, int Clipped, string Glyphs,
        string Liquids, string Materials, string States);
    public sealed record VerdictDto(Vec Stand, int Reason, int WeaponSlot, int[] Targets, int Reach, float Travel,
        float HarmAt, float HarmAlong, bool InAllowance, string Why);
    public sealed record UseDto(int Weapon, Vec Muzzle, Vec Aim, Vec Launch, int FireTick, int Target);
    public sealed record SegmentDto(Vec Stand, int Reason, int WeaponSlot, int[] Targets, VerdictDto Verdict,
        int Arrive, int Start, int End, List<UseDto> Uses, int EndsWhen);
    public sealed record ValidityDto(int Terrain, int Knowledge, List<int[]> Targets, float MaxUrgency,
        Vec RegionCentre, Vec RegionHalf, float Gap, int ProgressTick);
    public sealed record PlanDto(int Id, List<SegmentDto> Segments, float[] Outcome, float Weighted,
        ValidityDto Validity, bool BudgetCut, List<int[]> Kills);
    public sealed record RejectedDto(Vec Stand, float Weighted, string Reason, string LostOn);
    public sealed record SnapshotDto(int Version, int SensesTick, ulong GameTick, BodyDto Body, List<string?> Gear,
        PersistWeaponKnowledge.Bundle Knowledge, List<NpcDto> Npcs, List<ProjectileDto> Projectiles,
        PlayerDto Player, TerrainDto Terrain, List<VerdictDto> Verdicts, float AllowanceMs, int Simulations,
        bool Cut, int Candidates, int FrontSize, float[] Weights, PlanDto? Plan, List<RejectedDto> Rejected,
        bool Explored, int Cooldown, List<DeferredDto> Deferred, List<int[]> Hits, float AllowanceRadius);

    public static string Build(in ActionContext ctx, CompanionCombat combat, AttackPlan? plan,
        SearchAttackPlans.SearchResult? search, CombatWeights weights, PlanningBudget budget,
        float allowanceRadius)
    {
        var identity = new WeaponIdentity();
        NPC body = ctx.Npc;
        Player player = ctx.Player;
        ModifierState modifiers = ApplyCompanionModifiers.Current();

        var gear = new List<string?>(CompanionGear.SlotCount);
        CompanionGear slots = player.GetModPlayer<CompanionPlayer>().Gear;
        var weaponItems = new List<int>();
        for (int i = 0; i < CompanionGear.SlotCount; i++)
        {
            Item item = slots.Slots[i];
            gear.Add(item.IsAir ? null : identity.NameOfItem(item.type));
            if (!item.IsAir && (i == (int)GearSlot.FirstWeapon || i == (int)GearSlot.SecondWeapon))
                weaponItems.Add(item.type);
        }

        var npcs = new List<NpcDto>();
        var npcTypes = new List<int>();
        var bounds = new Bounds(body.Center);
        Dictionary<int, Dictionary<int, int>> addedBy = ShotOutcomes.ExportAddedBy();
        foreach (ThreatRecord threat in ctx.Senses.Threats.Threats)
        {
            NPC npc = threat.Npc;
            if (npc == null || !npc.active)
                continue;
            npcTypes.Add(npc.type);
            bounds.Add(npc.Center);
            bounds.AddGroundSeek(npc.Bottom);
            var buffs = new List<BuffDto>();
            for (int i = 0; i < npc.buffType.Length && i < npc.buffTime.Length; i++)
                if (npc.buffTime[i] > 0)
                    buffs.Add(new BuffDto(identity.NameOfBuff(npc.buffType[i]), npc.buffTime[i]));
            var authors = new List<AddedByDto>();
            if (addedBy.TryGetValue(npc.whoAmI, out Dictionary<int, int>? authorship))
                foreach ((int buff, int item) in authorship)
                    authors.Add(new AddedByDto(identity.NameOfBuff(buff), identity.NameOfItem(item)));
            PredictObservedMotion.ExportedTrack? track = PredictObservedMotion.ExportTrack(npc.whoAmI);
            ThreatDto? memory = ctx.Senses.Threats.TryExportMemory(npc.whoAmI, out float peak, out bool canP, out bool canC)
                ? new ThreatDto(peak, canP, canC) : null;
            npcs.Add(new NpcDto(npc.whoAmI, identity.NameOfNpc(npc.type),
                V(npc.position), V(npc.velocity), npc.life, npc.lifeMax, npc.defense, npc.damage,
                npc.knockBackResist, npc.width, npc.height, buffs, npc.onFire2, npc.noGravity, npc.noTileCollide,
                track == null ? null : new TrackDto(track.Tick, V(track.Position), V(track.Velocity),
                    V(track.Acceleration), track.Gravity, track.MaxFallSpeed, track.WaterSpeed, track.LavaSpeed,
                    track.HoneySpeed, track.ShimmerSpeed, track.NoGravity, track.NoTileCollide, track.Wet,
                    track.LavaWet, track.HoneyWet, track.ShimmerWet, track.MeanError, track.ErrorSamples),
                authors, memory));
        }

        var projectiles = new List<ProjectileDto>();
        foreach (Projectile projectile in Main.ActiveProjectiles)
        {
            if (!projectile.hostile || projectile.damage <= 0)
                continue;
            projectiles.Add(new ProjectileDto(projectile.whoAmI, identity.NameOfProjectile(projectile.type),
                V(projectile.position), V(projectile.velocity), projectile.width, projectile.height,
                projectile.damage, projectile.extraUpdates));
        }

        var verdicts = new List<VerdictDto>();
        if (search != null)
            foreach (AssessedStand assessed in search.Assessed)
            {
                bounds.Add(assessed.Proposal.Stand);
                verdicts.Add(ExportVerdict(assessed.Proposal, assessed.Verdict));
            }

        PlayerIntentRegion region = ctx.Senses.Intent.Region;
        var deferred = new List<DeferredDto>();
        foreach (var (key, wait) in combat.Planner.ExportDeferred(ctx.Senses.Tick))
            deferred.Add(new DeferredDto(key.Slot, key.Generation, wait.Remaining, V(wait.Target), V(wait.Body)));
        var hits = new List<int[]>();
        foreach ((int slot, int generation) in combat.Planner.ExportHits())
            hits.Add(new[] { slot, generation });
        var snapshot = new SnapshotDto(SchemaVersion, ctx.Senses.Tick, Main.GameUpdateCount,
            new BodyDto(V(body.Center), V(body.velocity), body.life, body.lifeMax,
                ctx.Companion.Mana.Current, ctx.Companion.Mana.Max,
                modifiers.ExtraProjectiles, modifiers.AddedPierce),
            gear,
            PersistWeaponKnowledge.ExportBundle(weaponItems, npcTypes, identity),
            npcs, projectiles,
            new PlayerDto(V(player.Center), V(player.velocity), player.statLife, player.statLifeMax2, player.dead,
                player.statManaMax2,
                new RegionDto(V(region.Centre), V(region.HalfSize), V(region.Lead), V(region.Heading),
                    V(region.Velocity), region.IsTravelling),
                ctx.Senses.Threats.PlayerDanger, ctx.Senses.Threats.CompanionDanger),
            ExportTerrain(bounds),
            verdicts,
            budget.AllowanceMilliseconds, budget.Simulations, budget.Cut,
            search?.CandidatesEvaluated ?? 0, search?.FrontSize ?? 0,
            new[] { weights.Damage, weights.ThreatRemoved, weights.PlayerHarmPrevented, weights.CompanionHarm,
                weights.PushDanger, weights.CompanyGap, weights.TimeToFirstDamage, weights.Mana },
            plan == null ? null : ExportPlan(plan),
            ExportRejected(search?.Rejected),
            ForecastUses.Explore(ctx),
            combat.CooldownTicks, deferred, hits, allowanceRadius);
        return JsonSerializer.Serialize(snapshot, Json);
    }

    private static Vec V(Vector2 v) => new(v.X, v.Y);

    private static VerdictDto ExportVerdict(StandProposal proposal, StandVerdict verdict)
        => new(V(proposal.Stand), (int)proposal.Reason, proposal.WeaponSlot, proposal.TargetSlots,
            (int)verdict.Reach, verdict.TravelTicks, verdict.HarmAtStand, verdict.HarmAlongTravel,
            verdict.InAllowance, verdict.Reason);

    private static PlanDto ExportPlan(AttackPlan plan)
    {
        var segments = new List<SegmentDto>(plan.Segments.Length);
        foreach (AttackSegment segment in plan.Segments)
        {
            var uses = new List<UseDto>(segment.Uses.Length);
            foreach (PlannedUse use in segment.Uses)
                uses.Add(new UseDto(use.WeaponSlot, V(use.Muzzle), V(use.AimPoint), V(use.LaunchDirection),
                    use.FireTick, use.TargetSlot));
            segments.Add(new SegmentDto(V(segment.Stand.Stand), (int)segment.Stand.Reason, segment.Stand.WeaponSlot,
                segment.Stand.TargetSlots, ExportVerdict(segment.Stand, segment.Verdict),
                segment.ArriveTick, segment.StartTick, segment.EndTick, uses, (int)segment.EndsWhen));
        }
        var targets = new List<int[]>();
        foreach ((int slot, int generation) in plan.Validity.Targets)
            targets.Add(new[] { slot, generation });
        var kills = new List<int[]>();
        if (plan.TargetKillTicks != null)
            foreach ((int slot, int tick) in plan.TargetKillTicks)
                kills.Add(new[] { slot, tick });
        CombatOutcome outcome = plan.Outcome;
        return new PlanDto(plan.Id, segments,
            new[] { outcome.DamagePerSecond, outcome.ThreatRemoved, outcome.PlayerHarmPrevented,
                outcome.CompanionHarmTaken, outcome.PushDangerAdded, outcome.CompanyGap,
                outcome.TimeToFirstDamage, outcome.ManaSpent },
            plan.Weighted,
            new ValidityDto(plan.Validity.TerrainRevision, plan.Validity.KnowledgeRevision, targets,
                plan.Validity.AdmittedMaxUrgency, V(plan.Validity.RegionCentre), V(plan.Validity.RegionHalfSize),
                plan.Validity.AdmittedCompanyGap, plan.Validity.LastProgressTick),
            plan.BudgetCut, kills);
    }

    private static List<RejectedDto> ExportRejected(IReadOnlyList<RejectedPlan>? rejected)
    {
        var exported = new List<RejectedDto>();
        if (rejected == null)
            return exported;
        foreach (RejectedPlan plan in rejected)
        {
            Vector2 stand = plan.Plan.Segments.Length > 0 ? plan.Plan.Segments[0].Stand.Stand : Vector2.Zero;
            exported.Add(new RejectedDto(V(stand), plan.Plan.Weighted, plan.Reason, plan.LostOn));
        }
        return exported;
    }

    private sealed class Bounds
    {
        private float minX, minY, maxX, maxY;
        private readonly List<(int X, int Y)> groundSeeks = new();

        public Bounds(Vector2 seed)
        {
            minX = maxX = seed.X;
            minY = maxY = seed.Y;
        }

        public void Add(Vector2 point)
        {
            minX = MathF.Min(minX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxX = MathF.Max(maxX, point.X);
            maxY = MathF.Max(maxY, point.Y);
        }

        /// <summary>One body whose predicted fall the window must contain: the forecast lands it.</summary>
        public void AddGroundSeek(Vector2 feet) => groundSeeks.Add(((int)(feet.X / 16f), (int)(feet.Y / 16f)));

        public (int X, int Y, int Width, int Height) Tiles()
        {
            const int pad = 24, cap = 200;
            int x = Math.Max(0, (int)(minX / 16f) - pad);
            int y = Math.Max(0, (int)(minY / 16f) - pad);
            int width = Math.Min(Main.maxTilesX - x, (int)(maxX / 16f) + pad - x);
            int height = Math.Min(Main.maxTilesY - y, (int)(maxY / 16f) + pad - y);
            // The pad is a walker's whole forecast: 180 ticks at 2 pixels is 23 tiles, so a thinner
            // window lets a forecast walk out of recorded terrain into the audit's solid margin and
            // land or stop on rock the live world never had. Falls reach further down than sideways,
            // so each body's column extends to the first ground below it.
            foreach ((int seekX, int seekY) in groundSeeks)
                for (int ty = seekY + 1; ty <= seekY + 60 && ty < Main.maxTilesY; ty++)
                {
                    if (seekX < 0 || seekX >= Main.maxTilesX)
                        break;
                    Tile tile = Main.tile[seekX, ty];
                    if (tile.HasTile && (Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]))
                    {
                        height = Math.Max(height, ty - y + 2);
                        break;
                    }
                }
            // Clamped, because a long-range weapon's stands can span half the world and the window
            // prices in tiles: past the cap the audit reads the near field exactly and the far field
            // not at all, which the clipped count does not say and the window's own bounds do.
            return (x, y, Math.Clamp(width, 1, cap), Math.Clamp(height, 1, cap));
        }
    }

    private static TerrainDto ExportTerrain(Bounds bounds)
    {
        (int x, int y, int width, int height) = bounds.Tiles();
        var glyphs = new StringBuilder(width * height);
        byte[] liquids = new byte[width * height * 2], materials = new byte[width * height * 4];
        byte[] states = new byte[width * height];
        int index = 0, clipped = 0;
        for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++, index++)
            {
                int tx = x + col, ty = y + row;
                if (!MovementQueries.World.InWorld(tx, ty)) { glyphs.Append('?'); clipped++; continue; }
                Tile tile = Main.tile[tx, ty];
                glyphs.Append(TextTileWorld.Glyph(MovementQueries.World.Shape(tx, ty), MovementQueries.World.Water(tx, ty),
                    MovementQueries.World.Lava(tx, ty), MovementQueries.World.PassThrough(tx, ty)));
                liquids[index * 2] = tile.LiquidAmount;
                liquids[index * 2 + 1] = (byte)tile.LiquidType;
                materials[index * 4] = (byte)tile.TileType;
                materials[index * 4 + 1] = (byte)(tile.TileType >> 8);
                materials[index * 4 + 2] = (byte)tile.WallType;
                materials[index * 4 + 3] = (byte)(tile.WallType >> 8);
                states[index] = (byte)((tile.HasTile ? 1 : 0) | (tile.IsActuated ? 2 : 0)
                    | (Main.tileSolid[tile.TileType] ? 4 : 0) | (Main.tileSolidTop[tile.TileType] ? 8 : 0)
                    | ((int)tile.Slope << 4) | (tile.IsHalfBlock ? 128 : 0));
            }
        return new TerrainDto(x, y, width, height, clipped, glyphs.ToString(),
            Convert.ToBase64String(liquids), Convert.ToBase64String(materials), Convert.ToBase64String(states));
    }
}
