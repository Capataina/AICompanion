#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Interactions.WorldProtection;

namespace AICompanion.Companion.Brain.Activities.NearbyAssistance;

/// <summary>
/// Compare fitting known drops with uncertain pot contents as one collecting purpose.
/// Shared costs select the offered method; actual contact pickup remains independent
/// in <see cref="Inventory.CompanionInventory"/>. A known drop is its own excursion: it proves
/// its own contact pose, walker reach and return, and is valued for the quantity the cargo can take.
/// </summary>
public sealed class CollectNearbyItems : PerformNearbyWorldWork, ICandidateFunnelSource
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

    /// <summary>Contact poses are proved inside the live pickup reach minus the navigator's arrival slack, so a body that
    /// stops twelve pixels short of the pose still intersects the drop. Proving at the full inflate picked the most
    /// marginal tile that still grazed, and on a slope that graze missed for thousands of ticks.</summary>
    private static float ProvePickupReach => MathF.Max(8f, PickupContactReach - Navigator.ArriveDistance);

    /// <summary>How far from a drop's own tile a contact cell can lie: the body's radius and the pickup reach, in tiles.</summary>
    private static readonly int ContactSearchTiles = (int)MathF.Ceiling((CircleContact.Radius + PickupContactReach) / 16f);

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
            ? Vector2.Distance(ctx.Npc.Center, position) / OrbPace.MaxSpeed + Weights.PotContentsHandlingTicks : 0f;
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

    // The known-drop method's stages, in the order a drop meets them.
    private const string StageCapacity = "cargo-capacity";
    private const string StageLanding = "landing-undecided";
    private const string StageContactPose = "no-contact-pose";
    private const string StageApproachUndecided = "approach-undecided";
    private const string StageUnreachable = "unreachable";

    /// <summary>What the last preparation did with each drop it looked at, nearest first: the drop that stayed unoffered
    /// for want of a contact pose and the one the cargo could not take read the same in the offer string and apart here.
    /// Pot contents are not in it, because a pot is a site the shared executor searches, not a drop.</summary>
    public CandidateFunnel Funnel { get; } = new(6,
        StageAllowance, StageCapacity, StageLanding, StageContactPose, StageApproachUndecided, StageUnreachable, CandidateFunnel.Offered);
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
        Funnel.Begin();
        if (ctx.Senses.Player.IsDead || ctx.Senses.Loot.Pickups.Count == 0)
            return;
        // Nearest first, and each drop proven for itself: the cargo takes some of it, a pose in pickup contact exists, the
        // companion can walk there, and walking there does not cut it off from a player it can reach now. One full,
        // unreachable or stranding drop must not block the next. A standable tile beside a drop proves none of this, and
        // mining's reach to the ore the drop fell from says nothing about the pit it fell into.
        foreach (var pickup in ctx.Senses.Loot.Pickups)
        {
            Item item = pickup.Item;
            if (!LootSense.IsWorldDrop(item)) continue;
            // Hearts, stars and the other IsAPickup heals are consumed by the player, never stored.
            // Contact pickup skips them; walking there anyway is the stand-on-a-heart freeze.
            if (ItemID.Sets.IsAPickup[item.type]) continue;
            string identity = FormattableString.Invariant($"item{item.whoAmI}:{item.type}");
            Point at = item.Center.ToTileCoordinates();
            float cost = pickup.DistanceToCompanion;
            if (!AllowsTarget(ctx, item.Bottom, item))
            {
                Funnel.Add(identity, at, cost, "", StageAllowance, "");
                continue;
            }
            int acceptable = ctx.Companion.Bag.AcceptableQuantity(item, ctx.Player);
            if (acceptable <= 0)
            {
                capacityRefused = true;
                Funnel.Add(identity, at, cost, StageAllowance, StageCapacity, FormattableString.Invariant($"stack={item.stack};fits=0"));
                continue;
            }
            string readings = FormattableString.Invariant($"stack={item.stack};fits={acceptable}");
            // Where the drop will be, not where it is. A drop released in the air had its contact pose
            // searched around its instantaneous bottom with a tolerance of one tile, so a falling item
            // had no pose at all until it landed — 431 rows of the 2026-09-14 capture were refused as
            // drop-has-no-contact-pose — and the moved-drop guard then held the body on every tick of a
            // fall, because a falling drop has always moved more than a tile since it was proven. Both
            // are the same mistake: an item in flight was priced as an object at rest.
            if (ForecastLanding(item) is not Vector2 landing)
            {
                Refuse(OfferEligibility.Unresolved, "drop-landing-undecided");
                Funnel.Add(identity, at, cost, StageCapacity, StageLanding, readings);
                continue;
            }
            Vector2 pose;
            if (TouchesDrop(ctx.Npc.Hitbox, item, ProvePickupReach))
                pose = ctx.Npc.Bottom;
            else
            {
                if (ContactPose(item, landing) is not Point contact)
                {
                    Refuse(OfferEligibility.KnownUnusable, "drop-has-no-contact-pose");
                    Funnel.Add(identity, at, cost, StageLanding, StageContactPose,
                        readings + FormattableString.Invariant($";landing={landing.X:0},{landing.Y:0}"));
                    continue;
                }
                // Every drop is asked, in distance order, because the question costs a hash lookup rather than a
                // search. The walk used to stop at a bound instead of skipping ahead, so that a farther drop was
                // never offered before a nearer one had been asked about; with nothing to ration, every drop is
                // asked about and the ordering property holds for free.
                Reachability.Reach reach = Excursion(ctx, contact);
                readings += FormattableString.Invariant($";pose={contact.X},{contact.Y}");
                if (reach == Reachability.Reach.Unknown)
                {
                    Refuse(OfferEligibility.Unresolved, "drop-approach-undecided");
                    Funnel.Add(identity, at, cost, StageContactPose, StageApproachUndecided, readings);
                    continue;
                }
                // One name, because the two-way region proves one thing: a drop outside it is either one the body
                // cannot get to or one it could not come home from, and the flood does not distinguish them. The
                // separate `drop-would-strand-return` was the marginal proof's name and went with that proof.
                if (reach == Reachability.Reach.No)
                {
                    Refuse(OfferEligibility.KnownUnusable, "drop-unreachable");
                    Funnel.Add(identity, at, cost, StageContactPose, StageUnreachable, readings);
                    continue;
                }
                pose = MovementQueries.HoverPoint(contact);
            }
            float near = Consideration.Inverse(pickup.DistanceToCompanion, Weights.LootReach);
            // The loot sense values the whole stack; the offer is for what the cargo can take, so its value is scaled by the
            // share of the whole stack's value that fits.
            float whole = LootSense.ValueOf(item, item.stack);
            float fits = whole > 0 ? LootSense.ValueOf(item, acceptable) / whole : 1f;
            // The same unit mining and chopping price their walk in: pixels to the working pose over walking speed.
            float trip = Vector2.Distance(ctx.Npc.Center, pose) / OrbPace.MaxSpeed;
            // The candidate records the landing the pose was proven at, not the drop's live bottom, so
            // the guard in Execute asks whether the *forecast* moved. A drop falling exactly as
            // predicted keeps one landing all the way down and is walked to without a single hold.
            candidate = new(item, item.type, landing, pose, Consideration.AtLeast(near, 0.2f), pickup.Value * fits, trip);
            Funnel.Add(identity, at, cost, "approach", "",
                readings + FormattableString.Invariant($";trip={trip:0.0};value={candidate.Value.Near * candidate.Value.Value:0.000}"));
            return;
        }
    }

    /// <summary>The engine's box for a body centred at <paramref name="centre"/>, which is what item pickup measures against.</summary>
    private static Rectangle BodyAt(Vector2 centre)
        => new((int)(centre.X - CircleContact.Radius), (int)(centre.Y - CircleContact.Radius), (int)CircleContact.Diameter, (int)CircleContact.Diameter);

    private static bool TouchesDrop(Rectangle body, Item item, float reach) => Touches(body, item.Hitbox, reach);

    private static bool Touches(Rectangle body, Rectangle drop, float reach)
    {
        body.Inflate((int)reach, (int)reach);
        return body.Intersects(drop);
    }

    /// <summary>
    /// Where a drop will come to rest, or none if it is still falling past the horizon. The arc is the
    /// game's own, from <c>Item.UpdateItem</c> and <c>MoveInWorld</c>: gravity added per tick and capped
    /// at a maximum fall speed, horizontal velocity damped and zeroed below a floor, with the wet
    /// figures when the item is in liquid. Stepping it is what makes a falling drop answerable — an item
    /// in the air is not a place, and a collection that prices one where it currently hangs either finds
    /// no pose at all or walks to a pose the drop has already left.
    ///
    /// <para>The floor is asked of the tile world rather than of <c>tileSolid</c>, because a slope or a
    /// half block is not the surface its solidity suggests; the forecast lands the item on the top of
    /// the first supporting row its track enters, which is where the game's own collision leaves it on
    /// flat ground and an approximation of a tile on a slope.</para>
    ///
    /// <para>It never runs a route search, and it must not: this is an activity, and the navigation
    /// boundary refuses one here. It reads tiles and the item's own numbers and nothing else.</para>
    /// </summary>
    private static Vector2? ForecastLanding(Item item)
    {
        Vector2 bottom = item.Bottom;
        // Already at rest on something: the landing is where it is.
        // The same row arithmetic the loop below uses, so a resting item and a landing item agree about
        // which row counts as the floor: the bottom sits on the top edge of the supporting row.
        if (item.velocity.Y == 0f && MovementQueries.IsSupport((int)MathF.Floor(bottom.X / 16f), (int)MathF.Floor(bottom.Y / 16f)))
            return bottom;
        // The liquid ladder is the game's own, in its own order: shimmer, then honey, then water, each supplying a
        // gravity, a fall cap and the share of its velocity a submerged item actually moves by. The share is a
        // separate quantity from the gravity and it is the one that was missing — a liquid slows an item twice
        // over, and modelling only the gravity leaves the forecast covering twice the distance in water and four
        // times in honey. Whether the position step uses the share at all is gated on `wet` alone, exactly as the
        // game gates it, rather than on whichever liquid chose the gravity.
        float gravity = Weights.DropGravity, maxFall = Weights.DropMaxFallSpeed;
        float share = Weights.DropWaterVelocityShare;
        if (item.shimmerWet)
        {
            gravity = Weights.DropShimmerGravity; maxFall = Weights.DropShimmerMaxFallSpeed;
            share = Weights.DropShimmerVelocityShare;
        }
        else if (item.honeyWet)
        {
            gravity = Weights.DropHoneyGravity; maxFall = Weights.DropHoneyMaxFallSpeed;
            share = Weights.DropHoneyVelocityShare;
        }
        else if (item.wet)
        {
            gravity = Weights.DropWetGravity; maxFall = Weights.DropWetMaxFallSpeed;
        }
        Vector2 velocity = item.velocity;
        for (int tick = 0; tick < Weights.DropForecastTicks; tick++)
        {
            // The wet step is taken from the velocity as it stood at the top of the tick, before this tick's
            // gravity, because the game captures it there and applies gravity afterwards. That one-tick lag is
            // small per tick and compounds over a fall, so a forecast that steps by the updated velocity lands
            // a submerged drop short of where the game puts it even with the share applied.
            Vector2 step = item.wet ? velocity * share : Vector2.Zero;
            velocity.Y = MathF.Min(velocity.Y + gravity, maxFall);
            velocity.X *= Weights.DropHorizontalDamping;
            if (MathF.Abs(velocity.X) < Weights.DropHorizontalFloor) velocity.X = 0f;
            if (!item.wet) step = velocity;
            Vector2 next = bottom + step;
            if (step.Y > 0f)
            {
                // Every row the bottom crosses this tick, so a fast item cannot pass through a floor
                // between two samples — at the capped fall speed it covers most of a tile a tick.
                int column = (int)MathF.Floor(next.X / 16f);
                for (int row = (int)MathF.Floor(bottom.Y / 16f); row <= (int)MathF.Floor(next.Y / 16f); row++)
                    if (MovementQueries.IsSupport(column, row))
                        return new Vector2(next.X, row * 16f);
            }
            bottom = next;
        }
        return null;
    }

    /// <summary>The hoverable cell nearest the drop from which a body that has arrived still picks it up, or none.
    /// Nearest-to-companion among cells that merely graze was the cell with the least overlap: arrival slack then
    /// missed, and the navigator reported Arrived so the body hung beside the gel without taking it.</summary>
    private static Point? ContactPose(Item item, Vector2 landing)
    {
        // Every geometric question below is asked about the landing rather than the live bottom: an
        // item still in the air is going to be somewhere else by the time a body flies to it, and a
        // cell proven against where it currently hangs is proven against a place it will not be.
        Point around = MovementQueries.Tile(landing);
        Rectangle drop = new((int)(landing.X - item.width / 2f), (int)(landing.Y - item.height), item.width, item.height);
        Point? best = null;
        float bestDistance = float.MaxValue;
        for (int dx = -ContactSearchTiles; dx <= ContactSearchTiles; dx++)
            for (int dy = -ContactSearchTiles; dy <= ContactSearchTiles; dy++)
            {
                Point tile = new(around.X + dx, around.Y + dy);
                if (!MovementQueries.IsHoverable(tile)) continue;
                Vector2 hover = MovementQueries.HoverPoint(tile);
                if (!Touches(BodyAt(hover), drop, ProvePickupReach)) continue;
                float distance = Vector2.DistanceSquared(hover, landing);
                if (distance >= bestDistance) continue;
                best = tile;
                bestDistance = distance;
            }
        return best;
    }

    /// <summary>
    /// Whether the body can walk to this contact pose and come home from it, read from the reach sense. It
    /// replaces a pair of fresh walker searches per drop, and with them everything those searches needed:
    /// a per-drop verdict cache keyed on pose, terrain revision and a recheck interval, and a bound on how
    /// many new questions one preparation could ask. A cache exists to make an expensive answer reusable and
    /// a bound exists to ration an expensive answer; membership of a flood already run is neither.
    ///
    /// <para>What the two-way region does not carry is the marginal return the old proof had — the drop was
    /// refused only where the companion could reach the player from where it stood and could not from the
    /// drop, so a companion already cut off from the player was not called stranded by going one step
    /// further. The region asks the absolute question instead: can the body come back at all. A companion in
    /// a sealed pocket therefore refuses drops inside that pocket, which the marginal rule allowed. That is
    /// the ruling rather than an oversight, and the sealed-pocket case is the one it costs.</para>
    /// </summary>
    private static Reachability.Reach Excursion(in ActionContext ctx, Point pose)
        => ctx.Senses.Reach.Reachable(pose) switch
        {
            ReachVerdict.Reachable => Reachability.Reach.Yes,
            ReachVerdict.NotYet => Reachability.Reach.Unknown,
            _ => Reachability.Reach.No,
        };

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
        // Where the walked-to drop last lay while it was still a world drop, so a drop that vanishes can be checked against the
        // world drops that could have absorbed it.
        if (dropAttempt is { } walking && ReferenceEquals(walking.Item, prepared.Item))
            dropAttempt = walking with { LastCentre = prepared.Item.Center };
        // The guard is on the forecast, not on the live position. A falling drop has always moved more
        // than a tile since it was proven, so comparing live positions held the body on every tick of
        // every fall; what actually invalidates the walk is the *landing* moving — the item bouncing
        // off a slope, being knocked sideways, or landing somewhere the forecast did not expect. A
        // landing that can no longer be forecast at all is a hold for the same reason.
        if (ForecastLanding(prepared.Item) is not Vector2 landing
            || Vector2.DistanceSquared(landing, prepared.Position) > MovedDropPixels * MovedDropPixels)
            return PositionRequest.Hold;
        if (dropAttempt is not { } open || !ReferenceEquals(open.Item, prepared.Item))
            dropAttempt = new(prepared.Item, prepared.Type, prepared.Item.stack, ctx.Companion.Bag, ctx.Companion.Bag.TransferSequence, prepared.Item.Center);
        return PositionRequest.ExactAt(prepared.Pose);
    }

    /// <summary>The drop this attempt walked toward, its stack when the walk began, the cargo's transfer mark then, and the
    /// drop's centre when it was last seen in the world.</summary>
    private readonly record struct DropAttempt(Item Item, int Type, int StartStack, Inventory.CompanionInventory Bag, long TransferMark, Vector2 LastCentre);
    private DropAttempt? dropAttempt;

    /// <summary>Terraria's <c>Item.CombineWithNearbyItems</c> merges two drops of one type whose centres are closer than this
    /// many pixels, summing horizontal and vertical distance: the lower world slot takes the stack, moves to the midpoint and
    /// the other is cleared and deactivated. The native threshold doubles while many items have spawned recently; this does
    /// not follow that, so a merge during a flood of drops still reads as the drop leaving the world.</summary>
    private const float NativeMergePixels = 30f;

    /// <summary>Whether a vanished walked-to drop was absorbed by another world drop of its type: one lies within the native
    /// merge distance of where it was last seen. The absorber moves to the midpoint of the two, so it is nearer than that.</summary>
    private static bool AbsorbedIntoWorldDrop(in DropAttempt drop)
    {
        foreach (Item other in Main.ActiveItems)
        {
            if (ReferenceEquals(other, drop.Item) || other.type != drop.Type || !LootSense.IsWorldDrop(other)) continue;
            if (MathF.Abs(other.Center.X - drop.LastCentre.X) + MathF.Abs(other.Center.Y - drop.LastCentre.Y) < NativeMergePixels)
                return true;
        }
        return false;
    }

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
    /// the purpose went away, which is invalid. Part received and the rest still lying there: partial. A drop that vanished
    /// because another world drop of its type absorbed it has not left the world: nothing received is an attempt replaced
    /// with the items still lying there, part received is partial. Pot attempts use the shared interaction conclusion.
    /// </summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
    {
        if (dropAttempt is { } drop)
        {
            int received = drop.Bag.TransferredSince(drop.TransferMark, drop.Item);
            bool gone = !LootSense.IsWorldDrop(drop.Item) || drop.Item.type != drop.Type;
            // What the ledger received from this drop is the claimed yield, so a report can hold the completion against
            // the pickups recorded under this attempt rather than taking the status on trust; a drop absorbed into another
            // world drop claims what it delivered before the merge like any other partial.
            if (gone && AbsorbedIntoWorldDrop(drop))
                return received > 0
                    ? new(AttemptStatus.Partial, "drop-partly-transferred", AttemptAttribution.NotApplicable, drop.Type, received)
                    : new(AttemptStatus.Attempted, "drop-merged-into-world-drop");
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
        if (broken) Infrastructure.Diagnostics.GodsEyeEvents.RecordWorldInteraction(ctx.Npc, tile, "break-pot", PerformNote + "native pot drops; collected yield unobserved");
        return broken;
    }
}
