#nullable enable

using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

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
///
/// <b>Nothing headless can prove this file is installed.</b> It is not on EngineReplay's compile list
/// — that is the point of it — and every fixture hands the audit a source of its own, so a tree where
/// <see cref="Install"/> is never called compiles and passes every row. The witness is the capture:
/// with no source the audit counts every decision and reads no observation, and
/// <c>AuditDecisionContracts.ObservationsRead</c> rides out on the closing line beside
/// <c>Audited</c> for the session reader to grade. Anything that moves the install call moves that
/// check's subject with it.
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
        "combat-target" or "collect-target" or "light-target" or "mine-target" or "chop-target" => true,
        _ => false,
    };

    /// <summary>Installs this reader as the audit's source. Called from the recorder's <c>Load</c>, so
    /// it is standing before any world opens and whether or not recording is switched on.</summary>
    public static void Install()
    {
        AuditDecisionContracts.Source = Read;
        AuditDecisionContracts.BindingSource = ReadBinding;
    }

    /// <summary>
    /// The step the activity holding the body was handed this tick, flattened for the effect contract,
    /// or null where it was handed none — keeping company, combat's continuation during an unsettled
    /// decision, or no companion at all.
    /// </summary>
    public static BoundStepForAudit? ReadBinding()
    {
        StepBinding? binding;
        try { binding = CharacterBody.CompanionNPC.Instance?.Brain.Activity.Binding; }
        catch (Exception) { return null; }
        return binding == null ? null : Flatten(binding, incidental: false);
    }

    /// <summary>
    /// The seam an accepted in-passing step is announced through: call it on the tick the course accepts
    /// the one-step binding, before the native call that performs it. See
    /// <see cref="AuditDecisionContracts.AcceptedIncidental"/> for the contract it keeps.
    /// </summary>
    public static void AcceptIncidental(StepBinding binding)
        => AuditDecisionContracts.AcceptIncidental(Flatten(binding, incidental: true), (long)Terraria.Main.GameUpdateCount);

    /// <summary>
    /// A step as values, with its work tile read by <c>ExecuteCourseBinding.WorkTileOf</c> — the same
    /// parser the positioner's work request is built from — for every step whose hand acts on a tile.
    /// It reads the purpose and the target rather than the domain, because a pot is tile work whichever
    /// domain publishes it; a drop is not, and carries its <c>item:&lt;slot&gt;</c> target.
    /// </summary>
    private static BoundStepForAudit Flatten(StepBinding binding, bool incidental)
    {
        OpportunityKey key = binding.Opportunity;
        int? x = null, y = null;
        // The pot is caught by its `tile:` target rather than by naming its purpose, because the purpose a
        // pot is ordered under is moving between domains while its target shape is not.
        if (key.Purpose is "mine" or "chop" or "light" || key.Target.StartsWith("tile:", StringComparison.Ordinal))
        {
            try { Microsoft.Xna.Framework.Point tile = ExecuteCourseBinding.WorkTileOf(key); x = tile.X; y = tile.Y; }
            // An unparseable tile target leaves the step without a work tile, and every tile effect under
            // it is then off its step — which is the right reading of a step nobody can locate.
            catch (ArgumentOutOfRangeException) { }
        }
        return new BoundStepForAudit(binding.Id, key.Domain, key.Purpose, key.Target, x, y, incidental);
    }

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
        // Every kind is tallied, not only the six target kinds, because the size contract the audit
        // keeps is now per kind and the runaway it exists to catch is `combat-use` — which is not a
        // target kind and so was invisible to this reader. One dictionary over a loop this method
        // already makes; the target list stays restricted, because the *age* contracts below it are
        // about targets and nothing else.
        var byKind = new Dictionary<string, int>(StringComparer.Ordinal);
        DecisionFactSnapshot? facts = course.Facts;
        if (facts != null)
        {
            IReadOnlyList<DecisionFact> all = facts.Facts;
            for (int i = 0; i < all.Count; i++)
            {
                DecisionFact fact = all[i];
                byKind[fact.Key.Kind] = byKind.TryGetValue(fact.Key.Kind, out int had) ? had + 1 : 1;
                if (!IsTargetKind(fact.Key.Kind)) continue;
                targets.Add(new(fact.Key.Kind, fact.Key.ToString(),
                    fact.Evidence == FactEvidence.Observed, fact.Evidence.ToString()));
            }
        }
        return new(admitted, targets, byKind);
    }
}
