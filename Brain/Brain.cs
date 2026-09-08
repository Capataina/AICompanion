#nullable enable

using Microsoft.Xna.Framework;
using AICompanion.Brain.Actions;
using AICompanion.Brain.DecisionMatrix;
using AICompanion.Brain.DecisionMatrix.Decision;
using AICompanion.Brain.DecisionMatrix.Navigation;
using AICompanion.Brain.DecisionMatrix.Positioning;
using AICompanion.Companion;

namespace AICompanion.Brain;

/// <summary>
/// The tick order of the companion's mind: senses read the world, reflexes may take
/// the body, the chooser picks an action, the action acts and asks for a spot, the
/// positioner picks the spot, the navigator walks there. Everything the overlay and
/// telemetry show is left on these objects after the tick.
/// </summary>
public sealed class Brain
{
    public readonly DecisionMatrix.Senses.Senses Senses = new();
    public readonly Chooser Chooser = new();
    public readonly Positioner Positioner = new();
    public readonly Navigator Navigator = new();
    public readonly DecisionMatrix.Reflexes.Reflexes Reflexes = new();

    public PositionRequest LastRequest { get; private set; }
    public CompanionAction? LastAction => Chooser.Current;

    public void Tick(CompanionNPC companion, Terraria.Player player)
    {
        Senses.Update(companion.NPC, player, companion.Breath);
        companion.Arsenal.Tick();
        companion.Chopper.Tick();

        if (Reflexes.TryTake(companion.NPC, companion.Motor, Senses))
            return;

        var ctx = new ActionContext(companion, Senses);
        CompanionAction action = Chooser.Choose(ctx);
        LastRequest = action.Execute(ctx);

        var profile = companion.Arsenal.ProfileFor(ctx, LastRequest.Target);
        // Lava is a crossable cost only while there is life to pay it with.
        AStar.AllowLava = Senses.Self.LifeFraction > 0.6f && !Senses.Self.InLava;
        // A drop with no way back is taken only after the player, or to save the body: a hunt
        // that dropped into a sealed pocket after its target stood at the rim for the rest of
        // run 5 (2026-09-08). Set before the positioner resolves, whose reach flood shares the rule.
        AStar.AllowOneWayDrops = LastRequest.Kind is RequestKind.WithPlayer or RequestKind.Guard || action is Actions.Survival.SurviveAction;
        Vector2? spot = Positioner.Resolve(LastRequest, Senses, profile);
        // Reachable enemies are priced like lava on the route, so a path to a spot beyond one
        // goes round it rather than through it.
        AStar.Avoid.Clear();
        foreach (var threat in Senses.Threats.Threats)
        {
            if (!threat.Reachable)
                continue;
            Rectangle box = threat.Npc.Hitbox;
            box.Inflate(24, 24);
            AStar.Avoid.Add(box);
        }
        if (spot is Vector2 feet)
        {
            Navigator.MoveTo(companion.NPC, companion.Motor, feet);
            // Stuck twice on the way to one spot: the first strike priced the step and the replan
            // found nothing better, so the spot itself is the problem. Refuse it for a while and
            // let the positioner answer with another, which is Caner's "choose a different
            // position to unstick itself" (2026-09-08).
            if (Navigator.StuckStrikes >= 2)
            {
                Positioner.Ban(NavGrid.FeetTile(feet), Weights.StuckSpotBanTicks);
                Navigator.ResetStrikes();
            }
        }
        else
        {
            companion.Motor.Stop();
            Navigator.Clear();
        }
    }
}
