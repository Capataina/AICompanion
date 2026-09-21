using System.Text.Json;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// The player's own motion history, frozen into the decision's facts so the contact forecast can ask
/// where he will be rather than assuming he stays where he was.
///
/// It is a fact of its own rather than a field on the victim capture because the two answer different
/// questions and change on different clocks: the victim is his life, his defence and his immunity, and
/// this is a track that the motion law extends every tick. Keeping them apart means a forecast that
/// consumed the track is dirtied when the track moves without being dirtied by a point of life.
///
/// The track is exported in exactly the shape a hostile's is, so <see cref="PredictObservedMotion"/>
/// restores and extends it by the same law. That is the property worth having: the player's predicted
/// path and a zombie's predicted path cannot disagree about physics, because there is one physics.
/// </summary>
public sealed record CapturedPlayerMotion(ulong Tick, int Width, int Height,
    PredictObservedMotion.ExportedTrack Motion)
{
    public static FactKey Key => new("player-motion", "local");

    public DecisionFact ToFact(long revision) => new(Key, revision,
        new(Text: JsonSerializer.Serialize(this)), FactEvidence.Observed);

    /// <summary>
    /// Observe the player and export his track. One operation: observing is a handful of arithmetic on
    /// values the engine already holds, and the forecast that samples this track pays per tick it
    /// actually samples, under its own budget, where the cost belongs.
    /// </summary>
    public static CapturedPlayerMotion? Capture(Player player, DecisionWorkBudget budget)
    {
        if (!budget.TrySpend("capture-player-motion")) return null;
        PredictObservedMotion.Observe(player);
        var track = PredictObservedMotion.ExportTrack(PredictObservedMotion.PlayerSlot);
        return track == null ? null : new(Main.GameUpdateCount, player.width, player.height, track);
    }
}
