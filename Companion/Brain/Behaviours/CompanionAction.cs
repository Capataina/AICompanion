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
    public abstract string Name { get; }

    /// <summary>0..1. Zero means "not now"; the product-of-considerations shape lives in each override.</summary>
    public abstract float Score(in ActionContext ctx);

    /// <summary>How many ticks this would keep the companion away from the player, 0 when it does not.</summary>
    public virtual float ForecastTicks(in ActionContext ctx) => 0f;

    /// <summary>Called on the tick this action takes over.</summary>
    public virtual void Enter(in ActionContext ctx) { }

    /// <summary>Called on the tick another action takes over.</summary>
    public virtual void Exit(in ActionContext ctx) { }

    /// <summary>Run one tick and say where to stand.</summary>
    public abstract PositionRequest Execute(in ActionContext ctx);
}
