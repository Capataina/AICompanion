#nullable enable
using AICompanion.Companion.Brain.ActivityCoordination;
using AICompanion.Companion.Brain.BehaviourSelection;

namespace AICompanion.Companion.HeadsUpDisplay;

public enum HudSymbol { None, Gathering, Combat, Assistance, Mining, Chopping, Guarding, Hunting, Lighting, Collecting, Company }
public readonly record struct HudIcon(HudSymbol Symbol, string Name, bool Subdued = false);

/// <summary>Stable purpose symbols, never the currently equipped weapon, ore material or method.</summary>
public static class DescribeCompanionHud
{
    public static HudIcon Family(ActivitySnapshot state)
        => state.Downed ? new(HudSymbol.None, "Downed") : state.Family switch
        {
            PurposeFamily.Gathering => new(HudSymbol.Gathering, "Gathering"),
            PurposeFamily.Combat => new(HudSymbol.Combat, "Combat"),
            PurposeFamily.NearbyAssistance => new(HudSymbol.Assistance, "Nearby assistance"),
            _ => new(HudSymbol.None, "No family selected"),
        };

    public static HudIcon Activity(ActivitySnapshot state)
    {
        if (state.Downed) return new(HudSymbol.None, "Downed");
        HudIcon icon = state.Activity switch
        {
            "mine" => new(HudSymbol.Mining, "Mining"),
            "chop" => new(HudSymbol.Chopping, "Chopping"),
            "guard" => new(HudSymbol.Guarding, "Guarding you"),
            "hunt" => new(HudSymbol.Hunting, "Hunting"),
            "place-torches" => new(HudSymbol.Lighting, "Lighting"),
            "collect" => new(HudSymbol.Collecting, "Collecting"),
            "keep-company" => new(HudSymbol.Company, "Keeping company"),
            _ => new(HudSymbol.None, "No activity selected"),
        };
        return state.Suspended && icon.Symbol != HudSymbol.None ? icon with { Subdued = true, Name = icon.Name + " (paused)" } : icon;
    }
}
