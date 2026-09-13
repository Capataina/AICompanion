#nullable enable

using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.Events;
using Terraria.ModLoader;
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
/// boss encounter wherever it is. The Old One's Army counts at any depth. Blood moon, solar eclipse and the
/// pumpkin and frost moons count only while the player is at or above the surface, which is where the game
/// runs them. An invasion counts exactly where the game would spawn its enemies for this player (see
/// <see cref="InvasionSpawnsNear"/>). Slime rain is deliberately not an encounter: it is mild enough that
/// suppressing all optional work for it would surprise.
///
/// A modded event that sets none of these flags cannot be recognised, so the fallback is observed pressure,
/// reported as unrecognised: the hostiles able to reach either actor, weighed the way the game's spawner
/// weighs nearby NPCs, exceed the spawn cap the game computed for the player this tick. Ordinary spawning
/// stops adding enemies once that weight reaches the cap, so a busy cave sits at the cap and never above
/// it, and a worm counts as its head because the game excludes its segments; only enemies arriving from
/// somewhere other than ordinary spawning push the weight past it. Pressure ramps in over its window and
/// drops to nothing the tick it lapses, because the return to ordinary is meant to be immediate rather than
/// wound down. The ramp length lives in BehaviourWeights.
/// </summary>
public sealed class EncounterSense
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    // The game's own spawn accounting lives in private statics of NPC. maxSpawns is recomputed for each
    // player inside NPC.SpawnNPC, which Main runs before the NPC update loop the companion's AI runs in, so
    // on an ordinary tick it holds this tick's cap for the one singleplayer player, after every vanilla
    // multiplier and every mod's EditSpawnRate (this mod's doubling included). The active ranges are set
    // once from the screen size and never reassigned. A game update that renames any of them must fail
    // here by name rather than silently measure against a default.
    private static readonly FieldInfo MaxSpawns = Bind("maxSpawns");
    private static readonly FieldInfo ActiveRangeX = Bind("activeRangeX");
    private static readonly FieldInfo ActiveRangeY = Bind("activeRangeY");

    /// <summary>0..1: how completely the world is about one thing. One for any recognised source.</summary>
    public float Intensity { get; private set; }

    /// <summary>`boss`, `event:&lt;name&gt;`, `observed-pressure` or `none`.</summary>
    public string Source { get; private set; } = "none";

    /// <summary>True only for native boss or event facts; observed pressure is always an inference.</summary>
    public bool Recognised { get; private set; }

    /// <summary>Consecutive observations on which the reachable spawn weight exceeded the game's spawn cap.</summary>
    public int PressureTicks { get; private set; }

    /// <summary>The spawn-slot weight of hostiles able to reach either actor, counted as the game counts toward its cap.</summary>
    public float SpawnWeight { get; private set; }

    /// <summary>The game's spawn cap for the player as last computed by its spawner.</summary>
    public int SpawnCap { get; private set; }

    public void Update(Player player, ThreatSense threats)
    {
        bool boss = false;
        float weight = 0f;
        int rangeX = (int)ActiveRangeX.GetValue(null)!, rangeY = (int)ActiveRangeY.GetValue(null)!;
        Rectangle playerBox = player.Hitbox;
        foreach (ThreatRecord threat in threats.Threats)
        {
            boss |= threat.IsBoss;
            if (threat.CanReachEither) weight += SpawnSlots(threat.Npc, playerBox, rangeX, rangeY);
        }
        bool surface = player.position.Y <= Main.worldSurface * 16.0;
        string? recognised = boss ? "boss"
            : DD2Event.Ongoing ? "event:old-ones-army"
            : surface && Main.bloodMoon ? "event:blood-moon"
            : surface && Main.eclipse ? "event:solar-eclipse"
            : surface && Main.pumpkinMoon ? "event:pumpkin-moon"
            : surface && Main.snowMoon ? "event:frost-moon"
            : InvasionSpawnsNear(player) ? "event:invasion"
            : null;
        SpawnWeight = weight;
        SpawnCap = (int)MaxSpawns.GetValue(null)!;
        PressureTicks = weight > SpawnCap ? PressureTicks + 1 : 0;
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

    /// <summary>
    /// What one NPC adds to a player's nearby spawn weight, mirroring NPC.CheckActive: an NPC that does not
    /// despawn to inactivity (every worm body and tail segment, among others) or that a mod keeps out of the
    /// check contributes nothing; the burning, chaos and water spheres (types 25, 30, 33), released critters
    /// and lifeless records are skipped; everything else counts its npcSlots when its active range, centred on
    /// it, touches the player's hitbox. Comparing strictly above the cap is what keeps a saturated cave out:
    /// the spawner admits one more while the weight is below the cap, so ordinary spawning can overshoot only
    /// by the slots of its last admission, which the pressure ramp outlasts.
    /// </summary>
    private static float SpawnSlots(NPC npc, Rectangle playerBox, int rangeX, int rangeY)
    {
        if (!npc.active || npc.townNPC || npc.DoesntDespawnToInactivity() || !NPCLoader.CheckActive(npc))
            return 0f;
        if (npc.type is 25 or 30 or 33 || npc.releaseOwner != 255 || npc.lifeMax <= 0)
            return 0f;
        var range = new Rectangle((int)(npc.position.X + npc.width / 2 - rangeX), (int)(npc.position.Y + npc.height / 2 - rangeY),
            rangeX * 2, rangeY * 2);
        if (!range.Intersects(playerBox))
            return 0f;
        return Main.slimeRain && Main.slimeRainNPC[npc.type] ? npc.npcSlots * Main.slimeRainNPCSlots : npc.npcSlots;
    }

    /// <summary>
    /// Whether the game would spawn invasion enemies for this player, by the gate NPC.SpawnNPC applies: an
    /// invasion that has arrived (no delay left) and has enemies left to send, a player above one screen below
    /// the surface line (any depth in a remix world), and a player within the invasion's spawning band around
    /// its position — or, while it sits at the world's centre, near a town NPC. The spawner rolls two in three
    /// for that town case; the roll rations spawns rather than deciding whether the invasion is present, so it
    /// is not reproduced. A flag set with nothing arriving here is not an encounter here.
    /// </summary>
    private static bool InvasionSpawnsNear(Player player)
    {
        if (Main.invasionType <= 0 || Main.invasionDelay != 0 || Main.invasionSize <= 0)
            return false;
        if (!(player.position.Y < Main.worldSurface * 16.0 + NPC.sHeight || Main.remixWorld))
            return false;
        const double band = 3000.0;
        double centre = Main.invasionX * 16.0;
        if (player.position.X > centre - band && player.position.X < centre + band)
            return true;
        if (Main.invasionX < Main.maxTilesX / 2 - 5 || Main.invasionX > Main.maxTilesX / 2 + 5)
            return false;
        foreach (NPC npc in Main.ActiveNPCs)
            if (npc.townNPC && Math.Abs(player.position.X - npc.Center.X) < band)
                return true;
        return false;
    }

    private static FieldInfo Bind(string name)
        => typeof(NPC).GetField(name, PrivateStatic)
            ?? throw new MissingFieldException(nameof(NPC), name + " (the game's spawn accounting, read by the encounter observation)");
}
