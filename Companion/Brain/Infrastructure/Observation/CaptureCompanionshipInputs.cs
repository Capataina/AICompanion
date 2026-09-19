using System.Text.Json;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>Freezes the same region, travel and preference inputs that live companionship
/// reads. Pure consequence forecasts never consult the current preference or player again.</summary>
public static class CaptureCompanionshipInputs
{
    public static FactKey Key => CapturedCompanionshipRegion.Key;

    public static DecisionFact Capture(in ActionContext context, long revision)
    {
        var region = context.Senses.Intent.Region;
        var travel = context.Senses.Player.Intent;
        var captured = new CapturedCompanionshipRegion(new(region.Centre.X, region.Centre.Y),
            new(region.HalfSize.X, region.HalfSize.Y), new(travel.X, travel.Y), Weights.PlayerProjectionCapTicks,
            PlayerIntegration.CompanionPreferences.Current.RecoveryRadius, !context.Senses.Player.IsDead);
        return new(Key, revision, new(Text: JsonSerializer.Serialize(captured)), FactEvidence.Observed);
    }
}
