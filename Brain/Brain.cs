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
        Vector2? spot = Positioner.Resolve(LastRequest, Senses, profile);
        // Lava is a crossable cost only while there is life to pay it with.
        AStar.AllowLava = Senses.Self.LifeFraction > 0.6f && !Senses.Self.InLava;
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
            Navigator.MoveTo(companion.NPC, companion.Motor, feet);
        else
        {
            companion.Motor.Stop();
            Navigator.Clear();
        }
    }
}
