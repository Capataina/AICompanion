#nullable enable

using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.PositionSelection;

/// <summary>Bounded destination evidence at nomination time. A solved firing position
/// is not proof of travel, arrival-time access or a subsequent native hit.</summary>
public readonly record struct PositionOffer(Vector2? Destination, string Reason,
    string Candidates, int SourceTick);
