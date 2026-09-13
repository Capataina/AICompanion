#nullable enable

using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.WorldObservation;
using AICompanion.Companion.CharacterBody;

namespace AICompanion.Companion.Brain.Behaviours;

/// <summary>
/// Everything an action needs in one handle. <paramref name="Stranded"/> is the one fact that
/// comes back up from navigation: the brain's word that the body is in a sealed pocket and this
/// tick is one for walking it rather than pressing at the player, read like the breath is read,
/// as last tick's outcome about the body and never as a decision.
/// </summary>
public readonly record struct ActionContext(CompanionNPC Companion, WorldObservation.Senses Senses, bool Stranded = false)
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
        bool collectingWork = Name == "collect" && identity is Terraria.Item && ctx.Companion.Brain.Chooser.IsCollectingWork(target);
        float radius = sameJob || collectingWork ? preferences.ActiveActivityRadius : preferences.NewActivityRadius;
        bool allowed = Microsoft.Xna.Framework.Vector2.DistanceSquared(target, ctx.Player.Bottom) <= radius * radius
            && Microsoft.Xna.Framework.Vector2.DistanceSquared(ctx.Npc.Bottom, ctx.Player.Bottom) <= radius * radius;
        return allowed;
    }

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

    /// <summary>The chooser clears the classification before each preparation, so a preparation path
    /// that returns without classifying reads as no opportunity rather than keeping last tick's.</summary>
    internal void ResetClassification() => Classify(OfferEligibility.NoOpportunity, "not-classified-this-preparation");

    public abstract string Name { get; }
    public abstract PurposeFamily Family { get; }
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

    /// <summary>How many ticks this would keep the companion away from the player, 0 when it does not.</summary>
    public virtual float ForecastTicks() => 0f;

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
