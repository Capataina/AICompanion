#nullable enable

using System.Collections.Generic;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Activities.Combat;
using AICompanion.Companion.Brain.Activities.Combat.Planning;
using AICompanion.Companion.Brain.Activities.Gathering;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// One frozen observation per brain tick, assembled from every domain's own capture.
///
/// This is the boundary the whole retained-course design rests on: after it returns, discovery,
/// binding, projection and comparison read this value and nothing else. No source may consult
/// Terraria or <see cref="Senses"/> again, so a decision is reproducible from the snapshot alone and
/// a replay cannot quietly observe a newer world than the decision did.
///
/// It composes rather than observes. Every capture already takes the live world and the shared
/// allowance and returns facts; what was missing was a caller that runs them together under one
/// identity and one clock, which is why no production code built a <see cref="DecisionFactSnapshot"/>
/// before this existed.
/// </summary>
public sealed class AssembleCourseSnapshot
{
    // Each capture holds a cursor across ticks so a sliced census resumes rather than restarting.
    // They are fields rather than locals for exactly that reason, and ResetWorld clears them together.
    private readonly CaptureAssistanceOpportunities assistance = new();
    private readonly CaptureGatheringOpportunities gathering = new();
    private long id;

    /// <summary>The snapshot most recently assembled, for the consumers that compare identity across
    /// ticks. Null until the first tick of a companion's life.</summary>
    public DecisionFactSnapshot? Current { get; private set; }

    /// <summary>
    /// Freeze this tick's world. The allowance is the brain tick's own, so a census that runs long
    /// takes its cut from the same budget everything else shares rather than minting time of its own;
    /// a cut census publishes partial coverage and its domain reports an unfinished search, which is
    /// the three-valued answer every consumer already knows how to refuse.
    /// </summary>
    public DecisionFactSnapshot Capture(in ActionContext context, CompanionCombat combat,
        SearchAttackPlans.SearchResult? search, DecisionWorkBudget budget)
    {
        // Closing the previous callback epoch first is what makes the watermark below meaningful:
        // it returns the ordinal this observation owns, and every receipt already drained belongs to
        // an earlier one. Taking the ordinal after the captures would let a strike landing mid-capture
        // appear to precede the observation that missed it.
        long ordinal = CollectNativeEffectReceipts.MarkBrainBoundary();

        var facts = new List<DecisionFact>();
        // Assistance owns drops, pots and light; gathering owns ore and trunks. Combat's census is the
        // planner's own priced front rather than a world scan, which is why it needs the search result
        // and why it carries no coverage fact: a front is complete by construction or absent.
        facts.AddRange(assistance.Capture(context.Senses, context));
        facts.AddRange(gathering.Capture(context, budget));
        facts.AddRange(CombatCourseFacts.Capture(context, combat, search));
        facts.Add(CaptureCompanionshipInputs.Capture(context, ordinal));
        facts.Add(CaptureCourseContactCensus.Capture(budget).ToFact(ordinal));

        // The five identity fields are compared in full by IsModelExtensionOf, which is how a derived
        // query that finishes after the freeze is admitted without letting it change the world the
        // decision was made about. The id rises per observation; the tick, epoch and watermark are the
        // world's own, so a snapshot cannot claim to extend one taken in a different world or frame.
        Current = new DecisionFactSnapshot(++id, CollectNativeEffectReceipts.WorldEpoch,
            context.Senses.Tick, ordinal, CollectNativeEffectReceipts.Watermark, facts);
        return Current;
    }

    /// <summary>A world reload must not leave a capture holding the previous world's sites, and must
    /// not let the next snapshot id look like a continuation of the old world's sequence.</summary>
    public void ResetWorld()
    {
        assistance.ResetWorld();
        gathering.ResetWorld();
        Current = null;
        id = 0;
    }
}
