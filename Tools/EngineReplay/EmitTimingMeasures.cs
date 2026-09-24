extern alias live;

using AICompanion.Tools.Ledger;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;

/// <summary>
/// The one way a fixture in this suite reports a wall-clock figure: as a ledger measure tagged timed and
/// sampled, never as a pass line.
///
/// <para>The owner ruled on 24 September 2026 that no time is a pass line, because "two milliseconds for a
/// specific playthrough might look a lot different than another playthrough". The day before, `combat is
/// admitted only where it can be executed` went red at 8.2 ms against a 5 ms bound because the machine slowed
/// 4.8 times mid-run, and the parent commit reproduced it in the same minute, so the bound had tested the
/// machine. A regression in cost is now caught by the scoreboard comparing each timing with its own history
/// on the same machine, which no number written into a fixture can do.</para>
///
/// <para>Both tags are always on, and that is why this helper exists rather than five call sites spelling them.
/// <see cref="EmitLedgerRows.TimedTag"/> tells the scheduler the row's number is a time; <see
/// cref="EmitLedgerRows.SampledTag"/> tells the scoreboard a second run of the same commit draws a different
/// value, so a delta is two draws rather than drift. A timing missing either tag is graded as something it is
/// not.</para>
///
/// <para>The regime is read, not declared. Unless the caller names one, the mode says whether
/// <c>LimitPlanningWork.Unbounded</c> was lifted at the moment of the call, because two fixtures here flip it
/// inside a case that the harness stamped as lifted, and a figure taken under the production clock filed as
/// unbounded would be compared against the wrong history. The <c>live::</c> copy is the one read, because it is
/// the one the brain reads; the suite compiles a second copy of the movement statics.</para>
/// </summary>
internal static class EmitTimingMeasures
{
    internal const string ProductionAllowances = "production-allowances";
    internal const string UnboundedAllowances = "unbounded-allowances";

    private static readonly string[] Tags = { EmitLedgerRows.TimedTag, EmitLedgerRows.SampledTag };

    /// <summary>
    /// File one timing. <paramref name="name"/> is the ledger key, so it must be a stable sentence: a renamed
    /// measure starts a new history. <paramref name="message"/> carries what was measured, how big the scene
    /// was and over how many samples, because a number without its scene cannot be compared with anything.
    /// </summary>
    internal static void Timing(string name, double milliseconds, string message, string? regime = null)
        => Measure(name, milliseconds, "ms", "down", message, regime);

    /// <summary>
    /// File a figure that is not a time but is decided by one — a count of searches that finished inside a
    /// wall-clock allowance — so it carries the same tags and regime as the timings beside it.
    /// </summary>
    internal static void Measure(string name, double value, string unit, string direction, string message, string? regime = null)
        => EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "EngineReplay", name, value, unit, direction,
            mode: "in-suite; " + (regime ?? (LimitPlanningWork.Unbounded ? UnboundedAllowances : ProductionAllowances)),
            tags: Tags, message: message);
}
