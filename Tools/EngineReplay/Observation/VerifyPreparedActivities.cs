extern alias live;

using Family = live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily;
using AttemptStatus = live::AICompanion.Companion.Brain.Activities.AttemptStatus;

/// <summary>
/// What survived the family chooser's deletion on 22 September 2026 (`AIC-419`): the activity owner's
/// attempt lifecycle, which is live — every course step the tick binds is selected and executed through
/// it — and the separation history the recorder's three reunion columns are written from.
///
/// Everything else this file held was the chooser's shared comparison: a captured board multiplied by
/// protection, commitment, horizon, reunion, time, player fit and order, a seeded arithmetic reference
/// for it, the family nomination contract, and the surface-zombie scene built through
/// `Chooser.SeparationAt`. None of those quantities exists under a course, which prices whole orders by
/// their forecast consequences, so the rows were removed rather than rewritten; the properties three of
/// them carried were moved into `Selection/Courses/CLAUDE.md` and `Selection/Opportunities/CLAUDE.md`
/// first, with the course rows that hold each one named there.
///
/// <see cref="PrepareAndScore"/> stays and is the reason the file does: about forty fixtures across this
/// suite drive an activity through it, and it is the activity contract — prepare, then read the value the
/// preparation captured — rather than anything the chooser owned.
/// </summary>
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
        VerifySeparationHistory();
        VerifyAttemptOwnership();
        Console.WriteLine("activity owner: attempts opened, concluded and identified; separation history accumulates only what it observed");
        return 0;
    }

    /// <summary>
    /// `AssessReunionCost` is observation rather than choice, which is why it outlived the comparison that
    /// used to read it: `ObserveCompanionship` runs it every tick and the recorder writes its three columns.
    /// Nothing decides on the delay cost any more, so these rows are about the accounting — unobserved time
    /// is not charged, a duplicate observation of one tick is not charged twice, a reunion clears the
    /// accumulation, and a dead player or a stranded companion owes nothing.
    /// </summary>
    private static void VerifySeparationHistory()
    {
        var history = new live::AICompanion.Companion.Brain.Infrastructure.Selection.AssessReunionCost();
        history.Observe(1, false, false);
        history.Observe(2, false, false);
        history.Observe(2, false, false);
        history.Observe(3, false, false);
        Require(history.ApartTicks == 2, "duplicate observations cannot charge separation twice");
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
