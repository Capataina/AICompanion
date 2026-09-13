extern alias live;

using Prepared = live::AICompanion.Companion.Brain.Infrastructure.Selection.PreparedActivity;
using Context = live::AICompanion.Companion.Brain.Infrastructure.Selection.ActivityComparisonContext;
using Evaluator = live::AICompanion.Companion.Brain.Infrastructure.Selection.EvaluatePreparedActivities;
using Families = live::AICompanion.Companion.Brain.Infrastructure.Selection.NominateFamilyActivities;
using Family = live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily;
using Candidate = live::AICompanion.Companion.Brain.Infrastructure.Selection.FamilyCandidate;
using Offer = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using AttemptStatus = live::AICompanion.Companion.Brain.Activities.AttemptStatus;

internal static class VerifyPreparedActivities
{
    internal static float PrepareAndScore(live::AICompanion.Companion.Brain.Activities.CompanionAction action,
        in live::AICompanion.Companion.Brain.Activities.ActionContext context)
    {
        action.Prepare(context);
        return action.Score();
    }

    public static int Run()
    {
        VerifyReunionCostsAndHistory();
        var context = new Context(.2f, false, 10, 20, 100, 1.25f, true, .5f);
        Prepared[] board =
        {
            new(0, "mine", .6f, 90, true, true, false, true, Offer.Usable),
            new(1, "follow", .7f, 0, false, false, true, false, Offer.Usable),
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
        foreach (var absent in new[] { Offer.NoOpportunity, Offer.PolicyForbidden, Offer.KnownUnusable })
        {
            Require(Evaluator.Evaluate(new[] { board[0] with { Eligibility = absent } }, context).Single() is { Final: 0, Error: "value-without-eligible-offer" },
                $"a positive value beside a {absent} offer must be rejected rather than allowed to win");
            Require(Evaluator.Evaluate(new[] { board[0] with { RawValue = 0, Eligibility = absent } }, context).Single().Error.Length == 0,
                $"a zero-valued {absent} offer is a truthful absence, not an evaluation error");
        }
        Require(Evaluator.Evaluate(new[] { board[0] with { Eligibility = Offer.Unresolved } }, context).Single() is { Error: "", Final: > 0 },
            "an unresolved investigation may carry bounded positive value");
        VerifyAttemptOwnership();
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
                random.Next(0, 600), i % 2 == 0, i % 3 == 0, i == 10, i == run % 11, Offer.Usable)).ToArray();
            var expected = candidates.Select(c =>
            {
                float protection = c.IsExcursion && !varied.Stranded ? 1 - varied.ProtectionUrgency : 1;
                float commitment = c.IsIncumbent ? varied.Commitment : 1;
                float duration = c.IsExcursion ? Math.Min(varied.InterruptibleTicks, c.ForecastTicks) : c.ForecastTicks;
                float horizon = duration > varied.ThreatHorizonTicks ? Math.Max(0, 1 - (duration - varied.ThreatHorizonTicks) / varied.HorizonOverrunTicks) : 1;
                return c.RawValue * protection * commitment * horizon;
            }).ToArray();
            bool useful = candidates.Any(c => !c.IsFollowing && c.HasTarget && expected[c.Index] > .1f);
            if (useful && varied.WithinActivityAllowance) expected[10] *= varied.FollowDuringUsefulWork;
            var actual = Evaluator.Evaluate(candidates, varied);
            Require(actual.All(c => c.Error.Length == 0 && c.Final == expected[c.Index]), "prepared comparison diverged from the valid-domain reference");
            var grouped = actual.Select(c => new Candidate((Family)(c.Index % 3), c)).ToArray();
            var flatWinner = actual.Where(c => c.Error.Length == 0 && c.Final > 0)
                .OrderByDescending(c => c.Final).ThenBy(c => c.Index).Select(c => (int?)c.Index).FirstOrDefault();
            Require(Families.Select(Families.Nominate(grouped))?.Index == flatWinner,
                "family nominations must match the independent flat reference on identical evaluated candidates");
            Require(Families.Select(Families.Nominate(grouped.Reverse().Concat(grouped).ToArray()))?.Index == flatWinner,
                "reordering or duplicating prepared candidates must not change the winner");
        }
        Require(Families.Nominate(Array.Empty<Candidate>()).Length == 3
            && Families.Select(Families.Nominate(Array.Empty<Candidate>())) == null,
            "three empty families must expose no nomination and select no activity");
        var zero = first[0] with { Final = 0 };
        var invalidCandidate = first[1] with { Final = float.PositiveInfinity, Error = "invalid" };
        Require(Families.Select(Families.Nominate(new[] { new Candidate(Family.Combat, zero), new Candidate(Family.Gathering, invalidCandidate) })) == null,
            "zero or invalid children must not manufacture a valuable family");
        var tie = first[1] with { Final = first[0].Final };
        Require(Families.Select(Families.Nominate(new[] { new Candidate(Family.Combat, tie), new Candidate(Family.NearbyAssistance, first[0]) }))?.Index == first[0].Index,
            "equal-valued children must use the same stable candidate order across families");
        Console.WriteLine("prepared activities: repeated/reordered comparison, invalid values and legacy arithmetic reference pass");
        return 0;
    }

    private static void VerifyReunionCostsAndHistory()
    {
        var context = new Context(0, false, float.PositiveInfinity, 12, 240, 1.15f, true, .2f, .12f);
        Prepared[] board = { new(0, "work", .7f, 31, true, true, false, false, Offer.Usable), new(1, "company", 1, 0, false, false, true, false, Offer.Usable) };
        var longWork = Evaluator.Evaluate(board, context);
        var shortWork = Evaluator.Evaluate(new[] { board[0] with { ForecastTicks = 1 }, board[1] }, context);
        Require(longWork[0].Final < longWork[1].Final && shortWork[0].Final > shortWork[1].Final,
            "the same departure context must distinguish long work from a quick finish");
        var calm = Evaluator.Evaluate(board, context with { ReunionDelayCostPerTick = 0 });
        Require(calm[0].Final > calm[1].Final && calm[0].Reunion == 1,
            "without reunion delay cost, useful work retains its ordinary opportunity");
        Require(Evaluator.Evaluate(board, context with { ReunionDelayCostPerTick = .24f })[0].Final < longWork[0].Final,
            "a more costly return must reduce optional work value on the same board");
        Require(Evaluator.Evaluate(new[] { board[0] with { IsExcursion = false } }, context)[0].Reunion == 1,
            "player protection must not inherit the optional-excursion cost");
        var history = new live::AICompanion.Companion.Brain.Infrastructure.Selection.AssessReunionCost();
        history.Observe(1, false, false);
        history.Observe(2, false, false);
        history.Observe(2, false, false);
        history.Observe(3, false, false);
        Require(history.ApartTicks == 2, "duplicate comparisons cannot charge separation twice");
        history.Observe(100, false, false);
        Require(history.ApartTicks == 2, "unobserved time cannot be charged as known separation");
        history.Evaluate(0, 100, false, false);
        float accumulated = history.DelayCostPerTick;
        history.Observe(101, false, false);
        history.Evaluate(0, 100, false, false);
        Require(history.DelayCostPerTick > accumulated, "repeated detours share accumulated separation rather than resetting by label");
        history.Observe(102, true, false);
        Require(history.ApartTicks == 0, "observed reunion clears accumulated separation");
        history.Evaluate(4, 100, true, false);
        Require(history.DelayCostPerTick == 0, "a dead player creates no reunion obligation");
        history.Evaluate(4, 100, false, true);
        Require(history.DelayCostPerTick == 0, "a sealed companion must retain local usefulness");
    }

    /// <summary>A minimal real activity whose only behaviour is naming its own conclusion, so the
    /// owner's attempt accounting is exercised without discovery, terrain or native tools.</summary>
    private sealed class ProbeActivity : live::AICompanion.Companion.Brain.Activities.CompanionAction
    {
        private readonly string name;
        public int Conclusions, Begins;
        public ProbeActivity(string name) => this.name = name;
        public override string Name => name;
        public override Family Family => Family.Gathering;
        public override object ActivityIdentity => name;
        public override void Prepare(in live::AICompanion.Companion.Brain.Activities.ActionContext ctx) { }
        public override float Score() => 1;
        public override live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest Execute(in live::AICompanion.Companion.Brain.Activities.ActionContext ctx)
            => live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest.Hold;
        public override void BeginAttempt() => Begins++;
        public override live::AICompanion.Companion.Brain.Activities.AttemptConclusion ConcludeAttempt(int productiveEffects)
        {
            Conclusions++;
            return new(AttemptStatus.Failed, $"probe-{name}-{productiveEffects}");
        }
    }

    private static void VerifyAttemptOwnership()
    {
        var owner = new live::AICompanion.Companion.Brain.Infrastructure.Selection.OwnCurrentActivity();
        var context = default(live::AICompanion.Companion.Brain.Activities.ActionContext);
        var mine = new ProbeActivity("mine");
        var chop = new ProbeActivity("chop");
        owner.Select(mine, context);
        owner.RecordProductiveEffect();
        Require(!owner.AttemptOpen && owner.RecentAttempts.Count == 0,
            "selection alone must open no attempt and credit no effect");
        owner.BeginExecution();
        long first = owner.AttemptId, activity = owner.Id;
        owner.RecordProductiveEffect();
        owner.RecordProductiveEffect();
        owner.Select(mine, context);
        owner.BeginExecution();
        Require(owner.AttemptOpen && owner.AttemptId == first && owner.RecentAttempts.Count == 0 && mine.Conclusions == 0,
            "reselecting the executing purpose must keep its attempt open without concluding it");
        owner.Suspend(context, "projectile");
        Require(owner.LastAttempt is { Status: AttemptStatus.Interrupted, Cause: "projectile", ProductiveEffects: 2 } interrupted
            && interrupted.AttemptId == first && interrupted.ActivityId == activity && mine.Conclusions == 0,
            "suspension must record an interruption carrying its effects and never ask the activity to reinterpret it");
        owner.Suspend(context, "projectile");
        Require(owner.RecentAttempts.Count == 1, "a repeated suspension must not invent a second interrupted attempt");
        owner.Select(mine, context);
        owner.BeginExecution();
        Require(owner.Id == activity && owner.AttemptId != first && owner.AttemptEffects == 0,
            "resuming must keep the purpose identity and open a fresh attempt with no inherited effects");
        owner.Select(chop, context);
        Require(owner.LastAttempt is { Status: AttemptStatus.Failed, Cause: "probe-mine-0", Activity: "mine" } replaced
            && replaced.ActivityId == activity && mine.Conclusions == 1 && !owner.AttemptOpen,
            "replacement must close the open attempt with the replaced activity's own conclusion and identity");
        owner.BeginExecution();
        owner.Select(null, context);
        Require(owner.LastAttempt is { Activity: "chop", Status: AttemptStatus.Failed } && chop.Conclusions == 1 && !owner.AttemptOpen,
            "an empty selection must close the open attempt");
        Require(owner.RecentAttempts.Select(a => a.AttemptId).Distinct().Count() == 3,
            "every concluded attempt must keep a distinct identity");
        Require(mine.Begins == 2 && chop.Begins == 1,
            $"the owner must announce each opened attempt exactly once, never on reselection or suspension; mine={mine.Begins} chop={chop.Begins}");
        var other = new live::AICompanion.Companion.Brain.Infrastructure.Selection.OwnCurrentActivity();
        other.Select(new ProbeActivity("other"), context);
        other.BeginExecution();
        Require(owner.RecentAttempts.All(a => a.AttemptId < other.AttemptId),
            "attempt identities must stay unique across owners, so a respawned brain cannot reuse ids a recorder cursor already wrote");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
