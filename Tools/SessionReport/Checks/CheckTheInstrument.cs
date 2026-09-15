#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether the body was ever held in place by something other than the world. The engine cannot
/// do this to a body it is integrating: <c>Collision_MoveWhileDry</c> does
/// <c>position += velocity</c> unconditionally, so a real velocity beside no displacement means
/// something wrote the position back during the AI phase, where the engine's own displacement
/// measure cannot see it.
///
/// The orb keeps this check for a reason its own body makes sharper rather than softer. The engine's
/// box collision is switched off for it, so the only thing that may legitimately stop it is the mod's
/// own circle contact — and the contact says so, in <c>touched_wall</c>, <c>wall_normal</c> and
/// <c>clearance</c>. A pinned stretch with no wall touched and clearance to spare is therefore a body
/// nothing in the record is entitled to be holding, which is a stronger statement than the walking
/// body could ever make, because a walker could always be resting on a tile nobody recorded.
/// </summary>
public sealed class TheBodyIsNeverPinned : ICheck
{
    /// <summary>A quarter of a second. Shorter is a wall met on the tick it is met.</summary>
    private const int MinPinnedTicks = 15;

    public string Name => "was the body ever held in place with a velocity it never spent";
    public string[] Needs => new[] { "pinned", "npc_vel" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column pinned = session["pinned"], velocity = session["npc_vel"];
        Column? action = session.Find("action");
        Column? wall = session.Find("touched_wall");
        Column? normal = session.Find("wall_normal");
        Column? clearance = session.Find("clearance");

        foreach (var stretch in FindStretches.Where(session.Count, i => pinned.Number[i] >= MinPinnedTicks, 1, allowGap: 30))
        {
            float worst = FindStretches.Max(pinned, stretch);
            string doing = action == null ? "" : $" The action was {FindStretches.Summarise(action, stretch, 3)}.";
            // The contact's own account is what separates a body a wall is entitled to stop from a body
            // nothing recorded is holding, so it is quoted rather than summarised away.
            string contact = wall == null || normal == null || clearance == null
                ? " This capture carries no contact columns, so whether a wall was touched is unreadable."
                : $" The contact reported touched_wall {FindStretches.Summarise(wall, stretch, 2)} along normal "
                  + $"{FindStretches.Summarise(normal, stretch, 2)}, with clearance {FindStretches.Summarise(clearance, stretch, 3)} px.";
            yield return new Finding(
                Severity.Definitive,
                Name,
                $"the body was pinned for up to {worst:n0} consecutive ticks",
                $"Velocity over the stretch was {FindStretches.Summarise(velocity, stretch, 3)} while the position did not change. "
                    + "This establishes a motion stall, not its cause. The orb is not integrated by the engine's box collision, so "
                    + "the mod's own circle contact is the only thing entitled to stop it: a stretch reporting no wall touched, or "
                    + "clearance well clear of zero, is a stall nothing in the record accounts for, while a wall touched throughout "
                    + "names the steering aiming at a point the wall lies between. Clearance at zero is a body overlapping terrain, "
                    + "which recovery clearance exists to answer."
                    + doing + contact,
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.End - stretch.Start + 1);
        }
    }
}
