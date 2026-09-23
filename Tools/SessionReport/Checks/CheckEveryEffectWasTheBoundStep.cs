#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether every native effect the companion caused was the step the course accepted, and landed on
/// that step's target.
///
/// <para><b>What it reads.</b> From schema 0.47.0 every `tool-effect`, `world-interaction` and `pickup`
/// occurrence ends with the step it was performed under — `binding-id`, `binding-origin` and
/// `binding-verdict` — written by the recorder's effect contract (`AuditDecisionContracts.ObserveEffect`)
/// at the moment of the effect. The contract's own `contract-violation` occurrences of kinds
/// `effect-without-binding` and `effect-off-binding` carry the session's running totals, which survive the
/// coalescing the individual records do not. This check reads both and reports each kind with its first
/// ticks and what the effect landed on, so a reader of a capture gets the finding without opening the
/// sidecar.</para>
///
/// <para><b>Why it is Definitive.</b> The verdict is not a threshold: the contract compares the tile or
/// item the hand acted on with the one the accepted step named, and an effect with no step or beside its
/// step is a second chooser by construction — the plan's tick step 9, "no tactical fallback may secretly
/// fire and leave the course believing a different action happened".</para>
///
/// <para><b>An effect the contract could not judge is its own finding.</b> `unaudited` means the binding
/// reader was never installed, which leaves both kinds at zero while every effect went unexamined — the
/// same silent-wiring failure `TheDecisionAuditRanOnTheDecisionsTheCaptureHolds` names for the decision
/// audit.</para>
/// </summary>
public sealed class EveryEffectWasTheAcceptedStep : ICheck, ICheckCoverage
{
    /// <summary>The schema that first names the step on every effect occurrence.</summary>
    internal static readonly Version First = new(0, 47, 0);

    internal const string WithoutBinding = "effect-without-binding";
    internal const string OffBinding = "effect-off-binding";

    /// <summary>The three occurrence kinds the effect contract annotates.</summary>
    internal static readonly string[] EffectKinds = { "tool-effect", "world-interaction", "pickup" };

    public string Name => "was every effect the companion caused the accepted step's";

    public string[] Needs => new[] { "tick" };

    public string? Missing(Session session)
    {
        if (CheckEvents.SidecarUnavailable(session, "the effect occurrences and the step each was performed under") is { } unavailable) return unavailable;
        return CompletedTransferClaimsWereReceived.SchemaAtLeast(session, First) ? null
            : $"effect occurrences naming the step they were performed under (first written by schema {First})";
    }

    public IEnumerable<Finding> Run(Session session)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        var byVerdict = new Dictionary<string, List<GodsEyeEvent>>(StringComparer.Ordinal);
        foreach (GodsEyeEvent e in log.Events)
        {
            if (!EffectKinds.Contains(e.kind, StringComparer.Ordinal)) continue;
            string? verdict = ReadGodsEyeEvents.Field(e.detail, "binding-verdict");
            if (verdict == null) continue;
            if (!byVerdict.TryGetValue(verdict, out var list)) byVerdict[verdict] = list = new List<GodsEyeEvent>();
            list.Add(e);
        }
        IReadOnlyDictionary<string, long> totals = ViolationTotals(log);
        // The denominator is the recorder's own `effects-audited` from the closing line where the capture
        // closed, because it counts every effect the contract judged including any whose occurrence the
        // transport dropped; a capture killed before its closing line falls back to the occurrences seen.
        long judged = ClosingEffectsAudited(session)
            ?? byVerdict.Where(pair => pair.Key is "bound" or WithoutBinding or OffBinding).Sum(pair => pair.Value.Count);

        foreach (string kind in new[] { WithoutBinding, OffBinding })
        {
            List<GodsEyeEvent> hits = byVerdict.TryGetValue(kind, out var found) ? found : new List<GodsEyeEvent>();
            long total = totals.TryGetValue(kind, out long counted) ? counted : 0;
            if (hits.Count == 0 && total == 0) continue;
            string what = kind == WithoutBinding
                ? "native effect(s) the companion caused with no accepted step: the activity holding the body was handed none and no in-passing step was accepted on that tick"
                : "native effect(s) that landed beside the accepted step they were performed under: the hand worked a target of its own choosing";
            string examples = string.Join("; ", hits.Take(3).Select(Describe));
            yield return new Finding(Severity.Definitive, Name, $"{Math.Max(hits.Count, total).ToString(CultureInfo.InvariantCulture)} {what}",
                $"{hits.Count.ToString(CultureInfo.InvariantCulture)} effect occurrence(s) of the {judged.ToString(CultureInfo.InvariantCulture)} effect(s) the contract judged read `{kind}`, "
                    + $"and the recorder's own `contract-violation` records count {total.ToString(CultureInfo.InvariantCulture)} in total"
                    + (hits.Count > 0 ? $". First: {examples}." : ".")
                    + " An effect performed off the course's step leaves the course believing a different action happened, which is the plan's tick step 9.",
                hits.Count > 0 ? (int)hits[0].tick : 0, hits.Count > 0 ? (int)hits[^1].tick : 0, (int)Math.Max(hits.Count, total));
        }

        if (byVerdict.TryGetValue("unaudited", out var unread) && unread.Count > 0)
            yield return new Finding(Severity.Definitive, Name,
                $"{unread.Count.ToString(CultureInfo.InvariantCulture)} native effect(s) went unjudged because the effect contract had no binding reader",
                "`binding-verdict=unaudited` means `AuditDecisionContracts.BindingSource` was null when the effect was recorded; "
                    + "`ReadLiveCourseForAudit.Install` sets it from the recorder's Load. With it unset both effect kinds stay at zero "
                    + "while no effect was examined, so a clean reading of this capture's effect contract is not evidence of anything.",
                (int)unread[0].tick, (int)unread[^1].tick, unread.Count);
    }

    /// <summary>One effect occurrence in a sentence: the tick, the kind, where it landed and the step it
    /// was judged against.</summary>
    private static string Describe(GodsEyeEvent e)
    {
        // A pickup's position is the item's and its identity is `related`; the tile effects carry the
        // tile's world position in `expected`.
        string where = e.kind == "pickup"
            ? $"item {e.related}"
            : string.Create(CultureInfo.InvariantCulture, $"tile {(int)(e.expected_x / 16)},{(int)(e.expected_y / 16)}");
        return string.Create(CultureInfo.InvariantCulture,
            $"tick {e.tick} {e.kind} {e.label} on {where} under step {ReadGodsEyeEvents.Field(e.detail, "binding-id") ?? "?"} ({ReadGodsEyeEvents.Field(e.detail, "binding-origin") ?? "?"})");
    }

    /// <summary>The effect contract's own count of judged effects, from the `# closing=` line (0.47.0), or
    /// null where the capture never closed or predates it.</summary>
    internal static long? ClosingEffectsAudited(Session session)
    {
        if (!session.Metadata.TryGetValue("closing", out string? closing)) return null;
        foreach (string part in closing.Split(';'))
            if (part.StartsWith("effects-audited=", StringComparison.Ordinal)
                && long.TryParse(part["effects-audited=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                return value;
        return null;
    }

    /// <summary>The largest running total each effect kind's `contract-violation` records reached, which is
    /// the session's count whatever the coalescing kept.</summary>
    internal static IReadOnlyDictionary<string, long> ViolationTotals(GodsEyeEventLog log)
    {
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (GodsEyeEvent e in log.Events)
        {
            if (!string.Equals(e.payload_kind, "contract-violation", StringComparison.Ordinal)) continue;
            if (e.payload is not { ValueKind: System.Text.Json.JsonValueKind.Object } payload
                || !payload.TryGetProperty("Fields", out var fields)
                || !fields.TryGetProperty("violation", out var violation)
                || !violation.TryGetProperty("Text", out var kindText)) continue;
            string kind = kindText.GetString() ?? "";
            if (kind is not (WithoutBinding or OffBinding)) continue;
            long total = fields.TryGetProperty("occurrences-total", out var totalField)
                && totalField.TryGetProperty("Text", out var totalText)
                && long.TryParse(totalText.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 1;
            totals[kind] = Math.Max(totals.TryGetValue(kind, out long had) ? had : 0, total);
        }
        return totals;
    }
}
