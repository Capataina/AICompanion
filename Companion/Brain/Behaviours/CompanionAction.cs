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
    public bool HasActivityAllowance => admittedIdentity != null;

    /// <summary>Only an entered job earns the continuation radius; discovering a target does not.</summary>
    protected bool AllowsTarget(in ActionContext ctx, Microsoft.Xna.Framework.Vector2 target, object? identity = null)
    {
        var preferences = PlayerIntegration.CompanionPreferences.Current;
        bool sameJob = admittedIdentity != null && Equals(admittedIdentity, identity ?? ActivityIdentity);
        bool collectingWork = Name == "loot" && ctx.Companion.Brain.Chooser.IsCollectingWork(target);
        float radius = sameJob || collectingWork ? preferences.ActiveActivityRadius : preferences.NewActivityRadius;
        bool allowed = Microsoft.Xna.Framework.Vector2.DistanceSquared(target, ctx.Player.Bottom) <= radius * radius
            && Microsoft.Xna.Framework.Vector2.DistanceSquared(ctx.Npc.Bottom, ctx.Player.Bottom) <= radius * radius;
        return allowed;
    }

    public void AdmitActivity() => admittedIdentity = ActivityIdentity;
    protected void ReleaseActivity() => admittedIdentity = null;
    public abstract string Name { get; }
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

    /// <summary>0..1. Zero means "not now"; the product-of-considerations shape lives in each override.</summary>
    public abstract float Score(in ActionContext ctx);

    /// <summary>How many ticks this would keep the companion away from the player, 0 when it does not.</summary>
    public virtual float ForecastTicks(in ActionContext ctx) => 0f;

    /// <summary>Called on the tick this action takes over.</summary>
    public virtual void Enter(in ActionContext ctx) { }

    /// <summary>Called on the tick another action takes over.</summary>
    public virtual void Exit(in ActionContext ctx) => ReleaseActivity();

    /// <summary>Run one tick and say where to stand.</summary>
    public abstract PositionRequest Execute(in ActionContext ctx);
}
