extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Policy = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicy;
using HandGrant = live::AICompanion.Companion.Brain.ActivityCoordination.HandGrant;
using ActivityPhase = live::AICompanion.Companion.Brain.BehaviourSelection.ActivityPhase;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;

/// <summary>
/// Proposal 1's P08 combined-safety scenes. Shared safety answers in a fixed order — environmental
/// escape, then collision avoidance, then combat spacing — and each answer must leave the rest of the
/// companion coherent: the hands keep fighting while the feet retreat, a reflex interrupts guarding
/// without ending it, guarding does not buy its position with contact, and a surfacing escape is not
/// vetoed into drowning by a projectile above the water. Enemy AI does not run and projectiles are not
/// advanced by the engine, so every hostile and shot is placed and, where a scene needs motion, moved by
/// hand; these establish the brain's responses to stated arrangements, not a live fight.
/// </summary>
internal static class VerifySafetyAftermath
{
    public static int Run()
    {
        // Every scene runs the full brain, whose searches the live tick bounds by wall-clock allowances.
        // Left in force, the surfacing escape under a shot reached air or drowned depending on how loaded
        // the machine was, so the verdict measured the machine. Lifting them keeps each query's work-count
        // limits and takes load out of the result.
        live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = true;
        try
        {
            TheHandsKeepFiringWhileCombatSpacingRetreats();
            AReflexSuspendsGuardingAndGuardingResumesAsTheSameActivity();
            GuardingReachesThePlayerPastAnInterveningHostileWithoutContact();
            SurfacingIsNotVetoedIntoDrowningByAProjectileAboveTheWater();
        }
        finally { live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = false; }
        Console.WriteLine("safety aftermath: the hands fire while combat spacing retreats, a projectile reflex suspends guarding and the same guard resumes, guarding passes an intervening hostile without contact, and a projectile over the only exit does not drown a surfacing escape");
        return 0;
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
    /// Attacks while retreating. A wounded companion beside a damageable zombie must take combat spacing,
    /// and on at least one tick where spacing owns the feet the hands must still be granted, still aimed
    /// and actually fire. Counting spacing ticks and fired ticks separately would pass on code that only
    /// alternates between retreating and shooting, so the qualifying condition is all of them on one tick.
    /// </summary>
    private static void TheHandsKeepFiringWhileCombatSpacingRetreats()
    {
        var (companion, player) = OpenFloor();
        player.Bottom = new Vector2(80 * 16, 90 * 16);
        companion.NPC.life = 12;
        Hostile(30, NPCID.Zombie, companion.NPC.Bottom + new Vector2(64, 0), damage: 20, life: 400);
        VerifyResponsiveFollowing.AdvanceNative(companion);
        int spacing = 0, fired = 0, firedWhileSpacing = 0, landedAfterSpacing = 0, endedAfterSpacing = 0;
        bool wasSpacing = false;
        for (int tick = 0; tick < 180; tick++)
        {
            Tick(companion);
            var brain = companion.Brain;
            bool isSpacing = brain.Safety.Active && brain.Safety.Kind == "combat-spacing";
            bool shot = companion.Arsenal.LastFireOutcome == "fired";
            spacing += isSpacing ? 1 : 0;
            fired += shot ? 1 : 0;
            if (isSpacing && shot && brain.EngageTarget?.whoAmI == 30 && brain.ControlGrants.Last?.Hand == HandGrant.Available)
                firedWhileSpacing++;
            if (wasSpacing && !brain.Safety.Active && brain.Safety.LastEndReason == "safe-state-observed")
            {
                endedAfterSpacing++;
                if (companion.Motor.State.OnGround && !companion.NPC.wet) landedAfterSpacing++;
            }
            wasSpacing = isSpacing;
            VerifyResponsiveFollowing.AdvanceNative(companion);
        }
        Console.WriteLine($"  retreat rows: spacing ticks {spacing}, fired ticks {fired}, fired while spacing {firedWhileSpacing}, spacing ended safe {endedAfterSpacing} (on dry ground {landedAfterSpacing})");
        Require(spacing > 0, $"a companion at twelve life beside a twenty-damage zombie must take combat spacing, or the row proves nothing; spacing={spacing}");
        Require(firedWhileSpacing > 0,
            $"on some tick where combat spacing owns the feet the hands must be granted, aimed at the zombie and fire; spacing={spacing} fired={fired}");
        Require(landedAfterSpacing == endedAfterSpacing,
            $"spacing that ends as safe must leave the body on dry ground; ended={endedAfterSpacing} on-ground={landedAfterSpacing}");
    }

    /// <summary>
    /// J12. Guarding is chosen against a zombie beside the player; then a hostile shot is placed on a
    /// collision course with the companion. That tick must carry exactly one control grant owned by the
    /// reflex, with the hands still granted, and guarding must be suspended rather than replaced — the
    /// same executor and the same activity identity. When the shot is gone, the same guard activity must
    /// execute again. Whether the dodge succeeds is deliberately not asserted: collision avoidance's
    /// movement is the shared movement system's, and its dry-floor dodge is a separate reported defect.
    /// </summary>
    private static void AReflexSuspendsGuardingAndGuardingResumesAsTheSameActivity()
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
            $"the reflex scene must first be guarding, or suspension proves nothing; current={chooser.Current?.Name} phase={chooser.Activity.Phase}");
        var guard = chooser.Current;
        long id = chooser.Activity.Id;

        Projectile shot = HostileShot(companion.NPC.Center - new Vector2(80, 0), new Vector2(8, 0));
        Tick(companion);
        var grant = companion.Brain.ControlGrants.Last!.Value;
        var safety = companion.Brain.Safety;
        Console.WriteLine($"  reflex row: observed shots {companion.Brain.Senses.Projectiles.Threats.Count}, safety {safety.Kind}/{safety.Reason}, owner {grant.AppliedOwner}, hand {grant.Hand}, current {chooser.Current?.Name}#{chooser.Activity.Id} phase {chooser.Activity.Phase}");
        Require(safety.Kind == "collision-avoidance" && safety.Reason == "predicted-collision",
            $"a shot ten ticks from the companion's body must trigger collision avoidance; kind={safety.Kind} reason={safety.Reason}");
        Require(grant.AppliedOwner == "combat-reflex" && grant.Hand == HandGrant.Available,
            $"the reflex tick's one grant must be owned by the reflex with the hands still granted; owner={grant.AppliedOwner} hand={grant.Hand}");
        Require(ReferenceEquals(chooser.Current, guard) && chooser.Activity.Id == id && chooser.Activity.Phase == ActivityPhase.Suspended,
            $"the reflex must suspend guarding, not replace it; current={chooser.Current?.Name}#{chooser.Activity.Id} (was guard#{id}) phase={chooser.Activity.Phase}");

        shot.active = false;
        VerifyResponsiveFollowing.AdvanceNative(companion);
        int resumedAt = -1;
        for (int tick = 0; tick < 90 && resumedAt < 0; tick++)
        {
            Tick(companion);
            if (!companion.Brain.Safety.Active && chooser.Activity.Phase == ActivityPhase.Executing) resumedAt = tick;
            VerifyResponsiveFollowing.AdvanceNative(companion);
        }
        Console.WriteLine($"  reflex aftermath: resumed after {resumedAt} ticks as {chooser.Current?.Name}#{chooser.Activity.Id}");
        Require(resumedAt >= 0 && ReferenceEquals(chooser.Current, guard) && chooser.Activity.Id == id,
            $"once the shot is gone the same guard activity must execute again; resumedAt={resumedAt} current={chooser.Current?.Name}#{chooser.Activity.Id} (was guard#{id})");
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

    private static (int SurfacedAt, int Life, int Breath, Vector2 AirCenter, string Kinds) RunPool(Vector2? shotAt)
    {
        VerifyCapturedEscape.BuildCapturedPool();
        for (int i = 0; i < Main.projectile.Length; i++) Main.projectile[i] ??= new Projectile();
        foreach (Projectile projectile in Main.projectile) projectile.active = false;
        var companion = VerifyCompanionLifecycle.Create();
        companion.Brain.Chooser.Actions.Clear();
        Main.player[0].dead = false;
        Main.player[0].Bottom = new Vector2(2024, 1376);
        companion.NPC.position = new Vector2(1356, 2016 - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero; companion.NPC.wet = true; companion.NPC.active = true;
        typeof(live::AICompanion.Companion.CharacterBody.CompanionBreath).GetProperty("Breath")!.SetValue(companion.Breath, 40);
        if (shotAt is Vector2 at) HostileShot(at, Vector2.Zero);
        var kinds = new SortedSet<string>();
        int dry = 0;
        Vector2 firstAir = Vector2.Zero;
        for (int tick = 0; tick < 630; tick++)
        {
            Tick(companion);
            kinds.Add($"{companion.Brain.Safety.Kind}/{companion.Brain.Safety.Reason}");
            VerifyResponsiveFollowing.AdvanceNative(companion);
            bool air = !Collision.DrownCollision(companion.NPC.position, companion.NPC.width, companion.NPC.height, 1f);
            if (air && dry == 0) firstAir = companion.NPC.Center;
            dry = air ? dry + 1 : 0;
            if (companion.IsDowned || companion.NPC.life <= 0) return (-1, companion.NPC.life, companion.Breath.Breath, firstAir, string.Join(",", kinds));
            if (dry >= 30) return (tick, companion.NPC.life, companion.Breath.Breath, firstAir, string.Join(",", kinds));
        }
        return (-1, companion.NPC.life, companion.Breath.Breath, firstAir, string.Join(",", kinds));
    }

    /// <summary>
    /// C02. The captured pool's full-brain escape is run once as recorded to find where the body first
    /// breaks the surface, and again with a stationary hostile shot hanging at that point. Every state the
    /// escape search considers is vetoed where the shot's predicted box meets the body, so a shot over the
    /// only exit could hold the escape pending while breath runs out. The pass is the escape still reaching
    /// sustained air with life left; which way it chose to pay — a hit, a longer route, or drowning damage
    /// — is printed rather than asserted, because the priced trade between those costs is the open question.
    /// </summary>
    private static void SurfacingIsNotVetoedIntoDrowningByAProjectileAboveTheWater()
    {
        var baseline = RunPool(null);
        Require(baseline.SurfacedAt >= 0, $"the captured pool must surface without pressure, or the pressure row proves nothing; {baseline}");
        var pressured = RunPool(baseline.AirCenter);
        Console.WriteLine($"  surfacing rows: baseline {baseline}, shot over exit {pressured}");
        Require(pressured.SurfacedAt >= 0 && pressured.Life > 0,
            $"a projectile hanging over the surfacing point must not hold the escape under water until it fails; baseline={baseline} pressured={pressured}");
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
            controls.Add($"{companion.Brain.Senses.Projectiles.Threats.Count}{companion.Brain.Safety.Kind}:{companion.Motor.AppliedControls.MoveX:0.#}{(companion.Motor.AppliedControls.Jump ? "J" : "")}");
            VerifyResponsiveFollowing.AdvanceNative(companion);
            arrow.position += arrow.velocity;
            if (arrow.Hitbox.Intersects(companion.NPC.Hitbox)) hitAt = tick;
        }
        Console.WriteLine($"dodge reproduction: {(hitAt >= 0 ? $"HIT at tick {hitAt}" : "dodged")}; controls {string.Join(" ", controls)}");
        return hitAt >= 0 ? 1 : 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("safety aftermath: " + message);
    }
}
