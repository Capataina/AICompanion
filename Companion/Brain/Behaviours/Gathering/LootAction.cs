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
    public override PurposeFamily Family => PurposeFamily.NearbyAssistance;

    private readonly record struct Candidate(Item Item, int Type, Vector2 Position, float Near, float Value, float Safety, float TripTicks);
    private Candidate? candidate;
    private Item? target => candidate?.Item;
    public override Vector2? ActivityTarget => candidate?.Position;
    public override object? ActivityIdentity => target;

    public override void Prepare(in ActionContext ctx)
    {
        candidate = null;
        if (ctx.Senses.Player.IsDead || ctx.Senses.Loot.Pickups.Count == 0)
            return;
        // The nearest pickup that fits somewhere and has a standable tile beside it; one item
        // in lava or on a ledge nobody can reach must not block every other item.
        LootSense.Pickup? chosen = null;
        foreach (var pickup in ctx.Senses.Loot.Pickups)
        {
            if (!pickup.Item.active || pickup.Item.stack <= 0 || !AllowsTarget(ctx, pickup.Item.Bottom, pickup.Item)) continue;
            if (!ctx.Companion.Bag.CanAccept(pickup.Item, ctx.Player))
                continue;
            if (MovementQueries.NearestStandable(MovementQueries.FeetTile(pickup.Item.Bottom), 3) == null)
                continue;
            chosen = pickup;
            break;
        }
        if (chosen is not LootSense.Pickup pick)
            return;
        float near = Consideration.Inverse(pick.DistanceToCompanion, Weights.LootReach);
        float safe = Consideration.AtLeast(1f - ctx.Senses.Threats.PlayerDanger, 0.05f);
        float trip = Vector2.Distance(ctx.Npc.Center, pick.Item.Center) * Weights.LootTripTicksPerPx / Companion.CompanionMotor.WalkSpeed * 1.5f;
        candidate = new(pick.Item, pick.Item.type, pick.Item.Bottom, Consideration.AtLeast(near, 0.2f), pick.Value, safe, trip);
    }

    public override float Score()
        => candidate is { } prepared ? prepared.Near * prepared.Value * prepared.Safety : 0f;

    public override float ForecastTicks()
        => candidate?.TripTicks ?? 0f;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(ItemID.None);
        if (candidate is not { } prepared || !prepared.Item.active || prepared.Item.stack <= 0
            || prepared.Item.type != prepared.Type || !ctx.Companion.Bag.CanAccept(prepared.Item, ctx.Player))
            return PositionRequest.Hold;
        return PositionRequest.ExactAt(prepared.Position);
    }
}
