#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using AICompanion.Companion.Brain.Infrastructure.Movement;

namespace AICompanion.Companion.CharacterBody;

/// <summary>
/// Draws the orb as the game's Destroyer probe, scaled so its drawn diameter is the body's
/// diameter plus a few pixels and rotated to face the direction of travel, until the orb's own
/// art exists. During tool work it draws a beam from the orb's centre to the worked tile in the
/// look of the game's Laser Drill, reusing that projectile's own texture strip and the draw loop
/// the game runs for it (<c>Main.DrawProj</c>, type 611): a muzzle frame at the centre, outer
/// and inner segment frames repeated along the beam, and a tip frame at the end. One look for
/// both mining and chopping.
/// </summary>
public static class DrawTheOrb
{
    /// <summary>The sprite overhangs the contact circle a little so the body reads as the thing that touches walls.</summary>
    private const float DrawnDiameter = CircleContact.Diameter + 4f;

    // The frames of the Laser Drill's strip, as the game's own draw names them.
    private static readonly Rectangle BeamMuzzle = new(0, 2, 0, 40), BeamOuter = new(0, 68, 0, 18), BeamInner = new(0, 46, 0, 18), BeamTip = new(0, 90, 0, 48);
    private static bool loaded;

    /// <summary>Request both textures once; the game loads NPC and projectile art lazily.</summary>
    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        Main.instance.LoadNPC(NPCID.Probe);
        Main.instance.LoadProjectile(ProjectileID.LaserDrill);
    }

    public static void Draw(SpriteBatch spriteBatch, Vector2 screenPosition, Color lightColour, CompanionNPC companion)
    {
        EnsureLoaded();
        NPC npc = companion.NPC;
        Texture2D probe = TextureAssets.Npc[NPCID.Probe].Value;
        int frames = Math.Max(1, Main.npcFrameCount[NPCID.Probe]);
        Rectangle frame = new(0, 0, probe.Width, probe.Height / frames);
        float scale = DrawnDiameter / Math.Max(1, frame.Width);
        Color colour = companion.IsDowned ? lightColour * 0.6f : lightColour;
        if (companion.BeamTarget is Vector2 target)
            DrawBeam(spriteBatch, screenPosition, lightColour, npc.Center, target);
        spriteBatch.Draw(probe, npc.Center - screenPosition, frame, colour, companion.Facing, frame.Size() / 2f, scale, SpriteEffects.None, 0f);
    }

    private static void DrawBeam(SpriteBatch spriteBatch, Vector2 screenPosition, Color lightColour, Vector2 from, Vector2 to)
    {
        Texture2D strip = TextureAssets.Projectile[ProjectileID.LaserDrill].Value;
        Vector2 delta = to - from;
        float length = delta.Length();
        if (length < 1f) return;
        Vector2 direction = delta / length;
        // The strip is vertical in texture space, muzzle above tip, so pointing its +Y along the
        // beam is a rotation of the beam's angle less a quarter turn — the same angle the game
        // reaches for the held drill through velocity.ToRotation() + π/2 and then + π.
        float rotation = MathF.Atan2(direction.Y, direction.X) - MathHelper.PiOver2;
        Color colour = new Color(255, 255, 255, 200).MultiplyRGB(lightColour);
        Rectangle muzzle = BeamMuzzle with { Width = strip.Width };
        spriteBatch.Draw(strip, from.Floor() - screenPosition, muzzle, colour, rotation, muzzle.Size() / 2f - Vector2.UnitY * 4f, 1f, SpriteEffects.None, 0f);
        float remaining = length + 16f - 40f;
        Vector2 cursor = from.Floor() + direction * 24f;
        Rectangle outer = BeamOuter with { Width = strip.Width };
        float drawn = 0f;
        while (remaining > 0f && drawn + 1f < remaining)
        {
            Rectangle piece = outer;
            if (remaining - drawn < piece.Height) piece.Height = (int)(remaining - drawn);
            spriteBatch.Draw(strip, cursor - screenPosition, piece, colour, rotation, new Vector2(piece.Width / 2f, 0f), 1f, SpriteEffects.None, 0f);
            drawn += piece.Height;
            cursor += direction * piece.Height;
        }
        Vector2 end = cursor;
        Rectangle inner = BeamInner with { Width = strip.Width };
        int pulses = remaining < 100f ? 9 : 18;
        if (remaining > 0f)
        {
            float step = remaining / pulses;
            Vector2 pulse = from.Floor() + direction * 24f + direction * step * 0.25f;
            for (int i = 0; i < pulses; i++)
            {
                float advance = i == 0 ? step * 0.75f : step;
                spriteBatch.Draw(strip, pulse - screenPosition, inner, colour, rotation, new Vector2(inner.Width / 2f, 0f), 1f, SpriteEffects.None, 0f);
                pulse += direction * advance;
            }
        }
        Rectangle tip = BeamTip with { Width = strip.Width };
        spriteBatch.Draw(strip, end - screenPosition, tip, colour, rotation, new Vector2(tip.Width / 2f, 0f), 1f, SpriteEffects.None, 0f);
    }
}
