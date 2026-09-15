#nullable enable

using System;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace AICompanion.Companion.Progression;

/// <summary>
/// The companion's experience, priced by the game's own numbers so a level takes a similar amount of play in any difficulty
/// or modpack (the owner's ruling of 15 September 2026). A kill earns the enemy's maximum life as the game set it in this
/// world; the bar for the next level is priced from the strongest enemy and the strongest boss fight killed so far, each
/// growing five percent a level since it was set; work fills a fixed share of whatever the bar is. It is character state
/// and persists with the player. Nothing in the brain reads it: a level changes what the companion can do and survive
/// through the mastery tree, never what it decides.
///
/// What is credited, and who earned it, is decided in <see cref="CreditKillsAndFights"/> and <see cref="CreditWork"/>; this
/// class is only the arithmetic, so every rule of the pricing can be tested without a world.
/// </summary>
public sealed class CompanionExperience
{
    /// <summary>
    /// The ruling's unit: one experience is 0.1% of an enemy's life. It multiplies what a kill earns and what a bar costs
    /// alike, so it cancels in every fraction and every level; the ledger keeps its values in this unit and the display
    /// divides it back out, which is why "x / y XP" reads as whole life points.
    /// </summary>
    public const double ExperiencePerLife = 0.001;

    /// <summary>A bar is this many kills of the strongest non-boss enemy killed so far, at the level it was set.</summary>
    public const double KillsOfStrongestEnemyPerBar = 200;

    /// <summary>The strongest boss fight killed again fills this share of a bar, at the level it was set.</summary>
    public const double StrongestBossShareOfBar = 0.15;

    /// <summary>Each anchor's bar grows by this factor per level since that anchor was set; a new anchor restarts its count,
    /// because a count from the first level runs away (1.05^100 is about 131).</summary>
    public const double GrowthPerLevel = 1.05;

    /// <summary>One ore broken, one tree felled or one torch placed fills this share of the current bar.</summary>
    public const double WorkShareOfBar = 0.001;

    /// <summary>What the player's own kill or work earns against the companion's.</summary>
    public const double PlayerShare = 0.5;

    /// <summary>
    /// A fill counts as complete within this relative distance of the bar. Work adds a thousandth of the bar a thousand
    /// times, and a thousandth is not exact in binary, so without it the thousandth ore can leave the bar a rounding error
    /// short; nothing real is ever this close to a bar without completing it.
    /// </summary>
    private const double CompletionTolerance = 1e-9;

    public int Level { get; private set; } = 1;

    /// <summary>Experience into the current level, in the ruling's unit.</summary>
    public double Into { get; private set; }

    // Zero until the first read prices it. Priced on read rather than at load, because a character loads at the menu, before
    // any world has a difficulty to price from; saved raw for the same reason, so saving at the menu stores no menu price.
    private double required;

    /// <summary>What the current level needs in full, in the ruling's unit, priced from the anchors on first read.</summary>
    public double Required
    {
        get { EnsurePriced(); return required; }
        private set => required = value;
    }

    /// <summary>The largest maximum life of any counting non-boss kill so far, zero before the first.</summary>
    public double EnemyAnchorLife { get; private set; }
    public int EnemyAnchorLevel { get; private set; } = 1;

    /// <summary>The largest whole-fight life of any boss fight killed so far, zero before the first.</summary>
    public double BossAnchorLife { get; private set; }
    public int BossAnchorLevel { get; private set; } = 1;

    /// <summary>
    /// Where the default enemy anchor comes from: the green slime's maximum life in the current world. A seam rather than a
    /// constant so a fixture can price a difficulty it names; the game's reading is the default.
    /// </summary>
    public static Func<double> DefaultEnemyLife = GreenSlimeLifeInThisWorld;

    /// <summary>The green slime's maximum life as this world's difficulty scales it, Journey's strength slider included.</summary>
    public static double GreenSlimeLifeInThisWorld() => GreenSlimeLife(Main.GameModeInfo);

    public static double GreenSlimeLife(GameModeData mode)
    {
        var slime = new NPC();
        slime.SetDefaults(NPCID.GreenSlime, new NPCSpawnParams { gameModeData = mode });
        return slime.lifeMax;
    }

    /// <summary>Experience earned into the level, rounded, for the notch's and the card's "x".</summary>
    public int IntoLevel => ToDisplay(Into);

    /// <summary>What the level needs, rounded, for "y". Saturates at <see cref="int.MaxValue"/>, which a bar priced from a
    /// modpack's late enemies can pass: the ledger keeps the true value and only the drawn number stops.</summary>
    public int NeededNow => Math.Max(1, ToDisplay(Required));

    /// <summary>Full is 1; the notch draws this.</summary>
    public float Fraction
    {
        get
        {
            double bar = Required;
            return bar > 0 ? (float)Math.Clamp(Into / bar, 0.0, 1.0) : 0f;
        }
    }

    private static int ToDisplay(double ruled)
    {
        double life = Math.Round(ruled / ExperiencePerLife);
        return life >= int.MaxValue ? int.MaxValue : (int)Math.Max(0, life);
    }

    /// <summary>The bar the pricing formula gives a level from the anchors as they stand: the larger of the bars that exist.</summary>
    public double FormulaFor(int level)
    {
        double enemyLife = EnemyAnchorLife > 0 ? EnemyAnchorLife : DefaultEnemyLife();
        int enemyLevel = EnemyAnchorLife > 0 ? EnemyAnchorLevel : 1;
        double bar = KillsOfStrongestEnemyPerBar * enemyLife * ExperiencePerLife * Math.Pow(GrowthPerLevel, level - enemyLevel);
        if (BossAnchorLife > 0)
            bar = Math.Max(bar, BossAnchorLife * ExperiencePerLife / StrongestBossShareOfBar * Math.Pow(GrowthPerLevel, level - BossAnchorLevel));
        return bar;
    }

    private void EnsurePriced()
    {
        if (required <= 0 || !double.IsFinite(required)) required = FormulaFor(Level);
    }

    /// <summary>What one credit did, for the record.</summary>
    public readonly record struct Credit(double Earned, int LevelBefore, int LevelAfter, bool EnemyAnchorChanged, bool BossAnchorChanged);

    /// <summary>A counting non-boss enemy died to the companion's killing blow or the player's.</summary>
    public Credit CreditEnemyKill(double lifeMax, bool byCompanion)
    {
        int before = Level;
        double earned = Earn(lifeMax * ExperiencePerLife * Share(byCompanion));
        bool anchored = false;
        if (lifeMax > 0 && (EnemyAnchorLife <= 0 || lifeMax > EnemyAnchorLife))
        {
            EnemyAnchorLife = lifeMax;
            EnemyAnchorLevel = Level;
            Reprice();
            anchored = true;
        }
        return new Credit(earned, before, Level, anchored, false);
    }

    /// <summary>A boss fight ended with its bodies killed; <paramref name="wholeLife"/> is the fight's total maximum life.</summary>
    public Credit CreditBossFight(double wholeLife, bool byCompanion)
    {
        int before = Level;
        double earned = Earn(wholeLife * ExperiencePerLife * Share(byCompanion));
        bool anchored = false;
        if (wholeLife > BossAnchorLife)
        {
            BossAnchorLife = wholeLife;
            BossAnchorLevel = Level;
            Reprice();
            anchored = true;
        }
        return new Credit(earned, before, Level, false, anchored);
    }

    /// <summary>One ore broken, one tree felled or one torch placed.</summary>
    public Credit CreditWork(bool byCompanion)
    {
        int before = Level;
        double earned = Earn(Required * WorkShareOfBar * Share(byCompanion));
        return new Credit(earned, before, Level, false, false);
    }

    private static double Share(bool byCompanion) => byCompanion ? 1.0 : PlayerShare;

    /// <summary>
    /// Fill the bar and complete every level the amount reaches, each at the price the anchors give before any anchor this
    /// credit sets: that is the first-kill rule, and it is why an anchor is only moved after this returns. A level's
    /// requirement is never below the one it replaces.
    /// </summary>
    private double Earn(double amount)
    {
        if (!(amount > 0) || !double.IsFinite(amount)) return 0;
        Into += amount;
        while (Into >= Required * (1 - CompletionTolerance))
        {
            Into = Math.Max(0, Into - Required);
            Level++;
            Required = Math.Max(Required, FormulaFor(Level));
        }
        return amount;
    }

    /// <summary>A new anchor reprices the level in progress, never below what it already needed.</summary>
    private void Reprice() => Required = Math.Max(Required, FormulaFor(Level));

    public TagCompound Save() => new()
    {
        ["level"] = Level,
        ["into"] = Into,
        ["required"] = required,
        ["enemyAnchorLife"] = EnemyAnchorLife,
        ["enemyAnchorLevel"] = EnemyAnchorLevel,
        ["bossAnchorLife"] = BossAnchorLife,
        ["bossAnchorLevel"] = BossAnchorLevel,
    };

    /// <summary>
    /// A save without a "level" is the placeholder curve's, which stored an integer "total" that bought nothing; it loads as
    /// a fresh level 1 and the "total" key is ignored, which the owner accepted. A value that is not a finite number in its
    /// range loads as the fresh value rather than refusing the character.
    /// </summary>
    public static CompanionExperience Load(TagCompound tag)
    {
        var experience = new CompanionExperience();
        if (!tag.ContainsKey("level")) return experience;
        static double NonNegative(double value) => double.IsFinite(value) && value > 0 ? value : 0;
        int level = Math.Max(1, tag.GetInt("level"));
        experience.Level = level;
        experience.Into = NonNegative(tag.GetDouble("into"));
        experience.required = NonNegative(tag.GetDouble("required"));
        experience.EnemyAnchorLife = NonNegative(tag.GetDouble("enemyAnchorLife"));
        experience.EnemyAnchorLevel = Math.Clamp(tag.GetInt("enemyAnchorLevel"), 1, level);
        experience.BossAnchorLife = NonNegative(tag.GetDouble("bossAnchorLife"));
        experience.BossAnchorLevel = Math.Clamp(tag.GetInt("bossAnchorLevel"), 1, level);
        return experience;
    }
}
