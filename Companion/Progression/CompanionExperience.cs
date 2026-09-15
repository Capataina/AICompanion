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
///
/// <para>Every value it keeps — the experience into the level, the level's requirement and both anchors — is in Normal
/// difficulty terms: a credit is divided by this world's life multiplier as it arrives, and a number is multiplied back
/// only where it is shown. The owner ruled on 15 September 2026 that a level takes the same kills in any world a character
/// enters, and a character carries its ledger between worlds of different difficulty; a ledger kept in the terms of the
/// world it was last played in would make a Master-levelled character in a Normal world pay three times the kills for the
/// level in progress and for every anchor it brought. The multiplier is the game's own: the green slime's maximum life in
/// this world over its maximum life in Normal, so Expert, Master, Journey's strength and any mod that scales enemy life
/// through the game's difficulty all move it.</para>
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

    /// <summary>Experience into the current level, in the ruling's unit, in Normal terms.</summary>
    public double Into
    {
        get { Normalise(); return into; }
        private set => into = value;
    }
    private double into;

    // Zero until the first read prices it. Priced on read rather than at load, because a character loads at the menu, and
    // saved raw for the same reason, so saving at the menu stores no menu price.
    private double required;

    /// <summary>What the current level needs in full, in the ruling's unit, in Normal terms, priced from the anchors on first read.</summary>
    public double Required
    {
        get { Normalise(); EnsurePriced(); return required; }
        private set => required = value;
    }

    /// <summary>The largest maximum life, in Normal terms, of any counting non-boss kill that outgrew the green slime; zero before one.</summary>
    public double EnemyAnchorLife
    {
        get { Normalise(); return enemyAnchorLife; }
        private set => enemyAnchorLife = value;
    }
    private double enemyAnchorLife;
    public int EnemyAnchorLevel { get; private set; } = 1;

    /// <summary>The largest whole-fight life, in Normal terms, of any boss fight killed so far, zero before the first.</summary>
    public double BossAnchorLife
    {
        get { Normalise(); return bossAnchorLife; }
        private set => bossAnchorLife = value;
    }
    private double bossAnchorLife;
    public int BossAnchorLevel { get; private set; } = 1;

    // A ledger saved before its values were kept in Normal terms holds the terms of whatever world it was last saved in,
    // which no save recorded. It is converted by the first world that reads it, on the reading that the world it is loaded
    // into is the world it was played in — true of every character saved before this change, which had one world to play.
    private bool savedInWorldTerms;

    /// <summary>
    /// The green slime's maximum life in the current world. A seam rather than a constant so a fixture can price a difficulty
    /// it names; the game's reading is the default. With <see cref="NormalEnemyLife"/> it is also this world's life multiplier.
    /// </summary>
    public static Func<double> DefaultEnemyLife = GreenSlimeLifeInThisWorld;

    /// <summary>The green slime's maximum life in Normal: the floor under the enemy anchor, and the Normal half of the multiplier.</summary>
    public static Func<double> NormalEnemyLife = () => GreenSlimeLife(GameModeData.NormalMode);

    /// <summary>The green slime's maximum life as this world's difficulty scales it, Journey's strength slider included.</summary>
    public static double GreenSlimeLifeInThisWorld() => GreenSlimeLife(Main.GameModeInfo);

    public static double GreenSlimeLife(GameModeData mode)
    {
        var slime = new NPC();
        slime.SetDefaults(NPCID.GreenSlime, new NPCSpawnParams { gameModeData = mode });
        return slime.lifeMax;
    }

    // The multiplier is read by every drawn number, every frame, and reading it builds two NPCs, so it is kept for the tick
    // it was read in, for the difficulty it was read under and for the readings it was read from.
    private static (ulong Tick, int Mode, Func<double> World, Func<double> Normal, double Value)? multiplier;

    /// <summary>This world's enemy life over Normal's, by the game's own green slime; one when either reading is unusable.</summary>
    public static double LifeMultiplier()
    {
        if (multiplier is { } kept && kept.Tick == Main.GameUpdateCount && kept.Mode == Main.GameMode
            && ReferenceEquals(kept.World, DefaultEnemyLife) && ReferenceEquals(kept.Normal, NormalEnemyLife))
            return kept.Value;
        double world = DefaultEnemyLife(), normal = NormalEnemyLife();
        double value = world > 0 && normal > 0 && double.IsFinite(world / normal) ? world / normal : 1;
        multiplier = (Main.GameUpdateCount, Main.GameMode, DefaultEnemyLife, NormalEnemyLife, value);
        return value;
    }

    /// <summary>A value this ledger keeps, in the terms of the world being played: what the record and the display show.</summary>
    public double InWorldTerms(double normal) => normal * LifeMultiplier();

    /// <summary>Experience earned into the level, rounded, in this world's terms, for the notch's and the card's "x".</summary>
    public int IntoLevel => ToDisplay(InWorldTerms(Into));

    /// <summary>What the level needs, rounded, in this world's terms, for "y". Saturates at <see cref="int.MaxValue"/>, which a
    /// bar priced from a modpack's late enemies can pass: the ledger keeps the true value and only the drawn number stops.</summary>
    public int NeededNow => Math.Max(1, ToDisplay(InWorldTerms(Required)));

    /// <summary>Full is 1; the notch draws this. A ratio, so the same in every world's terms.</summary>
    public float Fraction
    {
        get
        {
            double bar = Required;
            return bar > 0 ? (float)Math.Clamp(Into / bar, 0.0, 1.0) : 0f;
        }
    }

    /// <summary>Converts a ledger saved in a world's own terms, once, by the first world that reads it.</summary>
    private void Normalise()
    {
        if (!savedInWorldTerms || Main.gameMenu) return;
        savedInWorldTerms = false;
        double m = LifeMultiplier();
        into /= m;
        required /= m;
        enemyAnchorLife /= m;
        bossAnchorLife /= m;
    }

    private static int ToDisplay(double ruled)
    {
        double life = Math.Round(ruled / ExperiencePerLife);
        return life >= int.MaxValue ? int.MaxValue : (int)Math.Max(0, life);
    }

    /// <summary>
    /// The enemy anchor the pricing reads, in Normal terms: the strongest kill if it outgrew the green slime, else the green
    /// slime set at level 1. The slime is a floor and not only a default, because a weaker first kill — a one-life critter-like
    /// enemy, of which the game has several — anchored at its own life priced a bar far below the one it replaced, and the
    /// never-lower rule then held the bar flat for every level until a stronger kill came along.
    /// </summary>
    private (double Life, int Level) EnemyPrice()
    {
        double floor = NormalEnemyLife();
        return EnemyAnchorLife > floor ? (EnemyAnchorLife, EnemyAnchorLevel) : (floor, 1);
    }

    /// <summary>The bar the pricing formula gives a level from the anchors as they stand, in Normal terms: the larger of the bars that exist.</summary>
    public double FormulaFor(int level)
    {
        (double enemyLife, int enemyLevel) = EnemyPrice();
        double bar = KillsOfStrongestEnemyPerBar * enemyLife * ExperiencePerLife * Math.Pow(GrowthPerLevel, level - enemyLevel);
        if (BossAnchorLife > 0)
            bar = Math.Max(bar, BossAnchorLife * ExperiencePerLife / StrongestBossShareOfBar * Math.Pow(GrowthPerLevel, level - BossAnchorLevel));
        return bar;
    }

    private void EnsurePriced()
    {
        if (required <= 0 || !double.IsFinite(required)) required = FormulaFor(Level);
    }

    /// <summary>What one credit did, for the record; <see cref="Earned"/> is in Normal terms.</summary>
    public readonly record struct Credit(double Earned, int LevelBefore, int LevelAfter, bool EnemyAnchorChanged, bool BossAnchorChanged);

    /// <summary>A counting non-boss enemy died to the companion's killing blow or the player's; <paramref name="lifeMax"/> is
    /// its maximum life as this world set it.</summary>
    public Credit CreditEnemyKill(double lifeMax, bool byCompanion)
    {
        int before = Level;
        double life = lifeMax / LifeMultiplier();
        double earned = Earn(life * ExperiencePerLife * Share(byCompanion));
        bool anchored = false;
        if (life > EnemyPrice().Life)
        {
            EnemyAnchorLife = life;
            EnemyAnchorLevel = Level;
            Reprice();
            anchored = true;
        }
        return new Credit(earned, before, Level, anchored, false);
    }

    /// <summary>A boss fight ended with its bodies killed; <paramref name="wholeLife"/> is the fight's total maximum life as
    /// this world set it.</summary>
    public Credit CreditBossFight(double wholeLife, bool byCompanion)
    {
        int before = Level;
        wholeLife /= LifeMultiplier();
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

    /// <summary>The save key naming the terms the values are in; a save without it was written in its world's own terms.</summary>
    private const string UnitsKey = "units", NormalUnits = "normal-life";

    // Written through the fields rather than the properties, so a ledger saved at the menu before any world converted it is
    // saved exactly as it was loaded, still marked as in its world's terms.
    public TagCompound Save() => new()
    {
        [UnitsKey] = savedInWorldTerms ? "world-life" : NormalUnits,
        ["level"] = Level,
        ["into"] = into,
        ["required"] = required,
        ["enemyAnchorLife"] = enemyAnchorLife,
        ["enemyAnchorLevel"] = EnemyAnchorLevel,
        ["bossAnchorLife"] = bossAnchorLife,
        ["bossAnchorLevel"] = BossAnchorLevel,
    };

    /// <summary>
    /// A save without a "level" is the placeholder curve's, which stored an integer "total" that bought nothing; it loads as
    /// a fresh level 1 and the "total" key is ignored, which the owner accepted. A save with a level but not in Normal terms
    /// is converted by the first world that reads it. A value that is not a finite number in its range loads as the fresh
    /// value rather than refusing the character.
    /// </summary>
    public static CompanionExperience Load(TagCompound tag)
    {
        var experience = new CompanionExperience();
        if (!tag.ContainsKey("level")) return experience;
        static double NonNegative(double value) => double.IsFinite(value) && value > 0 ? value : 0;
        int level = Math.Max(1, tag.GetInt("level"));
        experience.Level = level;
        experience.into = NonNegative(tag.GetDouble("into"));
        experience.required = NonNegative(tag.GetDouble("required"));
        experience.enemyAnchorLife = NonNegative(tag.GetDouble("enemyAnchorLife"));
        experience.EnemyAnchorLevel = Math.Clamp(tag.GetInt("enemyAnchorLevel"), 1, level);
        experience.bossAnchorLife = NonNegative(tag.GetDouble("bossAnchorLife"));
        experience.BossAnchorLevel = Math.Clamp(tag.GetInt("bossAnchorLevel"), 1, level);
        experience.savedInWorldTerms = tag.GetString(UnitsKey) != NormalUnits;
        return experience;
    }
}
