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
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Doors;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Mining;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Torch;

namespace AICompanion.Companion.CharacterBody;

/// <summary>
/// The companion: a friendly NPC with a custom AI (aiStyle -1) whose every decision is made by
/// <see cref="Brain.Brain"/>. Its body is a twenty-pixel flying orb: the engine's gravity and tile
/// collision are switched off for it, and the motor moves it through the mod's own circle contact.
/// The engine's box still exists and is what enemies and projectiles hit, so it is targeted, takes
/// damage, is downed and dodges exactly as any NPC. This class owns what the brain needs a body for:
/// health that mirrors the player, the downed state, the presentation snapshot the renderer reads,
/// the motor, the weapons, the tools, the bag, and the stand-in player hostiles aim at.
///
/// It never dies. At zero life it is downed: it sinks to rest, lies still and takes no damage until
/// the player has stood within reach for <see cref="ReviveTicks"/> ticks or <see cref="SelfReviveTicks"/> pass.
/// </summary>
public class CompanionNPC : ModNPC
{
    private const float ReviveDistance = 48f;
    private const int ReviveTicks = 180;

    /// <summary>
    /// How long the companion lies there before getting up on its own. The player standing over it
    /// is the fast route and stays worth taking at three seconds; this is the floor that stops a
    /// down from ending the companion's participation in the session. A player who dies respawns
    /// without anyone's help, and on 2026-09-11 one down cost 6,100 ticks on the floor — a hundred
    /// seconds of a ten-minute session — because the player had walked on before it happened.
    /// </summary>
    private const int SelfReviveTicks = 600;
    private const float PickupReach = 28f;

    /// <summary>How long the drill beam stays drawn after a swing, so it holds through the cadence of the cuts.</summary>
    private const int BeamHoldTicks = 24;

    /// <summary>A liquid's hurt on contact: this much, every this many ticks of contact.</summary>
    public readonly record struct LiquidHurt(int Damage, int IntervalTicks);

    /// <summary>
    /// The two hurts, the one place they live. Water is gentle enough that a few tiles of crossing
    /// is survivable on a starter life pool: two life a third of a second is six a second, so a
    /// three-second crossing costs under a fifth of a hundred. Lava is lethal within a few seconds
    /// whatever the pool: twenty-five every tenth of a second, after defence, empties four hundred
    /// life in under two seconds.
    /// </summary>
    public static readonly LiquidHurt WaterHurt = new(2, 20);
    public static readonly LiquidHurt LavaHurt = new(25, 6);

    public Brain.Brain Brain { get; private set; } = new();
    public CompanionMotor Motor { get; private set; } = null!;
    public Arsenal Arsenal { get; } = new();
    public TileChopper Chopper { get; } = new();
    public TileMiner Miner { get; }
    public TorchBearer Torch { get; } = new();
    public DoorOpener Doors { get; } = new();
    public CompanionInventory Bag => Main.LocalPlayer.GetModPlayer<CompanionPlayer>().Bag;

    /// <summary>The player hostiles aim at; also what the game's own pick routine runs on.</summary>
    public HostileTargetStandIn StandIn { get; } = new();

    /// <summary>When true the matching liquid is neither a wall to the planner nor a hurt to the body. The mastery tree flips them.</summary>
    public bool ImmuneToWater { get; set; }
    public bool ImmuneToLava { get; set; }

    /// <summary>Scale the player-derived speed cap and acceleration; the mastery tree drives them, one is the body as handed over.</summary>
    public float SpeedMultiplier { get; set; } = 1f;
    public float AccelerationMultiplier { get; set; } = 1f;

    public CompanionNPC()
    {
        Miner = new TileMiner(StandIn.Player);
    }

    public bool IsDowned { get; private set; }
    public int RevivePercent => IsDowned
        ? (reviveProgress > 0 ? reviveProgress * 100 / ReviveTicks : downedTicks * 100 / SelfReviveTicks)
        : 0;

    private int reviveProgress;
    private int downedTicks;
    private bool loggedFirstTick;
    private int heldItemType;
    private int itemAnimation;
    private int itemAnimationMax;
    private float aimRotation;
    private Vector2? beamTarget;
    private int beamHold;

    public override string Texture => "Terraria/Images/NPC_" + NPCID.Probe;

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

    /// <summary>Spawn a companion in the air a couple of tiles above the player's head; returns the NPC slot, or Main.maxNPCs if none was free.</summary>
    public static int Spawn(Player player)
        => NPC.NewNPC(player.GetSource_Misc("companion"), (int)player.Center.X, (int)(player.position.Y - 32f), ModContent.NPCType<CompanionNPC>());

    public override void SetStaticDefaults()
    {
        Main.npcFrameCount[Type] = 1;
        NPCID.Sets.NPCBestiaryDrawModifiers hide = new() { Hide = true };
        NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, hide);
    }

    public override void SetDefaults()
    {
        // The engine's box is the contact circle's diameter on both sides: it is what enemies and
        // projectiles hit, and a box a different size from the circle is hit where the body is not.
        NPC.width = (int)CircleContact.Diameter;
        NPC.height = (int)CircleContact.Diameter;
        NPC.aiStyle = -1;
        NPC.friendly = true;
        NPC.damage = 0;
        NPC.defense = 0;
        NPC.lifeMax = 100;
        NPC.life = 100;
        // 1 is full knockback and 0 is immunity in this game; a light walker like a zombie is 0.5.
        NPC.knockBackResist = 0.75f;
        NPC.dontTakeDamage = false;
        NPC.HitSound = SoundID.NPCHit4;
        NPC.DeathSound = SoundID.NPCDeath14;
        NPC.value = 0f;
        // The engine leaves the body to the motor: no gravity, and no tile collision, which also
        // switches off the engine's own liquid detection and liquid slowdown for this body. The
        // motor does all three through the circle contact. The liquid speed factors are set to one
        // so that, should any engine path still read them, nothing is slowed twice.
        NPC.noGravity = true;
        NPC.noTileCollide = true;
        NPC.waterMovementSpeed = NPC.lavaMovementSpeed = NPC.honeyMovementSpeed = NPC.shimmerMovementSpeed = 1f;
        Motor = new CompanionMotor(this);
    }

    /// <summary>Never culled for being far from players; the companion manages its own distance.</summary>
    public override bool CheckActive() => false;

    /// <summary>Zero life downs the companion instead of killing it.</summary>
    public override bool CheckDead()
    {
        EnterDowned();
        return false;
    }

    public override void HitEffect(NPC.HitInfo hit)
    {
        Motor?.NotifyExternalHit();
        global::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry.RecordCompanionHit(hit);
    }

    public override void AI()
    {
        Player player = Main.LocalPlayer;
        MirrorStats(player);
        // A hand claim lasts one AI tick. Every consumer below reads the freshly resolved item;
        // a previous torch or weapon must not occupy its own fallback's hand on the next tick.
        heldItemType = ItemID.None;
        // `Torch.Shown` is deliberately not cleared here, and that is the whole of what the light
        // sense needs from this method. Between here and `Torch.Update` the brain runs, and the
        // sense asks whether the companion's own torch was out — a question about the light map it
        // is reading, which the previous tick's `AddLight` wrote. Clearing the answer before the
        // question made it permanently "no", so the sense never removed the companion's own glow,
        // read its own torchlight as room light, and put the torch out in the dark. The state is
        // recomputed at `Torch.Update` below; the paths that never reach it hide explicitly.
        if (itemAnimation > 0)
            itemAnimation--;
        if (beamHold > 0 && --beamHold == 0)
            beamTarget = null;
        Miner.Tick();
        // What the engine did with the last tick, read before anything acts on this one: whether
        // the body is being held in place against the move it was handed.
        Motor.Track();

        if (IsDowned)
        {
            UpdateDowned(player);
            // Nothing recomputes the torch on this path, so it is cleared here rather than ahead of
            // the brain: a downed companion shows no torch, and next tick's sense must read that.
            Torch.Hide();
        }
        else
        {
            Brain.Tick(this, player);
            // A liquid strike inside the motor can down the companion mid-tick; the downed branch
            // takes over next tick, and this tick's remaining work is skipped.
            if (!IsDowned)
            {
                // After the steps, because the direction the door swings is the direction the brain
                // asked the motor for this tick, and before anything reads the tiles again: a door
                // the body just opened is an opening the rest of the tick can use.
                Doors.Tick(NPC);
                CollectTouchedItems(player);
                // The torch takes the hand only when no action claimed it this tick: a tool or a
                // weapon held by chop, mine, hunt or guard always wins, and a torch that is not
                // in the hand gives no light and reveals nothing.
                Torch.Update(Brain.Senses.Light, NPC, heldItemType == ItemID.None,
                    Brain.Senses.Player.Predict(global::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.TorchHeadingLeadTicks));
            }
            else Torch.Hide();
            if (Torch.Shown)
                heldItemType = ItemID.Torch;
            if (!loggedFirstTick)
            {
                loggedFirstTick = true;
                Mod.Logger.Info($"CompanionNPC: first brain tick ran, action={Brain.LastAction?.Name ?? "-"} at tile {(int)(NPC.Center.X / 16)},{(int)(NPC.Center.Y / 16)}");
            }
        }

        UpdateFacing();
        StandIn.Sync(NPC, IsDowned);
        global::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry.Record(this);
    }

    // ---- presentation: what the renderer draws, recorded by whoever acts ----

    /// <summary>What the hand holds this tick, for the telemetry and the HUD.</summary>
    public int HeldItemType => heldItemType;

    /// <summary>The angle the sprite faces, in radians: the direction of travel while moving, the last one while still.</summary>
    public float Facing { get; private set; }

    /// <summary>The angle of the last aimed shot, recorded for the presentation; nothing draws it yet.</summary>
    public float AimRotation => aimRotation;

    /// <summary>The tile the drill beam is drawn to while tool work is running, or null.</summary>
    public Vector2? BeamTarget => beamTarget;

    /// <summary>Record the held item into the presentation snapshot. Nothing draws it yet: the owner
    /// wants held-weapon-versus-projectiles-only tested later, so the API stays and the drawing waits.</summary>
    public void SetHeldItem(int itemType) => heldItemType = itemType;

    /// <summary>The name the activities already call; the same recording as <see cref="SetHeldItem"/>.</summary>
    public void HoldItem(int itemType) => heldItemType = itemType;

    public void StartAnimation(int itemType, int ticks)
    {
        heldItemType = itemType;
        itemAnimation = itemAnimationMax = Math.Max(1, ticks);
    }

    public void SetAimRotation(Vector2 launch)
        => aimRotation = MathF.Atan2(launch.Y, launch.X);

    /// <summary>Draw the beam to a worked tile for a short hold; each swing refreshes it.</summary>
    public void ShowBeam(Vector2 worldPoint)
    {
        beamTarget = worldPoint;
        beamHold = BeamHoldTicks;
    }

    private void UpdateFacing()
    {
        Vector2 velocity = NPC.velocity;
        if (velocity.LengthSquared() > 0.25f)
            Facing = MathF.Atan2(velocity.Y, velocity.X);
    }

    // ---- health and mana ----

    /// <summary>
    /// The companion's mana pool, mirrored from the player like life and defence and spent by the
    /// weapons; the notch reads its fraction. Session state, not saved.
    /// </summary>
    public Weapons.CompanionMana Mana { get; } = new();

    private void MirrorStats(Player player)
    {
        int newMax = Math.Max(1, player.statLifeMax2);
        if (newMax != NPC.lifeMax)
        {
            NPC.life = Math.Clamp(NPC.life + (newMax - NPC.lifeMax), 1, newMax);
            NPC.lifeMax = newMax;
        }
        NPC.defense = player.statDefense;
        Mana.Sync(player);
        Mana.Tick();
    }

    private void EnterDowned()
    {
        IsDowned = true;
        NPC.life = 1;
        NPC.dontTakeDamage = true;
        Motor.EnterDowned();
        Brain.FollowRecovery.Update(false, true, false, NPC.Center, NPC.Center, false);
        reviveProgress = 0;
        downedTicks = 0;
        heldItemType = ItemID.None;
        itemAnimation = 0;
        beamTarget = null;
        Brain.Movement.Hold(Motor.State, preemptedBy: "downed");
    }

    private void UpdateDowned(Player player)
    {
        Brain.ApplyDownedControls(this);
        bool playerBeside = !player.dead && Vector2.Distance(player.Center, NPC.Center) <= ReviveDistance;
        reviveProgress = playerBeside ? reviveProgress + 1 : Math.Max(0, reviveProgress - 2);
        if (reviveProgress < ReviveTicks && ++downedTicks < SelfReviveTicks)
            return;
        IsDowned = false;
        NPC.life = NPC.lifeMax;
        NPC.dontTakeDamage = false;
        Motor.LeaveDowned();
        reviveProgress = 0;
        downedTicks = 0;
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
            // Asked before the transfer, while the drop is still the object the attempt walked to; after it a whole stack is air.
            var owner = Brain.Chooser.Activity;
            long claimingAttempt = owner.AttemptOpen && owner.Current is global::AICompanion.Companion.Brain.Activities.NearbyAssistance.CollectNearbyItems collect
                && collect.ClaimsDrop(item) ? owner.AttemptId : 0;
            if (Bag.Collect(item, player))
                global::AICompanion.Companion.Brain.Infrastructure.Diagnostics.GodsEyeEvents.RecordPickup(NPC, snapshot, before - (item.IsAir ? 0 : item.stack), "player-stacks-or-companion-bag", claimingAttempt);
        }
    }

    // ---- drawing ----

    public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
    {
        DrawTheOrb.Draw(spriteBatch, screenPos, drawColor, this);
        return false;
    }
}
