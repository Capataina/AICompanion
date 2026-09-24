#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
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

    public Brain.Brain Brain { get; private set; } = new();
    public CompanionMotor Motor { get; private set; } = null!;
    public CompanionCombat Combat { get; } = new();
    public TileChopper Chopper { get; } = new();
    public TileMiner Miner { get; }
    public TorchBearer Torch { get; } = new();
    public DoorOpener Doors { get; } = new();
    public CompanionInventory Bag => Main.LocalPlayer.GetModPlayer<CompanionPlayer>().Bag;

    /// <summary>The player hostiles aim at; also what the game's own pick routine runs on.</summary>
    public HostileTargetStandIn StandIn { get; } = new();

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
        Combat.Planner.Bind(NPC);
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
        // The engine leaves the body to the motor: no gravity, and no tile collision. Every liquid is air to this body, by
        // the owner's ruling of 15 September 2026, and the engine already agrees for a body with tile collision off:
        // `NPC.UpdateNPC_Inner` calls `UpdateCollision` only when `noTileCollide` is false, and that one method is where the
        // engine detects liquid, slows a wet NPC through `Collision_MoveWhileWet`, strikes it in lava and applies the shimmer
        // buff whose tick sets `shimmering` and ends in `GetShimmered`; the wet gravity ladder is skipped by `noGravity`.
        // The speed factors, the lava immunity and the shimmer buff immunity say the same thing to any engine path that reads
        // them outside that step, so the ruling does not rest only on a flag set for a different reason.
        NPC.noGravity = true;
        NPC.noTileCollide = true;
        NPC.waterMovementSpeed = NPC.lavaMovementSpeed = NPC.honeyMovementSpeed = NPC.shimmerMovementSpeed = 1f;
        NPC.lavaImmune = true;
        NPC.buffImmune[BuffID.Shimmer] = true;
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
        // Before anything of the companion's runs, so a recorded session holds the world exactly as this tick
        // found it: a replay puts it back and asks the same tick again. Inert unless a recording is open.
        global::AICompanion.Companion.Brain.Infrastructure.Diagnostics.ReplayInputs.BeforeTheCompanionTick(this);
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
            // If anything during the brain tick downed the companion, the downed branch takes over
            // next tick, and this tick's remaining work is skipped.
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
        global::AICompanion.Companion.Brain.Infrastructure.Diagnostics.ReplayInputs.AfterTheCompanionTick(this);
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
    public CompanionMana Mana { get; } = new();

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
            if (item.IsAir || item.noGrabDelay > 0)
                continue;
            // Hearts, mana stars and the like are consumed on touch by the player, never stored;
            // the companion leaves them for the player they heal.
            if (ItemID.Sets.IsAPickup[item.type])
                continue;
            var owner = Brain.Activity;
            // Arrival then hovers inside SettleRadius, which is larger than the prove slack. A gel the
            // walk proved then sat on for 22 s of Arrived without a transfer was the body drifting
            // outside PickupReach of a pose that still counted as arrived.
            Rectangle itemReach = reach;
            if (owner.AttemptOpen && owner.Current is global::AICompanion.Companion.Brain.Activities.NearbyAssistance.CollectNearbyItems walked
                && walked.ClaimsDrop(item))
                itemReach.Inflate((int)Navigator.SettleRadius, (int)Navigator.SettleRadius);
            if (!item.Hitbox.Intersects(itemReach))
                continue;
            Item snapshot = item.Clone();
            int before = item.stack;
            // Asked before the transfer, while the drop is still the object the attempt walked to; after it a whole stack is air.
            long claimingAttempt = owner.AttemptOpen && owner.Current is global::AICompanion.Companion.Brain.Activities.NearbyAssistance.CollectNearbyItems collect
                && collect.ClaimsDrop(item) ? owner.AttemptId : 0;
            if (Bag.Collect(item, player))
            {
                global::AICompanion.Companion.Brain.Infrastructure.Observation.CollectNativeEffectReceipts.RecordEffect("cargo-transfer",
                    snapshot.whoAmI, -1, before - (item.IsAir ? 0 : item.stack),
                    global::AICompanion.Companion.Brain.Infrastructure.Observation.NativeEffectAttribution.Unknown, snapshot.type);
                global::AICompanion.Companion.Brain.Infrastructure.Diagnostics.GodsEyeEvents.RecordPickup(NPC, snapshot, before - (item.IsAir ? 0 : item.stack), "player-stacks-or-companion-bag", claimingAttempt);
            }
        }
    }

    // ---- drawing ----

    public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
    {
        DrawTheOrb.Draw(spriteBatch, screenPos, drawColor, this);
        return false;
    }
}
