#nullable enable

using AICompanion.Companion.Inventory;
using AICompanion.Companion.PlayerIntegration;
using AICompanion.Companion.Brain.Activities;
using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>Immutable facts from one handed item; no decision snapshot retains an <see cref="Item"/> reference.</summary>
public readonly record struct ObservedGearCapability(int Type, int Prefix, int Stack, int Damage, int PickPower,
    int AxePower, int HammerPower, int UseTime, int UseAnimation, int Shoot, float ShootSpeed, int Mana, int UseAmmo,
    int UseStyle, bool NoMelee, bool Channel, int Width, float Scale, float KnockBack, bool Consumable, int DamageClassType)
{
    public static ObservedGearCapability From(Item item) => new(item.type, item.prefix, item.stack, item.damage,
        item.pick, item.axe, item.hammer, item.useTime, item.useAnimation, item.shoot, item.shootSpeed, item.mana,
        item.useAmmo, item.useStyle, item.noMelee, item.channel, item.width, item.scale, item.knockBack, item.consumable,
        item.DamageType.Type);
}

/// <summary>Frozen cargo slot facts. The immutable list in a decision fact never shares the bag's mutable item array.</summary>
public readonly record struct ObservedCargoSlot(int Type, int Prefix, int Stack, int MaxStack);

/// <summary>Frozen policy values, including copies of mining-list membership rather than a live list reference.</summary>
public readonly record struct ObservedPolicyCapabilities(WorkPolicy Mining, WorkPolicy Chopping, bool Combat,
    bool PotBreaking, bool TorchPlacement, MiningListMode MiningListMode,
    int MiningListRevision, System.Collections.Generic.IReadOnlyList<int> KnownOres,
    System.Collections.Generic.IReadOnlyList<int> MarkedOres);

/// <summary>Immutable capability and policy facts scoped to one world epoch.</summary>
public readonly record struct ObservedDecisionCapabilities(long WorldEpoch, long CapabilityRevision, long PolicyRevision,
    ObservedGearCapability FirstWeapon, ObservedGearCapability SecondWeapon, ObservedGearCapability Pickaxe,
    ObservedGearCapability Axe, System.Collections.Generic.IReadOnlyList<ObservedCargoSlot> Cargo,
    int CargoSlots, int CargoOccupiedSlots, int CargoEmptySlots, ObservedPolicyCapabilities Policy);

/// <summary>
/// Monotonic revisions tell a decision which observed capability or policy surface changed. Gear owns its source version;
/// policy preferences remain public, so their revision is derived from actual semantic content at observation time.
/// </summary>
public sealed class ObserveDecisionCapabilities
{
    public long CapabilityRevision { get; private set; }
    public long PolicyRevision { get; private set; }
    public long EncounterRevision { get; private set; }
    private bool observed;
    private long worldEpoch;
    private CompanionGear? gearOwner;
    private CompanionInventory? cargoOwner;
    private CompanionPreferences? preferenceOwner;
    private long gearVersion;
    private CargoContent[] cargoContent = System.Array.Empty<CargoContent>();
    private PolicyContent policyContent;

    public ObservedDecisionCapabilities Observe(long worldEpoch, CompanionGear gear, CompanionInventory cargo,
        CompanionPreferences preferences)
    {
        if (observed && this.worldEpoch != worldEpoch) Reset();
        gear.ObserveMutation();
        CargoContent[] nextCargo = CaptureCargo(cargo);
        PolicyContent nextPolicy = PolicyContent.From(preferences);
        if (observed)
        {
            if (!ReferenceEquals(gearOwner, gear) || !ReferenceEquals(cargoOwner, cargo)
                || gearVersion != gear.MutationVersion || !SameCargo(cargoContent, nextCargo)) CapabilityRevision++;
            if (!ReferenceEquals(preferenceOwner, preferences) || policyContent != nextPolicy) PolicyRevision++;
        }
        observed = true;
        this.worldEpoch = worldEpoch;
        gearOwner = gear;
        cargoOwner = cargo;
        preferenceOwner = preferences;
        gearVersion = gear.MutationVersion;
        cargoContent = nextCargo;
        policyContent = nextPolicy;

        int occupied = 0;
        foreach (Item item in cargo.Items)
            if (!item.IsAir) occupied++;
        var frozenCargo = System.Array.AsReadOnly(CopyCargo(nextCargo));
        var policy = FreezePolicy(preferences);
        return new ObservedDecisionCapabilities(worldEpoch, CapabilityRevision, PolicyRevision,
            ObservedGearCapability.From(gear[GearSlot.FirstWeapon]), ObservedGearCapability.From(gear[GearSlot.SecondWeapon]),
            ObservedGearCapability.From(gear.Pickaxe), ObservedGearCapability.From(gear.Axe), frozenCargo,
            CompanionInventory.Slots, occupied, CompanionInventory.Slots - occupied, policy);
    }

    public void CapabilityChanged() => CapabilityRevision++;
    public void PolicyChanged() => PolicyRevision++;
    public void EncounterChanged() => EncounterRevision++;
    public void Reset()
    {
        (CapabilityRevision, PolicyRevision, EncounterRevision) = (0, 0, 0);
        observed = false;
        worldEpoch = 0;
        gearOwner = null;
        cargoOwner = null;
        preferenceOwner = null;
        gearVersion = 0;
        cargoContent = System.Array.Empty<CargoContent>();
        policyContent = default;
    }

    private readonly record struct CargoContent(int Type, int Prefix, int Stack, int MaxStack);
    private readonly record struct PolicyContent(WorkPolicy Mining, WorkPolicy Chopping, bool Combat,
        bool PotBreaking, bool TorchPlacement, object MiningList, int Revision)
    {
        public static PolicyContent From(CompanionPreferences preferences) => new(preferences.Mining, preferences.Chopping,
            preferences.Combat, preferences.PotBreaking, preferences.TorchPlacement,
            preferences.MiningList, preferences.MiningList.Revision);
    }

    private static CargoContent[] CaptureCargo(CompanionInventory cargo)
    {
        var content = new CargoContent[cargo.Items.Length];
        for (int i = 0; i < content.Length; i++)
        {
            Item item = cargo.Items[i];
            content[i] = new CargoContent(item.type, item.prefix, item.stack, item.maxStack);
        }
        return content;
    }

    private static ObservedCargoSlot[] CopyCargo(CargoContent[] content)
    {
        var copy = new ObservedCargoSlot[content.Length];
        for (int i = 0; i < copy.Length; i++) copy[i] = new(content[i].Type, content[i].Prefix, content[i].Stack, content[i].MaxStack);
        return copy;
    }

    private static ObservedPolicyCapabilities FreezePolicy(CompanionPreferences preferences)
    {
        var list = preferences.MiningList.Snapshot();
        return new(preferences.Mining, preferences.Chopping, preferences.Combat, preferences.PotBreaking, preferences.TorchPlacement,
            list.Mode, list.Revision, System.Array.AsReadOnly(list.Known), System.Array.AsReadOnly(list.Marked));
    }

    private static bool SameCargo(CargoContent[] a, CargoContent[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
