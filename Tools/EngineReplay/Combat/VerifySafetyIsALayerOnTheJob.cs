extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Policy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using HandGrant = live::AICompanion.Companion.Brain.Infrastructure.Grants.HandGrant;
using ActivityPhase = live::AICompanion.Companion.Brain.Infrastructure.Selection.ActivityPhase;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using CompanionGear = live::AICompanion.Companion.Inventory.CompanionGear;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;

/// <summary>
/// The combined-safety scenes, restated on 15 September 2026 when safety became a layer on the job. No
/// response takes the body: the job keeps running and bends its motion away from a predicted hit. Each scene
/// asserts the companion stays coherent under that: an enemy beside the body does not keep it from a player
/// who walks away, the hands keep fighting while the job keeps the feet, a shot bends guarding without
/// suspending it and misses, and guarding does not buy its position with contact. A fifth scene asked that a
/// surfacing escape from water was not vetoed into drowning by a shot above it; every liquid became air to
/// the orb the same day, so there is no escape to veto and the scene went with it. Enemy AI does not run and projectiles are not advanced by the engine, so every hostile and shot is
/// placed and, where a scene needs motion, moved by hand; these establish the brain's responses to stated
/// arrangements, not a live fight.
/// </summary>
internal static class VerifySafetyIsALayerOnTheJob
{
    public static int Run()
    {
        // Every scene runs the full brain, whose searches the live tick bounds by wall-clock allowances. Left in
        // force, how far each search got before its deadline would decide the verdict, so the verdict would measure
        // the machine. Lifting them keeps each query's work-count limits and takes load out of the result.
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        try
        {
            AnEnemyBesideTheOrbDoesNotKeepItFromAPlayerWhoWalksAway();
            TheHandsKeepFiringWhileTheBodyKeepsItsJob();
            AShotBendsGuardingWithoutSuspendingItAndMisses();
            GuardingReachesThePlayerPastAnInterveningHostileWithoutContact();
        }
        finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false; }
        Console.WriteLine("safety is a layer on the job: an enemy beside the body does not keep it from a walking player, the hands fire while the job keeps the feet, a shot bends guarding without suspending it and misses, and guarding passes an intervening hostile without contact");
        return 0;
    }

    /// <summary>
    /// The first play of the orb, capture 2026-09-15_08-30-31-684: an unarmed companion keeping company among zombies while the
    /// player walked away. Combat spacing owned the body on 63% of that session and once held it still for 773 ticks while the
    /// player walked 658 px off. Safety rides on the job and is never a job of its own, so with a heavy hitter beside the body and
    /// nothing in its hands, keeping company must never be suspended, the body must not sit still while the player walks, and it
    /// must be with him where he stops. Enemy AI does not run, so the zombie stays where it is placed. The pass lines were written
    /// before the change they judge: no suspended tick, no still run of ten ticks while he walks, and the follow objective met
    /// within 900 ticks of his stopping.
    /// </summary>
    private static void AnEnemyBesideTheOrbDoesNotKeepItFromAPlayerWhoWalksAway()
    {
        var ctx = VerifyCompanyLocalMotion.BuildNeighbourhood(hazards: false);
        VerifyUsefulAssistance.ClearMeasuredLight();
        CompanionGear slots = ctx.Player.GetModPlayer<CompanionPlayer>().Gear;
        for (int i = 0; i < CompanionGear.SlotCount; i++) slots.Slots[i] = new Item();
        for (int i = 0; i < Main.projectile.Length; i++) Main.projectile[i] ??= new Projectile();
        foreach (Projectile projectile in Main.projectile) projectile.active = false;
        NPC zombie = Hostile(30, NPCID.Zombie, ctx.Npc.Bottom + new Vector2(48, 0), damage: 100);
        zombie.dontTakeDamage = true;
        var brain = ctx.Companion.Brain;
        Player player = ctx.Player;
        float stopAt = player.Bottom.X + 40 * 16;
        int suspended = 0, stillRun = 0, longestStill = 0, stoppedAt = -1, arrivedAt = -1;
        var owners = new SortedDictionary<string, int>();
        // The ticks leading up to the first still run of ten, so a failure names what owned the body and what it asked for.
        var recent = new Queue<string>();
        string? stillTrace = null;
        for (int tick = 0; tick < 2400 && arrivedAt < 0; tick++)
        {
            bool walking = player.Bottom.X < stopAt;
            player.velocity = walking ? new Vector2(3f, 0f) : Vector2.Zero;
            player.position += player.velocity;
            if (!walking && stoppedAt < 0) stoppedAt = tick;
            zombie.velocity = Vector2.Zero;
            VerifyOreWork.AdvanceBrain(ctx);
            string owner = brain.ControlGrants.Last?.AppliedOwner ?? "-";
            owners[owner] = owners.TryGetValue(owner, out int seen) ? seen + 1 : 1;
            if (brain.Chooser.Activity.Phase == ActivityPhase.Suspended && !brain.FollowRecovery.Active && !ctx.Companion.IsDowned) suspended++;
            stillRun = walking && ctx.Companion.Motor.State.Velocity.Length() < 0.3f ? stillRun + 1 : 0;
            Vector2 v = ctx.Companion.Motor.State.Velocity, d = ctx.Companion.Motor.DesiredVelocity;
            recent.Enqueue($"t{tick} {owner} {brain.LastRequest.Kind} request-anchor {brain.LastRequest.Anchor.X:0},{brain.LastRequest.Anchor.Y:0} nav {brain.Navigator.Status}/{brain.Navigator.ProgressReason} plan-failed {brain.Navigator.LastPlanFailed} stop {brain.Navigator.LastSearchStop} goal {brain.Navigator.Goal} vel {v.X:0.00},{v.Y:0.00} desired {d.X:0.00},{d.Y:0.00} centre {ctx.Npc.Center.X:0},{ctx.Npc.Center.Y:0} player {player.Bottom.X:0},{player.Bottom.Y:0}");
            if (recent.Count > 14) recent.Dequeue();
            if (stillRun == 10 && stillTrace == null) stillTrace = string.Join(" | ", recent);
            longestStill = Math.Max(longestStill, stillRun);
            // With him is inside the stopped player's region; sight of him stopped being a condition when losing it stopped being distance.
            if (!walking && brain.Senses.Intent.Objective.IsSatisfied(ctx.Npc.Center))
                arrivedAt = tick;
        }
        string ledger = $"suspended ticks {suspended}, longest still run while walking {longestStill}, "
            + $"stopped at {stoppedAt}, arrived at {arrivedAt}, owners {string.Join(", ", owners.Select(o => $"{o.Key}={o.Value}"))}, "
            + $"centre {ctx.Npc.Center}, player {player.Bottom}";
        Console.WriteLine($"  enemy-beside row: {ledger}");
        Require(suspended == 0, $"an enemy beside the body must never suspend keeping company; {ledger}");
        Require(longestStill < 10, $"the body must not sit still for ten ticks while the player walks; {ledger}; the ticks up to it: {stillTrace}");
        Require(arrivedAt >= 0 && arrivedAt - stoppedAt <= 900, $"the body must be with the player within 900 ticks of his stopping; {ledger}");
    }

    private static (CompanionNPC Companion, Player Player) OpenFloor()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 89));
        Main.tile[25, 89].ClearEverything();
        for (int i = 0; i < Main.projectile.Length; i++) Main.projectile[i] ??= new Projectile();
        foreach (Projectile projectile in Main.projectile) projectile.active = false;
        return (ctx.Companion, ctx.Player);
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
        // The engine's active-projectile iterator covers the first maxProjectiles slots; the array holds
        // one more, so a shot in its last element exists but is never observed.
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
    /// Attacks while keeping its job. A wounded companion beside a damageable zombie used to take combat spacing, and this
    /// row required a tick where spacing owned the feet while the hands fired. Since 15 September 2026 no enemy takes the
    /// body, so the row asks what survived the change: over the same scene the hands are granted and fire at the zombie on
    /// some tick, and the ordinary activity is never suspended.
    /// </summary>
    private static void TheHandsKeepFiringWhileTheBodyKeepsItsJob()
    {
        var (companion, player) = OpenFloor();
        player.Bottom = new Vector2(80 * 16, 90 * 16);
        companion.NPC.life = 12;
        Hostile(30, NPCID.Zombie, companion.NPC.Bottom + new Vector2(64, 0), damage: 20, life: 400);
        VerifyResponsiveFollowing.AdvanceNative(companion);
        int fired = 0, firedAtZombie = 0, suspendedTicks = 0;
        for (int tick = 0; tick < 180; tick++)
        {
            Tick(companion);
            var brain = companion.Brain;
            bool shot = companion.Arsenal.LastFireOutcome == "fired";
            fired += shot ? 1 : 0;
            if (shot && brain.EngageTarget?.whoAmI == 30 && brain.ControlGrants.Last?.Hand == HandGrant.Available) firedAtZombie++;
            if (brain.Chooser.Current != null && brain.Chooser.Activity.Phase == ActivityPhase.Suspended) suspendedTicks++;
            VerifyResponsiveFollowing.AdvanceNative(companion);
        }
        Console.WriteLine($"  keep-the-job rows: fired ticks {fired}, fired at the zombie {firedAtZombie}, suspended ticks {suspendedTicks}");
        Require(firedAtZombie > 0, $"on some tick the hands must be granted, aimed at the zombie and fire; fired={fired} at the zombie={firedAtZombie}");
        Require(suspendedTicks == 0,
            $"an enemy beside a wounded companion must not suspend its job; suspended ticks={suspendedTicks}");
    }

    /// <summary>
    /// J12, restated for the orb. Guarding is chosen against a zombie beside the player; then a hostile shot is placed on a
    /// collision course with the companion and advanced by hand every tick, as the engine would advance it. The reflex used to
    /// take that tick, owning the feet and suspending guarding until the shot was gone. Now some tick's movement must be owned by
    /// the evade step with the hands still granted, guarding must stay executing under the same activity identity throughout, and
    /// the shot must never meet the body. The dodge is asserted where the walker left it as a reported defect, because a body that
    /// bends its own motion ahead of a hit is exactly what replaced the takeover, and a bend that does not get clear is not one.
    /// </summary>
    private static void AShotBendsGuardingWithoutSuspendingItAndMisses()
    {
        var (companion, player) = OpenFloor();
        companion.NPC.Bottom = new Vector2(14 * 16, 90 * 16);
        player.Bottom = new Vector2(22 * 16, 90 * 16);
        Hostile(30, NPCID.Zombie, player.Bottom + new Vector2(40, 0));
        VerifyResponsiveFollowing.AdvanceNative(companion);
        var chooser = companion.Brain.Chooser;
        for (int tick = 0; tick < 60 && !(chooser.Current?.Name == "guard" && chooser.Activity.Phase == ActivityPhase.Executing); tick++)
        {
            Tick(companion);
            VerifyResponsiveFollowing.AdvanceNative(companion);
        }
        Require(chooser.Current?.Name == "guard" && chooser.Activity.Phase == ActivityPhase.Executing,
            $"the shot scene must first be guarding, or keeping the job proves nothing; current={chooser.Current?.Name} phase={chooser.Activity.Phase}");
        var guard = chooser.Current;
        long id = chooser.Activity.Id;

        Projectile shot = HostileShot(companion.NPC.Center - new Vector2(80, 0), new Vector2(8, 0));
        int evadeTicks = 0, hits = 0, suspended = 0, ticks = 0;
        bool sameGuard = true;
        HandGrant? evadeHand = null;
        for (; ticks < 30; ticks++)
        {
            Tick(companion);
            var grant = companion.Brain.ControlGrants.Last!.Value;
            if (grant.AppliedOwner == "evade") { evadeTicks++; evadeHand ??= grant.Hand; }
            if (chooser.Activity.Phase == ActivityPhase.Suspended) suspended++;
            sameGuard &= ReferenceEquals(chooser.Current, guard) && chooser.Activity.Id == id;
            VerifyResponsiveFollowing.AdvanceNative(companion);
            shot.position += shot.velocity;
            if (shot.Hitbox.Intersects(companion.NPC.Hitbox)) hits++;
        }
        shot.active = false;
        Console.WriteLine($"  shot row: evade ticks {evadeTicks} (hand {evadeHand}), suspended ticks {suspended}, same guard {sameGuard}, hits {hits}, body {companion.NPC.Center}");
        Require(evadeTicks > 0 && evadeHand == HandGrant.Available,
            $"a shot on a collision course must bend some tick's motion through the evade step with the hands still granted; evade ticks={evadeTicks} hand={evadeHand}");
        Require(suspended == 0 && sameGuard,
            $"the shot must neither suspend guarding nor replace it; suspended ticks={suspended} same guard={sameGuard} current={chooser.Current?.Name}#{chooser.Activity.Id} (was guard#{id})");
        Require(hits == 0, $"the bent motion must get the body clear of the shot; hits={hits} over {ticks} ticks");
    }

    /// <summary>
    /// C01. The companion starts on the far side of an intervening zombie from a player who has a second
    /// zombie on him. Guarding must move the companion past the intervening zombie to the player's side,
    /// and on no tick may the companion's raw hitbox overlap that zombie's. Contact damage never applies
    /// headless because enemy AI does not run, so contact is measured as geometry; and standing still also
    /// avoids contact, so reaching past the intervening zombie is part of the pass.
    /// </summary>
    private static void GuardingReachesThePlayerPastAnInterveningHostileWithoutContact()
    {
        var (companion, player) = OpenFloor();
        companion.NPC.Bottom = new Vector2(10 * 16, 90 * 16);
        player.Bottom = new Vector2(40 * 16, 90 * 16);
        NPC between = Hostile(31, NPCID.Zombie, new Vector2(25 * 16, 90 * 16));
        Hostile(30, NPCID.Zombie, player.Bottom + new Vector2(48, 0));
        VerifyResponsiveFollowing.AdvanceNative(companion);
        int contact = 0, guarding = 0, passedAt = -1;
        float nearest = float.MaxValue;
        for (int tick = 0; tick < 480; tick++)
        {
            Tick(companion);
            guarding += companion.Brain.Chooser.Current?.Name == "guard" ? 1 : 0;
            VerifyResponsiveFollowing.AdvanceNative(companion);
            between.velocity = Vector2.Zero;
            if (companion.NPC.Hitbox.Intersects(between.Hitbox)) contact++;
            nearest = MathF.Min(nearest, MathF.Abs(companion.NPC.Center.X - between.Center.X));
            if (passedAt < 0 && companion.NPC.Left.X > between.Right.X) passedAt = tick;
        }
        Console.WriteLine($"  intervening-hostile row: guard ticks {guarding}, contact ticks {contact}, passed at {passedAt}, nearest centre gap {nearest:0.0}, feet {companion.NPC.Bottom}");
        Require(guarding > 0, $"the scene must select guarding, or it proves nothing about guarding; guard ticks={guarding}");
        Require(contact == 0, $"guarding must never overlap the intervening zombie's hitbox; contact ticks={contact}");
        Require(passedAt >= 0, $"guarding must actually get past the intervening zombie, since standing still also avoids contact; feet={companion.NPC.Bottom}");
    }

    /// <summary>
    /// S01 reproduction, deliberately outside the default run: a companion standing on dry floor with no
    /// activities and a hostile arrow flying at its body height. The arrow is advanced by hand each tick.
    /// Exit 1 while the arrow's box meets the companion's, which is the reported defect: collision
    /// avoidance moves toward the player through a veto predicate and never considers a jump.
    /// </summary>
    public static int ReproduceDodgeOnDryFloor()
    {
        var (companion, player) = OpenFloor();
        player.Bottom = new Vector2(60 * 16, 90 * 16);
        companion.Brain.Chooser.Actions.Clear();
        VerifyResponsiveFollowing.AdvanceNative(companion);
        Projectile arrow = HostileShot(companion.NPC.Center - new Vector2(160, 0), new Vector2(8, 0));
        int hitAt = -1;
        var controls = new List<string>();
        for (int tick = 0; tick < 40 && hitAt < 0; tick++)
        {
            Tick(companion);
            controls.Add($"{companion.Brain.Senses.Projectiles.Threats.Count}{companion.Brain.Movement.LastEvade.Reason}:{companion.Motor.AppliedControls.Desired.X:0.#},{companion.Motor.AppliedControls.Desired.Y:0.#}");
            VerifyResponsiveFollowing.AdvanceNative(companion);
            arrow.position += arrow.velocity;
            if (arrow.Hitbox.Intersects(companion.NPC.Hitbox)) hitAt = tick;
        }
        Console.WriteLine($"dodge reproduction: {(hitAt >= 0 ? $"HIT at tick {hitAt}" : "dodged")}; controls {string.Join(" ", controls)}");
        return hitAt >= 0 ? 1 : 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("safety is a layer on the job: " + message);
    }
}
