#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;

/// <summary>
/// The companion's aim point as its projectiles' cursor. Companion shots are owned by the local player, so any
/// projectile AI that reads <c>Main.MouseWorld</c> would steer by the player's cursor; while a registered companion
/// projectile runs its AI the mouse reads as the aim point that shot was fired at instead, restored the moment the
/// AI returns. This is TerraGuardians' shipped mechanism, narrowed to the slots the flight recorder holds an aim for.
///
/// <para>No vanilla projectile is aimed this way alone: every vanilla mouse read sits behind a held button or a live
/// owner state the companion does not produce (a channelled beam dies the tick its owner stops channelling, before
/// the cursor matters), so the spoof's readers are modded projectiles that steer by the cursor and nothing else.
/// The K6 row holds the mechanism itself — the mouse reads as the aim during the AI and as the player's after —
/// and modded readers are covered by the play protocol, because no modded item exists headless.</para>
/// </summary>
public static class SpoofOwnerInputForShots
{
    private static readonly Stack<(int MouseX, int MouseY)> saved = new();

    /// <summary>
    /// Whether this projectile's AI should run under the spoof: a registered companion shot whose aim is known.
    /// Player traces never qualify — the recorder's <see cref="RecordProjectileFlights.AimFor"/> already excludes
    /// them — so the player's own cursor is untouched while his own shots fly.
    /// </summary>
    public static bool Applies(int projectileSlot) => RecordProjectileFlights.AimFor(projectileSlot) != null;

    /// <summary>
    /// Point the mouse at the shot's aim, saving the player's cursor. The write inverts the <c>MouseWorld</c>
    /// getter (<c>MouseScreen + screenPosition</c>, mirrored around the screen height under reversed gravity);
    /// an aim point off screen is written as the true value and AI reading the clamped cursor sees the clamp.
    /// </summary>
    public static void Enter(int projectileSlot)
    {
        if (RecordProjectileFlights.AimFor(projectileSlot) is not { } aim) return;
        saved.Push((Main.mouseX, Main.mouseY));
        Main.mouseX = (int)(aim.X - Main.screenPosition.X);
        Main.mouseY = Main.player[Main.myPlayer].gravDir == -1f
            ? (int)(Main.screenPosition.Y + Main.screenHeight - aim.Y)
            : (int)(aim.Y - Main.screenPosition.Y);
    }

    /// <summary>Give the player's cursor back. A stack rather than a slot, so an AI that nests projectile updates cannot restore the wrong cursor.</summary>
    public static void Exit(int projectileSlot)
    {
        if (RecordProjectileFlights.AimFor(projectileSlot) == null) return;
        if (saved.Count > 0)
            (Main.mouseX, Main.mouseY) = saved.Pop();
    }

    public static void Clear() => saved.Clear();
}

/// <summary>
/// The engine side of the cursor spoof: <c>PreAI</c> runs before the projectile's AI (vanilla or modded) and
/// <c>PostAI</c> after it, so the spoof holds exactly for the AI and for nothing else on the tick. Every
/// projectile pays a dictionary lookup per AI step rather than filtering in <c>AppliesToEntity</c>, because that
/// filter runs at instantiation and the aim registers after the spawn; the step observer stays in the recorder's
/// own hook, because two <c>PostAI</c> observers would feed every velocity twice and teach the arc a zero gravity.
/// </summary>
public sealed class SpoofCursorForCompanionShots : GlobalProjectile
{
    public override bool PreAI(Projectile projectile)
    {
        SpoofOwnerInputForShots.Enter(projectile.whoAmI);
        return true;
    }

    public override void PostAI(Projectile projectile)
    {
        SpoofOwnerInputForShots.Exit(projectile.whoAmI);
    }
}
