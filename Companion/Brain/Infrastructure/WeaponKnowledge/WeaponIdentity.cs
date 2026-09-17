#nullable enable

using System;
using System.Collections.Generic;
using Terraria.ID;
using Terraria.ModLoader;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge;

/// <summary>
/// Stable names for the id spaces weapon knowledge is keyed by. Vanilla ids never shuffle, but a modded
/// item's numeric id depends on load order, so everything persisted — the player save, the combat snapshot —
/// keys by name and resolves on the way back in (row K10). Names come from the game's own search tables, which
/// hold full names for modded content. Reverse lookups enumerate each space once and cache, because the proven
/// API surface is only <c>TryGetName</c>; a fixture passes a subclass with planted mappings to shuffle ids
/// headless, where no modded content exists to shuffle for real.
/// </summary>
public class WeaponIdentity
{
    private Dictionary<string, int>? items, projectiles, npcs, buffs;

    public virtual string NameOfItem(int id)
        => ItemID.Search.TryGetName(id, out string? name) && name != null ? name : $"item-{id}";

    public virtual string NameOfProjectile(int id)
        => ProjectileID.Search.TryGetName(id, out string? name) && name != null ? name : $"projectile-{id}";

    public virtual string NameOfNpc(int id)
        => NPCID.Search.TryGetName(id, out string? name) && name != null ? name : $"npc-{id}";

    public virtual string NameOfBuff(int id)
        => BuffID.Search.TryGetName(id, out string? name) && name != null ? name : $"buff-{id}";

    public virtual int? ItemOfName(string name) => Reverse(ref items, name, ItemLoader.ItemCount, NameOfItem);

    public virtual int? ProjectileOfName(string name) => Reverse(ref projectiles, name, ProjectileLoader.ProjectileCount, NameOfProjectile);

    public virtual int? NpcOfName(string name) => Reverse(ref npcs, name, NPCLoader.NPCCount, NameOfNpc);

    public virtual int? BuffOfName(string name) => Reverse(ref buffs, name, BuffLoader.BuffCount, NameOfBuff);

    private static int? Reverse(ref Dictionary<string, int>? cache, string name, int count, Func<int, string> forward)
    {
        cache ??= BuildReverse(count, forward);
        return cache.TryGetValue(name, out int id) ? id : null;
    }

    private static Dictionary<string, int> BuildReverse(int count, Func<int, string> forward)
    {
        var reverse = new Dictionary<string, int>(count);
        for (int id = 1; id < count; id++)
        {
            string name = forward(id);
            if (!reverse.ContainsKey(name))
                reverse[name] = id;
        }
        return reverse;
    }
}
