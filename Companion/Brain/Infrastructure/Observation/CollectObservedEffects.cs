#nullable enable

using System.Collections.Generic;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>Receipt watermarks are captured before evaluation so later native effects cannot be silently read as snapshot facts.</summary>
public readonly record struct ObservedBrainFacts(long Tick, long WorldEpoch, long ObservationOrdinal,
    PlayerMotionEvidence PlayerMotion, PlayerIntentRegions IntentRegions, long CapabilityRevision,
    long PolicyRevision, long EncounterRevision, long ReceiptWatermark);

/// <summary>Collects the immutable observation slice consumed by a decision; course manifests remain Selection-owned.</summary>
public sealed class CollectObservedEffects
{
    private readonly ObservePlayerMotionEvidence motion = new();
    public ObserveDecisionCapabilities Capabilities { get; } = new();
    public long WorldEpoch => CollectNativeEffectReceipts.WorldEpoch;

    public ObservedBrainFacts Capture(Senses senses, long observationOrdinal, long receiptWatermark)
        => new(senses.Tick, WorldEpoch, observationOrdinal, motion.Observe(senses.Player, senses.Tick), senses.Intent.Regions,
            Capabilities.CapabilityRevision, Capabilities.PolicyRevision, Capabilities.EncounterRevision, receiptWatermark);

    public void ResetWorld() { motion.Reset(); Capabilities.Reset(); }
}
