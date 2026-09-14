#nullable enable

using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace AICompanion.Companion.Brain.Infrastructure.Aiming;

/// <summary>
/// The motion the aimer flies each projectile type with. An item says what it fires and how fast,
/// and nothing about how that thing falls, so the motion is not a stat to read: it is a prior for
/// the two vanilla styles whose free flight the decompiled AI states plainly, and a straight line at
/// launch speed for everything else until the companion has watched its own shots. Session state,
/// never saved: a new session starts from the priors again.
/// </summary>
public static class ProjectileArcs
{
    private static readonly Dictionary<int, LearnedMotion> learned = new();

    /// <summary>The motion the aimer should fly this type with now: learned if it has been, else the prior.</summary>
    public static LearnedMotion MotionFor(int projectileType)
        => learned.TryGetValue(projectileType, out LearnedMotion motion) ? motion : Prior(projectileType);

    /// <summary>
    /// What the game's own AI says about a vanilla style, so a wooden arrow's first shot need not
    /// miss. Both numbers below are read from <c>Terraria.Projectile</c> as decompiled by
    /// <c>Tools/decompile.sh</c>, and the native-match row in <c>Tools/EngineReplay/Combat/VerifyArcLearning.cs</c>
    /// holds them against <c>VanillaAI</c> tick for tick.
    ///
    /// Style 1 with the <c>arrow</c> flag (<c>AI_001</c>): <c>else if (ai[0] >= 15f) { ai[0] = 15f; velocity.Y += 0.1f; }</c>
    /// with no horizontal drag, capped by <c>if (velocity.Y > 16f) velocity.Y = 16f</c>. Style 1 without
    /// the flag — bullets, lasers — takes none of that branch and flies straight.
    ///
    /// Style 2 (<c>AI</c>, the thrown-object branch): <c>num29 = 20; ai[0]++; if (ai[0] >= num29) { velocity.Y += 0.4f; velocity.X *= 0.97f; }</c>
    /// then the same 16 cap.
    ///
    /// Every other style, vanilla or modded, starts straight. A style the companion has not seen fly
    /// is not assumed to fall, because a wrong gravity misses in a way the learner then has to unlearn,
    /// while a straight guess misses in the one way a first observation corrects outright.
    /// </summary>
    public static LearnedMotion Prior(int projectileType)
    {
        if (!ContentSamples.ProjectilesByType.TryGetValue(projectileType, out Projectile? sample))
            return LearnedMotion.Straight;
        if (sample.aiStyle == ProjAIStyleID.Arrow && sample.arrow)
            return new LearnedMotion(GravityStartsAtPhase: 15, Gravity: .1f, HorizontalDrag: 1f, MaxFallSpeed: 16f);
        if (sample.aiStyle == ProjAIStyleID.ThrownProjectile)
            return new LearnedMotion(GravityStartsAtPhase: 20, Gravity: .4f, HorizontalDrag: .97f, MaxFallSpeed: 16f);
        return LearnedMotion.Straight;
    }

    /// <summary>
    /// A shot the companion just spawned, to be watched. The sampling and the fit arrive with the
    /// learner's next commit; until then registering records nothing and the priors are the motion.
    /// </summary>
    public static void Register(int projectileSlot, int projectileType, Microsoft.Xna.Framework.Vector2 launch) { }

    /// <summary>Forget everything learned, so a fixture measures the priors and the learning rather than the previous case's.</summary>
    public static void Reset() => learned.Clear();
}
