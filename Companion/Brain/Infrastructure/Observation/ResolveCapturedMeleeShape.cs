using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

public readonly record struct CapturedMeleeEnemy(int Type, Vector2 Position, int Width, int Height,
    int Direction, int SpriteDirection, int FrameY, float Ai0, float Ai2, float Ai3)
{
    public Vector2 Centre => Position + new Vector2(Width * .5f, Height * .5f);
    public static CapturedMeleeEnemy From(NPC enemy) => new(enemy.type, enemy.position, enemy.width, enemy.height,
        enemy.direction, enemy.spriteDirection, enemy.frame.Y, enemy.ai[0], enemy.ai[2], enemy.ai[3]);
}
public readonly record struct CapturedMeleeShape(Rectangle Box, float DamageMultiplier, int HitChannel);

/// <summary>Vanilla NPC.GetMeleeCollisionData over captured fields. The public native
/// method reads Main.npc and runs loader hooks; neither is valid inside a frozen forecast.
/// Constants and branch conditions below are native mechanics, checked against that method
/// by EngineReplay. This does not model NPCLoader.ModifyCollisionData overrides.</summary>
public static class ResolveCapturedMeleeShape
{
    public static CapturedMeleeShape Resolve(CapturedMeleeEnemy enemy, Rectangle victim, int hitChannel)
    {
        Rectangle body = new((int)enemy.Position.X, (int)enemy.Position.Y, enemy.Width, enemy.Height);
        float multiplier = 1f;
        int type = enemy.Type;
        if (((type >= 430 && type <= 436) || type == 591) && enemy.Ai2 > 5f)
        {
            if (enemy.SpriteDirection < 0) body.X -= 34;
            body.Width += 34; multiplier *= 1.25f;
        }
        else if (type >= 494 && type <= 495 && enemy.Ai2 > 5f)
        {
            if (enemy.SpriteDirection < 0) body.X -= 18;
            body.Width += 18; multiplier *= 1.25f;
        }
        else if (type == 460) SelectAttack(30, 14, 20, 1.35f);
        else if (type == 417 && enemy.Ai0 == 6f && enemy.Ai3 > 0f && enemy.Ai3 < 4f)
        {
            Rectangle attack = Utils.CenteredRectangle(enemy.Centre, new Vector2(100f));
            if (victim.Intersects(attack)) { body = attack; multiplier *= 1.35f; }
        }
        else if (type == 466) SelectAttack(30, 8, 32, 1.75f);
        else if (type == 576 || type == 577)
        {
            int width = 0, height = 0, xOffset = 0, yOffset = 0;
            switch (enemy.FrameY)
            {
                case 15: width = 120; height = 30; yOffset = 24; break;
                case 16: width = 120; height = 60; xOffset = 10; break;
                case 17: width = 100; height = 90; xOffset = 50; break;
                case 18: width = 100; height = 50; xOffset = 90; yOffset = 10; break;
            }
            if (width != 0)
            {
                hitChannel = 2;
                var attack = FacingAttack(width, height, xOffset, yOffset);
                if (victim.Intersects(attack)) { body = attack; multiplier *= 1.75f; }
            }
        }
        else if ((type == 552 || type == 553 || type == 554) && enemy.Ai0 > 0f && enemy.Ai0 < 24f)
            SelectAttack(34, 14, 20, 1.35f);
        else if (type == 668)
        {
            body.Height -= 80;
            if (enemy.FrameY == 15 && enemy.Ai0 != 4f)
            {
                var attack = FacingAttack(64, 180, -42, 80);
                if (victim.Intersects(attack)) body = attack;
            }
        }
        return new(body, multiplier, hitChannel);

        void SelectAttack(int width, int height, int feetOffset, float scale)
        {
            var attack = new Rectangle((int)enemy.Centre.X, (int)enemy.Position.Y + enemy.Height - feetOffset, width, height);
            if (enemy.Direction < 0) attack.X -= attack.Width;
            if (victim.Intersects(attack)) { body = attack; multiplier *= scale; }
        }
        Rectangle FacingAttack(int width, int height, int xOffset, int yOffset)
        {
            var attack = new Rectangle((int)enemy.Centre.X - xOffset * enemy.Direction,
                (int)enemy.Centre.Y - height + yOffset, width, height);
            if (enemy.Direction < 0) attack.X -= width;
            return attack;
        }
    }
}
