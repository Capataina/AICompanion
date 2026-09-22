#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether the decision audit was wired to anything in the session that wrote this capture.
///
/// <para>Every other reading of the audit reads what it *said*. This one reads whether it ran at all,
/// and it exists because the two ways its wiring fails are both silent in play and neither can be
/// caught by a headless row. The audit hangs off <c>RecordCourseTrace.Record</c> and takes its inputs
/// through a delegate that <c>ReadLiveCourseForAudit.Install</c> hands it from the recorder's
/// <c>Load</c>; remove the hook and every decision is recorded with nothing audited, and skip the
/// install and every decision is audited against no inputs, which quietly reduces six contracts to
/// the two transitions that read only the payload. Both look exactly like a session with nothing to
/// report — which is this folder's own standing trap, a diagnostic reading a producer that moved.</para>
///
/// <para>The capture's closing line carries both counts for exactly this. They are counts of the
/// session rather than of a row, so this check reads the trailer and the sidecar and no column; the
/// column it declares is a witness it never reads, because the counts first exist at schema 0.45.0
/// and a capture without them must skip by name rather than read an absence as a zero.</para>
/// </summary>
public sealed class TheDecisionAuditRanOnTheDecisionsTheCaptureHolds : ICheck, ICheckCoverage
{
    public string Name => "did the decision audit run on the decisions this capture holds";

    /// <summary>A witness, never read: `frame_ms` arrives with schema 0.45.0, which is the version that
    /// first writes the two counts in the closing line.</summary>
    public string[] Needs => new[] { "frame_ms" };

    public string? Missing(Session session)
        => CheckEvents.SidecarUnavailable(session, "the typed course decisions")
            ?? (Counts(session) == null
                ? "a capture closed by the 0.45.0 recorder, whose trailer carries the audit's own counts"
                : null);

    public IEnumerable<Finding> Run(Session session)
    {
        if (Counts(session) is not var (audited, read)) yield break;
        int decisions = ReadCourseDecisions.From(ReadGodsEyeEvents.Read(session.Path)).Decisions.Count;

        if (decisions > 0 && audited == 0)
            yield return new Finding(Severity.Definitive, Name,
                "the capture holds course decisions and the audit saw none of them",
                $"{decisions:n0} course decision(s) reached the sidecar and the audit counted 0. The audit is called from "
                    + "RecordCourseTrace.Record, so a count of zero beside a recorded decision means that call is gone or "
                    + "never reached — not that the decisions were clean. Every contract in this report measured nothing.",
                0, 0, decisions);

        if (audited > 0 && read == 0)
            yield return new Finding(Severity.Definitive, Name,
                "the audit ran on every decision and never once read the observation behind it",
                $"{audited:n0} decision(s) audited and 0 frozen observations read. ReadLiveCourseForAudit.Install hands the "
                    + "audit its source from the recorder's Load; with no source the four contracts that read the census and "
                    + "the target facts cannot fire at all, and only the two transitions that read the payload alone survive. "
                    + "A session in that state reports a handful of violations and looks healthier than one that is wired.",
                0, 0, (int)audited);
    }

    /// <summary>The two counts out of the closing trailer, or null where this capture's recorder never
    /// wrote them. They ride inside the <c>closing</c> value as <c>;key=value</c> pairs, which is how
    /// that line has always packed its counts.</summary>
    private static (long Audited, long Read)? Counts(Session session)
    {
        if (!session.Metadata.TryGetValue("closing", out string? closing)) return null;
        long? audited = Field(closing, "decisions-audited");
        long? read = Field(closing, "audit-observations-read");
        return audited is { } a && read is { } r ? (a, r) : null;
    }

    private static long? Field(string closing, string key)
    {
        foreach (string part in closing.Split(';'))
        {
            int equals = part.IndexOf('=');
            if (equals <= 0 || part[..equals] != key) continue;
            return long.TryParse(part[(equals + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                ? value : null;
        }
        return null;
    }
}
