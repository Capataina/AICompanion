extern alias live;

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using Combat = live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies;
using Threat = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatRecord;
using HandGrant = live::AICompanion.Companion.Brain.Infrastructure.Grants.HandGrant;
using ActivityPhase = live::AICompanion.Companion.Brain.Infrastructure.Selection.ActivityPhase;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using WorkPolicies = live::AICompanion.Companion.Brain.Activities.WorkPolicies;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;

/// <summary>
/// The combat stance's own rows: only combat fires (F1), shared eagerness (F2), danger over work
/// (F3), the dodge bending mining (F4), no weapon no offer (F5) and the hunting-off migration (F6).
/// </summary>
internal static class VerifyCombatActivity
{
    public static int OnlyCombatFires()
    {
        var (companion, player) = MineScene(TileID.Copper, new Point(25, 89), new Point(26, 89), new Point(27, 89));
        WaitForJob(companion, "mine", 300);
        using var _ = ScreenFollow.CentredOn(player.Center);
        using var hostile = ClearAfter.At(30);
        NPC zombie = Hostile(30, NPCID.Zombie, companion.NPC.Bottom + new Vector2(300, 0), life: 400);
        int fired = 0, notFighting = 0, mineTicks = 0, shootable = 0;
        var ctx = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, companion.Brain.Senses);
        for (int tick = 0; tick < 120; tick++)
        {
            Tick(companion);
            if (companion.Brain.Chooser.Current?.Name == "mine") mineTicks++;
            if (companion.Combat.LastFireOutcome == "fired") fired++;
            if (companion.Combat.LastFireOutcome == "not-fighting") notFighting++;
            if (companion.Combat.ShotSolves(ctx, live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.CompanionCombat.Muzzle(companion.NPC), zombie)) shootable++;
            VerifyResponsiveFollowing.AdvanceNative(companion);
        }
        Console.WriteLine($"  only-combat-fires: mine ticks {mineTicks}, fired {fired}, not-fighting {notFighting}, shootable {shootable} of 120");
        Require(mineTicks > 100, $"mining must keep the body for the scene to mean anything; mine ticks={mineTicks}");
        Require(shootable > 0, "the zombie must be shootable on some tick, or silence proves nothing");
        Require(fired == 0, $"no tick may fire while mining runs; fired={fired}");
        Require(notFighting > 0, "silence must read not-fighting, the gate's own word, not an accidental quiet");
        return 0;
    }

    public static int SharedEagerness()
    {
        var (companion, player) = OpenFloor();
        // The companion trails 750px behind a standing player — outside the region, so keeping
        // company pulls reunion — with a motionless zombie 400px ahead of it and 150px up: 380px
        // from the player, where a body that has never moved reads no urgency, and close enough
        // overhead that a bow shot solves from where the companion stands (the row asserts it) yet
        // low enough that the stands sampled round the target clear the hover ceiling. Only the
        // stance's own eagerness lifts combat over that pull. The screen is centred on the player as the live game
        // centres it, because the hunt side's nearness reads the screen. The scene is static: full
        // ticks run, with the body held where it stands after each, so the matchup is measured
        // rather than chased and the positioner still rescores onto it.
        player.Bottom = new Vector2(70 * 16, 90 * 16);
        companion.NPC.Bottom = new Vector2(70 * 16 - 750, 90 * 16);
        using var _ = ScreenFollow.CentredOn(player.Center);
        using var hostile = ClearAfter.At(30);
        NPC zombie = Hostile(30, NPCID.Zombie, companion.NPC.Bottom + new Vector2(400, -150));
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        companion.Brain.Senses.Update(companion.NPC, player);
        var reachCtx = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, companion.Brain.Senses);
        var muzzle = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.CompanionCombat.Muzzle(companion.NPC);
        Require(companion.Combat.ShotSolves(reachCtx, muzzle, zombie),
            "the eagerness scene needs the zombie shotable from where the companion stands, or it is not in reach");
        var combatAction = companion.Brain.Chooser.Actions.OfType<Combat>().Single();
        int combatWins = 0, planId = -1, planFront = 0;
        float combatFinal = 0f, companyFinal = 0f, companyRaw = 0f, planValue = 0f;
        string companyEligibility = "";
        Vector2 heldBottom = companion.NPC.Bottom;
        for (int tick = 0; tick < 60; tick++)
        {
            Tick(companion);
            companion.NPC.Bottom = heldBottom;
            companion.NPC.velocity = Vector2.Zero;
            if (tick < 30) continue;
            if (companion.Brain.Chooser.Current?.Name == "combat") combatWins++;
            foreach (var score in companion.Brain.Chooser.LastScores)
            {
                if (score.Action.Name == "combat") combatFinal = score.Final;
                if (score.Action.Name == "keep-company") { companyFinal = score.Final; companyRaw = score.Raw; companyEligibility = $"{score.Eligibility}/{score.EligibilityReason}"; }
            }
            if (combatAction.OfferedPlan != null)
            {
                planId = combatAction.OfferedPlan.Id;
                planValue = combatAction.OfferedPlan.Weighted;
                planFront = combatAction.OfferedFrontSize;
            }
        }
        Console.WriteLine($"  shared-eagerness: combat wins {combatWins}/30, combat final {combatFinal:0.000}, company final {companyFinal:0.000} raw {companyRaw:0.000} {companyEligibility}, plan {planId} value {planValue:0.000} front {planFront}");
        Require(planId >= 0, "combat's wins must come from a committed plan, not a scoreless offer");
        Require(combatWins == 30, $"a damageable hostile in reach must take the body off keeping company; combat won {combatWins}/30");
        return 0;
    }

    public static int DangerLiftsCombatOverWork()
    {
        var (companion, player) = MineScene(TileID.Copper, new Point(25, 89), new Point(26, 89), new Point(27, 89));
        WaitForJob(companion, "mine", 300);
        player.statLife = 40;
        using var hostile = ClearAfter.At(30);
        Hostile(30, NPCID.Zombie, player.Bottom + new Vector2(48, 0), damage: 20, life: 400);
        int combatTicks = 0;
        for (int tick = 0; tick < 120; tick++)
        {
            Tick(companion);
            if (companion.Brain.Chooser.Current?.Name == "combat") combatTicks++;
            VerifyResponsiveFollowing.AdvanceNative(companion);
        }
        Console.WriteLine($"  danger-over-work: combat ticks {combatTicks} of 120 with a zombie on a hurt player");
        Require(combatTicks > 60, $"a threat on a hurt player must lift combat off the vein; combat ticks={combatTicks}");
        return 0;
    }

    public static int DistantIdleEnemyKeepsOffTheVein()
    {
        var (companion, player) = MineScene(TileID.Copper, new Point(25, 89), new Point(26, 89), new Point(27, 89));
        WaitForJob(companion, "mine", 300);
        using var _ = ScreenFollow.CentredOn(player.Center);
        using var hostile = ClearAfter.At(30);
        Hostile(30, NPCID.Zombie, companion.NPC.Bottom + new Vector2(560, 0), life: 400);
        int mineTicks = 0, combatTicks = 0;
        for (int tick = 0; tick < 120; tick++)
        {
            Tick(companion);
            if (companion.Brain.Chooser.Current?.Name == "mine") mineTicks++;
            if (companion.Brain.Chooser.Current?.Name == "combat") combatTicks++;
            VerifyResponsiveFollowing.AdvanceNative(companion);
        }
        Console.WriteLine($"  distant-idle-enemy: mine ticks {mineTicks}, combat ticks {combatTicks} of 120");
        Require(mineTicks > 100 && combatTicks == 0,
            $"an unthreatening enemy far away must not take the body off the vein; mine={mineTicks} combat={combatTicks}");
        return 0;
    }

    public static int DodgeBendsMiningWithoutStoppingIt()
    {
        var (companion, player) = MineScene(TileID.Copper, new Point(25, 89), new Point(26, 89), new Point(27, 89));
        WaitForJob(companion, "mine", 300);
        var chooser = companion.Brain.Chooser;
        Projectile shot = HostileShot(companion.NPC.Center - new Vector2(80, 0), new Vector2(8, 0));
        int evadeTicks = 0, suspended = 0, hits = 0, mineTicks = 0;
        for (int tick = 0; tick < 30; tick++)
        {
            Tick(companion);
            var grant = companion.Brain.ControlGrants.Last!.Value;
            if (grant.AppliedOwner == "evade") evadeTicks++;
            if (chooser.Activity.Phase == ActivityPhase.Suspended) suspended++;
            if (chooser.Current?.Name == "mine") mineTicks++;
            VerifyResponsiveFollowing.AdvanceNative(companion);
            shot.position += shot.velocity;
            if (shot.Hitbox.Intersects(companion.NPC.Hitbox)) hits++;
        }
        shot.active = false;
        Console.WriteLine($"  dodge-bends-mining: evade ticks {evadeTicks}, mine ticks {mineTicks}, suspended {suspended}, hits {hits}");
        Require(evadeTicks > 0, $"a shot on a collision course must bend some tick's motion; evade ticks={evadeTicks}");
        Require(mineTicks == 30 && suspended == 0, $"the bend must neither suspend mining nor replace it; mine={mineTicks} suspended={suspended}");
        Require(hits == 0, $"the bent motion must get the body clear of the shot; hits={hits}");
        return 0;
    }

    public static int UnarmedOffersNoCombat()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.Bottom = companion.NPC.Bottom;
        var gear = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;
        gear.Slots[0] = new Item();
        gear.Slots[1] = new Item();
        NPC target = new NPC(); target.SetDefaults(NPCID.Zombie);
        target.whoAmI = 12; target.active = true; target.Bottom = companion.NPC.Bottom + new Vector2(160, 0);
        Main.npc[12] = target;
        companion.Brain.Senses.Update(companion.NPC, Main.LocalPlayer);
        companion.Brain.Senses.Threats.Threats.Clear();
        companion.Brain.Senses.Threats.Threats.Add(new Threat { Npc = target, DistanceToCompanion = 160, DistanceToPlayer = 160 });
        var context = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, companion.Brain.Senses);
        var combat = new Combat();
        float score = VerifyPreparedActivities.PrepareAndScore(combat, context);
        Console.WriteLine($"  unarmed: score {score}, eligibility {combat.Eligibility} ({combat.EligibilityReason})");
        Require(score == 0f && combat.Eligibility == live::AICompanion.Companion.Brain.Activities.OfferEligibility.NoOpportunity
            && combat.EligibilityReason == "no-weapon",
            $"an unarmed companion must offer no combat; score={score} {combat.Eligibility} {combat.EligibilityReason}");
        int combatTicks = 0, fired = 0;
        for (int tick = 0; tick < 60; tick++)
        {
            Tick(companion);
            if (companion.Brain.Chooser.Current?.Name == "combat") combatTicks++;
            if (companion.Combat.LastFireOutcome == "fired") fired++;
            VerifyResponsiveFollowing.AdvanceNative(companion);
        }
        Require(combatTicks == 0 && fired == 0, $"unarmed ticks must never run combat nor fire; combat={combatTicks} fired={fired}");
        return 0;
    }

    public static int HuntingOffMigration()
    {
        Preferences old = Preferences.Load(new TagCompound { ["hunting"] = (byte)0 });
        Require(!old.Combat, "an old save with hunting off must load as combat off");
        Preferences both = Preferences.Load(new TagCompound { ["combat"] = (byte)1, ["hunting"] = (byte)0 });
        Require(both.Combat, "a save that wrote combat must read combat, not the legacy hunting key");
        Preferences fresh = Preferences.Load(new TagCompound());
        Require(fresh.Combat, "a save that wrote neither key must default combat on");
        var tag = new TagCompound();
        new Preferences { Combat = false }.Save(tag);
        Require(Preferences.Load(tag).Combat == false, "combat off must round-trip through the combat key");
        Console.WriteLine("  hunting-off migration: legacy hunting=0 loads combat off, combat key wins, default on, round-trips");
        return 0;
    }

    private static (CompanionNPC Companion, Player Player) OpenFloor()
    {
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Disabled, TileID.Copper, new Point(25, 89));
        Main.tile[25, 89].ClearEverything();
        foreach (Projectile projectile in Main.projectile) projectile.active = false;
        return (ctx.Companion, ctx.Player);
    }

    private static (CompanionNPC Companion, Player Player) MineScene(ushort tile, params Point[] vein)
    {
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, tile, vein);
        foreach (Projectile projectile in Main.projectile) projectile.active = false;
        return (ctx.Companion, ctx.Player);
    }

    private static void WaitForJob(CompanionNPC companion, string job, int ticks)
    {
        for (int tick = 0; tick < ticks && companion.Brain.Chooser.Current?.Name != job; tick++)
        {
            Tick(companion);
            VerifyResponsiveFollowing.AdvanceNative(companion);
        }
        Require(companion.Brain.Chooser.Current?.Name == job,
            $"the scene must first be {job}, or the row proves nothing; current={companion.Brain.Chooser.Current?.Name}");
    }

    private static NPC Hostile(int slot, int type, Vector2 bottom, int damage = -1, int life = -1)
    {
        NPC npc = Main.npc[slot];
        npc.SetDefaults(type);
        npc.whoAmI = slot; npc.active = true; npc.velocity = Vector2.Zero;
        if (damage >= 0) npc.damage = damage;
        if (life > 0) npc.life = npc.lifeMax = life;
        npc.Bottom = bottom;
        return npc;
    }

    private static Projectile HostileShot(Vector2 center, Vector2 velocity, int damage = 20)
    {
        int slot = Main.maxProjectiles - 1;
        Projectile shot = Main.projectile[slot];
        shot.SetDefaults(ProjectileID.WoodenArrowHostile);
        shot.whoAmI = slot;
        shot.active = true; shot.hostile = true; shot.friendly = false; shot.damage = damage;
        shot.velocity = velocity;
        shot.Center = center;
        return shot;
    }

    private static void Tick(CompanionNPC companion)
    {
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
    }

    /// <summary>
    /// The screen centred on a point, as the live game centres it on the player: the fixture's
    /// screen otherwise sits at the origin and on-screen rules answer for a camera nobody plays
    /// with. Restores the previous origin on dispose, because the origin is process-global and
    /// later fixtures run against whatever it was left at.
    /// </summary>
    private readonly struct ScreenFollow : IDisposable
    {
        private readonly Vector2 previous;
        private ScreenFollow(Vector2 previous) => this.previous = previous;
        public static ScreenFollow CentredOn(Vector2 centre)
        {
            var previous = Main.screenPosition;
            Main.screenPosition = centre - new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
            return new ScreenFollow(previous);
        }
        public void Dispose() => Main.screenPosition = previous;
    }

    /// <summary>
    /// Wipes a planted hostile slot on scope exit, pass or fail, so a zombie never leaks into the
    /// next scene's observation: MostUrgent and the urgencies survive a threat-list rebuild.
    /// </summary>
    private readonly struct ClearAfter : IDisposable
    {
        private readonly int slot;
        private ClearAfter(int slot) => this.slot = slot;
        public static ClearAfter At(int slot) => new(slot);
        public void Dispose() => Main.npc[slot] = new NPC();
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
