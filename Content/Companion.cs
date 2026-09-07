#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AICompanion.Content;

/// <summary>
/// The companion. A friendly NPC with a custom AI (aiStyle -1, so vanilla runs
/// none of its own logic) that follows the nearest living player: walks toward
/// them, jumps when it runs into a wall, and teleports to them when it falls
/// too far behind. It cannot be damaged and never despawns.
///
/// This is the hello-world body. Everything the companion will later do
/// (mimicking, combat, orders, the upgrade tree) hangs off the state machine
/// that replaces the follow logic in <see cref="AI"/>.
///
/// The sprite is borrowed from the Guide until the companion has its own art;
/// the Guide's sheet has the standard town-NPC frame layout (frame 0 idle,
/// frames 2 to 15 the walk cycle), which <see cref="FindFrame"/> relies on.
/// </summary>
public class Companion : ModNPC
{
    /// <summary>How close, in pixels, the companion tries to stay to the player horizontally.</summary>
    private const float FollowDistance = 64f;

    /// <summary>Beyond this many pixels the companion gives up walking and teleports to the player.</summary>
    private const float TeleportDistance = 1400f;

    private const float WalkSpeed = 3.5f;
    private const float Acceleration = 0.25f;
    private const float JumpVelocity = -8.5f;

    public override string Texture => "Terraria/Images/NPC_" + NPCID.Guide;

    public override void SetStaticDefaults()
    {
        Main.npcFrameCount[Type] = Main.npcFrameCount[NPCID.Guide];
        // Keep the companion off the bestiary until it has its own art and entry.
        NPCID.Sets.NPCBestiaryDrawModifiers hide = new() { Hide = true };
        NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, hide);
    }

    public override void SetDefaults()
    {
        NPC.width = 18;
        NPC.height = 40;
        NPC.aiStyle = -1;
        NPC.friendly = true;
        NPC.damage = 0;
        NPC.defense = 15;
        NPC.lifeMax = 250;
        NPC.knockBackResist = 0f;
        NPC.dontTakeDamage = true;
        NPC.HitSound = SoundID.NPCHit1;
        NPC.DeathSound = SoundID.NPCDeath1;
        NPC.value = 0f;
    }

    /// <summary>Never culled for being far from players; the companion manages its own distance.</summary>
    public override bool CheckActive() => false;

    public override void AI()
    {
        Player? target = FindNearestLivingPlayer();
        if (target is null)
        {
            NPC.velocity.X *= 0.8f;
            return;
        }

        Vector2 toPlayer = target.Center - NPC.Center;

        if (toPlayer.Length() > TeleportDistance)
        {
            NPC.Bottom = target.Bottom;
            NPC.velocity = Vector2.Zero;
            NPC.netUpdate = true;
            return;
        }

        float dx = toPlayer.X;
        if (System.MathF.Abs(dx) > FollowDistance)
        {
            float desired = System.MathF.Sign(dx) * WalkSpeed;
            NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, desired, Acceleration);
            NPC.direction = NPC.spriteDirection = System.MathF.Sign(dx) >= 0 ? 1 : -1;
        }
        else
        {
            NPC.velocity.X *= 0.8f;
            if (System.MathF.Abs(NPC.velocity.X) < 0.1f)
                NPC.velocity.X = 0f;
        }

        // Standing on ground and something solid is in the way: jump.
        bool onGround = NPC.velocity.Y == 0f;
        bool blocked = NPC.collideX;
        bool playerIsAbove = toPlayer.Y < -48f && System.MathF.Abs(dx) < FollowDistance * 2f;
        if (onGround && (blocked || playerIsAbove))
        {
            NPC.velocity.Y = JumpVelocity;
        }
    }

    public override void FindFrame(int frameHeight)
    {
        bool walking = System.MathF.Abs(NPC.velocity.X) > 0.2f && NPC.velocity.Y == 0f;
        if (!walking)
        {
            NPC.frameCounter = 0;
            NPC.frame.Y = 0;
            return;
        }

        NPC.frameCounter += 1.0 + System.MathF.Abs(NPC.velocity.X) * 0.5;
        int walkFrames = 14;
        int index = 2 + (int)(NPC.frameCounter / 6) % walkFrames;
        NPC.frame.Y = frameHeight * index;
    }

    private Player? FindNearestLivingPlayer()
    {
        Player? best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            Player p = Main.player[i];
            if (!p.active || p.dead)
                continue;
            float d = Vector2.DistanceSquared(p.Center, NPC.Center);
            if (d < bestDistance)
            {
                best = p;
                bestDistance = d;
            }
        }
        return best;
    }
}
