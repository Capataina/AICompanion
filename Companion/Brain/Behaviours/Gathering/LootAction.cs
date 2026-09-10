#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using AICompanion.Companion.Brain.WorldObservation;

namespace AICompanion.Companion.Brain.Behaviours.Gathering;

/// <summary>
/// Walk to an item and pick it up. Scores by the nearest pickup's value and distance,
/// forecasts the trip, and so only wins when the horizon allows it; that is the whole
/// of "loot the slime's drops before coming back, unless the player is in danger".
/// Pickup itself happens on contact in <see cref="Inventory.CompanionInventory"/>.
/// </summary>
public sealed class LootAction : CompanionAction
{
    public override string Name => "loot";

    private Item? target;
    public override Vector2? ActivityTarget => target?.Bottom;
    public override object? ActivityIdentity => target;

    public override float Score(in ActionContext ctx)
    {
        target = null;
        if (ctx.Senses.Player.IsDead || ctx.Senses.Loot.Pickups.Count == 0)
            return 0f;
        // The nearest pickup that fits somewhere and has a standable tile beside it; one item
        // in lava or on a ledge nobody can reach must not block every other item.
        LootSense.Pickup? chosen = null;
        foreach (var candidate in ctx.Senses.Loot.Pickups)
        {
            if (!AllowsTarget(ctx, candidate.Item.Bottom, candidate.Item)) continue;
            if (!ctx.Companion.Bag.CanAccept(candidate.Item, ctx.Player))
                continue;
            if (MovementQueries.NearestStandable(MovementQueries.FeetTile(candidate.Item.Bottom), 3) == null)
                continue;
            chosen = candidate;
            break;
        }
        if (chosen is not LootSense.Pickup pick)
            return 0f;
        target = pick.Item;
        float near = Consideration.Inverse(pick.DistanceToCompanion, Weights.LootReach);
        float safe = Consideration.AtLeast(1f - ctx.Senses.Threats.PlayerDanger, 0.05f);
        return Consideration.AtLeast(near, 0.2f) * pick.Value * safe;
    }

    public override float ForecastTicks(in ActionContext ctx)
        => target == null ? 0f : Vector2.Distance(ctx.Npc.Center, target.Center) * Weights.LootTripTicksPerPx / Companion.CompanionMotor.WalkSpeed * 1.5f;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(ItemID.None);
        if (target == null || !target.active)
            return PositionRequest.Hold;
        return PositionRequest.ExactAt(target.Bottom);
    }
}
