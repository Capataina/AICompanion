#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using AICompanion.Companion.Brain.WorldInteractions.Mining;
using AICompanion.Companion.Brain.WorldInteractions.Torch;
using AICompanion.Companion.Brain.WorldInteractions.WorldProtection;

namespace AICompanion.Companion.Brain.Behaviours.Work;

/// <summary>One bounded interaction search shared by pot breaking and permanent lighting.</summary>
public abstract class PerformNearbyWorldWork : CompanionAction
{
    protected Point? target;
    private Vector2 stand;
    private ulong nextSearch, retryAfter;
    private bool needsJump, jumped;
    private ulong jumpStarted;
    // Tiles whose approach was tried and did not arrive, with the tick they may be offered again.
    private readonly System.Collections.Generic.Dictionary<Point, ulong> deferred = new();
    private Vector2 approachOrigin;
    private int approachTicks;
    public override Vector2? ActivityTarget => target?.ToWorldCoordinates();
    public override object? ActivityIdentity => target;
    protected abstract bool Enabled(in ActionContext ctx);
    protected abstract bool Candidate(in ActionContext ctx, Point tile);
    protected abstract bool Perform(in ActionContext ctx, Point tile);
    protected abstract float Utility { get; }
    protected virtual bool AllowJump => false;
    /// <summary>How long a tile whose approach never arrived stays out of the candidate set. Long
    /// enough that the companion leaves the area and does something else, short enough that a tile
    /// made reachable by the player digging through becomes available again in the same visit.</summary>
    private const int DeferFailedApproachTicks = 1800;
    protected virtual float CandidateCost(Vector2 feet, Point tile) => Vector2.DistanceSquared(feet, tile.ToWorldCoordinates());

    public override float Score(in ActionContext ctx)
    {
        if (!Enabled(ctx) || ctx.Player.dead) { target = null; ReleaseActivity(); return 0f; }
        if (target is Point old && (!Candidate(ctx, old) || !AllowsTarget(ctx, old.ToWorldCoordinates())))
        { target = null; ReleaseActivity(); }
        if (target == null && Main.GameUpdateCount >= nextSearch)
        {
            nextSearch = Main.GameUpdateCount + 90;
            float best = float.MaxValue;
            Point centre = ctx.Npc.Center.ToTileCoordinates();
            for (int x = centre.X - 18; x <= centre.X + 18; x++)
                for (int y = centre.Y - 14; y <= centre.Y + 14; y++)
                {
                    Point p = new(x, y);
                    float distance = CandidateCost(ctx.Npc.Bottom, p);
                    if (distance >= best) continue;
                    if (deferred.TryGetValue(p, out ulong until) && Main.GameUpdateCount < until) continue;
                    if (!AllowsTarget(ctx, p.ToWorldCoordinates(), p) || !Candidate(ctx, p)) continue;
                    Vector2 candidateStand;
                    bool jump = false;
                    if (OreFinder.InReach(ctx.Npc.Bottom, p)) candidateStand = ctx.Npc.Bottom;
                    else if (AllowJump && ProveInteractionJump.CanReach(NavGrid.World, ctx.Companion.Motor.State, body => OreFinder.InReach(body.Feet, p)))
                    { candidateStand = ctx.Npc.Bottom; jump = true; }
                    else if (OreFinder.Approach(p, ctx.Npc.Bottom, out candidateStand) != Reachability.Reach.Yes) continue;
                    target = p; best = distance; stand = candidateStand; needsJump = jump; jumped = false;
                    approachOrigin = ctx.Npc.Bottom; approachTicks = 0;
                }
        }
        if (deferred.Count > 0)
        {
            ulong now = Main.GameUpdateCount;
            foreach (Point expired in new System.Collections.Generic.List<Point>(deferred.Keys))
                if (now >= deferred[expired]) deferred.Remove(expired);
        }
        return target == null ? 0f : Utility * Math.Max(.05f, 1f - ctx.Senses.Threats.PlayerDanger);
    }
    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(ItemID.None);
        if (target is not Point tile) return PositionRequest.Hold;
        if (!OreFinder.InReach(ctx.Npc.Bottom, tile))
        {
            if (!needsJump)
            {
                // The approach can fail, and until this existed nothing said so. Score() only ever
                // dropped a target that vanished or left the activity envelope, so a cached stand
                // the body could not walk to was held for ever: on 2026-09-11 that was ticks 18,501
                // to 21,531 on one pot, 3,031 unbroken ticks with the movement system reporting
                // itself stalled on 1,826 of them, which was 97% of every stalled tick in the run.
                // An intent that cannot fail is an intent that cannot be given up, so covering no
                // ground for a full progress window defers this tile and hands the tick back.
                if (Vector2.DistanceSquared(approachOrigin, ctx.Npc.Bottom)
                    >= BehaviourSelection.Weights.ObjectiveProgressPixels * BehaviourSelection.Weights.ObjectiveProgressPixels)
                { approachOrigin = ctx.Npc.Bottom; approachTicks = 0; }
                else if (++approachTicks >= BehaviourSelection.Weights.ObjectiveProgressWindowTicks)
                {
                    deferred[tile] = Main.GameUpdateCount + DeferFailedApproachTicks;
                    BehaviourDiagnostics.GodsEyeEvents.RecordWorldInteraction(ctx.Npc, tile, "approach-abandoned",
                        $"no ground covered in {BehaviourSelection.Weights.ObjectiveProgressWindowTicks} ticks");
                    target = null; ReleaseActivity(); nextSearch = Main.GameUpdateCount + 60;
                    return PositionRequest.Hold;
                }
                return PositionRequest.ExactAt(stand);
            }
            if (!jumped)
            {
                // Validate against the live pose again: another behaviour may have moved us
                // after candidate discovery. The shared movement controller owns the impulse.
                if (!ProveInteractionJump.CanReach(NavGrid.World, ctx.Companion.Motor.State, body => OreFinder.InReach(body.Feet, tile)))
                { target = null; ReleaseActivity(); return PositionRequest.Hold; }
                jumped = true; jumpStarted = Main.GameUpdateCount;
                return PositionRequest.Hold with { JumpScale = 1f };
            }
            if (Main.GameUpdateCount - jumpStarted > 90) { target = null; ReleaseActivity(); }
            return PositionRequest.Hold;
        }
        if (Main.GameUpdateCount >= retryAfter)
        {
            retryAfter = Main.GameUpdateCount + 30;
            if (Perform(ctx, tile)) ctx.Companion.Brain.Chooser.RecordWork(tile.ToWorldCoordinates());
            target = null; ReleaseActivity(); nextSearch = Main.GameUpdateCount + 60;
        }
        return PositionRequest.Hold;
    }

    public override void Exit(in ActionContext ctx)
    {
        // A reflex or protective action can change a take-off pose; never resume its old jump.
        if (needsJump) { target = null; ReleaseActivity(); }
    }
}

public sealed class BreakNearbyPots : PerformNearbyWorldWork
{
    public override string Name => "break-pots";
    protected override float Utility => .62f;
    protected override bool Enabled(in ActionContext ctx) => PlayerIntegration.CompanionPreferences.Current.PotBreaking;
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
        if (!Candidate(ctx, tile) || !WorldGen.CanKillTile(tile.X, tile.Y)) return false;
        WorldGen.KillTile(tile.X, tile.Y);
        bool broken = !Main.tile[tile.X, tile.Y].HasTile;
        if (broken) BehaviourDiagnostics.GodsEyeEvents.RecordWorldInteraction(ctx.Npc, tile, "break-pot", "native pot drops");
        return broken;
    }
}

public sealed class PlaceNearbyTorches : PerformNearbyWorldWork
{
    public override string Name => "place-torches";
    protected override float Utility => .60f;
    protected override bool AllowJump => true;
    // Elevated lights clear floor clutter and reach across nearby ledges. Candidate validation
    // still requires native attachment, useful spacing, interaction reach and a safe landing.
    protected override float CandidateCost(Vector2 feet, Point tile)
    {
        Vector2 above = feet - new Vector2(0, Player.tileRangeY * 16f + 80f);
        return Math.Min(Vector2.DistanceSquared(above + new Vector2(64, 0), tile.ToWorldCoordinates()),
            Vector2.DistanceSquared(above - new Vector2(64, 0), tile.ToWorldCoordinates()));
    }
    protected override bool Enabled(in ActionContext ctx) => PlayerIntegration.CompanionPreferences.Current.TorchPlacement
        && (ctx.Npc.Center.Y / 16f > Main.worldSurface || !Main.dayTime)
        && PlaceSuppliedTorches.Supply(ctx.Companion.Bag.Items, ctx.Player.inventory) != null;
    protected override bool Candidate(in ActionContext ctx, Point tile)
    {
        if (!PlaceSuppliedTorches.Candidate(tile)) return false;
        Item? torch = PlaceSuppliedTorches.Supply(ctx.Companion.Bag.Items, ctx.Player.inventory);
        return torch != null && RecommendTorchPlacement.Accepts(tile, torch, ctx.Companion.Body.Player);
    }
    protected override bool Perform(in ActionContext ctx, Point tile)
    {
        bool placed = PlaceSuppliedTorches.Place(tile, ctx.Companion.Bag.Items, ctx.Player, out string source);
        BehaviourDiagnostics.GodsEyeEvents.RecordWorldInteraction(ctx.Npc, tile, placed ? "place-torch" : "placement-refused", source);
        return placed;
    }
}
