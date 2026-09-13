#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.MapIntegration;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions.Torch;

/// <summary>
/// Holds a torch up in the dark. A mod ability, not an item: nothing is consumed. The decision reads
/// the light field twice — how much dark air sits around the body, and how much sits at the player's
/// predicted feet — and lights on either, so a companion crossing a bright surface toward a hole ahead
/// has the torch up by the time it arrives rather than a second and a half after. It switches with
/// hysteresis (raise above one share of dark, lower below a lower one) plus a minimum hold, so the
/// torch cannot flicker on its own light, and the field has the companion's own torch subtracted so
/// the reading cannot be made by the thing it decides.
///
/// <para>Neither query being measured holds the current state rather than changing it: nobody has read
/// this neighbourhood, which is not the same as reading it as bright, and a torch that drops whenever
/// the companion walks off the engine's computed screen is a torch that drops in exactly the caves it
/// is for.</para>
///
/// <para>Whether the hand is free is the NPC's call (any action that holds a tool or weapon wins it),
/// and light, map reveal and the torch in the hand all follow that one answer: a companion swinging a
/// pickaxe neither glows nor reveals, because the torch is not out. <see cref="Lit"/> is the decision;
/// <see cref="Shown"/> is the decision and a free hand together.</para>
/// </summary>
public sealed class TorchBearer
{
    private const int MinimumHoldTicks = 180;
    private const int RevealEveryTicks = 10;

    /// <summary>How far the torch's light reaches for the map, in tiles.</summary>
    public int ReachTiles { get; set; } = 7;

    public bool Lit { get; private set; }

    /// <summary>The torch is actually out: lit and the hand was free this tick.</summary>
    public bool Shown { get; private set; }

    private int sinceChange;
    private int sinceReveal;

    /// <summary>Why the torch is lit or out this tick, for the telemetry: the answer that decided it, not
    /// a restatement of the state.</summary>
    public string Reason { get; private set; } = "unmeasured";

    public void Hide() => Shown = false;

    public void Update(LightSense light, NPC npc, bool handFree, Vector2 playerHeading)
    {
        sinceChange++;
        var here = light.DarkAirNear(npc.Center.ToTileCoordinates(), Weights.TorchHoldRadiusTiles);
        var ahead = light.DarkAirNear(playerHeading.ToTileCoordinates(), Weights.TorchHoldRadiusTiles);
        if (here.Unmeasured && ahead.Unmeasured)
        {
            // Nobody read either neighbourhood. That is not brightness, so the state stands: a torch that
            // drops whenever the companion walks off the engine's computed screen drops in exactly the
            // caves it exists for.
            Reason = Lit ? "lit-unmeasured" : "out-unmeasured";
        }
        else if (sinceChange >= MinimumHoldTicks)
        {
            float darkHere = here.DarkFraction, darkAhead = ahead.DarkFraction;
            if (!Lit && (darkHere > Weights.TorchRaiseDarkShare || darkAhead > Weights.TorchRaiseDarkShare))
            {
                Lit = true;
                sinceChange = 0;
                Reason = darkHere > Weights.TorchRaiseDarkShare ? "dark-near" : "dark-ahead";
            }
            else if (Lit && darkHere < Weights.TorchLowerDarkShare && darkAhead < Weights.TorchLowerDarkShare)
            {
                Lit = false;
                sinceChange = 0;
                Reason = "lit-room";
            }
            else Reason = Lit ? "held-lit" : "held-out";
        }
        else Reason = Lit ? "minimum-hold-lit" : "minimum-hold-out";
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
