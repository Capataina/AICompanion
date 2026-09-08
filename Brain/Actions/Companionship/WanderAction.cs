#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Brain.DecisionMatrix.Decision;

namespace AICompanion.Brain.Actions.Companionship;

/// <summary>
/// Nothing better to do: stand a while, stroll a while, occasionally hop, inside the
/// calm band. Its score is a floor, so anything real outscores it.
/// </summary>
public sealed class WanderAction : CompanionAction
{
    public override string Name => "wander";

    private enum Mode { Standing, Walking }
    private Mode mode = Mode.Standing;
    private int ticksLeft = 60;
    private Vector2 goal;
    private bool hop;

    public override float Score(in ActionContext ctx)
        => ctx.Senses.Player.IsDead ? 0f : Weights.WanderFloor;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(ItemID.None);
        if (ctx.Senses.Player.IsDead)
            return PositionRequest.Hold;
        if (--ticksLeft <= 0)
            PickNext(ctx);

        if (hop)
        {
            hop = false;
            ctx.Companion.Motor.Jump(0.7f);
        }
        return mode == Mode.Standing ? PositionRequest.Hold : PositionRequest.ExactAt(goal);
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
