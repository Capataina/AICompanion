#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using AICompanion.Companion.Brain.WorldInteractions.Chopping;

namespace AICompanion.Companion.Brain.Behaviours.Work;

/// <summary>
/// Retains one tree job. Mimic mode keeps the player-hit trigger and excludes that tree;
/// opportunistic mode uses the existing nearby-tree finder without inventing a second
/// chopping mechanism.
/// </summary>
public sealed class ChopAction : CompanionAction
{
    public override string Name => "chop";
    public override PurposeFamily Family => PurposeFamily.Gathering;
    public override Vector2? ActivityTarget => prepared?.Target;
    public override object? ActivityIdentity => tree?.Bottom;
    /// <summary>True only while the axe is actually out; the whole walk to the tree is empty-handed.</summary>
    public override bool HandsBusy => swinging;
    private bool swinging;
    public override void Exit(in ActionContext ctx) => swinging = false; // Retain the tree, release its tool phase.
    private readonly System.Collections.Generic.Dictionary<Point, ulong> deferred = new();

    private const int KeepJobTicks = 120;
    private const int SearchRadiusTiles = 40;
    private const int SearchEveryTicks = 60;

    private TreeFinder.ChoppableTree? tree;
    private Point? lastSearchedFor;
    private int sincePlayerHit;
    private int sinceSearch = SearchEveryTicks;
    private (Point from, Point goal, int revision)? reachKey;
    private Reachability.Reach approachReach;
    private int sinceReach = SearchEveryTicks;
    private readonly record struct Candidate(Vector2 Target, float Value, float TripTicks);
    private Candidate? prepared;
    public WorldInteractions.RemainingToolWork? RemainingWork { get; private set; }

    public override void Prepare(in ActionContext ctx)
    {
        float value = DiscoverValue(ctx);
        RemainingWork = value > 0 && tree is { } workTarget
            ? ctx.Companion.Chopper.EstimateRemaining(workTarget.Bottom, TileChopper.AxeFor(ctx.Player)) : null;
        prepared = value > 0 && RemainingWork is { } remaining && tree is { } found
            ? new(found.Bottom.ToWorldCoordinates(), value,
                Vector2.Distance(ctx.Npc.Bottom, found.StandPosition) / Companion.CompanionMotor.WalkSpeed + remaining.Ticks)
            : null;
    }

    public override float Score() => prepared?.Value ?? 0f;

    private float DiscoverValue(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        sinceSearch++;
        sinceReach++;
        if (tree is { } retained && (!AllowsTarget(ctx, retained.Bottom.ToWorldCoordinates())
            || WorldInteractions.WorldProtection.ProtectCompanionHomes.IsProtected(retained.Bottom)))
        { tree = null; ReleaseActivity(); sinceSearch = SearchEveryTicks; }
        var context = ctx;
        bool Accept(TreeFinder.ChoppableTree t) => AllowsTarget(context, t.Bottom.ToWorldCoordinates(), t.Bottom)
            && !WorldInteractions.WorldProtection.ProtectCompanionHomes.IsProtected(t.Bottom)
            && (!deferred.TryGetValue(t.Bottom, out ulong until) || Main.GameUpdateCount >= until);

        if (WorkPolicies.Chopping == WorkPolicy.Disabled)
        {
            tree = null;
            lastSearchedFor = null;
            ReleaseActivity();
            return 0f;
        }
        if (WorkPolicies.Chopping == WorkPolicy.Mimic)
        {
            if (p.IsChoppingTree)
            {
                sincePlayerHit = 0;
                if (tree is TreeFinder.ChoppableTree t && (!TileChopper.TreeStands(t.Bottom) || t.Bottom == p.ChoppedTree))
                    tree = null;
                bool newTree = lastSearchedFor != p.ChoppedTree;
                if (tree == null && (newTree || sinceSearch >= SearchEveryTicks))
                {
                    tree = TreeFinder.FindNearest(ctx.Npc.Center, SearchRadiusTiles, p.ChoppedTree, Accept);
                    lastSearchedFor = p.ChoppedTree;
                    sinceSearch = 0;
                }
            }
            else
            {
                // Between two swings the hit flag is down; retain this mimic job long enough
                // for a slow player swing, then release it rather than becoming a mission.
                sincePlayerHit++;
                if (sincePlayerHit > KeepJobTicks)
                {
                    tree = null;
                    lastSearchedFor = null;
                }
                else if (tree is TreeFinder.ChoppableTree t && !TileChopper.TreeStands(t.Bottom))
                    tree = null;
            }
        }
        else
        {
            if (tree is TreeFinder.ChoppableTree t && !TileChopper.TreeStands(t.Bottom))
                tree = null;
            // A changed player worksite can overlap our retained trunk before the next
            // periodic discovery. Prefer a separate job, but Auto may share the only tree.
            if (lastSearchedFor != p.ChoppedTree)
            {
                if (tree?.Bottom == p.ChoppedTree) tree = null;
                lastSearchedFor = p.ChoppedTree;
                sinceSearch = SearchEveryTicks;
            }
            if (tree == null && sinceSearch >= SearchEveryTicks)
            {
                TreeFinder.ChoppableTree? Find(Point? exclude)
                    => Nearest(context.Npc.Center,
                        TreeFinder.FindNearest(context.Npc.Center, SearchRadiusTiles, exclude, Accept),
                        TreeFinder.FindNearest(context.Player.Center, SearchRadiusTiles, exclude, Accept));
                tree = Find(p.ChoppedTree);
                if (tree == null && p.ChoppedTree != null) tree = Find(null);
                sinceSearch = 0;
            }
        }

        if (tree == null) { ReleaseActivity(); return 0f; }
        // A clear standing tile beside a trunk is only a geometric candidate. A sealed or
        // unfinished approach must yield to other jobs rather than winning forever.
        var key = (MovementQueries.FeetTile(ctx.Npc.Bottom),
            MovementQueries.FeetTile(tree.Value.StandPosition), TerrainChanges.Revision);
        if (reachKey != key || sinceReach >= SearchEveryTicks)
        {
            approachReach = MovementQueries.WalkerReach(key.Item1, key.Item2);
            reachKey = key;
            sinceReach = 0;
        }
        if (approachReach != Reachability.Reach.Yes)
        {
            if (deferred.Count > 64) deferred.Clear();
            deferred[tree.Value.Bottom] = Main.GameUpdateCount + 300;
            tree = null;
            ReleaseActivity();
            return 0f;
        }
        float safe = Consideration.AtLeast(1f - ctx.Senses.Threats.PlayerDanger, 0.1f);
        return 0.7f * safe;
    }

    public override float ForecastTicks()
        => prepared?.TripTicks ?? 0f;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        Item axe = TileChopper.AxeFor(ctx.Player);
        // The axe comes out only in position; on the walk there the hand stays empty, so the
        // torch can hold it in the dark.
        ctx.Companion.HoldItem(ItemID.None);
        swinging = false;
        if (WorkPolicies.Chopping == WorkPolicy.Disabled)
        {
            tree = null;
            lastSearchedFor = null;
            sinceSearch = SearchEveryTicks;
            ReleaseActivity();
            return PositionRequest.Hold;
        }
        if (prepared == null || tree is not TreeFinder.ChoppableTree t)
            return PositionRequest.Hold;

        if (Vector2.Distance(ctx.Npc.Bottom, t.StandPosition) <= 20f)
        {
            ctx.Companion.HoldItem(axe.type);
            swinging = true;
            ctx.Companion.Motor.Face(t.Bottom.X * 16f + 8f);
            if (ctx.Companion.Chopper.Swing(t.Bottom, axe))
            {
                ctx.Companion.StartAnimation(axe.type, axe.useAnimation);
                if (ctx.Companion.Chopper.LastOutcome is { } outcome)
                {
                    BehaviourDiagnostics.GodsEyeEvents.RecordToolEffect(ctx.Npc, "axe", outcome, ctx.Companion.Brain.Chooser.EvaluationId, ctx.Companion.Brain.Chooser.Activity.Id);
                    if (outcome.Productive) ctx.Companion.Brain.Chooser.RecordWork(t.Bottom.ToWorldCoordinates());
                }
            }
            return PositionRequest.Hold;
        }
        return PositionRequest.ExactAt(t.StandPosition);
    }

    private static TreeFinder.ChoppableTree? Nearest(Vector2 from, TreeFinder.ChoppableTree? a, TreeFinder.ChoppableTree? b)
        => a == null ? b : b == null ? a
            : Vector2.DistanceSquared(from, a.Value.StandPosition) <= Vector2.DistanceSquared(from, b.Value.StandPosition) ? a : b;
}
