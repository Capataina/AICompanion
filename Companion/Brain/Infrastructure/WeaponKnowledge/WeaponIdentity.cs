#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
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
    private static readonly Dictionary<Type, Dictionary<int, string>> VanillaNames = new();

    public virtual string NameOfItem(int id)
        => ItemID.Search.TryGetName(id, out string? item) && !string.IsNullOrEmpty(item) ? item
            : VanillaName(typeof(ItemID), id) ?? $"item-{id}";

    public virtual string NameOfProjectile(int id)
        => ProjectileID.Search.TryGetName(id, out string? projectile) && !string.IsNullOrEmpty(projectile) ? projectile
            : VanillaName(typeof(ProjectileID), id) ?? $"projectile-{id}";

    public virtual string NameOfNpc(int id)
        => NPCID.Search.TryGetName(id, out string? npc) && !string.IsNullOrEmpty(npc) ? npc
            : VanillaName(typeof(NPCID), id) ?? $"npc-{id}";

    public virtual string NameOfBuff(int id)
        => BuffID.Search.TryGetName(id, out string? buff) && !string.IsNullOrEmpty(buff) ? buff
            : VanillaName(typeof(BuffID), id) ?? $"buff-{id}";

    /// <summary>
    /// The vanilla enum field name for this id. Headless EngineReplay never fills the game's Search
    /// tables, and a bundle keyed as <c>projectile-1</c> would fail K10's name-keyed load. The
    /// field name is the same string Search would have returned for an unmodded id.
    /// </summary>
    private static string? VanillaName(Type idClass, int id)
    {
        if (!VanillaNames.TryGetValue(idClass, out Dictionary<int, string>? byId))
        {
            byId = new Dictionary<int, string>();
            foreach (FieldInfo field in idClass.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(short) && field.FieldType != typeof(int) && field.FieldType != typeof(ushort))
                    continue;
                object? value = field.GetValue(null);
                if (value == null)
                    continue;
                int n = Convert.ToInt32(value);
                if (!byId.ContainsKey(n))
                    byId[n] = field.Name;
            }
            VanillaNames[idClass] = byId;
        }
        return byId.TryGetValue(id, out string? name) ? name : null;
    }

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
