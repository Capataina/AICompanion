extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Policy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using LineOfSight = live::AICompanion.Companion.Brain.Infrastructure.Observation.LineOfSight;
using Guard = live::AICompanion.Companion.Brain.Activities.Combat.ProtectPlayer;
using Hunt = live::AICompanion.Companion.Brain.Activities.Combat.PursueAttackOpportunity;
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
        TheSameSmallAttackIsIgnoredAtFullHealthAndEscapedAtLowHealth();
        PursuitWeighsARepositionAgainstTheShotsItDelays();
        ProtectionIsWorthTheHarmAnInterventionCanRemove();
        ProtectionCountsTheTimeToReachAFiringPosition();
        LandedHitsAreRecordedApartFromTheAimedTarget();
        TheRecordCarriesPursuitAimAndHitApart();
        VerifyEncounterContext.Run();
        VerifyCombatActorMatrix.Run();
        Console.WriteLine("combat purpose: effective damage and remaining life decide threat consequence, low health turns a tolerable attack into an escape, pursuit weighs a reposition against the shots it delays, protection is worth only the harm an intervention can remove, pursuit, aim and landed-hit identities are recorded apart, and a boss or world event stops optional work only where it reaches, once, from native facts or observed pressure");
        return 0;
    }

    /// <summary>
    /// Pursuit target, aim target and landed hit are three facts. A real shot leaves the arsenal aimed at
    /// one of two zombies; the native hit hook then reports the projectile striking the other one — the
    /// piercing or blocking case — and then the aimed one. The ledger must name the struck NPC and the
    /// aimed one separately each time, ignore a projectile the companion never fired, and forget the
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
        live::AICompanion.Companion.Weapons.TrackLandedHits.Clear();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        var arsenal = ctx.Companion.Arsenal;
        NPC? aimed = arsenal.BestTarget(ctx);
        Require(aimed != null, "the identity scene needs the arsenal to choose a target");
        Require(arsenal.TryFire(ctx, aimed!), $"the identity scene needs a real shot; fire={arsenal.LastFireOutcome}");
        Projectile[] airborne = Main.projectile.Where(p => p.active).ToArray();
        Require(airborne.Length == 1, $"exactly one projectile must be in the air after the shot; active={airborne.Length}");
        Projectile shot = airborne[0];
        NPC other = aimed!.whoAmI == 30 ? Main.npc[31] : Main.npc[30];

        var hook = new live::AICompanion.Companion.Weapons.ObserveLandedCompanionHits();
        hook.OnHitByProjectile(other, shot, new NPC.HitInfo { Damage = 7 }, 7);
        var first = live::AICompanion.Companion.Weapons.TrackLandedHits.Last;
        Require(first is { } strayHit && strayHit.HitSlot == other.whoAmI && strayHit.AimSlot == aimed.whoAmI
            && !strayHit.StruckAimedTarget && strayHit.Damage == 7,
            $"a shot landing on the enemy in front must record that enemy as hit and the chosen one as aimed; got {first}");
        hook.OnHitByProjectile(aimed, shot, new NPC.HitInfo { Damage = 9 }, 9);
        Require(live::AICompanion.Companion.Weapons.TrackLandedHits.Last is { StruckAimedTarget: true, Damage: 9 }
            && live::AICompanion.Companion.Weapons.TrackLandedHits.Count == 2,
            $"the same shot reaching its aimed enemy must record a hit on the aimed target; got {(live::AICompanion.Companion.Weapons.TrackLandedHits.Last)}, count={(live::AICompanion.Companion.Weapons.TrackLandedHits.Count)}");

        int foreignSlot = shot.whoAmI == 0 ? 1 : 0;
        Main.projectile[foreignSlot] = new Projectile { whoAmI = foreignSlot, active = true, friendly = true, damage = 11, owner = Main.myPlayer };
        hook.OnHitByProjectile(aimed, Main.projectile[foreignSlot], new NPC.HitInfo { Damage = 11 }, 11);
        Require(live::AICompanion.Companion.Weapons.TrackLandedHits.Count == 2,
            "a projectile the companion never fired must not be attributed to it, even when it is the player's own");

        new live::AICompanion.Companion.Weapons.ForgetReusedShotSlots().OnSpawn(shot, new Terraria.DataStructures.EntitySource_Misc("engine-replay"));
        hook.OnHitByProjectile(aimed, shot, new NPC.HitInfo { Damage = 13 }, 13);
        Require(live::AICompanion.Companion.Weapons.TrackLandedHits.Count == 2,
            "a new projectile spawned into the shot's slot must not inherit the companion's shot");
        Main.projectile[foreignSlot].active = false;
        shot.active = false;
        live::AICompanion.Companion.Weapons.TrackLandedHits.Clear();
    }

    /// <summary>
    /// The appended identity columns carry the brain's own facts on a real recorded row. One shot is fired
    /// and its landing reported through the native hook, the whole brain then runs one recorded tick, and
    /// the row is read by column name — as every reader must, because columns are appended — against the
    /// hunt, the hands and the ledger as they stand after that tick.
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
            live::AICompanion.Companion.Weapons.TrackLandedHits.Clear();
            ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
            NPC? aimed = ctx.Companion.Arsenal.BestTarget(ctx);
            Require(aimed != null && ctx.Companion.Arsenal.TryFire(ctx, aimed), "the recorded identity scene needs a real shot");
            Projectile shot = Main.projectile.First(p => p.active);
            new live::AICompanion.Companion.Weapons.ObserveLandedCompanionHits().OnHitByProjectile(enemy, shot, new NPC.HitInfo { Damage = 5 }, 5);

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
            string[] appended = { "pursuit_target", "pursuit_value", "pursuit_access_ticks", "pursuit_evidence", "aim_target",
                "landed_hit_target", "landed_hit_aimed", "landed_hit_damage", "landed_hit_tick", "landed_hits",
                "guard_removal_ticks", "guard_usefulness", "top_threat_effective_player", "top_threat_effective_companion",
                "guard_access_ticks" };
            foreach (string name in appended)
                Require(Array.IndexOf(names, name) >= 0, "identity column missing from the recording: " + name);
            string declaration = lines.First(l => l.StartsWith("# text_columns="));
            foreach (string text in new[] { "pursuit_target", "pursuit_evidence", "aim_target", "landed_hit_target", "landed_hit_aimed" })
                Require(declaration.Split('=')[1].Split(',').Contains(text), "textual identity column not declared as text: " + text);
            string Value(string name) => values[Array.IndexOf(names, name)];
            string Identity(NPC? npc) => npc != null && npc.active
                ? $"{npc.whoAmI}:{(live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.Generation(npc))}" : "-";
            var hunt = ctx.Companion.Brain.Chooser.Actions.OfType<Hunt>().Single();
            var landed = live::AICompanion.Companion.Weapons.TrackLandedHits.Last;
            Require(landed is { } hit && Value("landed_hit_target") == $"{hit.HitSlot}:{hit.HitGeneration}"
                && Value("landed_hit_aimed") == $"{hit.AimSlot}:{hit.AimGeneration}" && Value("landed_hit_damage") == "5"
                && Value("landed_hits") == "1",
                $"the recorded landed hit must be the ledger's; row={Value("landed_hit_target")}/{Value("landed_hit_aimed")}/{Value("landed_hit_damage")}/{Value("landed_hits")}, ledger={landed}");
            Require(Value("aim_target") == Identity(ctx.Companion.Brain.EngageTarget),
                $"the recorded aim must be the hands' target; row={Value("aim_target")}, hands={Identity(ctx.Companion.Brain.EngageTarget)}");
            Require(Value("pursuit_target") == Identity(hunt.Target?.Npc)
                && Math.Abs(float.Parse(Value("pursuit_value"), System.Globalization.CultureInfo.InvariantCulture) - hunt.PursuitValue) < 1e-3f,
                $"the recorded pursuit must be the hunt's retained choice; row={Value("pursuit_target")}/{Value("pursuit_value")}, hunt={Identity(hunt.Target?.Npc)}/{hunt.PursuitValue}");
            Main.projectile.Where(p => p.active).ToList().ForEach(p => p.active = false);
            live::AICompanion.Companion.Weapons.TrackLandedHits.Clear();
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

    private readonly record struct GuardScene(float Guard, float Hunt, float Urgency, float Removal, float Usefulness);

    /// <summary>
    /// One hostile beside the player, with the companion across an open floor. Only the hostile differs
    /// between rows — its type, or for the matched pair only its life — so a change in guarding's value is
    /// the fight's length acting through the removal estimate. Enemy AI does not run; this values the
    /// arrangement, it does not stage a fight.
    /// </summary>
    private static GuardScene GuardAgainst(int type, int life = 0)
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 89));
        Main.tile[25, 89].ClearEverything();
        ctx.Player.Bottom = new Vector2(60 * 16, 90 * 16);
        ctx.Player.DefenseEffectiveness = MultipliableFloat.One * .5f;
        NPC enemy = Main.npc[30];
        enemy.SetDefaults(type);
        enemy.whoAmI = 30; enemy.active = true; enemy.velocity = Vector2.Zero;
        if (life > 0) { enemy.lifeMax = life; enemy.life = life; }
        enemy.Bottom = ctx.Player.Bottom - new Vector2(48, 0);
        var brain = ctx.Companion.Brain;
        brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        var threat = brain.Senses.Threats.Threats.Find(t => t.Npc == enemy);
        Require(threat != null && threat.CanReachPlayer && threat.Urgency > 0f,
            $"the guard scene's hostile (type {type}) must threaten the player before protection is read; urgency={threat?.Urgency}");
        brain.Senses.SetInterventionEstimate(ctx.Companion.Arsenal.EstimateInterventionTicks(ctx));
        var guard = brain.Chooser.Actions.OfType<Guard>().Single();
        var hunt = brain.Chooser.Actions.OfType<Hunt>().Single();
        float guardValue = VerifyPreparedActivities.PrepareAndScore(guard, ctx);
        float huntValue = VerifyPreparedActivities.PrepareAndScore(hunt, ctx);
        return new(guardValue, huntValue, threat!.Urgency, guard.RemovalTicks, guard.InterventionUsefulness);
    }

    /// <summary>
    /// Protection is worth the harm an intervention can remove. The matched pair holds one zombie beside
    /// the player at its ordinary life and at a hundred times it: the urgency is identical and only the
    /// fight's length differs, so guarding the tank must be worth exactly the usefulness share of guarding
    /// the ordinary zombie. The second pair is the owner's boss story — against the Eye of Cthulhu there is
    /// no version where standing between it and the player helps, while the small eyes on the player are
    /// things the companion can remove — and it must hold on starting weapons without any boss flag being
    /// read. Hunting must still offer the long fight: making protection pointless is not a refusal to fight.
    /// </summary>
    private static void ProtectionIsWorthTheHarmAnInterventionCanRemove()
    {
        var ordinary = GuardAgainst(NPCID.Zombie);
        var tank = GuardAgainst(NPCID.Zombie, life: 4500);
        var eye = GuardAgainst(NPCID.EyeofCthulhu);
        var servant = GuardAgainst(NPCID.ServantofCthulhu);
        foreach (var (name, scene) in new[] { ("ordinary", ordinary), ("tank", tank), ("eye", eye), ("servant", servant) })
            Console.WriteLine($"  guard row {name}: guard={scene.Guard:0.000} hunt={scene.Hunt:0.000} urgency={scene.Urgency:0.000} removal-ticks={scene.Removal:0.0} usefulness={scene.Usefulness:0.000}");

        Require(ordinary.Removal < Weights.GuardUsefulRemovalTicks && tank.Removal > Weights.GuardUsefulRemovalTicks
            && servant.Removal < Weights.GuardUsefulRemovalTicks && eye.Removal > Weights.GuardUsefulRemovalTicks,
            $"the rows must sit on the stated sides of the useful fight length; ordinary={ordinary.Removal}, tank={tank.Removal}, servant={servant.Removal}, eye={eye.Removal}");
        Require(MathF.Abs(tank.Urgency - ordinary.Urgency) < 1e-5f,
            $"the matched pair must threaten the player identically; tank={tank.Urgency}, ordinary={ordinary.Urgency}");
        Require(tank.Guard < ordinary.Guard,
            $"a threat the weapons would need far longer to remove must be worth less protection than the same threat they can remove; tank={tank.Guard}, ordinary={ordinary.Guard}");
        Require(MathF.Abs(tank.Guard - ordinary.Guard * tank.Usefulness) < 1e-4f,
            $"the fight's length must be the only difference between the matched guard values; tank={tank.Guard}, ordinary×usefulness={ordinary.Guard * tank.Usefulness}");
        Require(servant.Guard > eye.Guard,
            $"the small eyes on the player must be worth more protection than the boss itself; servant={servant.Guard}, eye={eye.Guard}");
        Require(tank.Hunt > 0f && eye.Hunt > 0f,
            $"a fight too long to protect through must still be a fight hunting offers; tank={tank.Hunt}, eye={eye.Hunt}");
    }

    private enum ShaftAccess { Sealed, Lip, InSight }

    private readonly record struct ShaftGuard(float Guard, float Usefulness, float Removal, string Access, float AccessTicks,
        float PlayerUrgency, float ProtectionUrgency, bool ReachComplete, int Target);

    // The guarded zombie's life in the shaft pair, chosen so removal is already past GuardUsefulRemovalTicks on
    // starting weapons. Below that length every access that leaves the sum inside it is guarded identically,
    // which is correct and would make "the lip reads below in-sight" unreachable rather than tested.
    private const int ShaftGuardedLife = 600, ShaftGuardPlayerX = 58;

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
        if (access == ShaftAccess.InSight)
            for (int x = ShaftLeft - 3; x < ShaftLeft; x++)
                for (int y = PitFloorY; y < ShaftFloorY; y++) { Tile air = Main.tile[x, y]; air.HasTile = false; }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar.InvalidateEdges();

        var companion = VerifyCompanionLifecycle.Create();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar.MsBudget = 0;
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2;
        player.DefenseEffectiveness = MultipliableFloat.One * .5f;
        player.velocity = Vector2.Zero;
        // Across the shaft from the companion, not beside it as in the pursuit rows: the in-sight opening is cut on
        // the companion's side, and cut between the zombie and a player standing there it doubled the zombie's
        // urgency to the player (0.295 to 0.591 on the first run), which the danger premise below refused.
        player.position = new Vector2(ShaftGuardPlayerX * 16f, PitFloorY * 16f - player.height);
        companion.NPC.position = new Vector2(NearStart * 16f, PitFloorY * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;

        Main.npc[VisibleSlot] = new NPC();
        NPC hidden = Main.npc[HiddenSlot];
        hidden.SetDefaults(NPCID.Zombie);
        hidden.whoAmI = HiddenSlot; hidden.active = true; hidden.velocity = Vector2.Zero;
        hidden.lifeMax = hidden.life = ShaftGuardedLife;
        hidden.noTileCollide = true;
        hidden.damage = 100;
        hidden.Bottom = new Vector2(HiddenX * 16f + 8f, ShaftFloorY * 16f);

        var brain = companion.Brain;
        brain.Senses.Update(companion.NPC, player, companion.Breath);
        var ctx = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, brain.Senses);
        var threat = brain.Senses.Threats.Threats.Find(t => t.Npc == hidden);
        Require(threat != null && threat.CanReachPlayer && threat.Urgency > 0f,
            $"shaft guard {access}: the zombie must threaten the player before protection is read; urgency={threat?.Urgency}");
        var profile = companion.Arsenal.ProfileFor(ctx, hidden);
        var request = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.LineOfFire, hidden.Center, hidden);
        // Settled, so a proven absence is the flood's finished answer rather than its budget.
        for (int i = 0; i < 400; i++) brain.Positioner.Resolve(request, brain.Senses, profile);
        brain.Senses.SetInterventionEstimate(companion.Arsenal.EstimateInterventionTicks(ctx));
        var guard = brain.Chooser.Actions.OfType<Guard>().Single();
        // The firing query's proven absence costs a completed sweep of every sampled stand rather than the first
        // handful, so one preparation answers Unknown for a sealed threat and only a sweep all the way round
        // establishes the absence this row reads a zero from. The sweep resumes on each scan and its cache is
        // keyed to the sense's clock, so the clock is what has to run; nothing else about the scene moves.
        var clock = typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.Senses).GetProperty("Tick")!;
        float guardValue = VerifyPreparedActivities.PrepareAndScore(guard, ctx);
        for (int scan = 0; scan < 400 && guard.Access == live::AICompanion.Companion.Brain.Activities.Combat.FiringAccess.Unknown; scan++)
        {
            clock.SetValue(brain.Senses, (int)clock.GetValue(brain.Senses)! + 21);
            guardValue = VerifyPreparedActivities.PrepareAndScore(guard, ctx);
        }
        return new(guardValue, guard.InterventionUsefulness, guard.RemovalTicks, guard.Access?.ToString() ?? "unasked", guard.AccessTicks,
            threat!.Urgency, brain.Senses.Threats.ProtectionUrgency, brain.Positioner.ReachComplete, (guard.ActivityIdentity as NPC)?.whoAmI ?? -1);
    }

    /// <summary>
    /// Proposal 1's P03 on protection: an intervention's time includes getting to where it can be made. One threat
    /// on the player, removal past the useful length, and three ways of shooting it. A shaft capped so nothing the
    /// companion can reach has a line is access that never arrives, so guarding is worth nothing and yields to
    /// safety and company; a lip a short walk away is worth protecting, less than a shot from here by exactly the
    /// walk's part of access plus removal. The danger the guard value multiplies is required identical across the
    /// open rows, because opening terrain can change what the zombie threatens and that must not pass for access.
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
            Console.WriteLine($"  guard access row {name}: guard={scene.Guard:0.0000} target={scene.Target} access={scene.Access} {scene.AccessTicks:0.0} removal={scene.Removal:0.0} share={scene.Usefulness:0.0000} player-urgency={scene.PlayerUrgency:0.000} protection-urgency={scene.ProtectionUrgency:0.000} reach-complete={scene.ReachComplete}");

        foreach (var (name, scene) in new[] { ("sealed", sealedShaft), ("lip", lip), ("in-sight", inSight) })
            Require(scene.Target == HiddenSlot && scene.Removal > Weights.GuardUsefulRemovalTicks && MathF.Abs(scene.Removal - lip.Removal) < 0.01f,
                $"shaft guard {name}: guarding must hold the shaft zombie with an identical removal already past the useful length; target={scene.Target}, removal={scene.Removal}, lip removal={lip.Removal}");
        Require(sealedShaft.Access == "None" && sealedShaft.ReachComplete,
            $"shaft guard sealed: the capped shaft must be a proven absence from a settled flood, or a zero proves nothing; access={sealedShaft.Access}, reach-complete={sealedShaft.ReachComplete}");
        Require(lip.Access == "AfterMoving" && float.IsFinite(lip.AccessTicks) && lip.AccessTicks > 0f,
            $"shaft guard lip: the open shaft must be a finite walk to its lip; access={lip.Access} {lip.AccessTicks}");
        Require(inSight.Access == "FromHere" && inSight.AccessTicks == 0f,
            $"shaft guard in-sight: the opened rock must let a weapon solve from where the companion stands; access={inSight.Access} {inSight.AccessTicks}");
        float lipDanger = lip.Guard / (lip.Usefulness * Weights.GuardUrgency), inSightDanger = inSight.Guard / (inSight.Usefulness * Weights.GuardUrgency);
        Require(MathF.Abs(lipDanger - inSightDanger) < 1e-4f,
            $"shaft guard: the danger guarding multiplies must be identical with the lip and in sight, or the pair measures danger rather than access; lip={lipDanger}, in-sight={inSightDanger}");

        Require(sealedShaft.Guard == 0f && sealedShaft.Usefulness == 0f,
            $"a threat no reachable position can shoot must be worth no protection; guard={sealedShaft.Guard}, share={sealedShaft.Usefulness}");
        Require(MathF.Abs(lip.Usefulness - Weights.GuardUsefulRemovalTicks / (lip.Removal + lip.AccessTicks)) < 1e-4f
            && MathF.Abs(inSight.Usefulness - Weights.GuardUsefulRemovalTicks / inSight.Removal) < 1e-4f,
            $"the share must be the useful length over access plus removal; lip={lip.Usefulness}, in-sight={inSight.Usefulness}");
        Require(lip.Guard > 0f && lip.Guard < inSight.Guard,
            $"a threat the companion must walk to the lip to shoot must be worth protecting, and less than the same threat it can shoot now; lip={lip.Guard}, in-sight={inSight.Guard}");
    }

    private const int PitFloorY = 80, ShaftLeft = 49, ShaftRight = 53, ShaftFloorY = 86, HiddenX = 51;
    private const int HiddenSlot = 30, VisibleSlot = 31;
    // The player stays beside the shaft in every row, so the hidden enemy's threat to the player is
    // identical and only the companion's start — the length of its reposition — changes between them.
    // The starts are placed against how the reachable-region flood prices a walk step, so a change to walk
    // pricing moves every wait; the premise checks in the pursuit rows fail by name when a row stops being
    // the short step, the priced walk inside the arsenal's window or the walk past it that it claims to be.
    private const int PlayerTileX = 44, NearStart = 45, MiddleStart = 22, FarStart = 9;

    private readonly record struct PursuitScene(int Pursuit, int Aim, string HiddenVerdict, float HiddenAccess,
        float HiddenValue, float VisibleValue, float HiddenDanger, float HiddenPlayerUrgency, string Evidence);

    /// <summary>
    /// A 5%-health zombie on the floor of a narrow shaft the companion cannot see into, and a full-health
    /// zombie three tiles along the open floor in the other direction. Only the lip of the shaft has a line
    /// down it, so the hidden enemy needs a reposition whose length is set by where the companion starts.
    /// The shaft-geometry discrimination is inherited from the firing-position fixture: the near floor is
    /// blind and the lip is sighted. A dangerous hidden enemy hits for a hundred and passes through rock,
    /// so it can reach both actors; a harmless one passes through rock too but hits for one. The difference
    /// matters through prevented harm, which the arsenal values as expected hit × danger × timing × a
    /// weight, so a hard hitter is worth a short reposition by a clear margin rather than a sliver a weapon
    /// retune could erase. The headless screen is empty, so hunting's on-screen rule plays no part here and
    /// a result says nothing about it.
    /// </summary>
    private static PursuitScene HuntPair(int companionX, bool hiddenDangerous, bool lineFromHere)
    {
        Main.maxTilesX = Main.maxTilesY = 120;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, null, new object[] { (ushort)120, (ushort)120 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = PitFloorY; y <= ShaftFloorY + 2; y++) { Tile rock = Main.tile[x, y]; rock.HasTile = true; rock.TileType = 1; }
        for (int x = ShaftLeft; x <= ShaftRight; x++)
            for (int y = PitFloorY; y < ShaftFloorY; y++) { Tile air = Main.tile[x, y]; air.HasTile = false; }
        // The opening stops short of the companion's own floor tile, so the body stands where it did.
        if (lineFromHere)
            for (int x = ShaftLeft - 3; x < ShaftLeft; x++)
                for (int y = PitFloorY; y < ShaftFloorY; y++) { Tile air = Main.tile[x, y]; air.HasTile = false; }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();

        var companion = VerifyCompanionLifecycle.Create();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar.MsBudget = 0;
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2;
        player.DefenseEffectiveness = MultipliableFloat.One * .5f;
        player.position = new Vector2(PlayerTileX * 16f, PitFloorY * 16f - player.height);
        companion.NPC.position = new Vector2(companionX * 16f, PitFloorY * 16f - companion.NPC.height);

        NPC hidden = Main.npc[HiddenSlot];
        hidden.SetDefaults(NPCID.Zombie);
        hidden.whoAmI = HiddenSlot; hidden.active = true; hidden.velocity = Vector2.Zero;
        hidden.life = Math.Max(1, hidden.lifeMax / 20);
        hidden.noTileCollide = true;
        hidden.damage = hiddenDangerous ? 100 : 1;
        hidden.Bottom = new Vector2(HiddenX * 16f + 8f, ShaftFloorY * 16f);
        NPC visible = Main.npc[VisibleSlot];
        visible.SetDefaults(NPCID.Zombie);
        visible.whoAmI = VisibleSlot; visible.active = true; visible.velocity = Vector2.Zero;
        // Three tiles away, so nearness and its own danger put the visible zombie first in the threat
        // list's candidate order: a rule that took the first admissible candidate would pursue it in
        // every row, and only a valuation of the reposition can choose the hidden enemy.
        visible.Bottom = new Vector2((companionX - 3) * 16f + 8f, PitFloorY * 16f);

        var brain = companion.Brain;
        brain.Senses.Update(companion.NPC, player, companion.Breath);
        var ctx = new live::AICompanion.Companion.Brain.Activities.ActionContext(companion, brain.Senses);
        var hiddenThreat = brain.Senses.Threats.Threats.Find(t => t.Npc == hidden);
        var visibleThreat = brain.Senses.Threats.Threats.Find(t => t.Npc == visible);
        Require(hiddenThreat != null && visibleThreat != null, "both zombies must be observed threats before pursuit is read");
        float hiddenDanger = MathF.Max(hiddenThreat!.Urgency, hiddenThreat.UrgencyToCompanion);
        var profile = companion.Arsenal.ProfileFor(ctx, hidden);
        var request = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.LineOfFire, hidden.Center, hidden);
        // The reachable region floods incrementally across rescores; settle it so the lip's reachability
        // is a fact rather than a flood budget.
        for (int i = 0; i < 400; i++) brain.Positioner.Resolve(request, brain.Senses, profile);
        brain.Senses.SetInterventionEstimate(companion.Arsenal.EstimateInterventionTicks(ctx));
        // The hands rank their shots before the feet prepare, as the previous tick's hands step would have.
        NPC? aim = companion.Arsenal.BestTarget(ctx);
        var hunt = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.Activities.Combat.PursueAttackOpportunity>().Single();
        VerifyPreparedActivities.PrepareAndScore(hunt, ctx);

        string hiddenVerdict = "unexamined"; float hiddenAccess = float.NaN, hiddenValue = float.NaN, visibleValue = float.NaN;
        foreach (string entry in hunt.PursuitEvidence.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] field = entry.Split(':');
            int slot = int.Parse(field[0]);
            float access = float.Parse(field[3], System.Globalization.CultureInfo.InvariantCulture);
            float value = float.Parse(field[4], System.Globalization.CultureInfo.InvariantCulture);
            if (slot == HiddenSlot) { hiddenVerdict = field[2]; hiddenAccess = access; hiddenValue = value; }
            if (slot == VisibleSlot) visibleValue = value;
        }
        return new(hunt.Target?.Npc.whoAmI ?? -1, aim?.whoAmI ?? -1, hiddenVerdict, hiddenAccess, hiddenValue, visibleValue, hiddenDanger,
            hiddenThreat.Urgency, hunt.PursuitEvidence);
    }

    /// <summary>
    /// Proposal 1's hidden-5%-versus-visible-100% pairs. The arsenal already shoots whatever it can reach
    /// from where the companion stands; the question here is only where the feet go. Four rows, each a
    /// matched change of one input from the first: reposition cost, the hidden enemy's threat, and whether
    /// the hidden enemy can be seen from where the companion already stands. The expected outcomes follow
    /// the arsenal's own unchanged valuation rather than a rule about low health: a short step to remove a
    /// hard-hitting enemy is worth the arrows it delays, a long walk is not, and finishing a harmless enemy
    /// is not worth an arrow into a healthy one.
    /// </summary>
    private static void PursuitWeighsARepositionAgainstTheShotsItDelays()
    {
        var cheapDangerous = HuntPair(NearStart, hiddenDangerous: true, lineFromHere: false);
        var middleDangerous = HuntPair(MiddleStart, hiddenDangerous: true, lineFromHere: false);
        var costlyDangerous = HuntPair(FarStart, hiddenDangerous: true, lineFromHere: false);
        var cheapHarmless = HuntPair(NearStart, hiddenDangerous: false, lineFromHere: false);
        var cheapDangerousInSight = HuntPair(NearStart, hiddenDangerous: true, lineFromHere: true);

        foreach (var (name, scene) in new[] { ("cheap", cheapDangerous), ("middle", middleDangerous), ("costly", costlyDangerous), ("harmless", cheapHarmless), ("in-sight", cheapDangerousInSight) })
            Console.WriteLine($"  pursuit row {name}: pursuit={scene.Pursuit} aim={scene.Aim} hidden-danger={scene.HiddenDanger:0.000} hidden-player-urgency={scene.HiddenPlayerUrgency:0.000} candidates={scene.Evidence}");
        foreach (var (name, scene) in new[] { ("cheap", cheapDangerous), ("middle", middleDangerous), ("costly", costlyDangerous), ("harmless", cheapHarmless) })
            Require(scene.HiddenVerdict == "AfterMoving" && float.IsFinite(scene.VisibleValue),
                $"{name}: the hidden enemy must need a reachable reposition and the visible one must be examined, or the row tests nothing; {scene.Evidence}");
        Require(cheapDangerous.HiddenAccess < middleDangerous.HiddenAccess && middleDangerous.HiddenAccess < costlyDangerous.HiddenAccess,
            $"the reposition rows must lengthen the wait in order; cheap={cheapDangerous.HiddenAccess}, middle={middleDangerous.HiddenAccess}, costly={costlyDangerous.HiddenAccess}");
        Require(middleDangerous.HiddenAccess < live::AICompanion.Companion.Weapons.Arsenal.HorizonTicks && middleDangerous.HiddenValue > 0f,
            $"the middle row must be a priced wait inside the arsenal's evaluation window, not a second truncation; wait={middleDangerous.HiddenAccess}, value={middleDangerous.HiddenValue}");
        Require(costlyDangerous.HiddenAccess > live::AICompanion.Companion.Weapons.Arsenal.HorizonTicks,
            $"the costly row tests a wait longer than the arsenal's whole evaluation window, so its hidden enemy is worth nothing inside it; wait={costlyDangerous.HiddenAccess}");
        // The companion's own exposure moves with its start, which is the physical cost of standing near
        // an enemy; the threat to the player must not, or the rows would differ in more than the reposition.
        Require(cheapDangerous.HiddenPlayerUrgency > 0f
            && MathF.Abs(cheapDangerous.HiddenPlayerUrgency - middleDangerous.HiddenPlayerUrgency) < 1e-4f
            && MathF.Abs(cheapDangerous.HiddenPlayerUrgency - costlyDangerous.HiddenPlayerUrgency) < 1e-4f,
            $"the reposition rows must hold the hidden enemy's threat to the player fixed; cheap={cheapDangerous.HiddenPlayerUrgency}, middle={middleDangerous.HiddenPlayerUrgency}, costly={costlyDangerous.HiddenPlayerUrgency}");
        Require(cheapDangerous.HiddenDanger > cheapHarmless.HiddenDanger,
            $"the dangerous hidden enemy must threaten more than the harmless one; {cheapDangerous.HiddenDanger} vs {cheapHarmless.HiddenDanger}");
        Require(cheapDangerous.HiddenValue > middleDangerous.HiddenValue && middleDangerous.HiddenValue >= costlyDangerous.HiddenValue,
            $"a longer reposition must lower the hidden enemy's delayed value; cheap={cheapDangerous.HiddenValue}, middle={middleDangerous.HiddenValue}, costly={costlyDangerous.HiddenValue}");
        Require(cheapDangerousInSight.HiddenVerdict == "FromHere",
            $"opening the line must let the hidden enemy be shot from where the companion stands; {cheapDangerousInSight.Evidence}");

        Require(cheapDangerous.Pursuit == HiddenSlot && cheapDangerous.Aim == VisibleSlot,
            $"a short step to remove a hard-hitting enemy must be pursued while the hands shoot the visible one; pursuit={cheapDangerous.Pursuit}, aim={cheapDangerous.Aim}, {cheapDangerous.Evidence}");
        Require(costlyDangerous.Pursuit == VisibleSlot,
            $"the same enemy behind a long walk must not be pursued over the arrows the walk would delay; pursuit={costlyDangerous.Pursuit}, {costlyDangerous.Evidence}");
        Require(cheapHarmless.Pursuit == VisibleSlot,
            $"a nearly dead harmless enemy must not pull pursuit off a healthy one it would cost an arrow to finish; pursuit={cheapHarmless.Pursuit}, {cheapHarmless.Evidence}");
        Require(cheapDangerousInSight.Pursuit == HiddenSlot && cheapDangerousInSight.Aim == HiddenSlot,
            $"once the dangerous enemy is visible from here, feet and hands must both take it; pursuit={cheapDangerousInSight.Pursuit}, aim={cheapDangerousInSight.Aim}, {cheapDangerousInSight.Evidence}");
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
        ctx.Companion.Brain.Senses.Update(ctx.Npc, player, ctx.Companion.Breath);
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
    /// J13: a small attack during nothing in particular must not send a healthy companion running, but the
    /// same attack at low health must. Only the companion's life differs between the two runs; the slime,
    /// its damage, the terrain and the absent ordinary offers are identical, and it cannot be damaged, so
    /// the arsenal cannot end the scene by killing it.
    /// </summary>
    private static void TheSameSmallAttackIsIgnoredAtFullHealthAndEscapedAtLowHealth()
    {
        bool Spaces(int life, out float danger)
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
            bool spaced = false;
            danger = 0f;
            for (int tick = 0; tick < 60 && !spaced; tick++)
            {
                VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
                VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
                var senses = ctx.Companion.Brain.Senses;
                Require(senses.Threats.Threats.Count == 1 && senses.Threats.PlayerDanger == 0f,
                    "the small-attack scene must threaten the companion alone");
                danger = MathF.Max(danger, senses.Threats.CompanionDanger);
                spaced = ctx.Companion.Brain.Safety.Active && ctx.Companion.Brain.Safety.Kind == "combat-spacing";
                VerifyResponsiveFollowing.AdvanceNative(ctx.Companion);
            }
            return spaced;
        }
        bool healthy = Spaces(100, out float healthyDanger);
        bool wounded = Spaces(12, out float woundedDanger);
        Require(!healthy, $"a three-damage slime must not make a full-health companion abandon work; danger={healthyDanger}");
        Require(wounded, $"the same slime must make a companion at twelve life create space; danger={woundedDanger}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
