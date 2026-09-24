extern alias live;

using System.Reflection;
using AICompanion.Tools.Ledger;

using Audit = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.AuditDecisionContracts;
using BoundStepForAudit = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BoundStepForAudit;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using ReadLiveCourseForAudit = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.ReadLiveCourseForAudit;
using RecordCourseTrace = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.RecordCourseTrace;
using StepBinding = live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.StepBinding;

/// <summary>
/// The decision and effect contracts the recorder audits in play, read around every default case, so a
/// fixture that drove the brain is graded on them as a second check beside its own assertions.
///
/// <para><b>What reaches the audit in a headless case, and what cannot.</b> The eight contract kinds come in
/// through three doors with different locks. The six decision contracts are audited inside
/// <c>RecordCourseTrace.Record</c>, which in the game runs them only while a recording is open; this file sets
/// its harness-only <c>AuditWithoutRecording</c> before every case, so every decision a case's brain records is
/// audited whether or not the case opened a recording, against a <c>Source</c> reading the course of the
/// companion <see cref="VerifyCompanionLifecycle.Create"/> built last (a case that built none audits with no
/// observation, which the contracts that need one skip). <c>decide-overran-allowance</c> is still called only
/// from the recorder's own per-tick row, so it is counted only where a case opened a recording, and it is a
/// wall clock this suite never grades anyway. The two effect contracts need no recording, only a
/// <c>BindingSource</c> to judge an effect against, and this file supplies that too: the step the case's
/// companion was handed.</para>
///
/// <para><b>Why not the live reader.</b> <c>ReadLiveCourseForAudit.ReadBinding</c> finds the companion by
/// scanning <c>Main.npc</c>, and <c>Create</c> puts no body in a slot, so that reader returns "no step" for
/// every fixture and every strike in the suite would be counted as a hand acting without one. The reader
/// here returns what the case's own companion holds, and throws — which the audit files as
/// <c>unaudited</c> and never as a violation — when the case built no companion through <c>Create</c>.
/// A step is flattened by the live reader's own private <c>Flatten</c>, called by reflection and checked
/// to exist before any case runs, so the work tile a strike is judged against is the one play uses.</para>
///
/// <para><b>What a firing means, per kind.</b> Five kinds are never legitimate and fail the case, which is the
/// fuzzer's own ruling on the same contracts (<c>FuzzTheDecisionContracts</c> asserts all five at zero):
/// the census contradicting its binder, an empty course beside usable work, one fact kind past its
/// bound, and a native effect with no step or beside its step. Two fire legitimately when a scene removes
/// what a course held — <c>accepted-use-absent-next-tick</c> and <c>activity-exited-during-decision</c> — and
/// a fixture that kills or teleports a hostile does that on purpose, so they are a measure, compared with
/// the case's own history. <c>decide-overran-allowance</c> is a wall clock, which the owner ruled is never a
/// pass line and which this suite times with its allowances lifted, so it is printed in the measure's
/// message and carried by no value.</para>
/// </summary>
internal static class AuditEveryCase
{
    /// <summary>A case whose subject is the audit: it fires contracts on purpose or grades the counts
    /// itself. The check still files its counts, and fails nothing.</summary>
    internal const string AuditsTheAuditTag = "audits-the-audit";

    /// <summary>The tag on the measure this check files, so the audit's rows can be read apart.</summary>
    internal const string ContractAuditTag = "contract-audit";

    /// <summary>The sub-row the measure is filed under.</summary>
    internal const string MeasureRow = "contract audit";

    internal static readonly string[] NeverLegitimate =
    {
        "census-admitted-binder-refused",
        "empty-course-beside-usable-work",
        "fact-count-above-bound",
        Audit.EffectWithoutBinding,
        Audit.EffectOffBinding,
    };

    internal static readonly string[] WorldExplainable =
    {
        "accepted-use-absent-next-tick",
        "activity-exited-during-decision",
    };

    internal const string WallClock = "decide-overran-allowance";

    private static CompanionNPC? latest;

    private static readonly MethodInfo Flatten = typeof(ReadLiveCourseForAudit)
        .GetMethod("Flatten", BindingFlags.Static | BindingFlags.NonPublic, new[] { typeof(StepBinding), typeof(bool) })
        ?? throw new InvalidOperationException("ReadLiveCourseForAudit.Flatten(StepBinding, bool) is gone, so the effect audit cannot be read around a case; "
            + "follow the rename here rather than letting every strike read unaudited");

    /// <summary>The companion a fixture just built is the one whose step an effect is judged against.</summary>
    internal static void Created(CompanionNPC companion) => latest = companion;

    /// <summary>Called at the end of the per-case reset, after the audit's own readers were cleared.</summary>
    internal static void BeforeCase()
    {
        latest = null;
        Audit.BindingSource = ReadTheCaseCompanionsStep;
        Audit.Source = () => latest == null ? null : ReadLiveCourseForAudit.ReadCourse(latest.Brain.Course);
        RecordCourseTrace.AuditWithoutRecording = true;
    }

    private static BoundStepForAudit? ReadTheCaseCompanionsStep()
    {
        CompanionNPC companion = latest
            ?? throw new InvalidOperationException("no companion was built through VerifyCompanionLifecycle.Create in this case");
        StepBinding? binding = companion.Brain.Activity.Binding;
        return binding == null ? null : (BoundStepForAudit)Flatten.Invoke(null, new object[] { binding, false })!;
    }

    /// <summary>The after-case check: the failures it adds, and the measure it files when anything was
    /// audited at all.</summary>
    internal static int Check(string name)
    {
        long decisions = Audit.Audited, observations = Audit.ObservationsRead, effects = Audit.EffectsAudited;
        if (decisions == 0 && effects == 0) return 0;
        IReadOnlyDictionary<string, long> counts = Audit.Counts;
        long Count(string kind) => counts.TryGetValue(kind, out long n) ? n : 0;
        EmitLedgerRows.RunningCase? running = EmitLedgerRows.Current;
        bool exempt = running?.Tags?.Contains(AuditsTheAuditTag) == true;

        int failures = 0;
        foreach (string kind in NeverLegitimate)
        {
            long fired = Count(kind);
            if (fired == 0 || exempt) continue;
            string last = (kind is Audit.EffectWithoutBinding or Audit.EffectOffBinding) && Audit.LastEffectViolation.Length > 0
                ? $"; the last: {Audit.LastEffectViolation}" : "";
            EmitLedgerRows.Detail($"contract audit: {kind} fired {fired} time(s) in this case{last}");
            failures++;
        }

        long explained = WorldExplainable.Sum(Count);
        string breakdown = string.Join(", ", counts.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));
        string message = $"decisions audited {decisions}, observations read {observations}, effects audited {effects}; "
            + (breakdown.Length == 0 ? "no contract fired" : breakdown)
            + (exempt ? $"; {AuditsTheAuditTag}: this case fires or grades the audit itself, so no kind fails it" : "")
            + (Count(WallClock) > 0 ? $"; {WallClock} is a wall clock under lifted allowances and is carried by no value" : "");
        if (running is { } outer)
            EmitLedgerRows.Measure(outer.Instrument, outer.Suite, outer.Name + EmitLedgerRows.SubRowSeparator + MeasureRow,
                explained, "firings", "down", outer.Mode,
                (outer.Tags ?? Array.Empty<string>()).Where(tag => tag != EmitLedgerRows.TimedTag).Append(ContractAuditTag).ToArray(), message);
        return failures;
    }
}
