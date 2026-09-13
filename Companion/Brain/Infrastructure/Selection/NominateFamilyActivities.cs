#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Companion.Brain.Infrastructure.Selection;

public enum PurposeFamily { Gathering, Combat, NearbyAssistance }
public readonly record struct FamilyCandidate(PurposeFamily Family, EvaluatedActivity Activity);
public readonly record struct FamilyNomination(PurposeFamily Family, EvaluatedActivity? Activity);

/// <summary>Families nominate concrete positive-value candidates. The parent compares the
/// same evaluated values; neither layer adds a weight, commitment bonus or discovery call.</summary>
public static class NominateFamilyActivities
{
    public static FamilyNomination[] Nominate(IReadOnlyList<FamilyCandidate> candidates)
    {
        var families = Enum.GetValues<PurposeFamily>();
        var nominations = new FamilyNomination[families.Length];
        for (int i = 0; i < families.Length; i++)
        {
            EvaluatedActivity? best = null;
            foreach (var candidate in candidates)
                if (candidate.Family == families[i] && Better(candidate.Activity, best)) best = candidate.Activity;
            nominations[i] = new(families[i], best);
        }
        return nominations;
    }

    public static EvaluatedActivity? Select(IReadOnlyList<FamilyNomination> nominations)
    {
        EvaluatedActivity? best = null;
        foreach (var nomination in nominations)
            if (nomination.Activity is { } candidate && Better(candidate, best)) best = candidate;
        return best;
    }

    private static bool Better(in EvaluatedActivity candidate, EvaluatedActivity? incumbent)
        => candidate.Error.Length == 0 && float.IsFinite(candidate.Final) && candidate.Final > 0
            && (incumbent is not { } current || candidate.Final > current.Final
                || candidate.Final == current.Final && candidate.Index < current.Index);
}
