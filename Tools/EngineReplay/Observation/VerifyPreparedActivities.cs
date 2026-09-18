extern alias live;

using Prepared = live::AICompanion.Companion.Brain.Infrastructure.Selection.PreparedActivity;
using Context = live::AICompanion.Companion.Brain.Infrastructure.Selection.ActivityComparisonContext;
using Evaluator = live::AICompanion.Companion.Brain.Infrastructure.Selection.EvaluatePreparedActivities;
using Families = live::AICompanion.Companion.Brain.Infrastructure.Selection.NominateFamilyActivities;
using Family = live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily;
using Candidate = live::AICompanion.Companion.Brain.Infrastructure.Selection.FamilyCandidate;
using Offer = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using AttemptStatus = live::AICompanion.Companion.Brain.Activities.AttemptStatus;
using Vector2 = Microsoft.Xna.Framework.Vector2;

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
        // Separation is the one cost of keeping the companion apart since 15 September 2026: a prepared share, one minus the
        // capped rejoin pull at the job's stand against the player's region carried along his travel. These rows hold the
        // evaluator's half — it multiplies exactly that share, for every job somewhere and for nothing that serves the player
        // directly — and the chooser's half, where the share comes from, is the surface-zombie scene below.
        var context = new Context(0, false, float.PositiveInfinity, 12, 240, 1.15f, true, .2f);
        Prepared[] board = { new(0, "work", .7f, 31, true, true, false, false, Offer.Usable), new(1, "company", .3f, 0, false, false, true, false, Offer.Usable) };
        var together = Evaluator.Evaluate(board, context);
        var apart = Evaluator.Evaluate(new[] { board[0] with { Separation = .5f }, board[1] }, context);
        Require(together[0].Reunion == 1 && together[0].Final > together[1].Final,
            $"a job whose stand is inside the player's region pays nothing for separation (work {together[0].Final:0.000}, company {together[1].Final:0.000})");
        Require(apart[0].Reunion == .5f && MathF.Abs(apart[0].Final - together[0].Final * .5f) < 1e-5f,
            $"a job whose stand is outside the region pays exactly its separation share ({apart[0].Final:0.00000} against half of {together[0].Final:0.00000})");
        Require(Evaluator.Evaluate(new[] { board[0] with { IsExcursion = false, ServesPlayerDirectly = true, Separation = .5f } }, context)[0].Reunion == 1,
            "player protection must not inherit the optional-excursion cost");
        Require(Evaluator.Evaluate(new[] { board[0] with { IsExcursion = false, Separation = .5f } }, context)[0].Reunion == .5f,
            "a job somewhere that is not an excursion — a hunt shooting from where the orb hovers — still pays for keeping the companion apart");
        var hittingFight = new Prepared(0, "combat", 1.02f, 227, true, true, false, true, Offer.Usable,
            ServesEncounter: true, ServesPlayerDirectly: true, Separation: 0f);
        var companyFloor = new Prepared(1, "company", .05f, 0, false, false, true, false, Offer.Usable,
            ServesPlayerDirectly: true);
        var held = Evaluator.Evaluate(new[] { hittingFight, companyFloor }, context);
        Require(held[0].Reunion == 1 && held[0].Protection == 1 && held[0].Final > held[1].Final,
            $"a hitting fight that still serves him is not zeroed by a long path to its stand (combat {held[0].Final:0.000} reunion {held[0].Reunion:0.000} vs company {held[1].Final:0.000})");
        var urgent = context with { ProtectionUrgency = 1f };
        var heldUrgent = Evaluator.Evaluate(new[] { hittingFight, companyFloor }, urgent);
        Require(heldUrgent[0].Protection == 1 && heldUrgent[0].Final > heldUrgent[1].Final,
            "player danger does not discount a fight that still serves him as if he were being left");
        Require(Evaluator.Evaluate(new[] { board[0] with { Separation = 1.5f } }, context)[0].Error == "invalid-separation",
            "a separation share outside zero to one is an error, not a bonus");
        VerifyTimeAndOrder();
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

    /// <summary>
    /// The owner's rulings of 15 September 2026, each as a row that fails without its mechanism: a job is worth what it
    /// delivers per time, so progress raises it with no bonus for having started; a companion outside the player's
    /// region is not talked out of following him by nearby work; a job the player's heading takes out of range is worth
    /// nothing; every trip job reports its trip; and close jobs are put in the order that delivers soonest, which
    /// flips when the geometry does and does not flip back and forth while the body flies toward its choice.
    /// </summary>
    private static void VerifyTimeAndOrder()
    {
        var calm = new Context(0, false, float.PositiveInfinity, 12, 240, 1.15f, true, .2f, 0, TaskWindowTicks: 300);

        var fresh = new Prepared(0, "hunt", .4f, 0, true, true, false, false, Offer.Usable, TaskTicks: 400);
        float freshFinal = Evaluator.Evaluate(new[] { fresh }, calm)[0].Final;
        float nearlyDoneFinal = Evaluator.Evaluate(new[] { fresh with { TaskTicks = 60 } }, calm)[0].Final;
        // The time term alone separates them: 300/(300+60) against 300/(300+400), a ratio of 1.944.
        Require(nearlyDoneFinal > freshFinal && Math.Abs(nearlyDoneFinal / freshFinal - (300f / 360f) / (300f / 700f)) < .001f,
            $"a job nearly done must be worth more than the same job fresh by exactly the time term, with no bonus for having started ({nearlyDoneFinal:0.000} against {freshFinal:0.000})");
        Require(Evaluator.Evaluate(new[] { fresh }, calm with { TaskWindowTicks = 0 })[0].Time == 1,
            "a board with no time window keeps the arithmetic it had before the time term existed");
        Require(Evaluator.Evaluate(new[] { fresh with { ServesPlayerDirectly = true } }, calm)[0].Time == 1,
            "protecting or keeping company with the player is never discounted for time");
        Require(Evaluator.Evaluate(new[] { fresh with { PlayerFit = 0 } }, calm)[0].Final == 0
            && live::AICompanion.Companion.Brain.Infrastructure.Selection.Chooser.FitAt(900, 1000, 1250) == 1
            && live::AICompanion.Companion.Brain.Infrastructure.Selection.Chooser.FitAt(1125, 1000, 1250) == .5f
            && live::AICompanion.Companion.Brain.Infrastructure.Selection.Chooser.FitAt(1300, 1000, 1250) == 0,
            "a job the player's heading takes beyond the started-job radius is worth nothing, and one inside the new-job radius loses nothing");

        // The surface zombie of 15 September 2026: a hunt the orb could shoot from where it hovered, not an excursion, while
        // the player dropped four hundred pixels into a cave. Both values come from the live geometry rather than being
        // written in: the region is built where the player is, led by his fall, and the hunt's separation is the rejoin pull
        // at the orb against that region carried along his fall for the hunt's own duration. So the hunt keeps the orb while
        // he has only set off, and loses to rejoining once his leaving has put the orb well outside his region.
        const float slack = 32f;
        var travelling = new Vector2(0f, 6f);
        Vector2 top = new(8000f, 4000f), orb = top + new Vector2(0f, -100f);
        var setOffRegion = live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegion.Around(top, new Vector2(0f, 1.5f * 120f), 1f, true, slack);
        Vector2 dropped = top + new Vector2(0f, 400f);
        var goneRegion = live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegion.Around(dropped, travelling * 120f, 1f, true, slack);
        float setOffSeparation = live::AICompanion.Companion.Brain.Infrastructure.Selection.Chooser.SeparationAt(setOffRegion, new Vector2(0f, 1.5f), orb, 250f);
        float goneSeparation = live::AICompanion.Companion.Brain.Infrastructure.Selection.Chooser.SeparationAt(goneRegion, travelling, orb, 250f);
        float setOffRejoin = MathF.Max(live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany.RejoinPull(setOffRegion, orb),
            live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.WanderFloor);
        float goneRejoin = MathF.Max(live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany.RejoinPull(goneRegion, orb),
            live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.WanderFloor);
        Prepared Hunt(float separation) => new(0, "hunt", .63f, 0, false, true, false, true, Offer.Usable, TaskTicks: 250, Separation: separation);
        Prepared Company(float value) => new(1, "company", value, 0, false, false, true, false, Offer.Usable, ServesPlayerDirectly: true);
        var settingOff = Evaluator.Evaluate(new[] { Hunt(setOffSeparation), Company(setOffRejoin) }, calm);
        var gone = Evaluator.Evaluate(new[] { Hunt(goneSeparation), Company(goneRejoin) }, calm);
        string zombie = $"setting off: separation {setOffSeparation:0.000}, hunt {settingOff[0].Final:0.000}, company {settingOff[1].Final:0.000}; dropped 400 px: separation {goneSeparation:0.000}, hunt {gone[0].Final:0.000}, company {gone[1].Final:0.000}";
        Console.WriteLine($"surface zombie through the one separation cost: {zombie}");
        Require(settingOff[0].Final > settingOff[1].Final && gone[1].Final > gone[0].Final,
            $"a hunt shooting from where the orb hovers keeps it while the player sets off and loses to rejoining once he has gone; {zombie}");

        var chooser = new live::AICompanion.Companion.Brain.Infrastructure.Selection.Chooser();
        foreach (var action in chooser.Actions)
        {
            if (!action.IsExcursion) continue;
            var declared = action.GetType().GetMethod("ForecastTicks")!.DeclaringType;
            Require(declared != typeof(live::AICompanion.Companion.Brain.Activities.CompanionAction),
                $"{action.Name} is a trip job and inherits a trip time of zero, so it would be scored as instant beside jobs that report theirs");
        }

        const float speed = 9f;
        var body = new Microsoft.Xna.Framework.Vector2(0, 0);
        Prepared Job(int index, string name, float raw, float x, float work)
        {
            float flight = Math.Abs(x - body.X) / speed;
            return new(index, name, raw, flight, true, true, false, false, Offer.Usable,
                TaskTicks: flight + work, Site: new Microsoft.Xna.Framework.Vector2(x, 0));
        }
        string Leader(Prepared[] board)
        {
            var result = live::AICompanion.Companion.Brain.Infrastructure.Selection.OrderNearbyTasks.Apply(
                Evaluator.Evaluate(board, calm), board, body, speed, calm.TaskWindowTicks, .4f, 5);
            return result.Evaluated.Where(e => e.Error.Length == 0 && e.Final > 0)
                .OrderByDescending(e => e.Final).ThenBy(e => e.Index).First().Name;
        }

        // A slime on the way to a dark corner. Alone, the corner scores higher; in order, killing the slime on the way
        // and then lighting delivers sooner than lighting and coming back.
        Prepared[] onTheWay = { Job(0, "hunt", .6f, 300, 90), Job(1, "place-torches", .62f, 900, 20) };
        var single = Evaluator.Evaluate(onTheWay, calm);
        Require(single[1].Final > single[0].Final, "premise: alone, the far corner outscores the slime on the way");
        Require(Leader(onTheWay) == "hunt", "a slime on the way to a dark corner is killed first");

        Prepared[] beyond = { Job(0, "hunt", .6f, 900, 90), Job(1, "place-torches", .62f, 300, 20) };
        Require(Leader(beyond) == "place-torches", "with the slime beyond the corner the order flips and the corner is lit first");

        // Two equal jobs in opposite directions: whichever leads first must keep leading while the body flies toward
        // it, with the running job marked incumbent as the chooser marks it.
        body = new Microsoft.Xna.Framework.Vector2(0, 0);
        string? leader = null;
        int switches = 0;
        for (int rescore = 0; rescore < 12; rescore++)
        {
            Prepared[] even = { Job(0, "hunt", .6f, -450, 60), Job(1, "mine", .6f, 450, 60) };
            if (leader != null) even = even.Select(p => p with { IsIncumbent = p.Name == leader }).ToArray();
            string now = Leader(even);
            if (leader != null && now != leader) switches++;
            leader = now;
            float direction = leader == "hunt" ? -1 : 1;
            body = new Microsoft.Xna.Framework.Vector2(Math.Clamp(body.X + direction * speed * 30, -450, 450), 0);
        }
        Require(switches == 0, $"two even jobs must not trade the lead while the body flies toward the one it chose ({switches} switches)");
        Console.WriteLine("time and order: progress, leaving, player fit, trip times, the order on the way and beyond, no flip-flop");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
