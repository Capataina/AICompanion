#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Content.Behaviours;

namespace AICompanion.Content;

/// <summary>
/// The companion: a friendly NPC with a custom AI (aiStyle -1, so vanilla runs none
/// of its own logic) that lives beside the one player. Each tick it mirrors the
/// player's max life and defence, then runs the first behaviour that wants control,
/// in this priority: Downed, Shoot, Chop, Wander, Follow.
///
/// It never dies. At zero life it is downed: it lies still and takes no damage until
/// the player has stood within reach for <see cref="ReviveTicks"/> ticks, then stands
/// back up at full life.
///
/// The body is the game's own female player renderer via <see cref="CompanionAppearance"/>;
/// the Guide sheet is only the fallback, which is why <see cref="FindFrame"/> still
/// assumes the town-NPC frame layout.
/// </summary>
public class Companion : ModNPC
{
    public enum Behaviour { Follow, Wander, Chop, Shoot, Downed }

    private const float FollowDistance = 64f;
    private const float TeleportDistance = 1400f;
    private const float WalkSpeed = 3.5f;
    private const float Acceleration = 0.25f;
    private const float JumpVelocity = -8.5f;

    /// <summary>How close the player must stand to revive, in pixels, and for how many ticks.</summary>
    private const float ReviveDistance = 48f;
    private const int ReviveTicks = 180;

    public Behaviour Current { get; private set; } = Behaviour.Follow;
    public bool IsDowned => Current == Behaviour.Downed;
    public int RevivePercent => reviveProgress * 100 / ReviveTicks;

    public TileChopper Chopper { get; } = new();
    private readonly BowBehaviour bow = new();
    private readonly WanderBehaviour wander = new();
    private readonly CompanionAppearance appearance = new();

    private int reviveProgress;
    private int heldItemType;
    private int itemAnimation;
    private int itemAnimationMax;
    private float itemRotation;

    public override string Texture => "Terraria/Images/NPC_" + NPCID.Guide;

    /// <summary>The one live companion, or null.</summary>
    public static NPC? Find()
    {
        int type = ModContent.NPCType<Companion>();
        foreach (NPC npc in Main.ActiveNPCs)
            if (npc.type == type)
                return npc;
        return null;
    }

    /// <summary>Spawn a companion at the player's feet; returns the NPC slot, or Main.maxNPCs if none was free.</summary>
    public static int Spawn(Player player)
        => NPC.NewNPC(player.GetSource_Misc("companion"), (int)player.Center.X, (int)player.Bottom.Y, ModContent.NPCType<Companion>());

    public override void SetStaticDefaults()
    {
        Main.npcFrameCount[Type] = Main.npcFrameCount[NPCID.Guide];
        NPCID.Sets.NPCBestiaryDrawModifiers hide = new() { Hide = true };
        NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, hide);
    }

    public override void SetDefaults()
    {
        NPC.width = 20;
        NPC.height = 42;
        NPC.aiStyle = -1;
        NPC.friendly = true;
        NPC.damage = 0;
        NPC.defense = 0;
        NPC.lifeMax = 100;
        NPC.life = 100;
        NPC.knockBackResist = 0f;
        NPC.dontTakeDamage = false;
        NPC.HitSound = SoundID.NPCHit1;
        NPC.DeathSound = SoundID.NPCDeath1;
        NPC.value = 0f;
    }

    /// <summary>Never culled for being far from players; the companion manages its own distance.</summary>
    public override bool CheckActive() => false;

    /// <summary>Zero life downs the companion instead of killing it.</summary>
    public override bool CheckDead()
    {
        EnterDowned();
        return false;
    }

    public override void AI()
    {
        Player player = Main.LocalPlayer;
        MirrorStats(player);
        TickAnimation();

        if (Current == Behaviour.Downed)
        {
            UpdateDowned(player);
            SyncBody();
            return;
        }

        if (player.dead)
        {
            Stop();
            SyncBody();
            return;
        }

        if (Vector2.Distance(player.Center, NPC.Center) > TeleportDistance)
        {
            NPC.Bottom = player.Bottom;
            NPC.velocity = Vector2.Zero;
        }

        if (bow.WantsToShoot(NPC))
            RunShoot(player);
        else if (Chopper.WantsToChop(NPC, player))
            RunChop(player);
        else if (MathF.Abs(player.Center.X - NPC.Center.X) <= WanderBehaviour.Leash && MathF.Abs(player.Center.Y - NPC.Center.Y) < 320f)
            RunWander(player);
        else
            RunFollow(player);

        SyncBody();
    }

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

    private void RunShoot(Player player)
    {
        Current = Behaviour.Shoot;
        // Keep up with the player while fighting; only stand still once close enough.
        if (MathF.Abs(player.Center.X - NPC.Center.X) > WanderBehaviour.Leash)
            MoveToward(player.Center, WalkSpeed);
        else
            Stop();
        if (bow.Update(NPC, player) is Vector2 launch)
        {
            Item bowItem = ContentSamples.ItemsByType[BowBehaviour.BowItemType];
            StartAnimation(BowBehaviour.BowItemType, bowItem.useAnimation);
            itemRotation = MathF.Atan2(launch.Y * NPC.direction, launch.X * NPC.direction);
        }
        else
        {
            heldItemType = BowBehaviour.BowItemType;
        }
    }

    private void RunChop(Player player)
    {
        Current = Behaviour.Chop;
        Item axe = TileChopper.AxeFor(player);
        heldItemType = axe.type;
        if (Chopper.Update(NPC, player, out bool swung))
        {
            Stop();
            if (swung)
                StartAnimation(axe.type, axe.useAnimation);
            return;
        }
        if (Chopper.Target is TreeFinder.ChoppableTree tree)
            MoveToward(tree.StandPosition, WalkSpeed);
    }

    private void RunWander(Player player)
    {
        Current = Behaviour.Wander;
        heldItemType = ItemID.None;
        (float speedX, bool hop) = wander.Update(NPC, player);
        if (speedX == 0f)
        {
            Stop();
        }
        else
        {
            NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, speedX, Acceleration);
            NPC.direction = NPC.spriteDirection = speedX > 0f ? 1 : -1;
            if (OnGround && NPC.collideX)
                NPC.velocity.Y = JumpVelocity;
        }
        if (hop && OnGround)
            NPC.velocity.Y = JumpVelocity * 0.7f;
    }

    private void RunFollow(Player player)
    {
        Current = Behaviour.Follow;
        heldItemType = ItemID.None;
        if (MathF.Abs(player.Center.X - NPC.Center.X) > FollowDistance)
            MoveToward(player.Center, WalkSpeed);
        else
            Stop();
    }

    private void EnterDowned()
    {
        Current = Behaviour.Downed;
        NPC.life = 1;
        NPC.dontTakeDamage = true;
        NPC.velocity.X = 0f;
        reviveProgress = 0;
        heldItemType = ItemID.None;
        itemAnimation = 0;
    }

    private void UpdateDowned(Player player)
    {
        NPC.velocity.X *= 0.8f;
        bool playerBeside = !player.dead && Vector2.Distance(player.Center, NPC.Center) <= ReviveDistance;
        reviveProgress = playerBeside ? reviveProgress + 1 : Math.Max(0, reviveProgress - 2);
        if (reviveProgress < ReviveTicks)
            return;

        Current = Behaviour.Follow;
        NPC.life = NPC.lifeMax;
        NPC.dontTakeDamage = false;
        reviveProgress = 0;
    }

    private bool OnGround => NPC.velocity.Y == 0f;

    private void MoveToward(Vector2 target, float speed)
    {
        float dx = target.X - NPC.Center.X;
        int dir = dx >= 0f ? 1 : -1;
        NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, dir * speed, Acceleration);
        NPC.direction = NPC.spriteDirection = dir;

        bool targetAbove = target.Y - NPC.Center.Y < -48f && MathF.Abs(dx) < FollowDistance * 2f;
        if (OnGround && (NPC.collideX || targetAbove))
            NPC.velocity.Y = JumpVelocity;
    }

    private void Stop()
    {
        NPC.velocity.X *= 0.8f;
        if (MathF.Abs(NPC.velocity.X) < 0.1f)
            NPC.velocity.X = 0f;
    }

    private void StartAnimation(int itemType, int ticks)
    {
        heldItemType = itemType;
        itemAnimation = itemAnimationMax = Math.Max(1, ticks);
    }

    private void TickAnimation()
    {
        if (itemAnimation > 0)
            itemAnimation--;
    }

    private void SyncBody()
    {
        if (appearance.UsesPlayerRenderer)
            appearance.Sync(NPC, Main.LocalPlayer, heldItemType, itemAnimation, itemAnimationMax, itemRotation);
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
    {
        if (!appearance.UsesPlayerRenderer)
            return true; // Guide sprite fallback
        float rotation = IsDowned ? NPC.direction * MathHelper.PiOver2 : 0f;

        // The player renderer writes to the device directly and expects a closed batch, which is
        // how vanilla calls it; drawing inside the open NPC batch puts the body behind everything
        // queued in it. Close, draw, then reopen with the NPC batch's own state.
        spriteBatch.End();
        appearance.Draw(rotation);
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
            DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
        return appearance.UsesPlayerRenderer ? false : true;
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
