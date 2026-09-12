extern alias live;

using Prepared = live::AICompanion.Companion.Brain.BehaviourSelection.PreparedActivity;
using Context = live::AICompanion.Companion.Brain.BehaviourSelection.ActivityComparisonContext;
using Evaluator = live::AICompanion.Companion.Brain.BehaviourSelection.EvaluatePreparedActivities;

internal static class VerifyPreparedActivities
{
    public static int Run()
    {
        var context = new Context(.2f, false, 10, 20, 100, 1.25f, true, .5f);
        Prepared[] board =
        {
            new(0, "mine", .6f, 90, true, true, false, true),
            new(1, "follow", .7f, 0, false, false, true, false),
        };
        var snapshot = board.ToArray();
        var first = Evaluator.Evaluate(board, context);
        Require(Math.Abs(first[0].Final - .54f) < .00001f && Math.Abs(first[1].Final - .35f) < .00001f,
            "captured work, interruption horizon and follow opportunity cost must each be charged once");
        Require(first.SequenceEqual(Evaluator.Evaluate(board, context)) && board.SequenceEqual(snapshot),
            "repeated comparison must neither mutate candidates nor change its results");
        Require(first.OrderBy(x => x.Index).SequenceEqual(Evaluator.Evaluate(board.Reverse().ToArray(), context).OrderBy(x => x.Index)),
            "candidate enumeration order must not change a candidate's value");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
        {
            var rejected = Evaluator.Evaluate(new[] { board[0] with { RawValue = invalid } }, context).Single();
            Require(rejected.Final == 0 && rejected.Error == "invalid-raw-value", "invalid utility must be rejected explicitly");
        }
        Require(Evaluator.Evaluate(board, context with { ThreatHorizonTicks = float.PositiveInfinity })[0].Horizon == 1,
            "no observed threat deadline must not become an evaluation error");
        Require(Evaluator.Evaluate(new[] { board[0] with { RawValue = float.MaxValue } }, context with { Commitment = float.MaxValue })[0].Error == "non-finite-product",
            "finite inputs that overflow must not manufacture an infinite winner");

        // Independent reference of the preceding chooser arithmetic over the valid domain.
        // This establishes the adapter's numeric preservation, not better gameplay choices.
        var random = new Random(423);
        for (int run = 0; run < 100; run++)
        {
            var varied = context with { ProtectionUrgency = (float)random.NextDouble(), ThreatHorizonTicks = random.Next(0, 120),
                Stranded = random.Next(2) == 0, WithinActivityAllowance = random.Next(2) == 0 };
            var candidates = Enumerable.Range(0, 11).Select(i => new Prepared(i, i.ToString(), (float)random.NextDouble() * 4,
                random.Next(0, 600), i % 2 == 0, i % 3 == 0, i == 10, i == run % 11)).ToArray();
            var expected = candidates.Select(c =>
            {
                float protection = c.IsExcursion && !varied.Stranded ? 1 - varied.ProtectionUrgency : 1;
                float commitment = c.IsIncumbent ? varied.Commitment : 1;
                float duration = c.IsExcursion ? Math.Min(varied.InterruptibleTicks, c.ForecastTicks) : c.ForecastTicks;
                float horizon = duration > varied.ThreatHorizonTicks ? Math.Max(0, 1 - (duration - varied.ThreatHorizonTicks) / varied.HorizonOverrunTicks) : 1;
                return c.RawValue * protection * commitment * horizon;
            }).ToArray();
            bool useful = candidates.Any(c => c.IsExcursion && c.HasTarget && expected[c.Index] > .1f);
            if (useful && varied.WithinActivityAllowance) expected[10] *= varied.FollowDuringUsefulWork;
            var actual = Evaluator.Evaluate(candidates, varied);
            Require(actual.All(c => c.Error.Length == 0 && c.Final == expected[c.Index]), "prepared comparison diverged from the valid-domain reference");
        }
        Console.WriteLine("prepared activities: repeated/reordered comparison, invalid values and legacy arithmetic reference pass");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
