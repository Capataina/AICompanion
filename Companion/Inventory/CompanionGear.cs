#nullable enable

using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AICompanion.Companion.Inventory;

/// <summary>The four places gear can go. The order is the order the slots are drawn and saved in.</summary>
public enum GearSlot { FirstWeapon = 0, SecondWeapon = 1, Pickaxe = 2, Axe = 3 }

/// <summary>
/// The companion's equipment: two weapons, a pickaxe and an axe, each a real game item the player
/// put there, from vanilla or from any mod. The ruling of 14 September 2026 is four slots and no
/// more — no armour, no accessory, no ammo, no mount — and that a handed item is read for its
/// numbers and never run through the player's item-use code. This class holds the items and
/// answers what each slot accepts; what a weapon does with those numbers is the arsenal's
/// (<c>../Brain/Infrastructure/Interactions/Firing/ItemWeapon.cs</c>), and what a tool's power gates is the interactions'.
///
/// Gear is equipment and never cargo: nothing here is picked up, stacked or handed over, and the
/// bag beside it never holds a weapon the companion fights with. A slot refuses by predicate, and
/// the refusal is the slot's whole opinion — an item it accepts is one the companion's own
/// mechanisms can express, and the reason it refuses is shown so the player learns the rule rather
/// than guessing at it.
/// </summary>
public sealed class CompanionGear
{
    public const int SlotCount = 4;

    /// <summary>The items, indexed by <see cref="GearSlot"/>. The UI writes here through the game's own slot
    /// handling after asking <see cref="Accepts"/>, and readers never hold a reference across ticks.</summary>
    public readonly Item[] Slots = new Item[SlotCount];
    private long mutationVersion;
    private readonly GearContent[] observedCapabilityContent = new GearContent[SlotCount];

    /// <summary>Monotonic identity of the four slots' semantic capability content.</summary>
    public long MutationVersion => mutationVersion;

    public CompanionGear()
    {
        for (int i = 0; i < SlotCount; i++)
            Slots[i] = new Item();
        CaptureCapabilityContent(observedCapabilityContent);
    }

    public Item this[GearSlot slot] => Slots[(int)slot];

    public Item Pickaxe => Slots[(int)GearSlot.Pickaxe];
    public Item Axe => Slots[(int)GearSlot.Axe];

    /// <summary>
    /// The pick power the interactions gate tiles with, exactly as <c>Player.PickTile</c> gates them
    /// for the player: zero with no pickaxe, so a tile that needs any power at all is refused.
    /// </summary>
    public int PickPower => Pickaxe.IsAir ? 0 : Pickaxe.pick;

    /// <summary>The axe power the interactions damage trunks with; zero with no axe.</summary>
    public int AxePower => Axe.IsAir ? 0 : Axe.axe;

    /// <summary>Runs Terraria's native slot operation and records an actual semantic mutation afterwards.</summary>
    public void EditSlots(Action<Item[]> edit)
    {
        if (edit == null) throw new ArgumentNullException(nameof(edit));
        try { edit(Slots); }
        finally { ObserveMutation(); }
    }

    /// <summary>
    /// Detects a replacement or in-place edit through the legacy public array. Four slots make this a cheap backstop;
    /// unchanged observations do not advance the version.
    /// </summary>
    public bool ObserveMutation()
    {
        var current = new GearContent[SlotCount];
        CaptureCapabilityContent(current);
        for (int i = 0; i < SlotCount; i++)
            if (current[i] != observedCapabilityContent[i])
            {
                Array.Copy(current, observedCapabilityContent, SlotCount);
                mutationVersion++;
                return true;
            }
        return false;
    }

    private readonly record struct GearContent(int Type, int Prefix, int Stack, int Damage, int Pick, int Axe, int Hammer,
        int UseTime, int UseAnimation, int Shoot, float ShootSpeed, int Mana, int UseAmmo, int UseStyle, bool NoMelee,
        bool Channel, int Width, float Scale, float KnockBack, bool Consumable, int DamageClassType);

    private void CaptureCapabilityContent(GearContent[] into)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            Item item = Slots[i];
            into[i] = new GearContent(item.type, item.prefix, item.stack, item.damage, item.pick, item.axe, item.hammer,
                item.useTime, item.useAnimation, item.shoot, item.shootSpeed, item.mana, item.useAmmo, item.useStyle,
                item.noMelee, item.channel, item.width, item.scale, item.knockBack, item.consumable, item.DamageType.Type);
        }
    }

    /// <summary>
    /// A stamp that changes whenever any slot's item changes, so the arsenal can re-enumerate its
    /// weapons only when the gear did rather than on every tick. Type and prefix name an item for
    /// this purpose; the same item with a new prefix is a different weapon. Stack is not in the
    /// stamp: a throwing knife that spends one from the stack is still that knife, and counting
    /// stack released every committed plan as gear-changed the moment a simulated use decremented it.
    /// </summary>
    public int Signature
    {
        get
        {
            // Deterministic in-process: System.HashCode's mixer is not a contract for "same
            // items, same value" across calls, and a changing signature releases the committed
            // plan as gear-changed before a stall can fire.
            int s = 17;
            foreach (Item item in Slots)
            {
                s = s * 31 + item.type;
                s = s * 31 + item.prefix;
            }
            return s;
        }
    }

    /// <summary>
    /// Whether a slot takes this item, and why not when it does not. Air is always accepted,
    /// because clearing a slot is a thing the player must always be able to do.
    ///
    /// A weapon slot accepts what the companion's two mechanisms can express: a projectile it can
    /// spawn itself (the item shoots something, and its ammo class has a default the companion can
    /// fire for free), or a swing it can perform itself (the game's swing use style with nothing
    /// shot). Everything whose behaviour lives outside those numbers is refused: yoyos, flails,
    /// spears and whips, whose projectile is a held thing steered by the player's own update;
    /// summon weapons, whose damage is dealt by minions the companion has no way to own; tools,
    /// whose home is their own slot; and a modded channelled item with its own firing hook, whose
    /// real behaviour is in code the companion never runs. A pickaxe slot wants pick power and an
    /// axe slot wants axe power, and nothing else.
    /// </summary>
    public static bool Accepts(GearSlot slot, Item item, out string reason)
    {
        reason = "";
        if (item.IsAir)
            return true;
        switch (slot)
        {
            case GearSlot.Pickaxe:
                if (item.pick > 0) return true;
                reason = "not a pickaxe";
                return false;
            case GearSlot.Axe:
                if (item.axe > 0) return true;
                reason = "not an axe";
                return false;
            default:
                return AcceptsAsWeapon(item, out reason);
        }
    }

    private static bool AcceptsAsWeapon(Item item, out string reason)
    {
        if (item.damage <= 0)
        {
            reason = "does no damage";
            return false;
        }
        if (item.pick > 0 || item.axe > 0 || item.hammer > 0)
        {
            reason = "a tool goes in its tool slot";
            return false;
        }
        if (ItemID.Sets.Yoyo[item.type])
        {
            reason = "yoyos are steered by the player";
            return false;
        }
        // Identity plus GetEffectInheritance is the relation the loader's CountsAsClass cache is built
        // from (DamageClassLoader.RebuildEffectInheritanceCache), asked directly because that cache
        // exists only once mods have loaded and this predicate also runs in the headless fixtures.
        if (item.DamageType == DamageClass.Summon || item.DamageType.GetEffectInheritance(DamageClass.Summon))
        {
            reason = "summons are the player's minions";
            return false;
        }
        if (item.shoot > 0)
        {
            // An ammo weapon fires its ammo, not its own placeholder: a Boomstick's own shoot names a powder
            // it never fires, so asking after the placeholder refuses every gun headless, where the powder was
            // never registered, while passing them in game for the wrong reason. The checks below read what
            // leaves the muzzle.
            int shot = item.shoot;
            if (item.useAmmo != 0)
            {
                Item? ammo = DefaultAmmo(item);
                if (ammo == null)
                {
                    reason = "no free ammo of its class";
                    return false;
                }
                if (ammo.shoot <= 0)
                {
                    reason = "its ammo fires nothing";
                    return false;
                }
                shot = ammo.shoot;
            }
            if (!ContentSamples.ProjectilesByType.TryGetValue(shot, out Projectile? projectile))
            {
                reason = "fires a projectile the game has not loaded";
                return false;
            }
            // A projectile the arc learner cannot fit — a bubble that rises, a shot that homes — is still
            // fired, at the intercept, and valued by what its uses achieve: the refusal used to sit here and
            // phase B removed it, because the residual learner already prices outcomes the geometry cannot see.
            if (ProjectileID.Sets.IsAWhip[shot] || projectile.aiStyle is ProjAIStyleID.Flail or ProjAIStyleID.Spear or ProjAIStyleID.Yoyo or ProjAIStyleID.Whip)
            {
                reason = "held weapons are steered by the player";
                return false;
            }
            if (item.channel && HasOwnFiringHook(item))
            {
                reason = "a channelled item with its own firing code";
                return false;
            }
            reason = "";
            return true;
        }
        if (item.useStyle == ItemUseStyleID.Swing && !item.noMelee)
        {
            reason = "";
            return true;
        }
        reason = "neither shot nor swung";
        return false;
    }

    /// <summary>
    /// The ammo a weapon that uses ammo fires for free, which is the game's own answer: every ammo
    /// class's <c>AmmoID</c> constant is the item id of that class's first ammo (a wooden arrow is 40,
    /// a musket ball 97, a seed 283, the first rocket 771), and <c>Player.PickAmmo</c> falls back to
    /// exactly this sample when an item needs no ammo held (<c>Terraria.Player.cs</c>,
    /// <c>ContentSamples.ItemsByType[sItem.useAmmo]</c> guarded by <c>item.ammo == sItem.useAmmo</c>). A
    /// modded ammo class whose id is not itself an ammo item of that class has no default, and the
    /// slot refuses the weapon rather than firing nothing.
    /// </summary>
    public static Item? DefaultAmmo(Item weapon)
        => weapon.useAmmo != 0
            && ContentSamples.ItemsByType.TryGetValue(weapon.useAmmo, out Item? ammo)
            && ammo.ammo == weapon.useAmmo
            ? ammo : null;

    /// <summary>
    /// Whether a modded item overrides <c>ModItem.Shoot</c>, which is where an item whose firing is
    /// code rather than stats puts that code. A vanilla item has no ModItem and never trips this. The
    /// check is by declaring type of the override, so a mod that overrides it to return the default
    /// still reads as having its own hook: the companion cannot tell the two apart without running it,
    /// and running it is what the ruling forbids. Hooks a mod adds through GlobalItem are outside what
    /// this can see, and that is a known limit rather than a promise.
    /// </summary>
    private static bool HasOwnFiringHook(Item item)
    {
        if (item.ModItem == null)
            return false;
        MethodInfo? shoot = item.ModItem.GetType().GetMethod("Shoot", BindingFlags.Instance | BindingFlags.Public,
            new[] { typeof(Player), typeof(EntitySource_ItemUse_WithAmmo), typeof(Vector2), typeof(Vector2), typeof(int), typeof(int), typeof(float) });
        return shoot != null && shoot.DeclaringType != typeof(ModItem);
    }

    public TagCompound Save()
    {
        var tag = new TagCompound();
        for (int i = 0; i < SlotCount; i++)
            if (!Slots[i].IsAir)
                tag[((GearSlot)i).ToString()] = ItemIO.Save(Slots[i]);
        return tag;
    }

    /// <summary>
    /// Load what was saved, whatever it is now. An item that no longer passes its slot's predicate —
    /// a mod unloaded since, so its item loads as the loader's placeholder with no damage — stays in
    /// the slot rather than being thrown away, because the placeholder carries the player's item and
    /// gives it back when the mod returns; the arsenal re-runs the predicate when it enumerates, so
    /// an idle item costs nothing, and the slot dims it so the player sees why it is idle.
    /// </summary>
    public void Load(TagCompound tag)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            string key = ((GearSlot)i).ToString();
            Slots[i] = tag.ContainsKey(key) ? ItemIO.Load(tag.GetCompound(key)) : new Item();
        }
        ObserveMutation();
    }
}
