#nullable enable

using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// How sure the reader is that what it found is wrong, which is the whole point of the tool: a
/// report that says "take a look around tick 3000" hands the reading back to the person who asked
/// for it. Three levels, and the boundary between them is evidence rather than severity of effect.
/// </summary>
public enum Severity
{
    /// <summary>
    /// Wrong by construction. Either the record contradicts itself, or the behaviour breaks a rule
    /// the design states outright: a body that cannot come home from more places than it can reach,
    /// damage taken in a tick the companion scored as danger zero, a weapon chosen over one the same
    /// tick scored higher. Nothing about the situation can make one of these correct.
    /// </summary>
    Definitive,

    /// <summary>
    /// A pattern that is wrong in every situation anyone has thought of, and could be right in one
    /// nobody has. A run of ticks under threat with nothing fired is a defect unless every arc was
    /// genuinely blocked; a traversal that took four times its proven ticks is a defect unless the
    /// world changed under it. The finding carries what would settle it.
    /// </summary>
    Potential,

    /// <summary>
    /// A shape in the numbers with no rule behind it. One action taking most of the session, a torch
    /// out in a fight, a distribution that leans further than expected. These are for reading, not
    /// for fixing, and they are where the defects nobody has met yet show up first.
    /// </summary>
    Oddity,
}

/// <summary>
/// One thing the reader found, with the place it happened and the numbers that prove it. The
/// <see cref="Detail"/> carries the mechanism and the threshold, so a finding can be argued with
/// rather than merely believed.
/// </summary>
public sealed record Finding(
    Severity Severity,
    string Check,
    string Title,
    string Detail,
    int FirstTick,
    int LastTick,
    int Rows);

/// <summary>
/// The contract every check implements. A check names the columns it cannot work without, and the
/// runner skips it and says so when the file predates one of them, because a check that silently
/// measures an absent column returns "nothing wrong" and a run with no coverage looks exactly like
/// a clean run.
/// </summary>
public interface ICheck
{
    /// <summary>What this check would find, phrased as the question it asks.</summary>
    string Name { get; }

    /// <summary>Every column this check reads. One missing name skips the whole check.</summary>
    string[] Needs { get; }

    IEnumerable<Finding> Run(Session session);
}
