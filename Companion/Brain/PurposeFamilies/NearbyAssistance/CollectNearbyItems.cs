#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.Behaviours.Work;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using AICompanion.Companion.Brain.WorldObservation;
using AICompanion.Companion.Brain.WorldInteractions.WorldProtection;

namespace AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance;

/// <summary>
/// Compare fitting known drops with uncertain pot contents as one collecting purpose.
/// Shared costs select the offered method; actual contact pickup remains independent
/// in <see cref="Inventory.CompanionInventory"/>.
/// </summary>
public sealed class CollectNearbyItems : PerformNearbyWorldWork
{
    public override string Name => "collect";
    public override PurposeFamily Family => PurposeFamily.NearbyAssistance;

    private readonly record struct DropCandidate(Item Item, int Type, Vector2 Position, float Near, float Value, float Safety, float TripTicks);
    private DropCandidate? candidate;
    private bool collectDrop;
    private float preparedValue, preparedTrip;
    public string Method => collectDrop ? "known-drop" : base.Score() > 0 ? "potential-pot-contents" : "none";
    public override Vector2? ActivityTarget => collectDrop ? candidate?.Position : base.ActivityTarget;
    public override object? ActivityIdentity => collectDrop ? candidate?.Item : base.ActivityIdentity;

    public override void Prepare(in ActionContext ctx)
    {
        collectDrop = false;
        PrepareDrop(ctx);
        base.Prepare(ctx);
        float dropValue = candidate is { } drop ? drop.Near * drop.Value * drop.Safety : 0f;
        float potValue = base.Score();
        float potTrip = base.ActivityTarget is { } position
            ? Vector2.Distance(ctx.Npc.Bottom, position) / Companion.CompanionMotor.WalkSpeed + Weights.PotContentsHandlingTicks : 0f;
        bool incumbent = ctx.Companion.Brain.Chooser.Current == this;
        var options = new PreparedActivity[]
        {
            new(0, "known-drop", dropValue, candidate?.TripTicks ?? 0f, true, candidate != null, false, incumbent),
            new(1, "potential-pot-contents", potValue, potTrip, true, base.ActivityTarget != null, false, incumbent),
        };
        var values = EvaluatePreparedActivities.Evaluate(options, ctx.Companion.Brain.Chooser.ComparisonContext(ctx));
        // Compare methods with the same costs used by the parent, then publish the chosen
        // raw offer. Publishing its already-discounted score would charge those costs twice.
        collectDrop = values[0].Final > 0 && values[0].Final >= values[1].Final;
        preparedValue = collectDrop ? dropValue : potValue;
        preparedTrip = collectDrop ? candidate!.Value.TripTicks : potTrip;
    }

    private void PrepareDrop(in ActionContext ctx)
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
        => preparedValue;

    public override float ForecastTicks()
        => preparedTrip;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        if (!collectDrop) return base.Execute(ctx);
        ctx.Companion.HoldItem(ItemID.None);
        if (candidate is not { } prepared || !prepared.Item.active || prepared.Item.stack <= 0
            || prepared.Item.type != prepared.Type || !ctx.Companion.Bag.CanAccept(prepared.Item, ctx.Player))
            return PositionRequest.Hold;
        return PositionRequest.ExactAt(prepared.Position);
    }

    protected override float Utility => Weights.PotContentsValue;
    protected override bool Enabled(in ActionContext ctx)
        => PlayerIntegration.CompanionPreferences.Current.PotBreaking && ctx.Companion.Bag.Count < Inventory.CompanionInventory.Slots;

    protected override bool Candidate(in ActionContext ctx, Point tile)
    {
        if (!WorldGen.InWorld(tile.X, tile.Y, 6) || ProtectCompanionHomes.IsProtected(tile)) return false;
        Tile t = Main.tile[tile.X, tile.Y];
        if (!t.HasTile || t.TileType != TileID.Pots) return false;
        Point origin = new(tile.X - t.TileFrameX / 18 % 2, tile.Y - t.TileFrameY / 18 % 2);
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++)
            if (ProtectCompanionHomes.IsProtected(origin + new Point(x, y))) return false;
        return true;
    }

    protected override bool Perform(in ActionContext ctx, Point tile)
    {
        if (!Enabled(ctx) || !Candidate(ctx, tile) || !WorldGen.CanKillTile(tile.X, tile.Y)) return false;
        WorldGen.KillTile(tile.X, tile.Y);
        bool broken = !Main.tile[tile.X, tile.Y].HasTile;
        if (broken) BehaviourDiagnostics.GodsEyeEvents.RecordWorldInteraction(ctx.Npc, tile, "break-pot", "native pot drops; collected yield unobserved");
        return broken;
    }
}
