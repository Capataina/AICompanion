#nullable enable

using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Position;

/// <summary>
/// The choice reasons that mean the offer is not settled, as opposed to the ones that mean it finished and
/// found nothing. The distinction is the whole point of the three-valued offer: a stand the reach flood has
/// not claimed yet is neither admitted nor refused, and reporting that as a proven impossibility is what let
/// a hunt be vetoed by its own budget and the companion flip to keeping company on the tick the budget ran
/// out. These strings are the vocabulary the resolver writes and the chooser reads; a reason not named here
/// is a settled answer.
/// </summary>
public static class PositionReasons
{
    /// <summary>The held destination was re-proved at this rescore and kept, so nothing else was searched.</summary>
    public const string Retained = "retained-position";
    /// <summary>The combat stand's tile is not yet claimed by the reach flood, so the method is neither admitted nor refused.</summary>
    public const string FireStandUndecided = "fire-stand-undecided";

    /// <summary>Whether the reason says the flood has not answered rather than that it answered.</summary>
    public static bool Undecided(string reason) => reason == FireStandUndecided;
}

/// <summary>Bounded destination evidence at nomination time. A solved firing position
/// is not proof of travel, arrival-time access or a subsequent native hit.</summary>
public readonly record struct PositionOffer(Vector2? Destination, string Reason,
    string Candidates, int SourceTick)
{
    /// <summary>Whether this offer's reason means the flood has not answered rather than that it answered.
    /// Unanswered is never a proven negative: the chooser classifies it unresolved, so a later rescore can still settle it.</summary>
    public bool Undecided => PositionReasons.Undecided(Reason);
}
