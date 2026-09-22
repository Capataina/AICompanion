#nullable enable

using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// The one place the decision audit reads the live course from, and the only file on either side of
/// that seam that names <c>Selection/</c>.
///
/// <b>Why it is a file of its own rather than four lines inside the audit.</b>
/// <see cref="AuditDecisionContracts"/> is compiled a second time into <c>Tools/EngineReplay</c>,
/// because <see cref="RecordCourseTrace"/> is on that project's explicit source list and calls it;
/// that second compile has the transport, the event writer and the behaviour weights beside it and
/// nothing of the decision tree. Reaching for <c>CompanionNPC.Instance.Brain.Course</c> from the
/// audit would drag the whole of <c>Selection/</c> and <c>Activities/</c> across that boundary, which
/// is not a thing the headless suite can compile. So the audit takes its inputs through a delegate
/// and this installs the real one.
///
/// It is also the shared reader this folder's own rule asks for — a surface reads its producer
/// through one reader rather than reaching for a static itself, which is why
/// <see cref="ReadCourseWorthPerActivity"/> exists beside it. And it is the seam a fixture replaces
/// to drive one decision with no world at all.
/// </summary>
public static class ReadLiveCourseForAudit
{
    /// <summary>The five fact kinds a target may be published under: combat's own, and the four
    /// assistance and gathering domains, which name their facts by the domain string itself.
    /// Gathering's two are carried although no contract reads them yet, because the *age* of a target's
    /// evidence is only ever measurable by something that watched every snapshot, and a kind left out
    /// here can never have one afterwards.</summary>
    private static bool IsTargetKind(string kind) => kind switch
    {
        "combat-target" or "collect-target" or "light-target" or "pot-target" or "mine-target" or "chop-target" => true,
        _ => false,
    };

    /// <summary>Installs this reader as the audit's source. Called from the recorder's <c>Load</c>, so
    /// it is standing before any world opens and whether or not recording is switched on.</summary>
    public static void Install() => AuditDecisionContracts.Source = Read;

    /// <summary>
    /// The live census and the frozen observation's target facts, flattened to values.
    ///
    /// Returns null where there is no companion — a tick between a death and a respawn, or a fixture
    /// driving the recorder with no brain — which the audit reads as "nothing to audit" rather than as
    /// an empty world. The scan is restricted to the five target kinds by one string switch, so on the
    /// 22 September capture it touches about sixty of the fifteen hundred facts a decision carries.
    /// </summary>
    public static DecisionInputs? Read()
    {
        DecideCourseEachTick? course;
        try { course = CharacterBody.CompanionNPC.Instance?.Brain.Course; }
        catch (Exception) { return null; }
        if (course == null) return null;

        var admitted = new List<CensusAdmission>();
        foreach ((string domain, int usable, int unresolved, int unusable, string reason) in course.Admitted)
            admitted.Add(new(domain, usable, unresolved, unusable, reason));

        var targets = new List<TargetFact>();
        DecisionFactSnapshot? facts = course.Facts;
        if (facts != null)
        {
            IReadOnlyList<DecisionFact> all = facts.Facts;
            for (int i = 0; i < all.Count; i++)
            {
                DecisionFact fact = all[i];
                if (!IsTargetKind(fact.Key.Kind)) continue;
                targets.Add(new(fact.Key.Kind, fact.Key.ToString(),
                    fact.Evidence == FactEvidence.Observed, fact.Evidence.ToString()));
            }
        }
        return new(admitted, targets);
    }
}
