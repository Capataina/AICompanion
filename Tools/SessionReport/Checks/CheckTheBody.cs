#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether the body went where the route sent it. This is the check the fall-through defect of
/// 2026-09-09 needed and nobody had: the record held a frozen pixel position beside a live route and
/// a velocity the collision cancelled every tick, and finding it took reading three columns by hand
/// against a decompiled collision routine. A body that is being driven and is not moving is wrong in
/// every situation, so it is the definitive form; a body at rest holding an unfinished route might be
/// waiting for something, so that is the potential form.
///
/// The column that says "driven" is <c>desired_vel</c> and not <c>npc_vel</c>, which is the single
/// thing about this check that the orb changed and the thing most likely to be undone by someone
/// tidying it. The motor writes <c>npc.velocity</c> from the displacement the circle contact allowed,
/// so a body pressed into a wall reads a velocity of almost exactly zero on precisely the ticks this
/// check exists to catch; <c>desired_vel</c> is what the brain asked the body to accelerate toward
/// after capping, which is the intent the stall is failing to deliver.
/// </summary>
public sealed class TheBodyMovesWhenDriven : ICheck
{
    /// <summary>Three quarters of a second. Shorter than this is a segment boundary or a coast to rest.</summary>
    private const int MinFrozenTicks = 45;

    /// <summary>Under a twentieth of a pixel a tick is a body asked for nothing rather than a body being pushed.</summary>
    private const float RestingSpeed = 0.05f;

    /// <summary>
    /// Navigator.ArriveDistance. A route whose remaining length is inside the arrival radius is a route
    /// the navigator is entitled to call finished, so it is not "somewhere still to be". The remaining
    /// length is used rather than the segment index because the index counts segments and stops at
    /// <c>Count - 2</c> on the last one, which is exactly the off-by-one convention trap this reader
    /// already carries for tile columns.
    /// </summary>
    private const float ArriveDistance = 12f;

    public string Name => "did the body move while it had somewhere to be";
    public string[] Needs => new[] { "npc_px", "route_points", "route_remaining_px", "reflex", "desired_vel", "npc_vel" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column pixels = session["npc_px"], points = session["route_points"], remaining = session["route_remaining_px"];
        Column reflex = session["reflex"], desired = session["desired_vel"], velocity = session["npc_vel"];
        Column? wall = session.Find("touched_wall");
        Column? clearance = session.Find("clearance");
        Column? pinned = session.Find("pinned");
        Column? action = session.Find("action");

        var frozen = FindStretches.Where(session.Count, i =>
            i > 0
            && string.Equals(pixels.Text[i], pixels.Text[i - 1], StringComparison.Ordinal)
            && points.Number[i] > 0f
            && remaining.Number[i] > ArriveDistance
            && reflex.Text[i] == "-",
            MinFrozenTicks);

        foreach (var stretch in frozen)
        {
            float asked = 0f, moved = 0f;
            for (int i = stretch.Start; i <= stretch.End; i++)
            {
                if (Session.TryPair(desired.Text[i], out float dx, out float dy))
                    asked = MathF.Max(asked, MathF.Max(MathF.Abs(dx), MathF.Abs(dy)));
                if (Session.TryPair(velocity.Text[i], out float vx, out float vy))
                    moved = MathF.Max(moved, MathF.Max(MathF.Abs(vx), MathF.Abs(vy)));
            }

            bool driven = asked > RestingSpeed;
            // What the contact says about the stall, which for this body is the whole diagnosis: the
            // engine does not integrate the orb, so a wall the contact reports is the only legitimate
            // reason it is not moving, and clearance at zero is a body already inside terrain.
            string contact = wall == null || clearance == null
                ? "This capture carries no contact columns, so whether a wall was holding it is unreadable."
                : $"The contact reported touched_wall {FindStretches.Summarise(wall, stretch, 2)} with clearance "
                  + $"{FindStretches.Summarise(clearance, stretch, 3)} px"
                  + (pinned == null ? "" : $", pinned up to {FindStretches.Max(pinned, stretch):n0} tick(s)") + ".";
            string doing = action == null ? "" : $" The action was {FindStretches.Summarise(action, stretch, 3)}.";

            yield return new Finding(
                driven ? Severity.Definitive : Severity.Potential,
                Name,
                driven
                    ? $"the body stood at one pixel for {stretch.Length} ticks while being driven at up to {asked:0.00} px/tick"
                    : $"the body stood at one pixel for {stretch.Length} ticks with an unfinished route",
                $"npc_px never changed from {pixels.Text[stretch.Start]} while {remaining.Number[stretch.Start]:0} px of a "
                    + $"{points.Number[stretch.Start]:0}-point route remained, and no reflex held it. The steering asked for up to "
                    + $"{asked:0.00} px/tick against a resting threshold of {RestingSpeed:0.00}, and the contact let through "
                    + $"{moved:0.00}. {contact}{doing} "
                    + (driven
                        ? "A body asked to move that does not move is either held by terrain the contact is reporting — in which "
                          + "case the route was planned through a corridor the body does not fit — or held by nothing the record "
                          + "names, which is a position written during the AI phase where the engine's own displacement cannot see it."
                        : "This may be legitimate — a Hold request, or arrival slack the navigator counts as arrived while the "
                          + "remaining length sits just above the radius — and what settles it is whether the request column names "
                          + "a kind that stands still."),
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }
}

/// <summary>
/// Whether a companion that could not plan to the player was ever counted as stranded. The
/// stranded count is what turns into the roam behaviour that walks a sealed pocket, and it only
/// starts when a plan toward the player comes back completely empty while the reach flood closed
/// under its own budget. A long run of failed player-bound plans with the count flat at zero means
/// the body was failing to reach him and the mechanism meant to notice never fired.
/// </summary>
public sealed class BeingUnableToReachHimGetsNoticed : ICheck
{
    /// <summary>Two seconds of consecutive failed plans toward him.</summary>
    private const int MinTicks = 120;

    public string Name => "was a companion that could not reach him ever counted as stranded";
    public string[] Needs => new[] { "plan_failed", "request", "stranded" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column failed = session["plan_failed"], request = session["request"], stranded = session["stranded"];

        var stretches = FindStretches.Where(session.Count, i =>
            failed.Number[i] == 1f
            && (request.Text[i] == "WithPlayer" || request.Text[i] == "Guard")
            && stranded.Number[i] == 0f,
            MinTicks, allowGap: 15);

        foreach (var stretch in stretches)
        {
            string reach = session.Has("reach_any", "reach_two_way")
                ? $"Reach over the stretch averaged {FindStretches.Mean(session["reach_any"], stretch):0} tiles with "
                  + $"{FindStretches.Mean(session["reach_two_way"], stretch):0} returnable. "
                : "";
            yield return new Finding(
                Severity.Potential,
                Name,
                $"{stretch.Length} ticks of failed plans toward him with the stranded count flat at zero",
                $"The request was {FindStretches.Summarise(request, stretch, 2)}. {reach}"
                    + "A partial route counts as a failed plan and correctly does not start the count, so this is the "
                    + "expected shape whenever the companion is flying as close as it can get; it is a defect only if "
                    + "the plans were genuinely empty, which the plans file for this session answers by whether it holds "
                    + "windows for these ticks.",
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }
}
