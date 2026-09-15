#nullable enable

using System;
using Terraria.ModLoader.IO;

namespace AICompanion.Companion.Progression;

/// <summary>
/// The companion's experience: a running total earned only from the actions the companion itself
/// completes beside the player (the owner's ruling of 14 September 2026 — the player's own kills,
/// ore and torches earn it nothing), and a level read off that total on a fixed curve. It is
/// character state and persists with the player. Nothing in the brain reads it: a level changes
/// what the companion can do and survive through the mastery tree, never what it decides.
///
/// The award comes from the attempt-outcome record the activity owner already emits with
/// attribution, so a new activity earns experience without a new hook: an attempt that concluded
/// with productive effects it was credited for is worth those effects. What one productive effect
/// is worth per family is a tuning number and lives here beside the curve, where the next person
/// balancing levels will look.
/// </summary>
public sealed class CompanionExperience
{
    /// <summary>
    /// Experience needed to go from level n to n+1. Quadratic, so early levels arrive in the first
    /// cave and the tree's later nodes take a playthrough: level 2 costs 100, level 10 costs 1,000,
    /// level 30 costs 3,000. The balancing target from the mastery design is about half the tree
    /// opened by Moon Lord; nothing has measured that yet.
    /// </summary>
    public static int NeededForLevel(int level) => Math.Max(1, level) * 100;

    public int Total { get; private set; }

    public int Level
    {
        get
        {
            int level = 1, spent = 0;
            while (spent + NeededForLevel(level) <= Total)
            {
                spent += NeededForLevel(level);
                level++;
            }
            return level;
        }
    }

    /// <summary>Experience earned inside the current level, for the notch's "x / y".</summary>
    public int IntoLevel
    {
        get
        {
            int level = 1, spent = 0;
            while (spent + NeededForLevel(level) <= Total)
            {
                spent += NeededForLevel(level);
                level++;
            }
            return Total - spent;
        }
    }

    /// <summary>Experience the current level needs in full, the notch's "y".</summary>
    public int NeededNow => NeededForLevel(Level);

    /// <summary>Full is 1; the notch draws this.</summary>
    public float Fraction => Math.Clamp((float)IntoLevel / NeededNow, 0f, 1f);

    /// <summary>
    /// What one productive effect of a family's work is worth: a hit landed or a threat removed for
    /// combat, a tile broken for gathering, a torch placed or a drop taken for nearby assistance.
    /// Combat pays most because its effects are rarest per attempt and cost the companion the most;
    /// these are the tuning numbers the folder guide says live here. A shared completion, where the
    /// player did part of the work, pays half.
    /// </summary>
    public static int WorthPerEffect(Brain.Infrastructure.Selection.PurposeFamily family) => family switch
    {
        Brain.Infrastructure.Selection.PurposeFamily.Combat => 15,
        Brain.Infrastructure.Selection.PurposeFamily.Gathering => 10,
        Brain.Infrastructure.Selection.PurposeFamily.NearbyAssistance => 5,
        _ => 0,
    };

    /// <summary>
    /// Credit an amount. Negative and zero amounts are refused rather than clamped, because an
    /// attempt that produced nothing is not an event here and a caller passing a negative number
    /// has a bug worth surfacing.
    /// </summary>
    public void Award(int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "experience is only ever earned");
        Total = checked(Total + amount);
    }

    public TagCompound Save() => new() { ["total"] = Total };

    public static CompanionExperience Load(TagCompound tag)
        => new() { Total = Math.Max(0, tag.GetInt("total")) };
}
