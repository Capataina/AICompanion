#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Companion.PlayerIntegration;

/// <summary>
/// <c>/companion</c> in chat. Spawns one companion at the player's feet, or, if
/// one already exists anywhere in the world, brings it to the player instead.
/// There is deliberately never more than one. Summoning once marks the character
/// so the companion returns on every later world enter.
/// </summary>
public class CompanionCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Command => "companion";
    public override string Usage => "/companion";
    public override string Description => "Spawn your companion, or call it to you if it already exists.";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        Player player = caller.Player;
        player.GetModPlayer<CompanionPlayer>().HasCompanion = true;

        NPC? existing = CompanionNPC.Find();
        if (existing != null)
        {
            existing.Bottom = player.Bottom;
            existing.velocity = Vector2.Zero;
            caller.Reply("Your companion is here.", Color.LightGreen);
            return;
        }

        if (CompanionNPC.Spawn(player) < Main.maxNPCs)
            caller.Reply("A companion joins you.", Color.LightGreen);
        else
            caller.Reply("No room for a companion right now (NPC limit reached).", Color.OrangeRed);
    }
}
