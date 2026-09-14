#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.CharacterBody;

/// <summary>
/// A <see cref="Player"/> that stands in for the companion wherever the game only understands
/// players: every enemy picks its target by one loop over the player slots that skips inactive,
/// dead and ghost players, so a companion with no slot is invisible to hostile AI. This sits in
/// the highest slot the map head renderer's per-player table accepts, inactive, and is switched
/// active only for the span in which a hostile's AI runs (<see cref="EnemyIntegration.CompanionAggro"/>).
/// Outside that span the slot is invisible to the player update, the spawn waves,
/// projectile-versus-player checks and every other reader of the array. Downed mirrors to
/// <c>dead</c>, so a boss leaves when the player is dead and the companion is down.
///
/// <para>Its box is the orb's box, centred on the orb's centre, because it is what enemies aim at;
/// the mining interaction also runs the game's own pick routine on it, which needs a player and
/// reads nothing about its shape. It draws nothing: the orb is drawn by <see cref="DrawTheOrb"/>.</para>
/// </summary>
public sealed class HostileTargetStandIn
{
    private readonly Player body;

    /// <summary>The body currently placed in the player array, for the aggro window; null when none is.</summary>
    public static Player? Placed { get; private set; }

    /// <summary>The stand-in player, for the aggro window and the game's pick routine.</summary>
    public Player Player => body;

    public HostileTargetStandIn()
    {
        body = new Player
        {
            whoAmI = Main.maxPlayers - 1,
            width = (int)Brain.Infrastructure.Movement.CircleContact.Diameter,
            height = (int)Brain.Infrastructure.Movement.CircleContact.Diameter,
            gravDir = 1f,
            direction = 1,
        };
        body.selectedItem = 0;
        body.active = false;
        body.dead = true;
        Main.player[body.whoAmI] = body;
        Placed = body;
    }

    /// <summary>Make the placed body visible (or not) to whatever reads the player array right now.</summary>
    public static void Expose(bool visible)
    {
        if (Placed != null)
            Placed.active = visible;
    }

    /// <summary>Put an ordinary empty player back in the slot; called on mod unload.</summary>
    public static void Withdraw()
    {
        if (Placed != null)
            Main.player[Placed.whoAmI] = new Player();
        Placed = null;
    }

    /// <summary>Copy what enemy AI reads: where the body is, how it moves, and whether it counts as alive.</summary>
    public void Sync(NPC npc, bool downed)
    {
        body.Center = npc.Center;
        body.velocity = npc.velocity;
        body.direction = npc.direction == 0 ? 1 : npc.direction;
        body.dead = downed;
        body.ghost = false;
        body.wet = npc.wet;
        body.statLifeMax2 = body.statLifeMax = npc.lifeMax;
        body.statLife = npc.life;
    }
}
