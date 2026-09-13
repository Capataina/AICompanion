extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Policy = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicy;
using LineOfSight = live::AICompanion.Companion.Brain.WorldObservation.LineOfSight;

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
        Console.WriteLine("combat purpose: effective damage and remaining life decide threat consequence, and low health turns a tolerable attack into an escape");
        return 0;
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
