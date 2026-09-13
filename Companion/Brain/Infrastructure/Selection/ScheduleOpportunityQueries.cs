#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Movement;

namespace AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>
/// Decides which activities prepare in one comparison, so an expensive family cannot spend the tick's
/// whole planning allowance and starve the others or the movement that follows. Each family prepares
/// inside its own share: the incumbent and any non-excursion activity (protection, keeping company)
/// always prepare, at least one optional child prepares, and further optional children prepare only
/// while the family's share is unspent, each under a deadline narrowed to what remains of it. A child
/// skipped for lack of allowance is reported as deferred rather than absent, and the family's next
/// comparison starts from it, so every child is prepared at least once per family-size comparisons.
/// </summary>
public sealed class ScheduleOpportunityQueries
{
    public readonly record struct FamilyQueries(PurposeFamily Family, int Prepared, int Deferred, double Milliseconds);

    public FamilyQueries[] LastFamilies { get; private set; } = Array.Empty<FamilyQueries>();
    private readonly Dictionary<PurposeFamily, int> firstOptional = new();

    /// <summary>Returns, per registered activity, whether it was prepared this comparison.</summary>
    public bool[] Prepare(IReadOnlyList<CompanionAction> actions, CompanionAction? incumbent, double familyMilliseconds, Action<int> prepare)
    {
        var prepared = new bool[actions.Count];
        var families = Enum.GetValues<PurposeFamily>();
        var summary = new FamilyQueries[families.Length];
        var members = new List<int>();
        var clock = new Stopwatch();
        for (int f = 0; f < families.Length; f++)
        {
            members.Clear();
            for (int i = 0; i < actions.Count; i++)
                if (actions[i].Family == families[f]) members.Add(i);
            clock.Restart();
            int optionalRun = 0, deferred = 0, firstDeferred = -1;
            foreach (int i in members)
                if (Mandatory(actions[i], incumbent)) { prepare(i); prepared[i] = true; }
            int start = firstOptional.TryGetValue(families[f], out int remembered) ? remembered : 0;
            for (int offset = 0; offset < members.Count; offset++)
            {
                int i = members[(start + offset) % members.Count];
                if (prepared[i]) continue;
                if (optionalRun > 0 && LimitPlanningWork.Spent(clock, familyMilliseconds))
                {
                    deferred++;
                    if (firstDeferred < 0) firstDeferred = (start + offset) % members.Count;
                    continue;
                }
                using (LimitPlanningWork.Narrow(familyMilliseconds - clock.Elapsed.TotalMilliseconds))
                    prepare(i);
                prepared[i] = true;
                optionalRun++;
            }
            if (firstDeferred >= 0) firstOptional[families[f]] = firstDeferred;
            summary[f] = new(families[f], members.Count - deferred, deferred, clock.Elapsed.TotalMilliseconds);
        }
        LastFamilies = summary;
        return prepared;
    }

    /// <summary>Mandatory children run under the tick's total allowance, not a narrowed share. The
    /// incumbent's preparation revalidates the work the body is executing, and its approach queries
    /// are fresh bounded searches that keep no progress between calls, so a narrowed deadline would
    /// turn a resolvable approach into the same undecided answer every tick rather than a slower one.</summary>
    private static bool Mandatory(CompanionAction action, CompanionAction? incumbent)
        => ReferenceEquals(action, incumbent) || !action.IsExcursion;
}
