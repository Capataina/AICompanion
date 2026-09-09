using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Brain.WorldObservation;

/// <summary>Observed hostile projectile motion; predictions are linear and refreshed each tick.</summary>
public sealed class ObserveProjectiles
{
    public readonly record struct Threat(Rectangle Hitbox, Vector2 Velocity, int Damage)
    {
        public Rectangle Predict(int ticks) => new(Hitbox.X + (int)(Velocity.X * ticks),
            Hitbox.Y + (int)(Velocity.Y * ticks), Hitbox.Width, Hitbox.Height);
    }
    public List<Threat> Threats { get; } = new();
    public void Update(NPC companion)
    {
        Threats.Clear();
        foreach (Projectile projectile in Main.ActiveProjectiles)
        {
            if (!projectile.hostile || projectile.damage <= 0) continue;
            // Include fast incoming shots even outside the ordinary local observation radius.
            float reach = 800f + projectile.velocity.Length() * 60f;
            if (Vector2.DistanceSquared(projectile.Center, companion.Center) > reach * reach) continue;
            Threats.Add(new Threat(projectile.Hitbox, projectile.velocity, projectile.damage));
        }
    }
}
