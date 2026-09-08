#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Brain.DecisionMatrix.Decision;
using AICompanion.Brain.Work.Chopping;

namespace AICompanion.Brain.Actions.Work;

/// <summary>
/// The player is really chopping a tree: go to the nearest other tree and chop it.
/// The target survives a pause in the player's swinging for a couple of seconds so a
/// slow swing does not drop the job every frame. The search for a new target scans a
/// box of tiles and solves a standing spot per hit, and Score runs every tick whether
/// or not this action wins, so the search runs only when the player starts on a new tree
/// or the target is gone, and never more often than a short cooldown; with one tree in
/// view and no other, that is what stops the scan running sixty times a second.
/// </summary>
public sealed class ChopAction : CompanionAction
{
    public override string Name => "chop";

    private const int KeepJobTicks = 120;
    private const int SearchRadiusTiles = 40;
    private const int SearchEveryTicks = 60;

    private TreeFinder.ChoppableTree? tree;
    private Point? lastSearchedFor;
    private int sincePlayerHit;
    private int sinceSearch = SearchEveryTicks;

    public override float Score(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        sinceSearch++;

        if (p.IsChoppingTree)
        {
            sincePlayerHit = 0;
            if (tree is TreeFinder.ChoppableTree t && (!TileChopper.TreeStands(t.Bottom) || t.Bottom == p.ChoppedTree))
                tree = null;
            bool newTree = lastSearchedFor != p.ChoppedTree;
            if (tree == null && (newTree || sinceSearch >= SearchEveryTicks))
            {
                tree = TreeFinder.FindNearest(ctx.Npc.Center, SearchRadiusTiles, p.ChoppedTree);
                lastSearchedFor = p.ChoppedTree;
                sinceSearch = 0;
            }
        }
        else
        {
            // Between two of the player's swings the hit flag is down; the job and the memory
            // of what was searched for both outlive that gap, or the cooldown would reset
            // on every swing.
            sincePlayerHit++;
            if (sincePlayerHit > KeepJobTicks)
            {
                tree = null;
                lastSearchedFor = null;
            }
            else if (tree is TreeFinder.ChoppableTree t && !TileChopper.TreeStands(t.Bottom))
                tree = null;
        }

        if (tree == null)
            return 0f;
        float safe = Consideration.AtLeast(1f - ctx.Senses.Threats.PlayerDanger, 0.1f);
        return 0.7f * safe;
    }

    public override float ForecastTicks(in ActionContext ctx)
        => tree == null ? 0f : Vector2.Distance(ctx.Npc.Bottom, tree.Value.StandPosition) / Companion.CompanionMotor.WalkSpeed + 120f;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        Item axe = TileChopper.AxeFor(ctx.Player);
        // The axe comes out only in position; on the walk there the hand stays empty, so the
        // torch can hold it in the dark.
        ctx.Companion.HoldItem(ItemID.None);
        if (tree is not TreeFinder.ChoppableTree t)
            return PositionRequest.Hold;

        if (System.MathF.Abs(ctx.Npc.Center.X - t.StandPosition.X) <= 20f)
        {
            ctx.Companion.HoldItem(axe.type);
            ctx.Companion.Motor.Face(t.Bottom.X * 16f + 8f);
            if (ctx.Companion.Chopper.Swing(t.Bottom, axe))
                ctx.Companion.StartAnimation(axe.type, axe.useAnimation);
            return PositionRequest.Hold;
        }
        return PositionRequest.ExactAt(t.StandPosition);
    }
}
