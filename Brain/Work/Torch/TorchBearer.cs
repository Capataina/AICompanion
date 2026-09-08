#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Brain.DecisionMatrix.Senses;
using AICompanion.Map;

namespace AICompanion.Brain.Work.Torch;

/// <summary>
/// Holds a torch up in the dark. A mod ability, not an item: nothing is consumed. The
/// decision reads the light sense's ambient number, which is measured away from the
/// companion's own glow, and switches with hysteresis (raise below one level, lower
/// above a higher one) plus a minimum hold, so the torch cannot flicker on its own
/// light. Lit, it emits a torch's colour at the hand and reveals the map through
/// <see cref="TorchMapReveal"/>. Whether the torch is actually shown is the NPC's
/// call: any action that holds a tool or weapon wins the hand.
/// </summary>
public sealed class TorchBearer
{
    /// <summary>Surface daylight reads near 1; a lit cave about 0.5; unlit caverns under 0.1. Dim is not dark.</summary>
    private const float RaiseBelow = 0.22f;
    private const float LowerAbove = 0.42f;
    private const int MinimumHoldTicks = 180;
    private const int RevealEveryTicks = 10;

    /// <summary>How far the torch's light reaches for the map, in tiles.</summary>
    public int ReachTiles { get; set; } = 7;

    public bool Lit { get; private set; }
    private int sinceChange;
    private int sinceReveal;

    public void Update(LightSense light, NPC npc)
    {
        sinceChange++;
        if (sinceChange >= MinimumHoldTicks)
        {
            if (!Lit && light.Ambient < RaiseBelow)
            {
                Lit = true;
                sinceChange = 0;
            }
            else if (Lit && light.Ambient > LowerAbove)
            {
                Lit = false;
                sinceChange = 0;
            }
        }
        if (!Lit)
            return;

        TorchID.TorchColor(TorchID.Torch, out float r, out float g, out float b);
        Vector2 hand = npc.Center + new Vector2(npc.direction * 10f, -6f);
        Lighting.AddLight(hand, r, g, b);

        if (++sinceReveal >= RevealEveryTicks)
        {
            sinceReveal = 0;
            TorchMapReveal.Reveal(npc.Center.ToTileCoordinates(), ReachTiles);
        }
    }
}
