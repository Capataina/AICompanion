#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using AICompanion.Content;

namespace AICompanion.Commands;

/// <summary>
/// <c>/companion</c> in chat. Spawns one companion at the player's feet, or, if
/// one already exists anywhere in the world, brings it to the player instead.
/// There is deliberately never more than one.
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
        int type = ModContent.NPCType<Companion>();

        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && npc.type == type)
            {
                npc.Bottom = player.Bottom;
                npc.velocity = Vector2.Zero;
                caller.Reply("Your companion is here.", Color.LightGreen);
                return;
            }
        }

        int index = NPC.NewNPC(player.GetSource_Misc("companion"), (int)player.Center.X, (int)player.Bottom.Y, type);
        if (index < Main.maxNPCs)
            caller.Reply("A companion joins you.", Color.LightGreen);
        else
            caller.Reply("No room for a companion right now (NPC limit reached).", Color.OrangeRed);
    }
}
