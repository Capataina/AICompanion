#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AICompanion.Tools.Ledger;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// A number this capture holds, produced by an instrument rather than by whoever last opened the
/// file, and kept in the ledger so the next capture is compared rather than read.
///
/// A measure is deliberately not a <see cref="ICheck"/>, and the distinction is the point rather
/// than tidiness. A check decides: it knows a rule the producer guarantees and reports a record
/// that breaks it, so it can be Definitive without a threshold. A measure decides nothing — it
/// says the companion was behind the moving player by more than three tiles on 71.5% of rows, and
/// whether that is bad is a question about the design that the plan answers and this file does
/// not. Folding the two together is how a number nobody declared becomes a pass line by accident,
/// which is the failure the whole ledger was specified to refuse.
///
/// So every row here carries a value and never a verdict, and the plan's declared pass lines
/// travel as tags on the row rather than as comparisons in the code. The scoreboard grades a
/// measure against the previous run and a noise band; nothing here grades it at all.
///
/// The before-numbers each of these must reproduce are pinned in <c>Tests/PlayMeasureTests.cs</c>
/// against the 13:27 capture of 14 September, because a measure that has never been run against a
/// real recording is a function, not an instrument: the defects these exist to track live in the
/// sparse, hub-shaped shape of a real play and a synthetic row set has none of them.
/// </summary>
public interface IMeasure
{
    /// <summary>The ledger case stem. Every row this measure emits is this name and a slash.</summary>
    string Name { get; }

    /// <summary>Every column it cannot work without. One missing name skips the whole measure.</summary>
    string[] Needs { get; }

    /// <summary>Null when the capture carries what it reads; otherwise what is absent, phrased to follow "the file has no".</summary>
    string? Missing(Session session) => null;

    IEnumerable<LedgerRow> Rows(Session session);
}

/// <summary>
/// The shared shape of a row these emit, so an instrument name, a suite and a mode are not retyped
/// eleven times with one of them eventually different.
/// </summary>
internal static class PlayRow
{
    public const string Instrument = "session-report";
    public const string Suite = "play";

    /// <summary>
    /// A share, carried as a percentage rather than a fraction because every number quoted about
    /// these captures in the research and the commit bodies is a percentage, and a ledger whose
    /// units differ from the prose around it makes every comparison a conversion.
    /// </summary>
    public static LedgerRow Share(string @case, double numerator, double denominator, string? direction, string message, params string[] tags)
        => new(Instrument, Suite, @case, "measure",
            denominator <= 0 ? 0 : 100.0 * numerator / denominator, "%", direction, "capture", tags,
            Message: denominator <= 0 ? $"no rows qualified: {message}" : $"{numerator:n0} of {denominator:n0} — {message}");

    public static LedgerRow Count(string @case, double value, string unit, string? direction, string message, params string[] tags)
        => new(Instrument, Suite, @case, "measure", value, unit, direction, "capture", tags, Message: message);

    public static LedgerRow Skipped(string @case, string reason)
        => new(Instrument, Suite, @case, "skipped", Mode: "capture", Message: reason);
}

/// <summary>
/// Reading conventions these measures share, each of which was a way to get the arithmetic wrong.
/// </summary>
internal static class ReadPlay
{
    /// <summary>
    /// The x of a <c>x,y</c> pair, parsed as a double straight from the cell text.
    ///
    /// Going through the parsed float column instead is wrong in two ways that both showed up the
    /// first time these measures ran. A float carries about seven significant digits, and a world
    /// coordinate in this capture is five digits before the point, so <c>54334.46</c> comes back
    /// rounded and every offset computed from it is out by a pixel or two — enough to move the
    /// median of eight thousand rows by three. Worse, a float widened back to a double is not the
    /// number that was written: <c>1.20</c> becomes 1.2000000476837158, which is greater than 1.2,
    /// so a threshold test on "faster than 1.2" silently admitted 114 rows recorded at exactly the
    /// floor and changed the denominator of every share taken from them.
    ///
    /// Both <c>npc_px</c> and <c>player_px</c> are the body's <c>Bottom</c>, whose X is the box's
    /// *centre* rather than its left edge; reading either as a left edge shifts every offset by
    /// half a body width, which produced a confident wrong diagnosis of the platform freeze once
    /// already. Taking both from the same convention is what makes their difference mean anything.
    /// </summary>
    public static double? Leading(Column column, int row)
    {
        string cell = column.Text[row];
        int comma = cell.IndexOf(',');
        if (comma <= 0) return null;
        return double.TryParse(cell.AsSpan(0, comma), NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : null;
    }

    /// <summary>
    /// The eligibility word in an <c>&lt;activity&gt;_offer</c> cell, which is written
    /// <c>Eligibility:reason</c>. An absent column and a cell the writer left as a dash both
    /// answer null, so a caller distinguishes "not eligible" from "not recorded" instead of
    /// folding the second into the first.
    /// </summary>
    public static string? Eligibility(Session session, string activity, int row)
    {
        Column? offer = session.Find($"{activity}_offer");
        if (offer == null) return null;
        string cell = offer.Text[row];
        if (cell.Length == 0 || cell == "-") return null;
        int colon = cell.IndexOf(':');
        return colon < 0 ? cell : cell[..colon];
    }

    /// <summary>The four eligibilities that mean the activity could not have been executed on that tick.</summary>
    public static bool Invalid(string? eligibility)
        => eligibility is "KnownUnusable" or "Unresolved" or "NoOpportunity" or "PolicyForbidden";

    /// <summary>
    /// The census beside the capture, as text. It is a separate artefact rather than a column, and
    /// an absent one is reported by name: a missing census and an empty one mean opposite things.
    /// </summary>
    public static string? Census(string sessionPath)
    {
        string path = Path.ChangeExtension(sessionPath, null) + "-census.txt";
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static double Median(List<double> values)
    {
        if (values.Count == 0) return double.NaN;
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : 0.5 * (values[middle - 1] + values[middle]);
    }
}
