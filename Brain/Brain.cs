#nullable enable

using Microsoft.Xna.Framework;
using AICompanion.Brain.Decision;
using AICompanion.Brain.Decision.Actions;
using AICompanion.Brain.Navigation;
using AICompanion.Brain.Positioning;
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
    public readonly Senses.Senses Senses = new();
    public readonly Chooser Chooser = new();
    public readonly Positioner Positioner = new();
    public readonly Navigator Navigator = new();
    public readonly Reflexes.Reflexes Reflexes = new();

    public PositionRequest LastRequest { get; private set; }
    public CompanionAction? LastAction => Chooser.Current;

    public void Tick(CompanionNPC companion, Terraria.Player player)
    {
        Senses.Update(companion.NPC, player);
        companion.Arsenal.Tick();
        companion.Chopper.Tick();

        if (Reflexes.TryTake(companion.NPC, companion.Motor, Senses))
            return;

        var ctx = new ActionContext(companion, Senses);
        CompanionAction action = Chooser.Choose(ctx);
        LastRequest = action.Execute(ctx);

        var profile = companion.Arsenal.ProfileFor(ctx, LastRequest.Target);
        Vector2? spot = Positioner.Resolve(LastRequest, Senses, profile);
        if (spot is Vector2 feet)
            Navigator.MoveTo(companion.NPC, companion.Motor, feet);
        else
        {
            companion.Motor.Stop();
            Navigator.Clear();
        }
    }
}
