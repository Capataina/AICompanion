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
/// <para>An unmeasured query casts no vote: nobody having read a neighbourhood is not the same as
/// reading it as bright, so a silent answer neither raises nor lowers, and with both silent the state
/// simply holds. A torch that drops whenever the companion walks off the engine's computed screen is a
/// torch that drops in exactly the caves it is for, and one that drops because the player's distant feet
/// are the only thing anyone measured is the same failure reached from the other side.</para>
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
        // The two halves are asymmetric on purpose, because the two mistakes cost differently. Raising asks
        // whether anybody saw dark air, so one measured dark answer is enough and a silent one is skipped.
        // Lowering asks whether there is no dark air anywhere the companion is about to be, which nobody can
        // answer about a place nobody read: a silent query therefore blocks it rather than abstaining. Letting
        // it abstain is the same defect as reading its share, one step further out — a companion deep in an
        // unlit cave the engine has not computed, with the player's predicted feet out in daylight, has one
        // bright answer and one silence, and every rule that lets the bright one carry alone drops the torch
        // in the dark. The cost of the other error is a torch held for a few seconds in a room already lit.
        bool raise = (!here.Unmeasured && here.DarkFraction > Weights.TorchRaiseDarkShare)
            || (!ahead.Unmeasured && ahead.DarkFraction > Weights.TorchRaiseDarkShare);
        bool lower = !here.Unmeasured && !ahead.Unmeasured
            && here.DarkFraction < Weights.TorchLowerDarkShare
            && ahead.DarkFraction < Weights.TorchLowerDarkShare;
        if (here.Unmeasured && ahead.Unmeasured)
        {
            // Nobody read either neighbourhood. That is not brightness, so the state stands: a torch that
            // drops whenever the companion walks off the engine's computed screen drops in exactly the
            // caves it exists for.
            Reason = Lit ? "lit-unmeasured" : "out-unmeasured";
        }
        else if (sinceChange >= MinimumHoldTicks)
        {
            if (!Lit && raise)
            {
                Lit = true;
                sinceChange = 0;
                Reason = !here.Unmeasured && here.DarkFraction > Weights.TorchRaiseDarkShare ? "dark-near" : "dark-ahead";
            }
            else if (Lit && lower)
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
