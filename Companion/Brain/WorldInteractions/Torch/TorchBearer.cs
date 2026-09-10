#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.WorldObservation;
using AICompanion.Companion.MapIntegration;

namespace AICompanion.Companion.Brain.WorldInteractions.Torch;

/// <summary>
/// Holds a torch up in the dark. A mod ability, not an item: nothing is consumed. The
/// decision reads the light sense's ambient number, which is measured away from the
/// companion's own glow, and switches with hysteresis (raise below one level, lower
/// above a higher one) plus a minimum hold, so the torch cannot flicker on its own
/// light. Whether the hand is free is the NPC's call (any action that holds a tool or
/// weapon wins it), and light, map reveal and the torch in the hand all follow that
/// one answer: a companion swinging a pickaxe neither glows nor reveals, because the
/// torch is not out. <see cref="Lit"/> is the decision; <see cref="Shown"/> is the
/// decision and a free hand together.
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

    /// <summary>The torch is actually out: lit and the hand was free this tick.</summary>
    public bool Shown { get; private set; }

    private int sinceChange;
    private int sinceReveal;
    public void Hide() => Shown = false;

    public void Update(LightSense light, NPC npc, bool handFree)
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
        // Player.ItemCheck's held-torch path requires !wet for ItemID.Torch; only
        // ItemID.Sets.WaterTorches bypass it. This ability holds the ordinary torch.
        Shown = Lit && handFree && !npc.wet;
        if (!Shown)
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
