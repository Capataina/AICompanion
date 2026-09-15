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
    internal static int Case(string name, Action test, string family = "row")
    {
        try
        {
            test();
            Console.WriteLine($"{family} {name}");
            return 0;
        }
        catch (InvalidOperationException error)
        {
            AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"{family} {name}: {error.Message}");
            return 1;
        }
    }
}
