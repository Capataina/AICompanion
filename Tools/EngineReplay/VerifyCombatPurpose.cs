extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Policy = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicy;
using LineOfSight = live::AICompanion.Companion.Brain.WorldObservation.LineOfSight;
using Guard = live::AICompanion.Companion.Brain.PurposeFamilies.Combat.ProtectPlayer;
using Hunt = live::AICompanion.Companion.Brain.PurposeFamilies.Combat.PursueAttackOpportunity;
using Weights = live::AICompanion.Companion.Brain.BehaviourSelection.Weights;

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
        Console.WriteLine("combat purpose: effective damage and remaining life decide threat consequence, low health turns a tolerable attack into an escape, pursuit weighs a reposition against the shots it delays, and protection is worth only the harm an intervention can remove");
        return 0;
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

    private const int PitFloorY = 80, ShaftLeft = 49, ShaftRight = 53, ShaftFloorY = 86, HiddenX = 51;
    private const int HiddenSlot = 30, VisibleSlot = 31;
    // The player stays beside the shaft in every row, so the hidden enemy's threat to the player is
    // identical and only the companion's start — the length of its reposition — changes between them.
    private const int PlayerTileX = 44, NearStart = 45, MiddleStart = 39, FarStart = 22;

    private readonly record struct PursuitScene(int Pursuit, int Aim, string HiddenVerdict, float HiddenAccess,
        float HiddenValue, float VisibleValue, float HiddenDanger, float HiddenPlayerUrgency, string Evidence);

    /// <summary>
    /// A 5%-health zombie on the floor of a narrow shaft the companion cannot see into, and a full-health
    /// zombie eight tiles along the open floor in the other direction. Only the lip of the shaft has a line
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
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();

        var companion = VerifyCompanionLifecycle.Create();
        live::AICompanion.Companion.Brain.SharedMovementSystem.AStar.MsBudget = 0;
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
        var ctx = new live::AICompanion.Companion.Brain.Behaviours.ActionContext(companion, brain.Senses);
        var hiddenThreat = brain.Senses.Threats.Threats.Find(t => t.Npc == hidden);
        var visibleThreat = brain.Senses.Threats.Threats.Find(t => t.Npc == visible);
        Require(hiddenThreat != null && visibleThreat != null, "both zombies must be observed threats before pursuit is read");
        float hiddenDanger = MathF.Max(hiddenThreat!.Urgency, hiddenThreat.UrgencyToCompanion);
        var profile = companion.Arsenal.ProfileFor(ctx, hidden);
        var request = new live::AICompanion.Companion.Brain.PositionSelection.PositionRequest(
            live::AICompanion.Companion.Brain.PositionSelection.RequestKind.LineOfFire, hidden.Center, hidden);
        // The reachable region floods incrementally across rescores; settle it so the lip's reachability
        // is a fact rather than a flood budget.
        for (int i = 0; i < 400; i++) brain.Positioner.Resolve(request, brain.Senses, profile);
        brain.Senses.SetInterventionEstimate(companion.Arsenal.EstimateInterventionTicks(ctx));
        // The hands rank their shots before the feet prepare, as the previous tick's hands step would have.
        NPC? aim = companion.Arsenal.BestTarget(ctx);
        var hunt = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.PurposeFamilies.Combat.PursueAttackOpportunity>().Single();
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
