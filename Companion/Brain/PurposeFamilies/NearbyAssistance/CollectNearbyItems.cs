#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using AICompanion.Companion.Brain.WorldObservation;
using AICompanion.Companion.Brain.WorldInteractions.WorldProtection;

namespace AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance;

/// <summary>
/// Compare fitting known drops with uncertain pot contents as one collecting purpose.
/// Shared costs select the offered method; actual contact pickup remains independent
/// in <see cref="Inventory.CompanionInventory"/>. A known drop is its own excursion: it proves
/// its own contact pose, walker reach and return, and is valued for the quantity the cargo can take.
/// </summary>
public sealed class CollectNearbyItems : PerformNearbyWorldWork
{
    public override string Name => "collect";
    public override PurposeFamily Family => PurposeFamily.NearbyAssistance;

    /// <summary>A drop chosen for collection: the item object and its type, where it lay when proven, the contact pose the
    /// companion walks to, the quantity the cargo can take, and what that quantity and walk are worth.</summary>
    private readonly record struct DropCandidate(Item Item, int Type, Vector2 Position, Vector2 Pose, float Near, float Value, float TripTicks);
    private DropCandidate? candidate;
    private bool collectDrop;
    private float preparedValue, preparedTrip;
    public string Method => collectDrop ? "known-drop" : base.Score() > 0 ? "potential-pot-contents" : "none";
    public override Vector2? ActivityTarget => collectDrop ? candidate?.Position : base.ActivityTarget;
    public override object? ActivityIdentity => collectDrop ? candidate?.Item : base.ActivityIdentity;

    /// <summary>The companion takes a world item whose hitbox meets its own hitbox grown by this many pixels on every side. It
    /// mirrors <c>CompanionNPC.PickupReach</c>, the contact pickup's own reach: if the two drift, a pose proven here either
    /// never touches the drop or sends the companion farther than contact needs.</summary>
    private const float PickupContactReach = 28f;

    /// <summary>How far from a drop's own tile a contact pose can stand: half the body and the pickup reach, in tiles.</summary>
    private static readonly int ContactSearchTiles = (int)MathF.Ceiling((BodyPhysics.Width / 2f + PickupContactReach) / 16f);

    /// <summary>A drop that has moved more than a tile from where it was proven is not where the proof applies; a tile is the
    /// resolution the contact pose was chosen at.</summary>
    private const float MovedDropPixels = 16f;

    public override void Prepare(in ActionContext ctx)
    {
        collectDrop = false;
        PrepareDrop(ctx);
        base.Prepare(ctx);
        float dropValue = candidate is { } drop ? drop.Near * drop.Value : 0f;
        float potValue = base.Score();
        float potTrip = base.ActivityTarget is { } position
            ? Vector2.Distance(ctx.Npc.Bottom, position) / Companion.CompanionMotor.WalkSpeed + Weights.PotContentsHandlingTicks : 0f;
        bool incumbent = ctx.Companion.Brain.Chooser.Current == this;
        var options = new PreparedActivity[]
        {
            new(0, "known-drop", dropValue, candidate?.TripTicks ?? 0f, true, candidate != null, false, incumbent,
                candidate != null ? OfferEligibility.Usable : OfferEligibility.NoOpportunity),
            new(1, "potential-pot-contents", potValue, potTrip, true, base.ActivityTarget != null, false, incumbent,
                base.ActivityTarget != null ? OfferEligibility.Usable : OfferEligibility.NoOpportunity),
        };
        var values = EvaluatePreparedActivities.Evaluate(options, ctx.Companion.Brain.Chooser.ComparisonContext(ctx));
        // Compare methods with the same costs used by the parent, then publish the chosen
        // raw offer. Publishing its already-discounted score would charge those costs twice.
        collectDrop = values[0].Final > 0 && values[0].Final >= values[1].Final;
        preparedValue = collectDrop ? dropValue : potValue;
        preparedTrip = collectDrop ? candidate!.Value.TripTicks : potTrip;
        // The two methods share one purpose, so the published classification describes the method
        // whose raw value is published. Pot contents stay unknown; the pot itself is a usable target.
        if (collectDrop) Classify(OfferEligibility.Usable, "known-drop-fits-cargo");
        else if (potValue > 0) Classify(OfferEligibility.Usable, "reachable-pot-contents-uncertain");
        else if (ctx.Senses.Player.IsDead) Classify(OfferEligibility.NoOpportunity, "player-dead");
        else if (candidate != null) Classify(OfferEligibility.Usable, "known-drop-valued-zero-under-shared-costs");
        else if (capacityRefused) Classify(OfferEligibility.KnownUnusable, "nearby-drops-exceed-cargo-capacity");
        else if (dropRefusal is { } refusal) Classify(refusal.Eligibility, refusal.Reason);
        else Classify(OfferEligibility.NoOpportunity, PlayerIntegration.CompanionPreferences.Current.PotBreaking
            ? "no-fitting-drop-or-reachable-pot" : "no-fitting-drop-and-pot-breaking-disabled");
    }

    private bool capacityRefused;
    // Why the nearest drops that fit were not offered. Undecided outranks known-unusable, because a search that declined to
    // answer is the one refusal that may resolve into an offer from somewhere else.
    private (OfferEligibility Eligibility, string Reason)? dropRefusal;

    private void Refuse(OfferEligibility eligibility, string reason)
    {
        if (dropRefusal is null || dropRefusal.Value.Eligibility == OfferEligibility.KnownUnusable && eligibility == OfferEligibility.Unresolved)
            dropRefusal = (eligibility, reason);
    }

    private void PrepareDrop(in ActionContext ctx)
    {
        candidate = null;
        capacityRefused = false;
        dropRefusal = null;
        if (ctx.Senses.Player.IsDead || ctx.Senses.Loot.Pickups.Count == 0)
            return;
        // Nearest first, and each drop proven for itself: the cargo takes some of it, a pose in pickup contact exists, the
        // companion can walk there, and walking there does not cut it off from a player it can reach now. One full,
        // unreachable or stranding drop must not block the next. A standable tile beside a drop proves none of this, and
        // mining's reach to the ore the drop fell from says nothing about the pit it fell into.
        int asked = 0;
        foreach (var pickup in ctx.Senses.Loot.Pickups)
        {
            Item item = pickup.Item;
            if (!LootSense.IsWorldDrop(item) || !AllowsTarget(ctx, item.Bottom, item)) continue;
            int acceptable = ctx.Companion.Bag.AcceptableQuantity(item, ctx.Player);
            if (acceptable <= 0)
            {
                capacityRefused = true;
                continue;
            }
            Vector2 pose;
            if (TouchesDrop(ctx.Npc.Hitbox, item))
                pose = ctx.Npc.Bottom;
            else
            {
                if (ContactPose(ctx.Npc.Bottom, item) is not Point contact)
                {
                    Refuse(OfferEligibility.KnownUnusable, "drop-has-no-contact-pose");
                    continue;
                }
                // Each reach question is a fresh bounded search, so one preparation asks about a bounded number of drops.
                if (asked++ >= Weights.CollectionReachCandidates) break;
                var (reach, back) = ProveExcursion(ctx, item, contact);
                if (reach == Reachability.Reach.Unknown) { Refuse(OfferEligibility.Unresolved, "drop-approach-undecided"); continue; }
                if (reach == Reachability.Reach.No) { Refuse(OfferEligibility.KnownUnusable, "drop-unreachable"); continue; }
                if (back == Reachability.Reach.No) { Refuse(OfferEligibility.KnownUnusable, "drop-would-strand-return"); continue; }
                pose = MovementQueries.FeetWorld(contact);
            }
            float near = Consideration.Inverse(pickup.DistanceToCompanion, Weights.LootReach);
            // The loot sense values the whole stack; the offer is for what the cargo can take, so its value is scaled by the
            // share of the whole stack's value that fits.
            float whole = LootSense.ValueOf(item, item.stack);
            float fits = whole > 0 ? LootSense.ValueOf(item, acceptable) / whole : 1f;
            // The same unit mining and chopping price their walk in: pixels to the working pose over walking speed.
            float trip = Vector2.Distance(ctx.Npc.Bottom, pose) / Companion.CompanionMotor.WalkSpeed;
            candidate = new(item, item.type, item.Bottom, pose, Consideration.AtLeast(near, 0.2f), pickup.Value * fits, trip);
            return;
        }
    }

    private static Rectangle BodyAt(Vector2 feet)
        => new((int)(feet.X - BodyPhysics.Width / 2f), (int)(feet.Y - BodyPhysics.Height), BodyPhysics.Width, BodyPhysics.Height);

    private static bool TouchesDrop(Rectangle body, Item item)
    {
        body.Inflate((int)PickupContactReach, (int)PickupContactReach);
        return body.Intersects(item.Hitbox);
    }

    /// <summary>The standable pose nearest the companion from which contact pickup touches the drop, or none.</summary>
    private static Point? ContactPose(Vector2 companionFeet, Item item)
    {
        Point around = MovementQueries.FeetTile(item.Bottom);
        Point? best = null;
        float bestDistance = float.MaxValue;
        for (int dx = -ContactSearchTiles; dx <= ContactSearchTiles; dx++)
            for (int dy = -ContactSearchTiles; dy <= ContactSearchTiles; dy++)
            {
                Point tile = new(around.X + dx, around.Y + dy);
                if (!MovementQueries.IsStandable(tile.X, tile.Y)) continue;
                Vector2 feet = MovementQueries.FeetWorld(tile);
                if (!TouchesDrop(BodyAt(feet), item)) continue;
                float distance = Vector2.DistanceSquared(feet, companionFeet);
                if (distance >= bestDistance) continue;
                best = tile;
                bestDistance = distance;
            }
        return best;
    }

    // Reach and return verdicts reused while a drop's contact pose and the terrain revision are unchanged, for a bounded time,
    // so a companion walking toward a drop does not ask the same two searches again on every tile it crosses.
    private readonly Dictionary<Item, (Point Pose, int Revision, ulong Tick, Reachability.Reach Reach, Reachability.Reach Return)> excursions
        = new(ReferenceEqualityComparer.Instance);

    private (Reachability.Reach Reach, Reachability.Reach Return) ProveExcursion(in ActionContext ctx, Item item, Point pose)
    {
        if (excursions.TryGetValue(item, out var known) && known.Pose == pose && known.Revision == TerrainChanges.Revision
            && Main.GameUpdateCount - known.Tick < (ulong)Weights.CollectionReachRecheckTicks)
            return (known.Reach, known.Return);
        Point feet = MovementQueries.FeetTile(ctx.Npc.Bottom);
        Reachability.Reach reach = MovementQueries.WalkerReach(feet, pose);
        Reachability.Reach back = Reachability.Reach.Yes;
        if (reach == Reachability.Reach.Yes)
        {
            // Marginal return: refused only when the companion can reach the player from where it stands and could not from
            // the drop. A companion already cut off from the player is not stranded further by collecting, and a player in
            // mid-air with no standable tile under them is not a proof that the return is gone.
            Point player = MovementQueries.FeetTile(ctx.Player.Bottom);
            if (MovementQueries.WalkerReach(pose, player) == Reachability.Reach.No
                && MovementQueries.WalkerReach(feet, player) != Reachability.Reach.No)
                back = Reachability.Reach.No;
        }
        if (excursions.Count > 32) excursions.Clear();
        excursions[item] = (pose, TerrainChanges.Revision, Main.GameUpdateCount, reach, back);
        return (reach, back);
    }

    public override float Score()
        => preparedValue;

    public override float ForecastTicks()
        => preparedTrip;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        if (!collectDrop) return base.Execute(ctx);
        ctx.Companion.HoldItem(ItemID.None);
        if (candidate is not { } prepared || !LootSense.IsWorldDrop(prepared.Item)
            || prepared.Item.type != prepared.Type || ctx.Companion.Bag.AcceptableQuantity(prepared.Item, ctx.Player) <= 0)
            return PositionRequest.Hold;
        // A drop that rolled or fell after it was proven is not where its reach and return were proven: hold this tick, and the
        // next preparation proves it where it now lies instead of walking to where it was.
        if (Vector2.DistanceSquared(prepared.Item.Bottom, prepared.Position) > MovedDropPixels * MovedDropPixels)
            return PositionRequest.Hold;
        if (dropAttempt is not { } open || !ReferenceEquals(open.Item, prepared.Item))
            dropAttempt = new(prepared.Item, prepared.Type, prepared.Item.stack, ctx.Companion.Bag, ctx.Companion.Bag.TransferSequence);
        return PositionRequest.ExactAt(prepared.Pose);
    }

    /// <summary>The drop this attempt walked toward, its stack when the walk began, and the cargo's transfer mark then.</summary>
    private readonly record struct DropAttempt(Item Item, int Type, int StartStack, Inventory.CompanionInventory Bag, long TransferMark);
    private DropAttempt? dropAttempt;

    /// <summary>Whether <paramref name="item"/> is the world item object this activity's drop attempt walked toward, which is the
    /// only pickup its conclusion reads from the transfer ledger. Every other contact pickup belongs to no collection attempt.</summary>
    public bool ClaimsDrop(Item item) => dropAttempt is { } drop && ReferenceEquals(drop.Item, item);

    public override void BeginAttempt()
    {
        base.BeginAttempt();
        dropAttempt = null;
    }

    /// <summary>
    /// A drop's attempt is concluded from what the cargo actually received from that item object after the walk began, never
    /// from the drop merely leaving the world, which the player's pickup or despawning also does. All of it received and the
    /// drop gone: the companion's own completion. Part received and the rest gone: shared. Gone with nothing received:
    /// the purpose went away, which is invalid. Part received and the rest still lying there: partial. Pot attempts use the
    /// shared interaction conclusion.
    /// </summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
    {
        if (dropAttempt is { } drop)
        {
            int received = drop.Bag.TransferredSince(drop.TransferMark, drop.Item);
            bool gone = !LootSense.IsWorldDrop(drop.Item) || drop.Item.type != drop.Type;
            // What the ledger received from this drop is the claimed yield, so a report can hold the completion against
            // the pickups recorded under this attempt rather than taking the status on trust.
            if (gone)
                return received <= 0
                    ? new(AttemptStatus.Invalid, "drop-left-world-without-companion-transfer")
                    : new(AttemptStatus.Complete, "drop-collected", received >= drop.StartStack ? AttemptAttribution.Companion : AttemptAttribution.Shared, drop.Type, received);
            return received > 0
                ? new(AttemptStatus.Partial, "drop-partly-transferred", AttemptAttribution.NotApplicable, drop.Type, received)
                : new(AttemptStatus.Attempted, "replaced-with-drop-still-in-world");
        }
        return base.ConcludeAttempt(productiveEffects);
    }

    protected override string CompletedEffect => "pot-broken-contents-unobserved";
    protected override float Utility => Weights.PotContentsValue;
    protected override bool Enabled(in ActionContext ctx)
        => PlayerIntegration.CompanionPreferences.Current.PotBreaking && ctx.Companion.Bag.Count < Inventory.CompanionInventory.Slots;
    protected override (OfferEligibility Eligibility, string Reason) DisabledOffer(in ActionContext ctx)
        => ctx.Player.dead ? (OfferEligibility.NoOpportunity, "player-dead")
            : !PlayerIntegration.CompanionPreferences.Current.PotBreaking ? (OfferEligibility.PolicyForbidden, "pot-breaking-disabled")
            : (OfferEligibility.KnownUnusable, "cargo-full-for-unknown-contents");

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
