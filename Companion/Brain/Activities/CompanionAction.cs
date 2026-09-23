#nullable enable

using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.CharacterBody;

namespace AICompanion.Companion.Brain.Activities;

/// <summary>
/// Everything an action needs in one handle. <paramref name="Stranded"/> is the one fact that
/// comes back up from navigation: the brain's word that the body is in a sealed pocket and this
/// tick is one for walking it rather than pressing at the player, read like the breath is read,
/// as last tick's outcome about the body and never as a decision.
/// </summary>
public readonly record struct ActionContext(CompanionNPC Companion, Infrastructure.Observation.Senses Senses, bool Stranded = false)
{
    public Terraria.NPC Npc => Companion.NPC;
    public Terraria.Player Player => Senses.PlayerEntity;
}

/// <summary>
/// One thing the companion can be doing. Scores itself from the senses, forecasts how
/// long it would keep the companion busy (the chooser charges anything that outlasts
/// the safety horizon), and when running, acts and asks the positioner for a spot.
/// An action may keep a small state machine of its own; that is where sequencing
/// inside one job lives, never across jobs.
/// </summary>
public abstract class CompanionAction
{
    private object? admittedIdentity;
    public virtual Microsoft.Xna.Framework.Vector2? ActivityTarget => null;
    public virtual object? ActivityIdentity => null;
    /// <summary>Validate captured non-entity facts without discovering or mutating a candidate.</summary>
    public virtual string PreparedTargetRejection => "";
    /// <summary>A captured positioning method that must be admitted before activation.
    /// Reading it cannot execute the activity or rediscover its target.</summary>
    public virtual PositionRequest? PreparedPositionRequest => null;
    public bool HasActivityAllowance => admittedIdentity != null;

    /// <summary>Only an entered job earns the continuation radius; discovering a target does not.</summary>
    protected bool AllowsTarget(in ActionContext ctx, Microsoft.Xna.Framework.Vector2 target, object? identity = null)
    {
        var preferences = PlayerIntegration.CompanionPreferences.Current;
        bool sameJob = admittedIdentity != null && Equals(admittedIdentity, identity ?? ActivityIdentity);
        bool collectingWork = Name == "collect" && identity is Terraria.Item && ctx.Companion.Brain.Activity.IsCollectingWork(target);
        float radius = sameJob || collectingWork ? preferences.ActiveActivityRadius : preferences.NewActivityRadius;
        // The work radius is measured to the player's intent region rather than to his body, and this
        // is the one place every activity's "near the player" test lives, so mining, chopping,
        // lighting, collection and hunting all inherit it from here. Anchored on his feet the radius
        // walks backwards as he does: a vein or a dark region a few tiles ahead of a travelling
        // player is at the far edge of a circle centred behind him, and it drops out of range at the
        // moment he starts walking towards it. Anchored on the region it leads him, so work he is
        // heading into comes into range before he arrives at it. It reads the region's heading — his
        // centre carried by the lead — and not the box's centre, which sits a third of the box above
        // him because that is where a companion idles, not where work near him is.
        Microsoft.Xna.Framework.Vector2 anchor = ctx.Senses.Intent.Region.Heading;
        bool allowed = Microsoft.Xna.Framework.Vector2.DistanceSquared(target, anchor) <= radius * radius
            && Microsoft.Xna.Framework.Vector2.DistanceSquared(ctx.Npc.Bottom, anchor) <= radius * radius;
        return allowed;
    }

    /// <summary>The radius <see cref="AllowsTarget"/> tests against for this job: the continuation radius
    /// once the job is admitted, the discovery radius before. The combat snapshot carries it so the audit
    /// replays the same allowance the decision was admitted against.</summary>
    public float AllowanceRadius(object? identity = null)
    {
        var preferences = PlayerIntegration.CompanionPreferences.Current;
        bool sameJob = admittedIdentity != null && Equals(admittedIdentity, identity ?? ActivityIdentity);
        return sameJob ? preferences.ActiveActivityRadius : preferences.NewActivityRadius;
    }

    /// <summary>
    /// The course step this activity was selected to perform on this tick, or null when it runs with
    /// none. An activity that performs course work acts on this step's target and on nothing else: the
    /// course chose the target, and an activity that searched for its own would put a second chooser
    /// behind the first — the body flying to the course's vein while the hand swung at the activity's.
    /// </summary>
    public Infrastructure.Selection.Courses.StepBinding? Bound { get; private set; }

    /// <summary>Hand this activity the step it is selected to perform. Called by the activity owner on
    /// every selection, before the purpose identity is read, so an activity that derives its identity
    /// from the step reads the step it was given.</summary>
    public void Accept(Infrastructure.Selection.Courses.StepBinding? step)
    {
        Bound = step;
        StepRefusal = null;
        OnAccept(step);
    }

    /// <summary>
    /// Why this activity refused the step it was handed on the execution since its last selection, or null when it
    /// did not. The tick hands it to the course, which is the only thing that can stop the step being ordered again:
    /// the hand refuses by name and never substitutes, but a refusal the course never hears leaves the same step bound
    /// on the next tick and refused again — 111 invalid attempts in 115 ticks on the lane B review's silent-edit scene.
    /// It is set only for a proof that this target cannot be worked as bound, never for a step that is merely not yet
    /// in reach; combat does not set it, because its activation refusals say the binding names an older plan rather
    /// than that the target cannot be fought, and the course's own next-use validation already retires those.
    /// </summary>
    public string? StepRefusal { get; private set; }

    /// <summary>Record that the step this activity was handed cannot be performed as bound.</summary>
    protected void RefuseStep(string reason)
    {
        if (Bound != null) StepRefusal = reason;
    }

    /// <summary>What an activity does when handed a step: adopt its target. Default does nothing, which
    /// is right only for an activity that never performs course work.</summary>
    protected virtual void OnAccept(Infrastructure.Selection.Courses.StepBinding? step) { }

    public void AdmitActivity() => admittedIdentity = ActivityIdentity;
    protected void ReleaseActivity() => admittedIdentity = null;
    internal void ReleaseAdmission() => admittedIdentity = null;

    /// <summary>What the latest preparation established about this offer. Written only by Prepare,
    /// so repeated comparison reads the same classification it reads the same value from.</summary>
    public OfferEligibility Eligibility { get; private set; }
    public string EligibilityReason { get; private set; } = "not-prepared";
    protected void Classify(OfferEligibility eligibility, string reason)
    {
        Eligibility = eligibility;
        EligibilityReason = reason;
    }

    /// <summary>Called by the activity owner when an attempt opens. An activity clears the evidence
    /// its conclusion reads here, so a conclusion can only read what happened after this call; a
    /// job ended by the same tick's preparation, before selection opened this attempt, belongs to
    /// the attempt that selection just closed. Tick comparisons cannot make that distinction.</summary>
    public virtual void BeginAttempt() { }

    /// <summary>Called by the activity owner when selection replaces an executing attempt, before
    /// Exit clears any method state. Interruption is concluded by the owner and never reaches here.
    /// The default claims no completion: only an activity with its own success evidence may.</summary>
    public virtual AttemptConclusion ConcludeAttempt(int productiveEffects)
        => productiveEffects > 0
            ? new(AttemptStatus.Partial, "replaced-after-productive-effect")
            : new(AttemptStatus.Attempted, "replaced-before-productive-effect");

    public abstract string Name { get; }
    public abstract PurposeFamily Family { get; }

    /// <summary>
    /// Which course opportunity domains this activity performs, so the recorder can say what the course
    /// thought each job was worth under the activity's own column names. Empty means the course mints no
    /// opportunity for it — keeping company is the case, since an empty course *is* companionship rather
    /// than a domain anybody discovers.
    ///
    /// It is declared here, beside the activity, rather than derived from the name or held in a table in
    /// the recorder, because the two vocabularies genuinely differ and every difference is a decision:
    /// combat's activity is `combat` while its domain is combat's own use domain, lighting's activity is
    /// `place-torches` while its domain is `light-target`, and collection performs two domains rather than
    /// one. `ExecuteCourseBinding` writes the same mapping in the other direction for the same reason, and
    /// a third copy in the recorder would be the one that drifted.
    /// </summary>
    public virtual string[] CourseDomains => System.Array.Empty<string>();
    /// <summary>Optional excursions yield to regrouping; protection and survival opt out.</summary>
    public virtual bool IsExcursion => true;

    /// <summary>
    /// Whether a tool is in the arm the throw needs, right now. This is a phase of an action and
    /// never a property of which action is running: mining and chopping deliberately keep the hand
    /// empty for the whole walk to the vein, so gating the hands on the action's name switched
    /// shooting off for the approach as well as the swing. Across the two 2026-09-11 sessions that
    /// was 4,801 ticks reading hands-busy, every one of them during mine or chop, and 3,930 of them
    /// with a torch in the hand — a free arm reported as occupied.
    /// </summary>
    public virtual bool HandsBusy => false;

    /// <summary>Refresh discovery and capture candidate facts before utility comparison.</summary>
    public abstract void Prepare(in ActionContext ctx);

    /// <summary>0..1. Zero means "not now"; the product-of-considerations shape lives in each override.</summary>
    public abstract float Score();

    /// <summary>How many ticks this would keep the companion away from the player, 0 when it does not. An excursion
    /// must override it: an excursion inheriting zero is scored as instant, which is how a torch trip of 218 ticks
    /// outbid a slime hunt that honestly reported its flight (15 September 2026), and a fixture refuses it.</summary>
    public virtual float ForecastTicks() => 0f;

    /// <summary>
    /// How many ticks until this job is done, travel included: what the chooser's time term and the order of nearby
    /// jobs read. It defaults to <see cref="ForecastTicks"/> because a forecast that already counts the work (mining,
    /// chopping, a nearby interaction) has nothing to add; an activity whose forecast counts only the flight — hunting,
    /// which fires from its stand — adds the work here.
    /// </summary>
    public virtual float TaskTicks() => ForecastTicks();

    /// <summary>Whether this activity's whole worth is where the player is — protecting him or keeping him company —
    /// rather than a job somewhere. Such an activity is never discounted for time, never charged for being apart and
    /// never put in an order with jobs.</summary>
    public virtual bool ServesPlayerDirectly => false;

    /// <summary>Observe an executing activity after its controls and hands resolve. The
    /// activity owner withholds this callback during suspension.</summary>
    public virtual void ObserveOutcome(in ActionContext ctx) { }

    /// <summary>Called on the tick this action takes over.</summary>
    public virtual void Enter(in ActionContext ctx) { }

    /// <summary>Called on the tick another action takes over.</summary>
    public virtual void Exit(in ActionContext ctx) => ReleaseActivity();

    /// <summary>Release the interrupted physical method while retaining the current purpose's
    /// continuation allowance. Resumption still prepares candidates and enters again.</summary>
    public virtual void Suspend(in ActionContext ctx)
    {
        Exit(ctx);
        AdmitActivity();
    }

    /// <summary>Run one tick and say where to stand.</summary>
    public abstract PositionRequest Execute(in ActionContext ctx);
}
