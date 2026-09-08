#nullable enable

using Terraria;

namespace AICompanion.Companion;

/// <summary>
/// The companion breathes like a player. NPCs have no breath in the game, so this is
/// the player's own rule (Player.CheckDrowning) run on the NPC's box: while the head is
/// under liquid a countdown ticks and each time it completes one unit of breath goes;
/// at zero breath every completed countdown costs life instead, through the NPC's own
/// strike so zero life downs the companion the way any hit would; out of liquid breath
/// comes back several units a tick. Lava and honey do not count as drowning, as for the
/// player. The sense reads these numbers; nothing else writes them.
/// </summary>
public sealed class CompanionBreath
{
    public const int BreathMax = 200;
    public const int BreathCDMax = 7;
    public const int DrownDamage = 2;
    public const int RecoverPerTick = 3;

    public int Breath { get; private set; } = BreathMax;
    public bool HeadUnderwater { get; private set; }
    private int breathCD;

    public float Fraction => Breath / (float)BreathMax;

    /// <summary>Ticks until the first drowning damage at the current rate, from now.</summary>
    public int TicksLeft => Breath * BreathCDMax - breathCD;

    public void Update(NPC npc)
    {
        HeadUnderwater = npc.wet && !npc.lavaWet && !npc.honeyWet && Collision.DrownCollision(npc.position, npc.width, npc.height, 1f);
        if (!HeadUnderwater)
        {
            Breath = System.Math.Min(BreathMax, Breath + RecoverPerTick);
            breathCD = 0;
            return;
        }
        if (++breathCD < BreathCDMax)
            return;
        breathCD = 0;
        if (Breath > 0)
        {
            Breath--;
            return;
        }
        npc.SimpleStrikeNPC(DrownDamage, 0, noPlayerInteraction: true);
    }

    public void Reset()
    {
        Breath = BreathMax;
        breathCD = 0;
    }
}
