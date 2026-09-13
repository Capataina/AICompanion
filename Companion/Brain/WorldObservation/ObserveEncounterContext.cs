#nullable enable

using System;
using Terraria;
using Terraria.GameContent.Events;
using AICompanion.Companion.Brain.BehaviourSelection;

namespace AICompanion.Companion.Brain.WorldObservation;

/// <summary>
/// Whether the world is currently about one thing — a boss fight or a world event — how strongly, and
/// whether that is a recognised fact or an inference. The owner's boss and event conduct turns on this
/// state of the world rather than on the identity of a monster: optional work stops mattering while it
/// holds and comes back the moment it ends, and a modded invasion nobody wrote anything for should look
/// the same as a vanilla one.
///
/// Recognised sources are native facts. An observed hostile carrying the game's own boss flag makes it a
/// boss encounter wherever it is. The Old One's Army counts at any depth. Blood moon, solar eclipse, the
/// pumpkin and frost moons and an active invasion count only while the player is at or above the surface,
/// which is where the game runs them and the same gate its invasion progress display applies. Slime rain
/// is deliberately not an encounter: it is mild enough that suppressing all optional work for it would
/// surprise. A modded event that sets none of these flags cannot be recognised, so the fallback is
/// observed pressure — enough hostiles able to reach either actor, sustained — reported as unrecognised,
/// which keeps an inferred encounter distinguishable from a known one in every reader. Pressure ramps in
/// over its window and drops to nothing the tick it lapses, because the return to ordinary is meant to be
/// immediate rather than wound down. Thresholds live in BehaviourWeights.
/// </summary>
public sealed class EncounterSense
{
    /// <summary>0..1: how completely the world is about one thing. One for any recognised source.</summary>
    public float Intensity { get; private set; }

    /// <summary>`boss`, `event:&lt;name&gt;`, `observed-pressure` or `none`.</summary>
    public string Source { get; private set; } = "none";

    /// <summary>True only for native boss or event facts; observed pressure is always an inference.</summary>
    public bool Recognised { get; private set; }

    /// <summary>Consecutive observations with at least the pressure count of hostiles able to reach an actor.</summary>
    public int PressureTicks { get; private set; }

    public void Update(Player player, ThreatSense threats)
    {
        int reaching = 0;
        bool boss = false;
        foreach (ThreatRecord threat in threats.Threats)
        {
            boss |= threat.IsBoss;
            if (threat.CanReachEither) reaching++;
        }
        bool surface = player.position.Y <= Main.worldSurface * 16.0;
        string? recognised = boss ? "boss"
            : DD2Event.Ongoing ? "event:old-ones-army"
            : !surface ? null
            : Main.bloodMoon ? "event:blood-moon"
            : Main.eclipse ? "event:solar-eclipse"
            : Main.pumpkinMoon ? "event:pumpkin-moon"
            : Main.snowMoon ? "event:frost-moon"
            : Main.invasionType > 0 ? "event:invasion"
            : null;
        PressureTicks = reaching >= Weights.EncounterPressureHostiles ? PressureTicks + 1 : 0;
        float observed = Math.Clamp(PressureTicks / MathF.Max(1f, Weights.EncounterPressureTicks), 0f, 1f);
        if (recognised != null)
        {
            Intensity = 1f; Source = recognised; Recognised = true;
        }
        else if (observed > 0f)
        {
            Intensity = observed; Source = "observed-pressure"; Recognised = false;
        }
        else
        {
            Intensity = 0f; Source = "none"; Recognised = false;
        }
    }
}
