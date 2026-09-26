extern alias live;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;

/// <summary>
/// The companion's settings, taken from the session being replayed rather than from this process's
/// defaults.
///
/// It is not a nicety. A world run that ignores them replays a different companion: the capture of
/// 22 September 2026 ran with chopping on Mimic, so the companion never touched a tree all session
/// and every census line read <c>mimic-awaiting-player-tree-contact</c> — and the first replay of
/// it, under this process's Opportunistic default, spent the tail of the run chopping. Every row
/// about what the brain chose was therefore about a brain with an extra job.
///
/// The live occurrence outranks the header, and that ordering is the whole of the file's judgement.
/// The capture carries both: <c># config=</c> in the header block and a <c>configuration</c>
/// occurrence written on the first tick, and on that capture they disagree — the header says
/// Opportunistic and the occurrence says Mimic. The occurrence is what the session ran under,
/// because the census reasons it wrote agree with it and the header was written before the value
/// settled. The disagreement is reported rather than resolved silently, because a header that lies
/// is a recorder defect somebody should fix rather than a quirk this file absorbs.
/// </summary>
internal static class ApplyTheRecordedPreferences
{
    /// <summary>
    /// Applies what the capture recorded and returns the sentence every row carries about it.
    ///
    /// A capture with neither source leaves this process's defaults standing and says so, because a
    /// default silently substituted for a recorded value is the thing that made the first replay
    /// wrong.
    /// </summary>
    public static string From(string occurrence, string header)
    {
        string source = occurrence.Length > 0 ? occurrence : header;
        if (source.Length == 0)
            return "the capture records no configuration, so this run used the process defaults "
                + $"({Describe(Preferences.Current)}) and every row about what the brain chose is about those defaults rather than about the session's";

        var fields = source.Split(';')
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .ToDictionary(part => part[0].Trim(), part => part[1].Trim(), StringComparer.Ordinal);

        var applied = new Preferences();
        if (Policy(fields, "mining") is { } mining) applied.Mining = mining;
        if (Policy(fields, "chopping") is { } chopping) applied.Chopping = chopping;
        if (Flag(fields, "combat") is { } combat) applied.Combat = combat;
        if (Flag(fields, "pot_breaking") is { } pots) applied.PotBreaking = pots;
        if (Flag(fields, "torch_placement") is { } torches) applied.TorchPlacement = torches;
        // A capture before schema 0.51.0 carries `distance_mode`, and it is read by nobody: the distances are fixed now,
        // so a replay of an older session runs at today's distances whatever that session was set to.
        // A fresh instance rather than a list of assignments onto the standing one, for the reason
        // the engine suite's own reset gives: every default lives on the property initialisers, so
        // assigning the fields this file happens to know about would leave the rest holding whatever
        // the last run left while looking maintained.
        Preferences.Current = applied;

        string disagreement = occurrence.Length > 0 && header.Length > 0 && header != occurrence
            ? $"; the capture's header disagrees with it ({header}) and the occurrence is the one the session ran under"
            : "";
        return $"preferences from the capture's own {(occurrence.Length > 0 ? "configuration occurrence" : "header line")}: {Describe(applied)}{disagreement}";
    }

    private static string Describe(Preferences preferences)
        => $"mining={preferences.Mining};chopping={preferences.Chopping};combat={preferences.Combat};"
        + $"pot_breaking={preferences.PotBreaking};torch_placement={preferences.TorchPlacement}";

    private static WorkPolicy? Policy(IReadOnlyDictionary<string, string> fields, string name)
        => fields.TryGetValue(name, out string? value) && Enum.TryParse(value, out WorkPolicy policy) ? policy : null;

    private static bool? Flag(IReadOnlyDictionary<string, string> fields, string name)
        => fields.TryGetValue(name, out string? value) ? value.Equals("true", StringComparison.OrdinalIgnoreCase) : null;
}
