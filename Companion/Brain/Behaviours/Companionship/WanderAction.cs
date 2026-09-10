#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;

namespace AICompanion.Companion.Brain.Behaviours.Companionship;

/// <summary>
/// Nothing better to do: stand a while, stroll a while, occasionally hop, inside the
/// calm band. Its score is a floor, so anything real outscores it. Stranded, it is the
/// action that walks the pocket: the score rises under every combat action's ceiling and
/// the request is a roam, which the positioner answers from the region the body can reach.
/// </summary>
public sealed class WanderAction : CompanionAction
{
    public override string Name => "wander";

    private enum Mode { Standing, Walking }
    private Mode mode = Mode.Standing;
    private int ticksLeft = 60;
    private Vector2 goal;
    private bool hop;

    public override void Enter(in ActionContext ctx)
    {
        // A wander target belongs to the player's current neighbourhood. Interruption may
        // have carried both actors across the world; resuming cannot revive that old target.
        mode = Mode.Standing;
        ticksLeft = 1;
        hop = false;
    }

    public override float Score(in ActionContext ctx)
        => ctx.Senses.Player.IsDead ? 0f : ctx.Stranded ? Weights.StrandedWander : Weights.WanderFloor;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(ItemID.None);
        if (ctx.Senses.Player.IsDead)
            return PositionRequest.Hold;
        if (ctx.Stranded)
            return new PositionRequest(RequestKind.Roam, ctx.Npc.Bottom);
        if (mode == Mode.Walking && Vector2.DistanceSquared(goal, ctx.Senses.Player.Bottom) > Weights.CalmBandFar * Weights.CalmBandFar)
        {
            mode = Mode.Standing;
            ticksLeft = 1;
        }
        if (--ticksLeft <= 0)
            PickNext(ctx);

        float jump = hop ? 0.7f : 0f;
        hop = false;
        return (mode == Mode.Standing ? PositionRequest.Hold : PositionRequest.ExactAt(goal)) with { JumpScale = jump };
    }

    private void PickNext(in ActionContext ctx)
    {
        if (mode == Mode.Walking || Main.rand.NextBool(3))
        {
            mode = Mode.Standing;
            ticksLeft = Main.rand.Next(60, 240);
            hop = Main.rand.NextBool(20);
            return;
        }
        mode = Mode.Walking;
        ticksLeft = Main.rand.Next(60, 240);
        float offset = Main.rand.NextFloat(-Weights.CalmBandFar * 0.7f, Weights.CalmBandFar * 0.7f);
        goal = ctx.Senses.Player.Bottom + new Vector2(offset, 0f);
        hop = Main.rand.NextBool(12);
    }
}
