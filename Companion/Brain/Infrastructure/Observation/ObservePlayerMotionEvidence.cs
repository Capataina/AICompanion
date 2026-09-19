#nullable enable

using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>One bounded observation of player travel. It records what was seen, never a declared destination.</summary>
public readonly record struct PlayerMotionEvidence(Vector2 Position, Vector2 Velocity, Vector2 HeldDirection,
    float NetDisplacement, float PathLength, float Coherence, long ObservationTick, long Episode,
    bool Reversed, bool Discontinuous);

/// <summary>Builds motion evidence from the existing coherent-travel inference without creating a competing predictor.</summary>
public sealed class ObservePlayerMotionEvidence
{
    private Vector2 priorTravel;
    private long episode;
    private long priorTick = -1;

    public PlayerMotionEvidence Observe(PlayerSense player, long tick)
    {
        Vector2 travel = player.Intent;
        bool discontinuous = priorTick >= 0 && (tick != priorTick + 1 || player.Activity.Discontinuous);
        bool reversed = priorTravel.LengthSquared() > 0f && travel.LengthSquared() > 0f
            && Vector2.Dot(priorTravel, travel) < 0f;
        if (discontinuous) episode++;
        priorTravel = travel;
        priorTick = tick;
        return new PlayerMotionEvidence(player.Position, player.Velocity, player.HeldMove, player.Activity.NetDisplacement,
            player.Activity.PathLength, player.Activity.Coherence, tick, episode, reversed, discontinuous);
    }

    public void Reset() { priorTravel = Vector2.Zero; priorTick = -1; episode = 0; }
}
