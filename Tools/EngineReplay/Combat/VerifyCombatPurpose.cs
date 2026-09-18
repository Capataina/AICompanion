extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Policy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using LineOfSight = live::AICompanion.Companion.Brain.Infrastructure.Observation.LineOfSight;
using Combat = live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// Proposal 1's P08 acceptance scenes for purposeful combat, each a matched pair or matrix that
/// differs in one named input and asserts the scene threatens who it claims to before reading any
/// outcome. Enemy AI never runs; hostiles are placed and held, so these establish how the brain values
/// and responds to a stated arrangement, not how a live fight unfolds.
/// </summary>
internal static class VerifyCombatPurpose
{
    public static int Run()
    {
        ThreatConsequenceCountsEffectiveDamageAgainstRemainingLife();
        PursuitWeighsARepositionAgainstTheShotsItDelays();
        ProtectionIsWorthTheHarmAnInterventionCanRemove();
        ProtectionCountsTheTimeToReachAFiringPosition();
        LandedHitsAreRecordedApartFromTheAimedTarget();
        TheRecordCarriesPursuitAimAndHitApart();
        VerifyEncounterContext.Run();
        VerifyCombatActorMatrix.Run();
        // Last deliberately, and it is the only row here whose position is chosen rather than incidental.
        // This fixture aborts on its first throw. This row once went red on a brain finding — it wanted a
        // companion at twelve life to create space from a slime — and standing second it took the seven rows
        // below it with it every run, two of which turned out to be carrying walker-era defects of their own
        // once they could be seen. A row that can go red on the brain goes last, so that what it reports is
        // the only thing it hides.
        TheSameSmallAttackWeighsMoreAtLowHealthWithoutTakingTheBody();
        Console.WriteLine("combat purpose: effective damage and remaining life decide threat consequence, low health reads more danger without taking the body, pursuit weighs a reposition against the shots it delays, protection is worth only the harm an intervention can remove, plan, aim and landed-hit identities are recorded apart, and a boss or world event stops optional work only where it reaches, once, from native facts or observed pressure");
        return 0;
    }

    /// <summary>
    /// Plan target, aim target and landed hit are three facts. A real shot leaves the hand under a searched
    /// plan aimed at one of two zombies; the native hit hook then reports the projectile striking the other
    /// one — the piercing or blocking case — and then the aimed one. The ledger must name the struck NPC and
    /// the aimed one separately each time, ignore a projectile the companion never fired, and forget the
    /// companion's shot once a new projectile spawns into its slot, so slot reuse cannot inherit it.
    /// </summary>
    private static void LandedHitsAreRecordedApartFromTheAimedTarget()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 89));
        Main.tile[25, 89].ClearEverything();
        ctx.Player.Bottom = ctx.Npc.Bottom - new Vector2(32, 0);
        void Hostile(int slot, float offset)
        {
            NPC npc = Main.npc[slot];
            npc.SetDefaults(NPCID.Zombie);
            npc.whoAmI = slot; npc.active = true; npc.velocity = Vector2.Zero;
            npc.Bottom = ctx.Npc.Bottom + new Vector2(offset, 0);
        }
        Hostile(30, 200f);
        Hostile(31, 110f);
        live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Clear();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        CombatFixture.FiredUse fired = CombatFixture.FireOnce(ctx.Companion, ctx);
        Require(fired.Fired && fired.Plan != null, "the identity scene needs a real shot under a searched plan");
        NPC aimed = Main.npc[fired.Plan!.PrimaryTarget];
        Projectile[] airborne = Main.projectile.Where(p => p.active).ToArray();
        Require(airborne.Length == 1, $"exactly one projectile must be in the air after the shot; active={airborne.Length}");
        Projectile shot = airborne[0];
        NPC other = aimed!.whoAmI == 30 ? Main.npc[31] : Main.npc[30];

        var hook = new live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ObserveLandedCompanionHits();
        hook.OnHitByProjectile(other, shot, new NPC.HitInfo { Damage = 7 }, 7);
        var first = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Last;
        Require(first is { } strayHit && strayHit.HitSlot == other.whoAmI && strayHit.AimSlot == aimed.whoAmI
            && !strayHit.StruckAimedTarget && strayHit.Damage == 7,
            $"a shot landing on the enemy in front must record that enemy as hit and the chosen one as aimed; got {first}");
        hook.OnHitByProjectile(aimed, shot, new NPC.HitInfo { Damage = 9 }, 9);
        Require(live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Last is { StruckAimedTarget: true, Damage: 9 }
            && live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Count == 2,
            $"the same shot reaching its aimed enemy must record a hit on the aimed target; got {(live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Last)}, count={(live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Count)}");

        int foreignSlot = shot.whoAmI == 0 ? 1 : 0;
        Main.projectile[foreignSlot] = new Projectile { whoAmI = foreignSlot, active = true, friendly = true, damage = 11, owner = Main.myPlayer };
        hook.OnHitByProjectile(aimed, Main.projectile[foreignSlot], new NPC.HitInfo { Damage = 11 }, 11);
        Require(live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Count == 2,
            "a projectile the companion never fired must not be attributed to it, even when it is the player's own");

        new live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ForgetReusedShotSlots().OnSpawn(shot, new Terraria.DataStructures.EntitySource_Misc("engine-replay"));
        hook.OnHitByProjectile(aimed, shot, new NPC.HitInfo { Damage = 13 }, 13);
        Require(live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Count == 2,
            "a new projectile spawned into the shot's slot must not inherit the companion's shot");
        Main.projectile[foreignSlot].active = false;
        shot.active = false;
        live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Clear();
    }

    /// <summary>
    /// The appended identity columns carry the brain's own facts on a real recorded row. One shot is fired
    /// under a searched plan and its landing reported through the native hook, the whole brain then runs one
    /// recorded tick, and the row is read by column name — as every reader must, because columns are appended —
    /// against the offered plan, the hands and the ledger as they stand after that tick.
    /// </summary>
    private static void TheRecordCarriesPursuitAimAndHitApart()
    {
        var savePath = typeof(Terraria.Program).GetField("SavePath", System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
        object? priorSavePath = savePath.GetValue(null);
        string root = Path.Combine(Path.GetTempPath(), "aic-combat-identities-" + Guid.NewGuid().ToString("N"));
        savePath.SetValue(null, root);
        var config = ModContent.GetInstance<live::AICompanion.Companion.DiagnosticsConfiguration.CompanionDiagnosticsConfig>();
        if (config == null)
        {
            config = new live::AICompanion.Companion.DiagnosticsConfiguration.CompanionDiagnosticsConfig();
            ContentInstance.Register(config);
        }
        // A recorded row with a hostile present writes `NPC.TypeName` into the target, top-threat and
        // engage columns, which reads Lang's NPC name cache; a headless process never loaded localisation,
        // so every entry is null and the writer would throw. Empty names are supplied for exactly the null
        // entries and those are restored to null afterwards, so no later fixture inherits them.
        var nameCache = (Terraria.Localization.LocalizedText[])typeof(Lang)
            .GetField("_npcNameCache", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var filledNames = new List<int>();
        for (int type = 0; type < nameCache.Length; type++)
            if (nameCache[type] == null) { nameCache[type] = Terraria.Localization.LocalizedText.Empty; filledNames.Add(type); }
        try
        {
            config.RecordTelemetry = true;
            config.OnChanged();
            var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 89));
            Main.tile[25, 89].ClearEverything();
            ctx.Player.Bottom = ctx.Npc.Bottom - new Vector2(32, 0);
            NPC enemy = Main.npc[30];
            enemy.SetDefaults(NPCID.Zombie);
            enemy.whoAmI = 30; enemy.active = true; enemy.velocity = Vector2.Zero;
            enemy.Bottom = ctx.Npc.Bottom + new Vector2(200, 0);
            live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Clear();
            ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
            CombatFixture.FiredUse fired = CombatFixture.FireOnce(ctx.Companion, ctx);
            Require(fired.Fired && fired.Plan != null, "the recorded identity scene needs a real shot under a searched plan");
            Projectile shot = Main.projectile.First(p => p.active);
            new live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ObserveLandedCompanionHits().OnHitByProjectile(enemy, shot, new NPC.HitInfo { Damage = 5 }, 5);

            var recorder = new live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry();
            VerifyObservationLifecycle.Attach(recorder);
            recorder.OnWorldLoad();
            string path = Directory.GetFiles(live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry.Folder, "*.tsv")
                .OrderByDescending(File.GetLastWriteTimeUtc).First();
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
            recorder.OnWorldUnload();

            string[] lines = File.ReadAllLines(path);
            int header = Array.FindIndex(lines, l => l.StartsWith("tick\t"));
            Require(header >= 0 && header + 1 < lines.Length, "the recorder wrote no row for the identity scene");
            string[] names = lines[header].Split('\t'), values = lines[header + 1].Split('\t');
            Require(names.Length == values.Length, $"identity row and header widths disagree: {names.Length}/{values.Length}");
            string[] appended = { "plan_id", "plan_segment", "plan_stand", "plan_targets", "plan_dps", "plan_travel",
                "plan_uses", "aim_target", "landed_hit_target", "landed_hit_aimed", "landed_hit_damage",
                "landed_hit_tick", "landed_hits", "plan_kill_tick", "plan_threat_removed",
                "top_threat_effective_player", "top_threat_effective_companion", "plan_invalid" };
            foreach (string name in appended)
                Require(Array.IndexOf(names, name) >= 0, "identity column missing from the recording: " + name);
            string declaration = lines.First(l => l.StartsWith("# text_columns="));
            foreach (string text in new[] { "plan_stand", "plan_targets", "plan_uses", "aim_target", "landed_hit_target", "landed_hit_aimed", "plan_invalid" })
                Require(declaration.Split('=')[1].Split(',').Contains(text), "textual identity column not declared as text: " + text);
            string Value(string name) => values[Array.IndexOf(names, name)];
            string Identity(NPC? npc) => npc != null && npc.active
                ? $"{npc.whoAmI}:{(live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.Generation(npc))}" : "-";
            var combat = ctx.Companion.Brain.Chooser.Actions.OfType<Combat>().Single();
            var landed = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Last;
            Require(landed is { } hit && Value("landed_hit_target") == $"{hit.HitSlot}:{hit.HitGeneration}"
                && Value("landed_hit_aimed") == $"{hit.AimSlot}:{hit.AimGeneration}" && Value("landed_hit_damage") == "5"
                && Value("landed_hits") == "1",
                $"the recorded landed hit must be the ledger's; row={Value("landed_hit_target")}/{Value("landed_hit_aimed")}/{Value("landed_hit_damage")}/{Value("landed_hits")}, ledger={landed}");
            Require(Value("aim_target") == Identity(ctx.Companion.Brain.EngageTarget),
                $"the recorded aim must be the hands' target; row={Value("aim_target")}, hands={Identity(ctx.Companion.Brain.EngageTarget)}");
            var plan = combat.OfferedPlan;
            Require(plan != null, "the recorded identity scene needs the tick to offer a plan");
            Require(Value("plan_id") == plan!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                && Value("plan_segment") == combat.OfferedSegment.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"the recorded plan must be the offered one; row={Value("plan_id")}/{Value("plan_segment")}, combat={plan.Id}/{combat.OfferedSegment}");
            Vector2 stand = plan.Segments[combat.OfferedSegment].Stand.Stand;
            string expectedStand = FormattableString.Invariant($"{stand.X:0},{stand.Y:0}");
            string expectedTargets = string.Join("|", plan.Validity.Targets.Select(t =>
                FormattableString.Invariant($"{t.Slot}:{t.Generation}")));
            if (expectedTargets.Length == 0) expectedTargets = "-";
            var segment = plan.Segments[combat.OfferedSegment];
            string expectedUses = segment.Uses.Length == 0 ? "-" : string.Join("|", segment.Uses.Select(use =>
                FormattableString.Invariant($"{use.WeaponSlot}>{use.TargetSlot}@{use.FireTick}")));
            Require(Value("plan_stand") == expectedStand && Value("plan_targets") == expectedTargets && Value("plan_uses") == expectedUses,
                $"the recorded stand, targets and uses must be the offered segment's; row={Value("plan_stand")}/{Value("plan_targets")}/{Value("plan_uses")}");
            Require(Value("plan_travel") == segment.Verdict.TravelTicks.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                && Value("plan_dps") == plan.Outcome.DamagePerSecond.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                && Value("plan_threat_removed") == plan.Outcome.ThreatRemoved.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                $"the recorded plan numbers must be the offered outcome's; row={Value("plan_travel")}/{Value("plan_dps")}/{Value("plan_threat_removed")}");
            string expectedKill = "-1";
            // The plan's kill ticks run on the senses clock, not the engine counter the tick column
            // carries; the recorded value is ticks-from-the-row either way, but the expectation must
            // subtract the same base the recorder did. No tick runs between the recording and this
            // read, so the live senses tick is the recording's.
            int rowTick = ctx.Companion.Brain.Senses.Tick;
            if (plan.TargetKillTicks != null)
                foreach ((int slot, int at) in plan.TargetKillTicks)
                    if (slot == plan.PrimaryTarget)
                        expectedKill = Math.Max(0, at - rowTick).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Require(Value("plan_kill_tick") == expectedKill,
                $"the recorded kill tick must be the plan's predicted kill of its primary; row={Value("plan_kill_tick")}, expected={expectedKill}");
            Require(Value("plan_invalid") == ctx.Companion.Combat.Planner.LastInvalidation,
                $"the recorded invalidation must be the planner's; row={Value("plan_invalid")}");
            Main.projectile.Where(p => p.active).ToList().ForEach(p => p.active = false);
            live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Clear();
        }
        finally
        {
            foreach (int type in filledNames) nameCache[type] = null!;
            config.RecordTelemetry = false;
            config.OnChanged();
            typeof(live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry)
                .GetMethod("Close", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.Invoke(null, new object[] { "fixture-close" });
            savePath.SetValue(null, priorSavePath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private readonly record struct GuardScene(float Guard, float Urgency, float ThreatRemoved, float KillIn, bool Offered);

    /// <summary>
    /// One hostile beside the player, with the companion twelve tiles down the same open floor. Only the
    /// hostile differs between rows — its type, or for the matched pair only its life — so a change in the
    /// fight's value is the fight's length acting through what the plan removes. Enemy AI does not run;
    /// this values the arrangement, it does not stage a fight. Twelve tiles, not thirty-seven: a use fires
    /// when the hands are ready and the body has arrived, so from the ore setup's tile the flight alone
    /// leaves the killing blow past the window and the ordinary zombie survives what the row claims it dies
    /// to — honestly, because the plan cannot fire before it arrives. At twelve the bow kills it at the
    /// fifth landing with room to spare, and the tank still survives by two orders of magnitude.
    /// </summary>
    private static GuardScene GuardAgainst(int type, int life = 0)
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 89));
        Main.tile[25, 89].ClearEverything();
        ctx.Player.Bottom = new Vector2(60 * 16, 90 * 16);
        ctx.Player.DefenseEffectiveness = MultipliableFloat.One * .5f;
        ctx.Npc.Bottom = new Vector2(45 * 16, 90 * 16);
        // The scene owns the threat list: every hostile slot starts inactive, so earlier scenes' zombies
        // are never scanned in beside the planted one.
        for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
        NPC enemy = Main.npc[30];
        enemy.SetDefaults(type);
        enemy.whoAmI = 30; enemy.active = true; enemy.velocity = Vector2.Zero;
        if (life > 0) { enemy.lifeMax = life; enemy.life = life; }
        enemy.Bottom = ctx.Player.Bottom - new Vector2(48, 0);
        var brain = ctx.Companion.Brain;
        brain.Senses.Update(ctx.Npc, ctx.Player);
        var threat = brain.Senses.Threats.Threats.Find(t => t.Npc == enemy);
        Require(threat != null && threat.CanReachPlayer && threat.Urgency > 0f,
            $"the guard scene's hostile (type {type}) must threaten the player before protection is read; urgency={threat?.Urgency}");
        brain.Senses.SetInterventionEstimate(ctx.Companion.Combat.EstimateInterventionTicks(ctx));
        var combat = brain.Chooser.Actions.OfType<Combat>().Single();
        PrimeAndSettle(brain, ctx, combat);
        var settled = brain.Senses.Threats.Threats.Find(t => t.Npc == enemy);
        var plan = combat.OfferedPlan;
        float killIn = float.PositiveInfinity;
        if (plan?.TargetKillTicks != null)
            foreach ((int slot, int at) in plan.TargetKillTicks)
                if (slot == enemy.whoAmI)
                    killIn = at - plan.Validity.LastProgressTick;
        return new(combat.Score(), settled?.Urgency ?? 0f, plan?.Outcome.ThreatRemoved ?? 0f, killIn,
            combat.Eligibility == live::AICompanion.Companion.Brain.Activities.OfferEligibility.Usable);
    }

    /// <summary>
    /// Settles the reach flood before preparing, so the offer is the search's finished answer rather than
    /// what the first flood slice happened to reach. Preparing on a growing flood reads whatever is reachable
    /// *so far* — usually the shot from here — and a row comparing values across scenes would then compare
    /// flood budgets, not fights. The resolves only pump the flood; the threat list, the urgency and the
    /// terrain stay as the scene built them, where a fresh `Senses.Update` would rebuild them.
    /// </summary>
    private static void PrimeAndSettle(live::AICompanion.Companion.Brain.Brain brain,
        live::AICompanion.Companion.Brain.Activities.ActionContext ctx, Combat combat)
    {
        // WithPlayer, which walks the scored path and refreshes the flood on the way; the combat
        // stance's own FireFrom only holds a point and never grows anything.
        var request = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer, ctx.Player.Bottom);
        // The flood is bounded per advance and grows across resolves: three thousand, the count the
        // assistance matrix uses, because four hundred no longer completes it. Nothing else about the scene
        // moves — the threat list, the urgency and the terrain stay as the scene built them.
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
            brain.Positioner.Resolve(request, brain.Senses);
        Require(brain.Positioner.ReachComplete, "the reach flood must complete before the offer can be read as the search's answer");
        VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(combat.Eligibility != live::AICompanion.Companion.Brain.Activities.OfferEligibility.Unresolved,
            $"the stand search must decide on a completed flood; still {combat.EligibilityReason}");
    }

    /// <summary>
    /// Protection is worth the harm an intervention can remove. The matched pair holds one zombie beside
    /// the player at its ordinary life and at a hundred times it: the urgency is identical and only the
    /// fight's length differs, so the ordinary zombie dies inside the plan's horizon and the tank survives
    /// it, and guarding the tank is worth less. The length acts through the outcome the evaluator priced —
    /// a nonlinear simulation, not the old linear share — so the pair pins the direction and the kill, not
    /// an exact formula. The second pair is the owner's boss story — against the Eye of Cthulhu there is
    /// no version where standing between it and the player helps, while the small eyes on the player are
    /// things the companion can remove — and it must hold on starting weapons without any boss flag being
    /// read. Combat must still offer the long fight: making protection pointless is not a refusal to fight.
    /// </summary>
    private static void ProtectionIsWorthTheHarmAnInterventionCanRemove()
    {
        // Starting weapons fire at twice their item use time, so a full-life zombie is not a kill
        // inside the three-second horizon; one shot is, and that is the length the pair needs.
        var ordinary = GuardAgainst(NPCID.Zombie, life: 8);
        var tank = GuardAgainst(NPCID.Zombie, life: 4500);
        var eye = GuardAgainst(NPCID.EyeofCthulhu);
        var servant = GuardAgainst(NPCID.ServantofCthulhu);
        foreach (var (name, scene) in new[] { ("ordinary", ordinary), ("tank", tank), ("eye", eye), ("servant", servant) })
            Console.WriteLine($"  guard row {name}: guard={scene.Guard:0.000} urgency={scene.Urgency:0.000} threat-removed={scene.ThreatRemoved:0.000} kill-in={scene.KillIn:0.0} offered={scene.Offered}");

        Require(float.IsFinite(ordinary.KillIn) && float.IsPositiveInfinity(tank.KillIn),
            $"the ordinary zombie must die inside the plan's horizon and the tank must survive it; ordinary={ordinary.KillIn}, tank={tank.KillIn}");
        Require(ordinary.ThreatRemoved > tank.ThreatRemoved,
            $"the same danger over a longer fight must remove less of it; ordinary={ordinary.ThreatRemoved}, tank={tank.ThreatRemoved}");
        Require(MathF.Abs(tank.Urgency - ordinary.Urgency) < 1e-5f,
            $"the matched pair must threaten the player identically; tank={tank.Urgency}, ordinary={ordinary.Urgency}");
        Require(tank.Guard < ordinary.Guard,
            $"a threat the weapons would need far longer to remove must be worth less protection than the same threat they can remove; tank={tank.Guard}, ordinary={ordinary.Guard}");
        Require(servant.Guard > eye.Guard,
            $"the small eyes on the player must be worth more protection than the boss itself; servant={servant.Guard}, eye={eye.Guard}");
        Require(tank.Offered && eye.Offered,
            $"a fight too long to protect through must still be a fight combat offers; tank={tank.Offered}, eye={eye.Offered}");
    }

    private enum ShaftAccess { Sealed, Lip, InSight }

    private readonly record struct ShaftGuard(float Guard, float Weighted, bool Offered, string Reason, float Travel, bool SolvesFromHere,
        float ThreatRemoved, float KillIn, float PlayerUrgency, float ProtectionUrgency, bool ReachComplete, int Target, string Funnel);

    // The guarded zombie is one shot from dead in every row, so both open rows end the same kill and
    // differ only in when — the reposition priced as pure delay. At ordinary life the "shot from here" is a
    // grazing knife throw the plan cannot repeat, while the lip drops cleanly down the shaft, so the rows
    // differed in shot quality rather than access and the lip won. One hit kills either way, so quality drops
    // out and only the flight remains. The old rows needed 600 life for the share formula's saturation; the
    // evaluator prices time linearly and saturates nothing, so the premise is gone with the formula.
    private const int ShaftGuardPlayerX = 58;

    /// <summary>
    /// The shaft geometry with one zombie on its floor that passes through rock and hits hard, so it threatens the
    /// player beside the shaft in every row, and the companion three tiles away on the open floor. Rows differ
    /// only in how the companion can shoot it: the mouth capped so no reachable tile has a line (a proven
    /// absence once the flood is settled), the mouth open so the lip is a short walk, or the rock beside the
    /// shaft opened so a weapon solves from where the companion stands.
    /// </summary>
    private static ShaftGuard GuardShaft(ShaftAccess access)
    {
        Main.maxTilesX = Main.maxTilesY = 120;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, null, new object[] { (ushort)120, (ushort)120 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = PitFloorY; y <= ShaftFloorY + 2; y++) { Tile rock = Main.tile[x, y]; rock.HasTile = true; rock.TileType = 1; }
        // The capped shaft keeps its mouth row solid, so the floor is continuous and the cavity below is sealed.
        for (int x = ShaftLeft; x <= ShaftRight; x++)
            for (int y = access == ShaftAccess.Sealed ? PitFloorY + 1 : PitFloorY; y < ShaftFloorY; y++) { Tile air = Main.tile[x, y]; air.HasTile = false; }
        // A trench from under the companion to the shaft, not a slot: a three-wide cut lets one grazing arc
        // through while the plan's other aims clip its edges, so the "shot from here" removed a tenth of the
        // zombie and read as worthless beside the lip's clean drop. The row compares access, not aim quality,
        // so the opened line must shoot as cleanly as the lip it is measured against.
        if (access == ShaftAccess.InSight)
            for (int x = ShaftLeft - 5; x < ShaftLeft; x++)
                for (int y = PitFloorY; y < ShaftFloorY; y++) { Tile air = Main.tile[x, y]; air.HasTile = false; }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();

        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2;
        player.DefenseEffectiveness = MultipliableFloat.One * .5f;
        player.velocity = Vector2.Zero;
        // Across the shaft from the companion, not beside it as in the pursuit rows: the in-sight opening is cut on
        // the companion's side, and cut between the zombie and a player standing there it doubled the zombie's
        // urgency to the player (0.295 to 0.591 on the first run), which the danger premise below refused.
        player.position = new Vector2(ShaftGuardPlayerX * 16f, PitFloorY * 16f - player.height);
        // The orb hovers a tile above the floor in all three shaft-guard scenes, because an orb
        // sitting on the floor cannot shoot steeply down past the edge of its own floor tile (the
        // arrow's hitbox clips that tile before it clears the opened rock), and a hovering body is
        // what positioning gives it; the three scenes share the height so their danger premise holds.
        companion.NPC.position = new Vector2(NearStart * 16f, PitFloorY * 16f - companion.NPC.height - 16f);
        companion.NPC.velocity = Vector2.Zero;

        // The scene owns the threat list: every hostile slot starts inactive, so earlier scenes' zombies
        // are never scanned in beside the planted one.
        for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
        NPC hidden = Main.npc[HiddenSlot];
        hidden.SetDefaults(NPCID.Zombie);
        hidden.whoAmI = HiddenSlot; hidden.active = true; hidden.velocity = Vector2.Zero;
        hidden.life = Math.Max(1, hidden.lifeMax / 20);
        hidden.noTileCollide = true;
        // It flies as well as phases: the forecast integrates the NPC's own gravity, so a phasing walker
        // with gravity is predicted falling through the floor it no longer collides with, and a use aimed
        // at its arrival position aims into rock and never solves.
        hidden.noGravity = true;
        hidden.damage = 100;
        hidden.Bottom = new Vector2(HiddenX * 16f + 8f, ShaftFloorY * 16f);

        var brain = companion.Brain;
        brain.Senses.Update(companion.NPC, player);
        var ctx = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, brain.Senses);
        var threat = brain.Senses.Threats.Threats.Find(t => t.Npc == hidden);
        Require(threat != null && threat.CanReachPlayer && threat.Urgency > 0f,
            $"shaft guard {access}: the zombie must threaten the player before protection is read; urgency={threat?.Urgency}");
        brain.Senses.SetInterventionEstimate(companion.Combat.EstimateInterventionTicks(ctx));
        var combat = brain.Chooser.Actions.OfType<Combat>().Single();
        // Settled, so a proven absence is the flood's finished answer rather than its budget.
        PrimeAndSettle(brain, ctx, combat);
        var plan = combat.OfferedPlan;
        float killIn = float.PositiveInfinity;
        if (plan?.TargetKillTicks != null)
            foreach ((int slot, int at) in plan.TargetKillTicks)
                if (slot == hidden.whoAmI)
                    killIn = at - plan.Validity.LastProgressTick;
        var settled = brain.Senses.Threats.Threats.Find(t => t.Npc == hidden);
        Vector2 muzzle = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.CompanionCombat.Muzzle(companion.NPC);
        return new(combat.Score(), plan?.Weighted ?? 0f,
            combat.Eligibility == live::AICompanion.Companion.Brain.Activities.OfferEligibility.Usable,
            combat.EligibilityReason,
            plan?.Current(brain.Senses.Tick).Verdict.TravelTicks ?? float.NaN,
            companion.Combat.ShotSolves(ctx, muzzle, hidden),
            plan?.Outcome.ThreatRemoved ?? 0f, killIn,
            settled?.Urgency ?? 0f, brain.Senses.Threats.ProtectionUrgency, brain.Positioner.ReachComplete,
            plan?.PrimaryTarget ?? -1, combat.Funnel.Describe());
    }

    /// <summary>
    /// Proposal 1's P03 on protection: an intervention's time includes getting to where it can be made. One threat
    /// on the player, an ordinary zombie both open rows kill, and three ways of shooting it. A shaft capped so
    /// nothing the companion can reach has a line is refused as a proven absence, and the funnel names the zombie
    /// it was about; a lip a short flight away ends the same kill later than a shot from here, so it is worth
    /// protecting and worth less. The danger is required identical across the open rows, because opening terrain
    /// can change what the zombie threatens and that must not pass for access.
    /// </summary>
    private static void ProtectionCountsTheTimeToReachAFiringPosition()
    {
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        ShaftGuard sealedShaft, lip, inSight;
        try
        {
            sealedShaft = GuardShaft(ShaftAccess.Sealed);
            lip = GuardShaft(ShaftAccess.Lip);
            inSight = GuardShaft(ShaftAccess.InSight);
        }
        finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false; }
        foreach (var (name, scene) in new[] { ("sealed", sealedShaft), ("lip", lip), ("in-sight", inSight) })
            Console.WriteLine($"  guard access row {name}: guard={scene.Guard:0.0000} offered={scene.Offered} reason={scene.Reason} target={scene.Target} travel={scene.Travel:0.0} solves-here={scene.SolvesFromHere} threat-removed={scene.ThreatRemoved:0.000} kill-in={scene.KillIn:0.0} player-urgency={scene.PlayerUrgency:0.000} protection-urgency={scene.ProtectionUrgency:0.000} reach-complete={scene.ReachComplete} funnel={scene.Funnel}");

        Require(!sealedShaft.Offered && sealedShaft.ReachComplete,
            $"shaft guard sealed: the capped shaft must be refused from a settled flood, or a zero proves nothing; reason={sealedShaft.Reason}, reach-complete={sealedShaft.ReachComplete}");
        Require(sealedShaft.Funnel.Contains($"npc{HiddenSlot}:", StringComparison.Ordinal) && sealedShaft.Funnel.Contains("unplannable", StringComparison.Ordinal),
            $"shaft guard sealed: the funnel must name the sealed zombie as refused for want of a stand; funnel={sealedShaft.Funnel}");
        Require(lip.Offered && lip.Target == HiddenSlot && float.IsFinite(lip.Travel) && lip.Travel > 0f && !lip.SolvesFromHere,
            $"shaft guard lip: the open shaft must be a finite flight to a stand that shoots down it; offered={lip.Offered} target={lip.Target} travel={lip.Travel} solves-here={lip.SolvesFromHere} reason={lip.Reason}");
        Require(inSight.Offered && inSight.Target == HiddenSlot && inSight.SolvesFromHere && inSight.Travel < lip.Travel,
            $"shaft guard in-sight: the opened rock must let a weapon solve from where the companion hovers, sooner than the lip's flight; offered={inSight.Offered} target={inSight.Target} solves-here={inSight.SolvesFromHere} travel={inSight.Travel} reason={inSight.Reason}");
        Require(float.IsFinite(lip.KillIn) && float.IsFinite(inSight.KillIn) && lip.KillIn > inSight.KillIn
            && lip.ThreatRemoved > 0.2f && inSight.ThreatRemoved > 0.2f,
            $"shaft guard: both open rows must end the same fight, the lip's kill later by its flight; lip kill={lip.KillIn} removed={lip.ThreatRemoved}, in-sight kill={inSight.KillIn} removed={inSight.ThreatRemoved}");
        Require(MathF.Abs(lip.PlayerUrgency - inSight.PlayerUrgency) < 1e-4f,
            $"shaft guard: the danger must be identical with the lip and in sight, or the pair measures danger rather than access; lip={lip.PlayerUrgency}, in-sight={inSight.PlayerUrgency}");

        Require(sealedShaft.Guard == 0f,
            $"a threat no reachable position can shoot must be worth no protection; guard={sealedShaft.Guard}, reason={sealedShaft.Reason}");
        // The ordering is read on the plans' unclamped value, not the offers: both plans saturate the
        // offer's clamp, so the chooser sees two maxed fights and the better-priced one only in the plans.
        Require(lip.Guard > 0f && inSight.Guard > 0f && lip.Weighted < inSight.Weighted,
            $"a threat the companion must fly to the lip to shoot must be worth protecting, and less than the same threat it can shoot now; lip={lip.Guard}/{lip.Weighted}, in-sight={inSight.Guard}/{inSight.Weighted}");
    }

    private const int PitFloorY = 80, ShaftLeft = 49, ShaftRight = 53, ShaftFloorY = 86, HiddenX = 51;
    private const int HiddenSlot = 30, VisibleSlot = 31;
    // The player stays beside the shaft in every row, so the hidden enemy's threat to the player is
    // identical and only the companion's start — the length of its reposition — changes between them.
    // The starts are placed against how the reachable-region flood prices a walk step, so a change to walk
    // pricing moves every wait; the premise checks in the pursuit rows fail by name when a row stops being
    // the short step, the priced walk inside the arsenal's window or the walk past it that it claims to be.
    private const int PlayerTileX = 44, NearStart = 45, MiddleStart = 22, FarStart = 9;

    // The pursuit scene needs its own world, twice as wide, and the reason is the body rather than the scene.
    // Its three rows are a near, a middle and a far reposition, and the far one exists to price a wait longer
    // than `CompanionCombat.HorizonTicks` — the window the evaluator prices inside — so that an enemy the
    // companion could only reach after it is worth nothing now. A walker reached that wait inside a 120-tile world; the
    // orb flies the same detour at about 3.2 ticks a tile and the old far start priced 137.6 against a horizon
    // of 180, so the row asserted a truncation that no longer happened and the wait it measured sat comfortably
    // inside the window. The distances are the only thing that grew: the shaft, the pit and the hidden enemy
    // keep the shape they had, shifted right so the far start has floor under it. They are separate constants
    // from the ones above because `ProtectionCountsTheTimeToReachAFiringPosition` builds its own world from
    // those, and it is about a guard's access rather than about a wait against the horizon.
    private const int PursuitWorldWidth = 240, PursuitShift = 120;
    private const int PursuitShaftLeft = ShaftLeft + PursuitShift, PursuitShaftRight = ShaftRight + PursuitShift;
    // The player stands at the shaft's own edge rather than beside the near start: the hidden zombie must be
    // the severe threat and the visible one the mild one, and with the player beside the near start the visible
    // zombie's two tiles beat the hidden one's hundred damage in the urgency the planner trusts (0.507 against
    // 0.294), so every row pursued the visible zombie and the pairs tested nothing. At the shaft's edge the
    // hidden enemy is adjacent to the player and genuinely severe, the visible one eight tiles off and mild.
    private const int PursuitHiddenX = HiddenX + PursuitShift, PursuitPlayerX = ShaftLeft + PursuitShift;
    private const int PursuitNearStart = NearStart + PursuitShift, PursuitMiddleStart = MiddleStart + PursuitShift;
    // Sixty-six tiles behind the near start rather than the walker's thirty-six, which is what carries the wait
    // past 180. Sixty priced 212.2 under the three-row sight beam and 176.3 once sight became the single-tile
    // walk, because a thinner line finds a nearer lip to shoot from. It cannot go further: the visible zombie
    // stands three tiles beyond the companion, and the hunt admits a target only inside the new-activity radius
    // of the player (seventy tiles), so at sixty-eight that zombie was refused as a target and the row read
    // "the visible one must be examined", and at seventy-five the companion itself was outside the radius.
    // Since 15 September 2026 the radius is sixty-two and a half tiles and the orb flies three times the player's
    // speed, so no start inside the radius prices a wait past the window; this start is kept as the scene beyond the
    // radius, which is what its row now asserts.
    private const int PursuitFarStart = PursuitNearStart - 66;

    private readonly record struct PursuitScene(int Pursuit, bool SolvesFromHere, float Travel, float Weighted,
        float HiddenDanger, float HiddenPlayerUrgency, string Evidence);

    /// <summary>
    /// A 5%-health zombie on the floor of a narrow shaft the companion cannot see into, and a full-health
    /// zombie three tiles along the open floor in the other direction. Only the lip of the shaft has a line
    /// down it, so the hidden enemy needs a reposition whose length is set by where the companion starts.
    /// The shaft-geometry discrimination is inherited from the firing-position fixture: the near floor is
    /// blind and the lip is sighted. A dangerous hidden enemy hits for a hundred and passes through rock,
    /// so it can reach both actors; a harmless one passes through rock too but hits for one. The difference
    /// matters through prevented harm, which the evaluator prices as expected hits no longer landing, so a
    /// hard hitter is worth a short reposition by a clear margin rather than a sliver a weapon retune could
    /// erase. The hands fire the running plan's uses, so there is no independent aim to diverge from the feet.
    /// </summary>
    /// <param name="hoverTiles">
    /// How far above the floor the orb hovers. The reposition rows sit it on the floor, where their
    /// waits were calibrated; the in-sight row hovers one tile up, because an orb sitting on the
    /// floor cannot shoot steeply down past the edge of its own floor tile — the arrow's hitbox
    /// clips that tile before it clears the cut — and a hovering body is what positioning gives it.
    /// </param>
    private static PursuitScene HuntPair(int companionX, bool hiddenDangerous, bool lineFromHere, int hoverTiles = 0)
    {
        Main.maxTilesX = PursuitWorldWidth;
        Main.maxTilesY = 120;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, null, new object[] { (ushort)PursuitWorldWidth, (ushort)120 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < PursuitWorldWidth - 5; x++)
            for (int y = PitFloorY; y <= ShaftFloorY + 2; y++) { Tile rock = Main.tile[x, y]; rock.HasTile = true; rock.TileType = 1; }
        for (int x = PursuitShaftLeft; x <= PursuitShaftRight; x++)
            for (int y = PitFloorY; y < ShaftFloorY; y++) { Tile air = Main.tile[x, y]; air.HasTile = false; }
        // The opening stops short of the companion's own floor tile, so the body stands where it did.
        if (lineFromHere)
            for (int x = PursuitShaftLeft - 3; x < PursuitShaftLeft; x++)
                for (int y = PitFloorY; y < ShaftFloorY; y++) { Tile air = Main.tile[x, y]; air.HasTile = false; }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();

        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2;
        player.DefenseEffectiveness = MultipliableFloat.One * .5f;
        player.position = new Vector2(PursuitPlayerX * 16f, PitFloorY * 16f - player.height);
        companion.NPC.position = new Vector2(companionX * 16f, PitFloorY * 16f - companion.NPC.height - hoverTiles * 16f);

        // The scene owns the threat list: every hostile slot starts inactive, so earlier scenes' zombies
        // are never scanned in beside the planted pair.
        for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
        NPC hidden = Main.npc[HiddenSlot];
        hidden.SetDefaults(NPCID.Zombie);
        hidden.whoAmI = HiddenSlot; hidden.active = true; hidden.velocity = Vector2.Zero;
        hidden.life = Math.Max(1, hidden.lifeMax / 20);
        hidden.noTileCollide = true;
        // It flies as well as phases: the forecast integrates the NPC's own gravity, so a phasing walker
        // with gravity is predicted falling through the floor it no longer collides with, and a use aimed
        // at its arrival position aims into rock and never solves. The offer-validity fixture phases the
        // same way for the same reason.
        hidden.noGravity = true;
        hidden.damage = hiddenDangerous ? 100 : 1;
        hidden.Bottom = new Vector2(PursuitHiddenX * 16f + 8f, ShaftFloorY * 16f);
        NPC visible = Main.npc[VisibleSlot];
        visible.SetDefaults(NPCID.Zombie);
        visible.whoAmI = VisibleSlot; visible.active = true; visible.velocity = Vector2.Zero;
        // Three tiles away, so nearness and its own danger put the visible zombie first in the threat
        // list's candidate order: a rule that took the first admissible candidate would pursue it in
        // every row, and only a valuation of the reposition can choose the hidden enemy.
        visible.Bottom = new Vector2((companionX - 3) * 16f + 8f, PitFloorY * 16f);

        var brain = companion.Brain;
        brain.Senses.Update(companion.NPC, player);
        var ctx = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, brain.Senses);
        var hiddenThreat = brain.Senses.Threats.Threats.Find(t => t.Npc == hidden);
        var visibleThreat = brain.Senses.Threats.Threats.Find(t => t.Npc == visible);
        Require(hiddenThreat != null && visibleThreat != null, "both zombies must be observed threats before pursuit is read");
        brain.Senses.SetInterventionEstimate(companion.Combat.EstimateInterventionTicks(ctx));
        var combat = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies>().Single();
        PrimeAndSettle(brain, ctx, combat);

        var plan = combat.OfferedPlan;
        var settled = brain.Senses.Threats.Threats.Find(t => t.Npc == hidden);
        Vector2 muzzle = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.CompanionCombat.Muzzle(companion.NPC);
        string threatLedger = string.Join("|", brain.Senses.Threats.Threats.Select(t =>
            FormattableString.Invariant($"{t.Npc.whoAmI}:urgency={t.Urgency:0.000}:reachPlayer={t.CanReachPlayer}:reachCompanion={t.CanReachCompanion}:toCompanion={t.DistanceToCompanion:0}")));
        Console.WriteLine($"  pursuit scene at x={companionX}: pursuit={plan?.PrimaryTarget}, threats={threatLedger}");
        return new(plan?.PrimaryTarget ?? -1, companion.Combat.ShotSolves(ctx, muzzle, hidden),
            plan?.Current(brain.Senses.Tick).Verdict.TravelTicks ?? float.NaN, plan?.Weighted ?? 0f,
            MathF.Max(settled?.Urgency ?? 0f, settled?.UrgencyToCompanion ?? 0f), settled?.Urgency ?? 0f,
            $"offer={combat.Eligibility}/{combat.EligibilityReason} funnel={combat.Funnel.Describe()}");
    }

    /// <summary>
    /// Proposal 1's hidden-5%-versus-visible-100% pairs. The planner chooses stand, weapons, targets and aims
    /// together, and the hands fire the running plan's uses; the question here is which enemy the offered plan
    /// pursues. Five rows, each a matched change of one input from the first: reposition cost, the hidden
    /// enemy's threat, and whether the hidden enemy can be shot from where the companion already stands. The
    /// expected outcomes follow the evaluator's own valuation rather than a rule about low health: a short
    /// step to remove a hard-hitting enemy is worth the arrows it delays, a long walk lowers the value, and
    /// finishing a harmless enemy is not worth an arrow into a healthy one.
    /// </summary>
    private static void PursuitWeighsARepositionAgainstTheShotsItDelays()
    {
        var cheapDangerous = HuntPair(PursuitNearStart, hiddenDangerous: true, lineFromHere: false);
        var middleDangerous = HuntPair(PursuitMiddleStart, hiddenDangerous: true, lineFromHere: false);
        var costlyDangerous = HuntPair(PursuitFarStart, hiddenDangerous: true, lineFromHere: false);
        var cheapHarmless = HuntPair(PursuitNearStart, hiddenDangerous: false, lineFromHere: false);
        var cheapDangerousInSight = HuntPair(PursuitNearStart, hiddenDangerous: true, lineFromHere: true, hoverTiles: 1);

        foreach (var (name, scene) in new[] { ("cheap", cheapDangerous), ("middle", middleDangerous), ("costly", costlyDangerous), ("harmless", cheapHarmless), ("in-sight", cheapDangerousInSight) })
            Console.WriteLine($"  pursuit row {name}: pursuit={scene.Pursuit} solves-here={scene.SolvesFromHere} travel={scene.Travel:0.0} weighted={scene.Weighted:0.000} hidden-danger={scene.HiddenDanger:0.000} hidden-player-urgency={scene.HiddenPlayerUrgency:0.000} candidates={scene.Evidence}");
        foreach (var (name, scene) in new[] { ("cheap", cheapDangerous), ("middle", middleDangerous), ("harmless", cheapHarmless) })
            Require(!scene.SolvesFromHere,
                $"{name}: the hidden enemy must need a reposition, or the row tests nothing; {scene.Evidence}");
        foreach (var (name, scene) in new[] { ("cheap", cheapDangerous), ("middle", middleDangerous) })
            Require(scene.Pursuit == HiddenSlot && float.IsFinite(scene.Travel) && scene.Travel > 0f,
                $"{name}: the dangerous hidden enemy must be pursued by a reachable reposition; pursuit={scene.Pursuit} travel={scene.Travel}; {scene.Evidence}");
        Require(cheapDangerous.Travel < middleDangerous.Travel,
            $"the reposition rows must lengthen the wait in order; cheap={cheapDangerous.Travel}, middle={middleDangerous.Travel}");
        Require(middleDangerous.Travel < live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.CompanionCombat.HorizonTicks
            && middleDangerous.Weighted > 0f,
            $"the middle row must be a priced wait inside the evaluation window, not a second truncation; wait={middleDangerous.Travel}, value={middleDangerous.Weighted}");
        // The costly row used to price a wait longer than the whole evaluation window, so that an enemy behind
        // a long walk was worth nothing now. That case no longer exists in play and the row no longer tests it: on
        // 15 September 2026 the owner put the orb at three times the player's speed and the new-job radius at 1000 px, and
        // the longest reposition that radius admits is priced well inside the window at that speed. What the far start
        // witnesses now is the radius itself: the same dangerous enemy, far enough away, is not examined or pursued.
        Require(costlyDangerous.Pursuit != HiddenSlot && costlyDangerous.Evidence.Contains("activity-allowance", StringComparison.Ordinal),
            $"costly: the same dangerous enemy beyond the new-job radius must not be examined or pursued; pursuit={costlyDangerous.Pursuit}, {costlyDangerous.Evidence}");
        // The companion's own exposure moves with its start, which is the physical cost of standing near
        // an enemy; the threat to the player must not, or the rows would differ in more than the reposition.
        Require(cheapDangerous.HiddenPlayerUrgency > 0f
            && MathF.Abs(cheapDangerous.HiddenPlayerUrgency - middleDangerous.HiddenPlayerUrgency) < 1e-4f
            && MathF.Abs(cheapDangerous.HiddenPlayerUrgency - costlyDangerous.HiddenPlayerUrgency) < 1e-4f,
            $"the reposition rows must hold the hidden enemy's threat to the player fixed; cheap={cheapDangerous.HiddenPlayerUrgency}, middle={middleDangerous.HiddenPlayerUrgency}, costly={costlyDangerous.HiddenPlayerUrgency}");
        Require(cheapDangerous.HiddenDanger > cheapHarmless.HiddenDanger,
            $"the dangerous hidden enemy must threaten more than the harmless one; {cheapDangerous.HiddenDanger} vs {cheapHarmless.HiddenDanger}");
        Require(cheapDangerous.Pursuit == HiddenSlot && middleDangerous.Pursuit == HiddenSlot,
            $"both reposition rows must pursue the hidden enemy; cheap={cheapDangerous.Pursuit}, middle={middleDangerous.Pursuit}");
        Require(cheapDangerousInSight.SolvesFromHere,
            $"opening the line must let the hidden enemy be shot from where the companion stands; {cheapDangerousInSight.Evidence}");

        Require(cheapDangerous.Pursuit == HiddenSlot,
            $"a short step to remove a hard-hitting enemy must be pursued over the healthy zombie beside the body; pursuit={cheapDangerous.Pursuit}, {cheapDangerous.Evidence}");
        Require(middleDangerous.Pursuit == HiddenSlot,
            $"an imminent rescue of the player must beat the fight beside the body: the middle walk is long but the hidden enemy hits for a hundred and dies to one arrow; pursuit={middleDangerous.Pursuit}, {middleDangerous.Evidence}");
        Require(cheapHarmless.Pursuit == VisibleSlot,
            $"a nearly dead harmless enemy must not pull pursuit off a healthy one it would cost an arrow to finish; pursuit={cheapHarmless.Pursuit}, {cheapHarmless.Evidence}");
        Require(cheapDangerousInSight.Pursuit == HiddenSlot,
            $"once the dangerous enemy is visible from here, the plan must take it; pursuit={cheapDangerousInSight.Pursuit}, {cheapDangerousInSight.Evidence}");
    }

    private readonly record struct Consequence(float PlayerUrgency, float CompanionUrgency, float PlayerDanger, float CompanionDanger, float OldPlayerUrgency, float OldCompanionUrgency);

    /// <summary>
    /// One zombie between the player and the companion, with nothing varied but the named input. Its raw
    /// contact damage is the same in every scene, so any change in a urgency is the victim's defence,
    /// effectiveness, endurance or remaining life acting through the game's own damage arithmetic.
    /// </summary>
    private static Consequence Scene(int playerDefense = 0, float effectiveness = .5f, float endurance = 0f,
        int playerLife = 100, int companionDefense = 0, int companionLife = 100)
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 89));
        Main.tile[25, 89].ClearEverything();
        Player player = ctx.Player;
        player.Bottom = ctx.Npc.Bottom + new Vector2(40, 0);
        player.statDefense = Player.DefenseStat.Default + playerDefense;
        // ResetEffects assigns this from the world's difficulty in the live game. A headless Player has
        // never run it and carries zero, which would make defence look ignored for the wrong reason.
        player.DefenseEffectiveness = MultipliableFloat.One * effectiveness;
        player.endurance = endurance;
        player.statLife = playerLife;
        ctx.Npc.defense = companionDefense;
        ctx.Npc.life = companionLife;
        NPC enemy = Main.npc[30];
        enemy.SetDefaults(NPCID.Zombie);
        enemy.whoAmI = 30; enemy.active = true; enemy.velocity = Vector2.Zero;
        enemy.Bottom = ctx.Npc.Bottom - new Vector2(48, 0);
        ctx.Companion.Brain.Senses.Update(ctx.Npc, player);
        var threats = ctx.Companion.Brain.Senses.Threats;
        Require(threats.Threats.Count == 1 && threats.Threats[0].CanReachPlayer && threats.Threats[0].CanReachCompanion,
            $"the consequence scene must hold one zombie that can reach both actors; threats={threats.Threats.Count}");
        var t = threats.Threats[0];
        // The formula this change replaced, recomputed from the record's public facts: raw damage over a
        // quarter of maximum life. At zero defence and full health the new estimate must equal it, so
        // the scale every threshold was tuned against is unchanged where nothing about the victim is.
        float OldShare(float max) => Math.Clamp(t.ExpectedDamage / MathF.Max(1f, max * .25f), .2f, 1f);
        float oldPlayer = OldShare(player.statLifeMax2) * Math.Clamp(1f - t.TicksToPlayer / 360f, 0f, 1f) * (t.HasSightOnPlayer ? 1f : .5f);
        bool sees = t.DistanceToCompanion < 700f && LineOfSight.Between(enemy, ctx.Npc);
        float oldCompanion = OldShare(ctx.Npc.lifeMax) * Math.Clamp(1f - t.TicksToCompanion / 360f, 0f, 1f) * (sees ? 1f : .5f);
        return new(t.Urgency, t.UrgencyToCompanion, threats.PlayerDanger, threats.CompanionDanger, oldPlayer, oldCompanion);
    }

    private static void ThreatConsequenceCountsEffectiveDamageAgainstRemainingLife()
    {
        var baseline = Scene();
        Require(baseline.PlayerUrgency > 0f && baseline.CompanionUrgency > 0f,
            $"the baseline zombie must threaten both actors; player={baseline.PlayerUrgency}, companion={baseline.CompanionUrgency}");
        Require(MathF.Abs(baseline.PlayerUrgency - baseline.OldPlayerUrgency) < 1e-5f
            && MathF.Abs(baseline.CompanionUrgency - baseline.OldCompanionUrgency) < 1e-5f,
            $"with no defence and full health the estimate must equal raw damage over a quarter of life; player {baseline.PlayerUrgency} vs {baseline.OldPlayerUrgency}, companion {baseline.CompanionUrgency} vs {baseline.OldCompanionUrgency}");

        var armouredPlayer = Scene(playerDefense: 12);
        Require(armouredPlayer.PlayerUrgency < baseline.PlayerUrgency && armouredPlayer.CompanionUrgency == baseline.CompanionUrgency,
            $"player defence must lower only the player's consequence; player {armouredPlayer.PlayerUrgency} vs {baseline.PlayerUrgency}, companion {armouredPlayer.CompanionUrgency} vs {baseline.CompanionUrgency}");
        var harderDifficulty = Scene(playerDefense: 12, effectiveness: .75f);
        Require(harderDifficulty.PlayerUrgency < armouredPlayer.PlayerUrgency,
            $"the same defence at the game's higher effectiveness must absorb more; {harderDifficulty.PlayerUrgency} vs {armouredPlayer.PlayerUrgency}");
        var enduring = Scene(endurance: .5f);
        Require(enduring.PlayerUrgency < baseline.PlayerUrgency && enduring.CompanionUrgency == baseline.CompanionUrgency,
            $"player endurance must lower only the player's consequence; {enduring.PlayerUrgency} vs {baseline.PlayerUrgency}");

        var armouredCompanion = Scene(companionDefense: 12);
        Require(armouredCompanion.CompanionUrgency < baseline.CompanionUrgency && armouredCompanion.PlayerUrgency == baseline.PlayerUrgency,
            $"companion defence must lower only the companion's consequence; companion {armouredCompanion.CompanionUrgency} vs {baseline.CompanionUrgency}");

        var woundedCompanion = Scene(companionLife: 60);
        var badlyWoundedCompanion = Scene(companionLife: 30);
        Require(baseline.CompanionUrgency < woundedCompanion.CompanionUrgency && woundedCompanion.CompanionUrgency < badlyWoundedCompanion.CompanionUrgency
            && woundedCompanion.PlayerUrgency == baseline.PlayerUrgency && badlyWoundedCompanion.PlayerUrgency == baseline.PlayerUrgency,
            $"the same hit must weigh more on a companion with less life left, and not on the player; 100={baseline.CompanionUrgency}, 60={woundedCompanion.CompanionUrgency}, 30={badlyWoundedCompanion.CompanionUrgency}");
        Require(badlyWoundedCompanion.CompanionDanger > baseline.CompanionDanger && badlyWoundedCompanion.PlayerDanger == baseline.PlayerDanger,
            "combined danger must follow the per-threat consequence for the companion only");

        var woundedPlayer = Scene(playerLife: 30);
        Require(woundedPlayer.PlayerUrgency > baseline.PlayerUrgency && woundedPlayer.CompanionUrgency == baseline.CompanionUrgency,
            $"the same hit must weigh more on a player with less life left, and not on the companion; {woundedPlayer.PlayerUrgency} vs {baseline.PlayerUrgency}");
    }

    /// <summary>
    /// J13, restated for the orb. A small attack beside a healthy companion and the same attack beside one at
    /// twelve life must both leave the body to its job. Until 15 September 2026 this row also required that
    /// neither run start a safety response; the environmental escape was the last response that could take the
    /// body and it went that day, so nothing remains for an enemy to start and that half has no subject. What the
    /// wound still changes is how dangerous the companion reads, which hunting's own-skin term consumes, so the
    /// wounded run must read more danger than the healthy one. Only the companion's life differs between the
    /// runs; the slime cannot be damaged, so the arsenal cannot end the scene by killing it.
    /// </summary>
    private static void TheSameSmallAttackWeighsMoreAtLowHealthWithoutTakingTheBody()
    {
        string gate = "";
        void Responds(int life, out float danger)
        {
            var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 89));
            Main.tile[25, 89].ClearEverything();
            ctx.Player.Bottom = new Vector2(80 * 16, 90 * 16);
            ctx.Npc.life = life;
            NPC slime = Main.npc[30];
            slime.SetDefaults(NPCID.BlueSlime);
            slime.whoAmI = 30; slime.active = true; slime.damage = 3; slime.dontTakeDamage = true; slime.velocity = Vector2.Zero;
            slime.Bottom = ctx.Npc.Bottom + new Vector2(64, 0);
            VerifyResponsiveFollowing.AdvanceNative(ctx.Companion);
            ctx.Companion.Brain.Chooser.Actions.Clear();
            danger = 0f;
            for (int tick = 0; tick < 60; tick++)
            {
                VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
                VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
                var senses = ctx.Companion.Brain.Senses;
                Require(senses.Threats.Threats.Count == 1 && senses.Threats.PlayerDanger == 0f,
                    "the small-attack scene must threaten the companion alone");
                danger = MathF.Max(danger, senses.Threats.CompanionDanger);
                if (tick == 0)
                {
                    var threat = senses.Threats.Threats[0];
                    float exposure = live::AICompanion.Companion.Brain.Infrastructure.Position.Positioner.PredictedExposureAt(ctx.Npc.Center, senses);
                    bool sees = live::AICompanion.Companion.Brain.Infrastructure.Observation.LineOfSight.Between(slime, ctx.Npc);
                    var rows = new System.Text.StringBuilder();
                    for (int y = (int)(ctx.Npc.Center.Y / 16) - 2; y <= (int)(ctx.Npc.Center.Y / 16) + 1; y++)
                    {
                        rows.Append(' ').Append(y).Append(':');
                        for (int x = (int)(ctx.Npc.Center.X / 16) - 1; x <= (int)(slime.Center.X / 16) + 1; x++)
                            rows.Append(live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.IsBlock(x, y) ? '#' : '.');
                    }
                    gate = FormattableString.Invariant(
                        $"life={life} inTrouble={senses.Threats.CompanionInTrouble} danger={senses.Threats.CompanionDanger:0.000} canReach={threat.CanReachCompanion} ticksToCompanion={threat.TicksToCompanion:0.0} sees={sees} orb={ctx.Npc.Center} slime={slime.Center} rows={rows} exposure={exposure:0.000}");
                }
                VerifyResponsiveFollowing.AdvanceNative(ctx.Companion);
            }
        }
        Responds(100, out float healthyDanger);
        string healthyGate = gate;
        Responds(12, out float woundedDanger);
        Require(woundedDanger > healthyDanger,
            $"the same slime must read as more dangerous to a companion at twelve life; healthy danger={healthyDanger} wounded danger={woundedDanger}; healthy gate: {healthyGate}; wounded gate: {gate}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
