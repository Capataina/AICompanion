#nullable enable

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge;

/// <summary>
/// One counter for everything the weapon knowledge believes: every volley taught, every law refitted, every
/// response folded. The simulation cache keys on it beside the world's own revision, and the target hold breaks
/// on it beside the learner's, so nothing aimed or held outlives the beliefs it was priced with.
/// </summary>
public static class KnowledgeRevision
{
    public static int Current { get; private set; }

    public static void Bump() => Current++;

    public static void Reset() => Current = 0;
}
