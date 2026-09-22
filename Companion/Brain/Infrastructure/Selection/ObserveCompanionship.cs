#nullable enable

using System;
using AICompanion.Companion.Brain.Activities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>
/// Everything companionship observes each tick: how long the body has been apart, how long it would take
/// to get back, what that delay costs, and how hard it is being pulled home. It is observation rather
/// than choice, which is why it is its own thing and not part of any decision.
///
/// **It used to live inside `Chooser.Choose`, and all of it but the apart-tick count was dead in play for
/// ten hours on 21 September 2026.** `CoordinateBrainTick` called the apart-tick observation directly, so
/// the count kept running, while `Reunion.Evaluate`, the return estimate and the regroup urgency were
/// reached only through `Choose` — which `0bb2c8a` took off the tick at 08:21. The recorder's three
/// reunion columns therefore carried one live number beside two frozen defaults, and
/// `KeepCompany.CalculateReunionValue` read a regroup urgency of exactly zero. Nothing went red, because
/// a frozen float is a legal float, and `4e7d094` found it at 18:42 that evening by reading the columns.
///
/// **What `AIC-419` did the next day is relocate this out of the class it deleted, not revive it.** Every
/// number here was already live at that lane's branch point, and the row below is `4e7d094`'s rather than
/// the extraction's. Saying so matters because the deletion is the conspicuous commit: a reader crediting
/// the extraction with the fix dates the defect a day late and assumes the guard is as young as the class.
///
/// The general shape, which is the part worth keeping and the reason this is a separate class rather
/// than a method on whatever decides: **a decision procedure that also observes leaves its observations
/// stranded when something replaces the decision.** Anything else moved off a decision is checked the
/// same way — by asking what it wrote, not only what it returned.
/// `Tools/EngineReplay/Observation/VerifyTheCourseOwnsTheTick.cs`'s companionship row is that check, and
/// it is proved by mutation: restoring the pre-fix wiring reddens it.
/// </summary>
public sealed class ObserveCompanionship
{
    /// <summary>Separation history and the marginal cost of delaying reunion. Nothing decides on the cost;
    /// the recorder writes it with its departure and apart-tick inputs, and a reader of those columns is
    /// reading a diagnostic rather than a factor.</summary>
    public AssessReunionCost Reunion { get; } = new();
    /// <summary>How hard the body is being pulled home, zero anywhere inside the player's intent region.
    /// `KeepCompany.CalculateReunionValue` reads it, so this is a live input and not only a column.</summary>
    public float RegroupUrgency { get; private set; }
    public float EstimatedReturnTicks { get; private set; }

    public void Observe(in ActionContext ctx)
    {
        var objective = ctx.Senses.Intent.Objective;
        Reunion.Observe(Terraria.Main.GameUpdateCount,
            objective.IsSatisfied(ctx.Npc.Center), ctx.Senses.Player.IsDead);

        // Reunion, excursion and return cost are all "how far from the player", and they measure to
        // the intent region's centre: a companion pricing its way back to where the player was
        // standing prices a trip that is already out of date on a player who is walking.
        var region = ctx.Senses.Intent.Region;
        var delta = region.Centre - ctx.Npc.Center;
        EstimatedReturnTicks = delta.Length() / Infrastructure.Movement.OrbPace.MaxSpeed;
        var navigator = ctx.Companion.Brain.Navigator;
        // The route home is priced to the cell at the region's centre, which is air a third of the box
        // above the player's centre rather than his feet, so it is the plain tile. It was `FeetTile` while
        // the centre was his feet, because the tile feet floor into is the solid floor row the flood never
        // holds, and priced to that the estimate was null and the straight line silently won every tick.
        if (ctx.Companion.Brain.Positioner.EstimatedTravelTicks(Infrastructure.Movement.MovementQueries.Tile(ctx.Npc.Center),
            Infrastructure.Movement.MovementQueries.Tile(region.Centre)) is float knownTravel)
            EstimatedReturnTicks = MathF.Max(EstimatedReturnTicks, knownTravel);
        if (ctx.Companion.Brain.LastRequest.Kind is Infrastructure.Position.RequestKind.WithPlayer or Infrastructure.Position.RequestKind.FireFrom
            && navigator.Path != null)
            EstimatedReturnTicks = MathF.Max(EstimatedReturnTicks, navigator.RemainingEstimatedRouteTicks);
        float movingAway = delta.LengthSquared() > 1f ? Microsoft.Xna.Framework.Vector2.Dot(ctx.Senses.Player.Intent, Microsoft.Xna.Framework.Vector2.Normalize(delta)) : 0f;
        Reunion.Evaluate(movingAway, EstimatedReturnTicks, ctx.Senses.Player.IsDead, ctx.Stranded);
        // Regrouping is measured on the gap beyond the region's rectangle, like every other separation, so it is exactly
        // zero anywhere inside the region: the owner ruled there is no pull there, whatever the body is doing and whatever a
        // route round a thin wall would cost. The travel pressure below is gated the same way for the same reason.
        float gapBeyond = region.GapBeyond(ctx.Npc.Center);
        bool insideRegion = gapBeyond <= 0f;
        RegroupUrgency = ctx.Senses.Player.IsDead ? 0f : Infrastructure.Observation.CalculateRegroupUrgency.Evaluate(
            gapBeyond, EstimatedReturnTicks, movingAway, navigator.StuckTicks, MathF.Max(region.HalfSize.X, region.HalfSize.Y),
            Weights.RegroupFullDistance, Weights.RegroupFreeReturnTicks, Weights.RegroupFullReturnTicks);
        if (!ctx.Senses.Player.IsDead && !insideRegion)
        {
            // A nearby player behind a floor can have a long route. Geometric closeness must
            // not suppress measured return pressure when companionship is still unsatisfied.
            float travelPressure = Math.Clamp((EstimatedReturnTicks + navigator.StuckTicks - Weights.RegroupFreeReturnTicks)
                / Math.Max(1f, Weights.RegroupFullReturnTicks - Weights.RegroupFreeReturnTicks), 0f, 1f);
            RegroupUrgency = Math.Max(RegroupUrgency, travelPressure);
        }
    }
}
