#nullable enable

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>The three-valued answer every reach question in the brain passes around: a place the
/// body can get to, a place proven closed, and a question the flood has not finished answering.</summary>
public static class Reachability
{
    public enum Reach { Yes, No, Unknown }
}
