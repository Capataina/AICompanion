#nullable enable

using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.WorldObservation;
using Terraria;

namespace AICompanion.Companion.Brain.BehaviourSelection;

/// <summary>The target binding captured when an adapter prepares. Validation consumes live
/// availability without discovering a replacement or changing the prepared utility.</summary>
public readonly record struct ValidatePreparedActivity(object? Identity, int Generation, int ItemType)
{
    public static ValidatePreparedActivity Capture(CompanionAction action)
    {
        object? identity = action.ActivityIdentity;
        return new(identity, identity is NPC npc ? HostileAttackSources.Generation(npc) : 0,
            identity is Item item ? item.type : 0);
    }

    public string Rejection(CompanionAction action)
    {
        if (!Equals(Identity, action.ActivityIdentity)) return "prepared-target-changed";
        if (Identity is NPC npc)
        {
            if (!npc.active || npc.life <= 0) return "prepared-enemy-unavailable";
            if (HostileAttackSources.Generation(npc) != Generation) return "prepared-enemy-generation-changed";
        }
        if (Identity is Item item)
        {
            if (!item.active || item.stack <= 0) return "prepared-item-unavailable";
            if (item.type != ItemType) return "prepared-item-type-changed";
        }
        return "";
    }
}
