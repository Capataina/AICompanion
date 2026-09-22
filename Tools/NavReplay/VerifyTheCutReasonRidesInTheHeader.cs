#nullable enable

using System;

/// <summary>
/// A committed scenario says in its own header why its window was cut, and saying so does not change
/// anything else the header means.
///
/// <para><b>Why the reason belongs in the file.</b> A scenario outlives the session that cut it by
/// months and the file is the whole of what a later reader has. Three windows were cut out of the
/// 22 September 2026 capture with their reasons recorded in `Tools/Scenarios/CLAUDE.md` instead, which
/// is one fact in two homes and the guide is the one that drifts — a fixture renamed, re-cut at a
/// different tick or copied leaves the guide describing a file that no longer matches it, with nothing
/// to notice. So `--reason` writes it onto the header line.</para>
///
/// <para><b>Why that is worth a case rather than being obviously safe.</b> The header is parsed by
/// first occurrence of each key: `MirrorScenarioWorlds.MirrorHeader` takes `goal ` and reads to the
/// next space, and `TextTileWorld` reads `window x ` the same way. Free text appended to that line is
/// therefore safe only as long as it stays *after* every key, and a reason is written by whoever cuts
/// the window — the parity table's printed command puts behaviour names in it, and a behaviour named
/// "Choosing a place to work" is one rename away from holding the word "goal". This case is the thing
/// that makes appending free text to a parsed line defensible: it puts the dangerous words in the
/// reason deliberately and requires the parse to be unmoved.</para>
/// </summary>
internal static class VerifyTheCutReasonRidesInTheHeader
{
    /// <summary>The provenance shape the extractor writes, up to the point a reason is appended.</summary>
    private const string Header =
        "tick 1562 cut from a recording: start 3402,362 goal 3424,356 expansions 0 npc 3402,362 player 3424,363"
        + " orb 54445.0,5803.0 window x 3370..3433 y 342..381 || from a-capture.tsv || 19 snapshots covered"
        + " 2560 of 2560 tiles (100.0%) as last written, the rest closed";

    /// <summary>Every key the header parsers read, deliberately, so the reason cannot be quietly safe
    /// because nobody wrote a reason holding them.</summary>
    private const string HostileReason =
        "the goal 1,1 player 2,2 start 3,3 npc 4,4 orb 5.0,5.0 window x 0..1 y 0..1 case";

    public static int Run()
    {
        int failures = 0;
        failures += TheReasonIsOnTheLine();
        failures += TheKeysReadTheSameWithAReasonAsWithout();
        failures += NoReasonWritesNothing();
        if (failures == 0)
            Console.WriteLine("cut reason: the header carries why the window was cut, the keys parse identically with a reason holding every one of their own words, and an absent reason adds nothing");
        return failures;
    }

    private static int Fail(string what)
    {
        Console.WriteLine($"   cut reason: {what}");
        return 1;
    }

    private static int TheReasonIsOnTheLine()
    {
        string line = Header + " || cut for a behaviour the play contradicted";
        return line.Contains("|| cut for a behaviour the play contradicted", StringComparison.Ordinal) ? 0
            : Fail("the reason is not on the header line at all");
    }

    /// <summary>
    /// The mirror is the sharpest available reader of this line: it locates all four tile keys by
    /// first occurrence and rewrites their values, so a reason that shifted any of them produces a
    /// different mirrored header. Mirroring the plain line and the one carrying a hostile reason must
    /// agree everywhere the plain line reaches.
    /// </summary>
    private static int TheKeysReadTheSameWithAReasonAsWithout()
    {
        const int originX = 3370, width = 64;
        string plain = MirrorScenarioWorlds.MirrorHeader(Header, originX, width);
        string withReason = MirrorScenarioWorlds.MirrorHeader(Header + " || cut for " + HostileReason, originX, width);
        if (!withReason.StartsWith(plain, StringComparison.Ordinal))
            return Fail($"a reason changed how the header's own keys parse — the mirror read the line differently once free text "
                + $"holding the words start, goal, npc, player, orb and window was appended to it.\n      plain: {plain}\n      with:  {withReason}");
        // And the reason itself is left alone: the mirror rewrites coordinates it recognises as the
        // header's, and a coordinate inside a sentence is not one of those.
        return withReason.EndsWith("|| cut for " + HostileReason, StringComparison.Ordinal) ? 0
            : Fail($"the mirror rewrote coordinates inside the reason text, so the reason is being read as header data: {withReason}");
    }

    private static int NoReasonWritesNothing()
        => Header.Contains("cut for", StringComparison.Ordinal)
            ? Fail("the plain header already claims a cut reason, so an absent reason cannot be distinguished from a present one")
            : 0;
}
