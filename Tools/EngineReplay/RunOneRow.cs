/// <summary>
/// One named row inside a fixture, run so that its failure is reported and the rows after it still run.
///
/// <para>It lived in <c>VerifyMovementFailures</c>, which was this suite's largest fixture and is gone with the
/// walking body it classified failures for. Four fixtures in three other folders called it, so it was never
/// that fixture's own helper — it was the suite's, sitting wherever it happened to have been written first.
/// Here it has a home whose name says what it does, and deleting any one fixture cannot take it away
/// again.</para>
///
/// <para>The distinction it keeps is between a row and a case. <c>EmitLedgerRows.Case</c> is the ledger's unit
/// and files a row in the run file; this is the unit inside one of those, so a fixture with a dozen scenes
/// reports every scene's failure rather than aborting on the first. The failure text goes through
/// <c>Detail</c>, which prints it and folds it into the ledger row the enclosing case will file — a fixture
/// that printed its own verdict instead would be invisible to the scoreboard, which is the boundary
/// <c>check-navigation-boundary.sh</c> enforces.</para>
/// </summary>
internal static class RunOneRow
{
    /// <summary>Run one row; return 1 if it failed. Only <see cref="InvalidOperationException"/> is caught,
    /// because that is what these fixtures' own <c>Require</c> throws — anything else is the instrument
    /// breaking rather than the thing under test failing, and it belongs to the enclosing case.</summary>
    ///
    /// <para>Inside a ledger case it also files the row's own verdict as a sub-row, <c>outer :: name</c>
    /// (<see cref="AICompanion.Tools.Ledger.EmitLedgerRows.SubRow"/>), so the run file carries one verdict
    /// per assertion rather than one per fixture file, and a red row no longer leaves the rows after it
    /// silent in the store. Outside a case — a flag run — it prints exactly as before and files nothing.</para>
    internal static int Case(string name, Action test, string family = "row")
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            test();
            Console.WriteLine($"{family} {name}");
            AICompanion.Tools.Ledger.EmitLedgerRows.SubRow(name, passed: true, durationMs: clock.Elapsed.TotalMilliseconds);
            return 0;
        }
        catch (InvalidOperationException error)
        {
            AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"{family} {name}: {error.Message}");
            AICompanion.Tools.Ledger.EmitLedgerRows.SubRow(name, passed: false, $"{family} {name}: {error.Message}", clock.Elapsed.TotalMilliseconds);
            return 1;
        }
    }

    /// <summary>
    /// The row shape the fixtures with their own <c>GREEN</c>/<c>RED</c> helper already had: every row runs,
    /// any exception is that row's failure, and the verdict is printed as <c>GREEN name</c> or
    /// <c>RED name: why</c> exactly as those helpers printed it. What it adds is what the helpers lacked —
    /// the red reason folded into the enclosing case's row through <c>Detail</c>, and a sub-row per row —
    /// so converting a helper is one line in its body and its output does not change.
    /// </summary>
    internal static int GreenOrRed(string name, Action test)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            test();
            Console.WriteLine($"GREEN {name}");
            AICompanion.Tools.Ledger.EmitLedgerRows.SubRow(name, passed: true, durationMs: clock.Elapsed.TotalMilliseconds);
            return 0;
        }
        catch (Exception error)
        {
            AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"RED {name}: {error.Message}");
            AICompanion.Tools.Ledger.EmitLedgerRows.SubRow(name, passed: false, $"{error.GetType().Name}: {error.Message}", clock.Elapsed.TotalMilliseconds);
            return 1;
        }
    }

    /// <summary>
    /// A row named after the method that runs it: <c>DisabledDoesNotStartAJob</c> files as
    /// "disabled does not start a job". It is the conversion for a fixture whose <c>Run</c> is a list of
    /// scene methods called in order, which aborted at its first failing one; each call becomes one of
    /// these, and the method's own name — already a sentence in the file — is the row's name, so the
    /// conversion adds no second name that could drift from the method it labels.
    /// </summary>
    internal static int Case(Action test, string family)
    {
        string method = test.Method.Name;
        if (method.Contains('<'))
            throw new ArgumentException($"a row run by a lambda has no method name to file under ({method}); name it explicitly");
        return Case(Sentence(method), test, family);
    }

    /// <summary>A PascalCase method name as the lower-case sentence it spells; a run of capitals stays one
    /// word (<c>NPCTarget</c> is "npc target").</summary>
    internal static string Sentence(string pascal)
    {
        var text = new System.Text.StringBuilder(pascal.Length + 8);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            bool boundary = i > 0 && char.IsUpper(c)
                && (!char.IsUpper(pascal[i - 1]) || (i + 1 < pascal.Length && char.IsLower(pascal[i + 1])));
            if (boundary) text.Append(' ');
            text.Append(char.ToLowerInvariant(c));
        }
        return text.ToString();
    }
}
