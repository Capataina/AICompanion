#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Companion.Weapons;
using AICompanion.Companion.Inventory;
using AICompanion.Companion.PlayerIntegration;
using AICompanion.Companion.Brain.WorldInteractions.Chopping;
using AICompanion.Companion.Brain.WorldInteractions.Doors;
using AICompanion.Companion.Brain.WorldInteractions.Mining;
using AICompanion.Companion.Brain.WorldInteractions.Torch;

namespace AICompanion.Companion.CharacterBody;

/// <summary>
/// The companion: a friendly NPC with a custom AI (aiStyle -1) whose every decision
/// is made by <see cref="Brain.Brain"/>. This class owns what the brain needs a body
/// for: health that mirrors the player, the downed state, the held item and its
/// animation, the motor, the weapons, the chopper, the bag, and drawing through the
/// game's own player renderer with the Guide sheet as the fallback.
///
/// It never dies. At zero life it is downed: after clearing any interrupted recovery flight
/// from solid terrain, it lies still and takes no damage until
/// the player has stood within reach for <see cref="ReviveTicks"/> ticks.
/// </summary>
public class CompanionNPC : ModNPC
{
    private const float ReviveDistance = 48f;
    private const int ReviveTicks = 180;
    private const float PickupReach = 28f;

    public Brain.Brain Brain { get; private set; } = new();
    public CompanionMotor Motor { get; private set; } = null!;
    public Arsenal Arsenal { get; } = new();
    public TileChopper Chopper { get; } = new();
    public TileMiner Miner { get; }
    public TorchBearer Torch { get; } = new();
    public CompanionBreath Breath { get; } = new();
    public DoorOpener Doors { get; } = new();
    public CompanionInventory Bag => Main.LocalPlayer.GetModPlayer<CompanionPlayer>().Bag;

    /// <summary>The drawing-only body; the map layer draws its head.</summary>
    public CompanionBody Body => body;

    private readonly CompanionBody body = new();

    public CompanionNPC()
    {
        Miner = new TileMiner(body.Player);
    }

    public bool IsDowned { get; private set; }
    public int RevivePercent => reviveProgress * 100 / ReviveTicks;

    private int reviveProgress;
    private bool loggedFirstTick;
    private int heldItemType;
    private int itemAnimation;
    private int itemAnimationMax;
    private float itemRotation;

    public override string Texture => "Terraria/Images/NPC_" + NPCID.Guide;

    /// <summary>The one live companion, or null.</summary>
    public static NPC? Find()
    {
        int type = ModContent.NPCType<CompanionNPC>();
        foreach (NPC npc in Main.ActiveNPCs)
            if (npc.type == type)
                return npc;
        return null;
    }

    public static CompanionNPC? Instance => Find()?.ModNPC as CompanionNPC;

    /// <summary>Spawn a companion at the player's feet; returns the NPC slot, or Main.maxNPCs if none was free.</summary>
    public static int Spawn(Player player)
        => NPC.NewNPC(player.GetSource_Misc("companion"), (int)player.Center.X, (int)player.Bottom.Y, ModContent.NPCType<CompanionNPC>());

    public override void SetStaticDefaults()
    {
        Main.npcFrameCount[Type] = Main.npcFrameCount[NPCID.Guide];
        NPCID.Sets.NPCBestiaryDrawModifiers hide = new() { Hide = true };
        NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, hide);
    }

    public override void SetDefaults()
    {
        // The player's own hitbox (Player.defaultWidth and defaultHeight), because the
        // companion is drawn as a player and is meant to fit wherever the player fits.
        // BodyPhysics.Width and Height are the same two numbers and must stay equal to
        // these: the planner proves every move against that box and the game moves this
        // one, so changing either alone plans routes the body cannot walk.
        NPC.width = 20;
        NPC.height = 42;
        NPC.aiStyle = -1;
        NPC.friendly = true;
        NPC.damage = 0;
        NPC.defense = 0;
        NPC.lifeMax = 100;
        NPC.life = 100;
        // 1 is full knockback and 0 is immunity in this game; a light walker like a zombie is 0.5.
        NPC.knockBackResist = 0.75f;
        NPC.dontTakeDamage = false;
        NPC.HitSound = SoundID.NPCHit1;
        NPC.DeathSound = SoundID.NPCDeath1;
        NPC.value = 0f;
        // SimulateTerrariaBody previews these ordinary NPC liquid multipliers. Keep the
        // companion's defaults explicit so an inherited NPC type cannot change its physics.
        NPC.waterMovementSpeed = NPC.lavaMovementSpeed = 0.5f;
        NPC.honeyMovementSpeed = 0.25f;
        NPC.shimmerMovementSpeed = 0.375f;
        Motor = new CompanionMotor(NPC);
    }

    /// <summary>Never culled for being far from players; the companion manages its own distance.</summary>
    public override bool CheckActive() => false;

    /// <summary>
    /// The body passes through the platform it stands on only on a tick the navigator asked
    /// for it (a fall-through step of the path), the way a player presses down; the flag is
    /// cleared here so a request never outlives its tick.
    /// </summary>
    public override bool? CanFallThroughPlatforms()
    {
        bool wants = Motor.WantsFallThrough;
        Motor.WantsFallThrough = false;
        return wants ? true : null;
    }

    /// <summary>Zero life downs the companion instead of killing it.</summary>
    public override bool CheckDead()
    {
        EnterDowned();
        return false;
    }

    public override void HitEffect(NPC.HitInfo hit)
    {
        Motor?.NotifyExternalHit();
        global::AICompanion.Companion.Brain.BehaviourDiagnostics.BrainTelemetry.RecordCompanionHit(hit);
    }

    public override void AI()
    {
        Player player = Main.LocalPlayer;
        MirrorStats(player);
        // A hand claim lasts one AI tick. Every consumer below reads the freshly resolved item;
        // a previous torch or weapon must not occupy its own fallback's hand on the next tick.
        heldItemType = ItemID.None;
        Torch.Hide();
        if (itemAnimation > 0)
            itemAnimation--;
        Miner.Tick();
        // What the engine did with the last tick, read before anything acts on this one: whether
        // the body is being held in place, and how far the offline motion rule's prediction of
        // where it would be missed. Both describe the tick before, because the engine's collision
        // and its position += velocity run after AI returns.
        Motor.Track();

        if (IsDowned)
        {
            UpdateDowned(player);
        }
        else
        {
            // Breath before the brain, so the senses read this tick's value; a drowning strike
            // here can down the companion, and the downed branch takes over next tick.
            Breath.Update(NPC);
            if (IsDowned)
                UpdateDowned(player);
            else
                Brain.Tick(this, player);
            // After the steps, because the direction the door swings is the direction the brain
            // asked the motor for this tick, and before anything reads the tiles again: a door the
            // body just opened is an opening the rest of the tick can use.
            if (!IsDowned)
            {
                Doors.Tick(NPC);
                CollectTouchedItems(player);
            }
            // The torch takes the hand only when no action claimed it this tick: a tool or a
            // weapon held by chop, mine, hunt or guard always wins, and a torch that is not
            // in the hand gives no light and reveals nothing.
            if (!IsDowned) Torch.Update(Brain.Senses.Light, NPC, heldItemType == ItemID.None);
            if (Torch.Shown)
                heldItemType = ItemID.Torch;
            if (!loggedFirstTick)
            {
                loggedFirstTick = true;
                Mod.Logger.Info($"CompanionNPC: first brain tick ran, action={Brain.LastAction?.Name ?? "-"} at tile {(int)(NPC.Center.X / 16)},{(int)(NPC.Center.Y / 16)}");
            }
        }

        body.Sync(NPC, player, heldItemType, itemAnimation, itemAnimationMax, itemRotation, IsDowned);
        global::AICompanion.Companion.Brain.BehaviourDiagnostics.BrainTelemetry.Record(this);
    }

    /// <summary>What the hand holds this tick, for the telemetry and the HUD.</summary>
    public int HeldItemType => heldItemType;

    // ---- what the brain and its actions call ----

    public void HoldItem(int itemType) => heldItemType = itemType;

    public void StartAnimation(int itemType, int ticks)
    {
        heldItemType = itemType;
        itemAnimation = itemAnimationMax = Math.Max(1, ticks);
    }

    public void SetAimRotation(Vector2 launch)
        => itemRotation = MathF.Atan2(launch.Y * NPC.direction, launch.X * NPC.direction);

    // ---- health ----

    private void MirrorStats(Player player)
    {
        int newMax = Math.Max(1, player.statLifeMax2);
        if (newMax != NPC.lifeMax)
        {
            NPC.life = Math.Clamp(NPC.life + (newMax - NPC.lifeMax), 1, newMax);
            NPC.lifeMax = newMax;
        }
        NPC.defense = player.statDefense;
    }

    private void EnterDowned()
    {
        IsDowned = true;
        NPC.life = 1;
        NPC.dontTakeDamage = true;
        Motor.EnterDowned();
        Brain.FollowRecovery.Update(false, true, false, NPC.Bottom, NPC.Bottom, false);
        reviveProgress = 0;
        heldItemType = ItemID.None;
        itemAnimation = 0;
        Brain.Movement.Hold(Motor.State);
    }

    private void UpdateDowned(Player player)
    {
        Motor.Apply(global::AICompanion.Companion.Brain.SharedMovementSystem.Controls.None, "downed");
        bool playerBeside = !player.dead && Vector2.Distance(player.Center, NPC.Center) <= ReviveDistance;
        reviveProgress = playerBeside ? reviveProgress + 1 : Math.Max(0, reviveProgress - 2);
        if (reviveProgress < ReviveTicks)
            return;
        IsDowned = false;
        NPC.life = NPC.lifeMax;
        NPC.dontTakeDamage = false;
        Breath.Reset();
        reviveProgress = 0;
    }

    // ---- loot on contact ----

    private void CollectTouchedItems(Player player)
    {
        Rectangle reach = NPC.Hitbox;
        reach.Inflate((int)PickupReach, (int)PickupReach);
        foreach (Item item in Main.ActiveItems)
        {
            if (item.IsAir || item.noGrabDelay > 0 || !item.Hitbox.Intersects(reach))
                continue;
            // Hearts, mana stars and the like are consumed on touch by the player, never stored;
            // the companion leaves them for the player they heal.
            if (ItemID.Sets.IsAPickup[item.type])
                continue;
            Item snapshot = item.Clone();
            int before = item.stack;
            if (Bag.Collect(item, player))
                global::AICompanion.Companion.Brain.BehaviourDiagnostics.GodsEyeEvents.RecordPickup(NPC, snapshot, before - (item.IsAir ? 0 : item.stack), "player-stacks-or-companion-bag");
        }
    }

    // ---- drawing ----

    public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
    {
        if (!body.UsesPlayerRenderer)
            return true; // Guide sprite fallback
        float rotation = IsDowned ? NPC.direction * MathHelper.PiOver2 : 0f;

        // The player renderer writes to the device directly and expects a closed batch, which is
        // how vanilla calls it; drawing inside the open NPC batch puts the body behind everything
        // queued in it. Close, draw, then reopen with the NPC batch's own state.
        spriteBatch.End();
        body.Draw(rotation);
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
            DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
        return !body.UsesPlayerRenderer;
    }

    public override void FindFrame(int frameHeight)
    {
        bool walking = MathF.Abs(NPC.velocity.X) > 0.2f && NPC.velocity.Y == 0f;
        if (!walking)
        {
            NPC.frameCounter = 0;
            NPC.frame.Y = 0;
            return;
        }
        NPC.frameCounter += 1.0 + MathF.Abs(NPC.velocity.X) * 0.5;
        int index = 2 + (int)(NPC.frameCounter / 6) % 14;
        NPC.frame.Y = frameHeight * index;
    }
}
