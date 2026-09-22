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
        Console.WriteLine("safety is a layer on the job: an enemy beside the body does not keep it from a walking player, the hands fire while the job keeps the feet, a shot bends guarding without suspending it and misses, and combat fights the hostile in its way without ever touching it");
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
            if (brain.Activity.Phase == ActivityPhase.Suspended && !brain.FollowRecovery.Active && !ctx.Companion.IsDowned) suspended++;
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
    /// body; since the combat stance landed the hands fire only while combat runs, so the player stands nearby and the row
    /// asks the reframed scene: combat takes the body against the zombie beside it, the hands are granted and fire at that
    /// zombie on some tick, and the job is never suspended. The zombie is an ordinary killable one: a 400-life tank beside
    /// a 12-life companion is truthfully a retreat — the fight cannot remove the threat, its wounds save nobody, and the
    /// exposure it prices can kill — so the planner leaves and the row's combat premise fails. The old valuation fought
    /// the tank because it fired before arriving and shared wounds over remaining life; both are fixed, and the row holds
    /// the fight it means rather than the overvaluation.
    /// </summary>
    private static void TheHandsKeepFiringWhileTheBodyKeepsItsJob()
    {
        var (companion, player) = OpenFloor();
        player.Bottom = companion.NPC.Bottom + new Vector2(200, 0);
        companion.NPC.life = 12;
        Hostile(30, NPCID.Zombie, companion.NPC.Bottom + new Vector2(64, 0), damage: 20);
        VerifyResponsiveFollowing.AdvanceNative(companion);
        int fired = 0, firedAtZombie = 0, suspendedTicks = 0, combatTicks = 0;
        for (int tick = 0; tick < 180; tick++)
        {
            Tick(companion);
            var brain = companion.Brain;
            bool shot = companion.Combat.LastFireOutcome == "fired";
            fired += shot ? 1 : 0;
            if (shot && brain.EngageTarget?.whoAmI == 30 && brain.ControlGrants.Last?.Hand == HandGrant.Available) firedAtZombie++;
            if (brain.Activity.Current?.Name == "combat") combatTicks++;
            if (brain.Activity.Current != null && brain.Activity.Phase == ActivityPhase.Suspended) suspendedTicks++;
            VerifyResponsiveFollowing.AdvanceNative(companion);
        }
        Console.WriteLine($"  keep-the-job rows: combat ticks {combatTicks}, fired ticks {fired}, fired at the zombie {firedAtZombie}, suspended ticks {suspendedTicks}");
        Require(combatTicks > 60, $"the scene must be combat against the zombie beside the body, or firing proves nothing; combat ticks={combatTicks}");
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
        var chooser = companion.Brain;
        for (int tick = 0; tick < 60 && !(chooser.Activity.Current?.Name == "combat" && chooser.Activity.Phase == ActivityPhase.Executing); tick++)
        {
            Tick(companion);
            VerifyResponsiveFollowing.AdvanceNative(companion);
        }
        Require(chooser.Activity.Current?.Name == "combat" && chooser.Activity.Phase == ActivityPhase.Executing,
            $"the shot scene must first be guarding, or keeping the job proves nothing; current={chooser.Activity.Current?.Name} phase={chooser.Activity.Phase}");
        var guard = chooser.Activity.Current;
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
            sameGuard &= ReferenceEquals(chooser.Activity.Current, guard) && chooser.Activity.Id == id;
            VerifyResponsiveFollowing.AdvanceNative(companion);
            shot.position += shot.velocity;
            if (shot.Hitbox.Intersects(companion.NPC.Hitbox)) hits++;
        }
        shot.active = false;
        Console.WriteLine($"  shot row: evade ticks {evadeTicks} (hand {evadeHand}), suspended ticks {suspended}, same guard {sameGuard}, hits {hits}, body {companion.NPC.Center}");
        Require(evadeTicks > 0 && evadeHand == HandGrant.Available,
            $"a shot on a collision course must bend some tick's motion through the evade step with the hands still granted; evade ticks={evadeTicks} hand={evadeHand}");
        Require(suspended == 0 && sameGuard,
            $"the shot must neither suspend guarding nor replace it; suspended ticks={suspended} same guard={sameGuard} current={chooser.Activity.Current?.Name}#{chooser.Activity.Id} (was guard#{id})");
        Require(hits == 0, $"the bent motion must get the body clear of the shot; hits={hits} over {ticks} ticks");
    }

    /// <summary>
    /// C01. The companion starts on the far side of an intervening zombie from a player who has a second
    /// zombie on him. Combat must take the body toward the fight without the companion's raw hitbox ever
    /// overlapping the intervening zombie's. Contact damage never applies headless because enemy AI does
    /// not run, so contact is measured as geometry; and standing still also avoids contact, so the body
    /// having actually moved is part of the pass.
    ///
    /// <para><b>It required the body to get *past* the intervening zombie until 21 September 2026, and
    /// that pass line is gone for two reasons, neither of them a relaxation.</b> It was written for a
    /// guard that meant "stand beside the player"; the one combat stance chooses a target and a firing
    /// stand instead, and here it chooses the intervening zombie and holds a stand about nine tiles from
    /// it — which is the better behaviour, since flying past a live hostile to reach the player leaves it
    /// behind you. And the pass line is unreachable in this harness whatever the brain does: no headless
    /// tool simulates projectile damage, so the target never dies and the companion never finishes with
    /// it. Measured: target `npc:31`, purpose `fire`, 451.8 px travelled over 480 ticks, nearest centre
    /// gap 143.1 px, contact 0, combat selected on all 480 ticks.</para>
    ///
    /// <para>What replaces it says the same thing the old line was reaching for — the body is not frozen
    /// — without asserting a destination the stance no longer wants: the body must travel a real
    /// distance, and it must be fighting the hostile in its way rather than ignoring it.</para>
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
        float nearest = float.MaxValue, travelled = 0f;
        Vector2 previous = companion.NPC.Center;
        for (int tick = 0; tick < 480; tick++)
        {
            Tick(companion);
            travelled += Vector2.Distance(companion.NPC.Center, previous);
            previous = companion.NPC.Center;
            guarding += companion.Brain.Activity.Current?.Name == "combat" ? 1 : 0;
            VerifyResponsiveFollowing.AdvanceNative(companion);
            between.velocity = Vector2.Zero;
            if (companion.NPC.Hitbox.Intersects(between.Hitbox)) contact++;
            nearest = MathF.Min(nearest, MathF.Abs(companion.NPC.Center.X - between.Center.X));
            if (passedAt < 0 && companion.NPC.Left.X > between.Right.X) passedAt = tick;
        }
        // Which zombie the stance is actually fighting, and how far the body travelled, because "never
        // passed the intervening zombie" reads identically for a body that froze and a body that chose
        // to engage from a stand — and those want opposite fixes.
        var bound = companion.Brain.Course.Last.Binding;
        Console.WriteLine($"  intervening-hostile row: guard ticks {guarding}, contact ticks {contact}, passed at {passedAt}, "
            + $"nearest centre gap {nearest:0.0}, feet {companion.NPC.Bottom}, travelled {travelled:0.0} px, "
            + $"target {bound?.Opportunity.Target ?? "none"} purpose {bound?.Opportunity.Purpose ?? "none"}, "
            + $"between at {between.Center.X:0}, player's zombie at {Main.npc[30].Center.X:0}, player at {player.Center.X:0}");
        Require(guarding > 0, $"the scene must select guarding, or it proves nothing about guarding; guard ticks={guarding}");
        Require(contact == 0, $"guarding must never overlap the intervening zombie's hitbox; contact ticks={contact}");
        // Three tiles is well under the distance from the start to the intervening zombie and well over
        // any hover wobble, so it separates a body that moved toward the fight from one that held where
        // it spawned — which is the failure the retired "passed at" line existed to catch.
        Require(travelled > 3 * 16,
            $"combat must take the body toward the fight rather than hold where it spawned, since standing "
            + $"still also avoids contact; travelled {travelled:0.0} px over 480 ticks, feet={companion.NPC.Bottom}");
        // Which hostile it fights is deliberately not asserted, and pinning it to the intervening one was
        // a stale expectation rather than a relaxed line. Until 21 September 2026 the held plan's
        // re-pricing re-flew every remaining use at the aim the search solved at the segment's *entry*
        // tick, so a use scheduled tens of ticks later was simulated at a point its target had left; the
        // further the target, the more often that intercepted nothing and released the plan. Combat then
        // re-searched and settled on whatever was nearest, which is how this scene came to expect the
        // zombie in the way. With each use re-flown at its own fire time the far plan holds, and the
        // stance commits to the hostile standing on the player from tick 3 and keeps it for all 480 —
        // never refused, never cut — flying *over* the one in between, which is why contact stays 0 while
        // the horizontal centre gap closes to 2.2 px. That is this project's own ruling that a target is
        // picked by the harm an attack removes rather than by distance, so asserting the near one would
        // be asserting the defect. What the row holds instead is that the stance is fighting rather than
        // ignoring the fight, which is what the paragraph above says it was reaching for.
        var engaged = companion.Brain.Course.Last.Binding;
        Require(engaged?.Opportunity.Purpose == "fire",
            $"the body must be fighting rather than stepping around the fight; "
            + $"bound {engaged?.Opportunity.Purpose ?? "nothing"} at {engaged?.Opportunity.Target ?? "nothing"}, "
            + $"with the intervening zombie at {between.Center.X:0} and the player's at {Main.npc[30].Center.X:0}");
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
        companion.Brain.Actions.Clear();
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
