using System;
using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>Checks the handoff between intention, destination, controls and measured motion.</summary>
public sealed class ArrivalDoesNotStrandFollowing : ICheck
{
    public string Name => "does arrival fulfil the follow objective";
    public string[] Needs => new[] { "action", "request", "brain_fresh", "recovery_active", "follow_objective_valid", "spot", "route_points", "desired_vel", "npc_px", "follow_reason", "wall_elapsed_ms" };
    public IEnumerable<Finding> Run(Session s)
    {
        bool Contradiction(int i)
        {
            if (s["action"].Text[i] is not ("walk-with" or "keep-company") || s["request"].Text[i] != "WithPlayer" || s["brain_fresh"].Number[i] != 1
                || s["recovery_active"].Number[i] != 0 || s["follow_objective_valid"].Number[i] != 0
                || s["route_points"].Number[i] != 0
                // A zero desire is a brake, so asking for nothing is the orb's form of issuing no
                // movement. Reading the `control` cell for the walker's `move=0.00;jump=0` here would
                // make this predicate permanently false, because that cell now reads `desired=x,y`.
                || !Session.TryPair(s["desired_vel"].Text[i], out float wx, out float wy) || Math.Abs(wx) + Math.Abs(wy) > 0.01f
                || !Session.TryPair(s["spot"].Text[i], out float x, out float y)
                || !Session.TryPair(s["npc_px"].Text[i], out float bx, out float by)) return false;
            // `spot` is a destination tile and npc_px is the orb's centre in world pixels; the tile's
            // centre is the point the navigator flies to, so both sides are centres.
            float dx = x * 16 + 8 - bx;
            float dy = y * 16 + 8 - by;
            // Older schemas lack navigator status; their twelve-pixel arrival contract
            // can still be checked from the recorded destination and the body's centre.
            return s.Find("nav_status") is { } status ? status.Text[i] == "Arrived" : dx * dx + dy * dy <= 144.1f;
        }
        foreach (var span in FindStretches.Where(s.Count, Contradiction, 120))
            yield return new Finding(Severity.Definitive, Name, "the navigator stopped at its destination while following remained unsatisfied",
                $"Ticks {s.Tick(span.Start)}–{s.Tick(span.End)}, {s["wall_elapsed_ms"].Number[span.End] - s["wall_elapsed_ms"].Number[span.Start]:0} ms. "
                + $"No route or movement was issued for {span.Length} samples; objective reason {FindStretches.Summarise(s["follow_reason"], span)}. "
                + $"Body centre starts at {s["npc_px"].Text[span.Start]}, chosen destination {s["spot"].Text[span.Start]}. "
                + "Destination acceptance and stopping tolerance disagree. This is not evidence of an unreachable player or a valid detour.", s.Tick(span.Start), s.Tick(span.End), span.Length);
    }
}

/// <summary>
/// Whether a claimed arrival lies inside the success region its destination was admitted against (schema 0.27.0).
/// The positioner snapshots the region on the resolve that admits a destination and the recorder marks an arrival
/// claim only on a tick the ordinary branch asked the navigator to travel and it reported Arrived, so every judged
/// row pairs one admitted region with the body the navigator judged. The geometry is recomputed here from the
/// region's own columns rather than read from the recorder's verdict.
///
///   follow-comfort   the intent region's own box, snapshotted at admission, with the navigator's arrival radius
///                    reserved inside it, so while the comfort is at least that radius an arrival outside it is
///                    Definitive. There is one box: the request's anchor travels beside it as evidence of what was
///                    aimed at and stopped widening acceptance when the region gained its own growth
///   tool-reach       every stand a tool proof returns reaches its tile, and the box half of reach is arithmetic, so a
///                    stand declared outside its own box is Definitive while reach never changed in the capture.
///                    An arrival outside the box is Potential: the navigator's radius is wider than the band a searched
///                    stand is verified over, and a stand kept because the body already reached from it is verified
///                    at one pixel. An arrival inside the box on a row whose activity still asked for its stand is
///                    Potential too, because the line to an exposed face is not interval-shaped
///   firing-position  the arc belongs to a moving target, so arriving where no arc now solves is Potential only
///
/// Meeting places, roams and exact requests without a work tile declare no region and are not judged. Every geometry
/// here is measured on the orb's centre, because that is the one point the navigator, the region and the reach box
/// all measure: <c>SuccessRegion.Contains</c> is handed <c>npc.Center</c> and <c>FindToolAccess.InReachBox</c> takes
/// a centre against the tile's own centre with no eye height in it.
/// </summary>
public sealed class ClaimedArrivalsStayInsideTheirSuccessRegion : ICheck
{
    /// <summary>Navigator.ArriveDistance; ChronicleTests pins it against the producer.</summary>
    internal const float ArriveDistance = 12f;
    /// <summary>Navigator.SettleRadius: the arrival radius plus the hover's radius and a four-pixel margin. An arrived body
    /// drifts inside it, and follow acceptance reserves it rather than the arrival radius; ChronicleTests pins it.</summary>
    internal const float SettleRadius = ArriveDistance + 16f + 4f;
    // On arrival the navigator hovers around the goal inside the settle radius, so a sample that is not held is momentum
    // still being shed; half a second of claimed arrival is a body hovering where it was sent, not a landing in progress.
    private const int SettledSamples = 30;
    // Centres, anchors and player positions are written to two decimals; half a pixel covers that and nothing a tile
    // decision could turn on.
    private const float Slack = .5f;

    public string Name => "does a claimed arrival lie inside its purpose's success region";
    // `touched_wall` is here as a schema witness rather than because the rule reads it, and it is the
    // one requirement in this list that is not obvious from the geometry above. `npc_px` exists in
    // every schema this reader has ever seen and changed meaning without changing name: under the
    // walking body it was an after-helpers compatibility position anchored on the feet, and it is now
    // the orb's centre, which is the point the region is handed. A silent reinterpretation is the one
    // kind of staleness the coverage block cannot catch by itself, so the check names a column only
    // the orb's row carries and skips a capture that predates it.
    public string[] Needs => new[] { "tick", "wall_elapsed_ms", "region_kind", "region_revision", "region_terrain", "region_anchor_px", "region_player_px", "region_comfort",
        "region_work_tile", "region_reach", "region_arrival", "npc_px", "touched_wall", "spot", "fire" };

    public IEnumerable<Finding> Run(Session s)
    {
        string Kind(int i) => s["region_kind"].Text[i];
        bool Claimed(int i) => s["region_arrival"].Text[i] != "-";
        bool Pair(string column, int i, out float x, out float y) => Session.TryPair(s[column].Text[i], out x, out y);
        bool Body(int i, out float x, out float y) => Pair("npc_px", i, out x, out y);
        string Span(Stretch span) => $"Ticks {s.Tick(span.Start)}–{s.Tick(span.End)}, {s["wall_elapsed_ms"].Number[span.End] - s["wall_elapsed_ms"].Number[span.Start]:0} ms, destination revision {s["region_revision"].Text[span.Start]}";

        bool OutsideFollow(int i)
        {
            if (!Claimed(i) || Kind(i) != "follow-comfort" || !Body(i, out float bx, out float by)
                || !Pair("region_player_px", i, out float px, out float py)
                || !Pair("region_comfort", i, out float cx, out float cy)) return false;
            // One box. The anchor is deliberately not a second acceptance box any more, and admitting it
            // here would pass exactly the arrival the single-box rule was written to catch: a body parked
            // beside a far-off meeting anchor while the region it was admitted to has moved on.
            return Math.Abs(bx - px) > cx + Slack || Math.Abs(by - py) > cy + Slack;
        }
        foreach (var span in FindStretches.Where(s.Count, OutsideFollow, 1))
        {
            Pair("region_comfort", span.Start, out float cx, out float cy);
            bool reserved = Math.Min(cx, cy) >= SettleRadius;
            yield return new Finding(reserved ? Severity.Definitive : Severity.Potential, Name,
                "claimed purpose arrival outside its declared success region: following stopped outside the region its destination was admitted against",
                $"{Span(span)}. Body centre {s["npc_px"].Text[span.Start]}; admitted against region centre {s["region_player_px"].Text[span.Start]} "
                + $"with comfort {s["region_comfort"].Text[span.Start]}, anchor {s["region_anchor_px"].Text[span.Start]}; destination tile {s["spot"].Text[span.Start]}. "
                + (reserved ? "Acceptance reserves the navigator's settle radius, the arrival radius plus the hover's drift, inside the region on both axes, so an arrival outside it names a destination that was never admitted, or an arrival the navigator should not have claimed."
                    : "The comfort is below the navigator's settle radius, so acceptance could not reserve it and a hovering arrival just outside is possible without a defect.")
                + " Whether the player has since moved is a separate question, which the follow-objective check answers.", s.Tick(span.Start), s.Tick(span.End), span.Length);
        }

        bool Box(int i, float x, float y)
        {
            if (!Pair("region_work_tile", i, out float tx, out float ty) || !Pair("region_reach", i, out float rx, out float ry)) return true;
            return Math.Abs(x - (tx * 16 + 8)) <= rx * 16 + 8 + Slack && Math.Abs(y - (ty * 16 + 8)) <= ry * 16 + 8 + Slack;
        }
        bool StandOutside(int i) => Kind(i) == "tool-reach" && Pair("region_anchor_px", i, out float sx, out float sy) && !Box(i, sx, sy);
        var reaches = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < s.Count; i++) if (Kind(i) == "tool-reach") reaches.Add(s["region_reach"].Text[i]);
        foreach (var span in FindStretches.Where(s.Count, StandOutside, 1))
            yield return new Finding(reaches.Count <= 1 ? Severity.Definitive : Severity.Potential, Name,
                $"a tool stand declared outside its own working region: stand {s["region_anchor_px"].Text[span.Start]} cannot reach tile {s["region_work_tile"].Text[span.Start]} with reach {s["region_reach"].Text[span.Start]}",
                $"{Span(span)}. Every stand a tool proof returns reaches its tile, and the reach box is arithmetic on the stand, the tile and the reach, so the request declares a working position its own proof could not have admitted. "
                + (reaches.Count <= 1 ? "Reach never changed in this capture." : $"Reach changed in this capture ({string.Join(", ", reaches)}), so the proof may have used a wider reach than the one recorded here."),
                s.Tick(span.Start), s.Tick(span.End), span.Length);

        foreach (var span in FindStretches.Where(s.Count, i => Claimed(i) && Kind(i) == "tool-reach" && Body(i, out float x, out float y) && !Box(i, x, y), SettledSamples))
        {
            Pair("region_anchor_px", span.Start, out float sx, out float sy);
            Body(span.Start, out float bx, out float by);
            yield return new Finding(Severity.Potential, Name,
                "claimed purpose arrival outside its declared success region: the navigator stopped outside the tool's reach box",
                $"{Span(span)}. Body centre {s["npc_px"].Text[span.Start]}, {bx - sx:0.0},{by - sy:0.0} from the stand {s["region_anchor_px"].Text[span.Start]}, "
                + $"working tile {s["region_work_tile"].Text[span.Start]}, destination tile {s["spot"].Text[span.Start]}. The first contract that failed is a usable working position: the navigator accepts any pose "
                + $"within {ArriveDistance:0} pixels of its destination and then hovers within {SettleRadius:0} pixels of it, while a searched stand is verified only at itself and eight pixels to each side, a stand kept because the body already reached is verified at one pixel, "
                + "and the destination is the free cell nearest the stand rather than the stand itself.", s.Tick(span.Start), s.Tick(span.End), span.Length);
        }

        foreach (var span in FindStretches.Where(s.Count, i => Claimed(i) && Kind(i) == "tool-reach" && Body(i, out float x, out float y) && Box(i, x, y), SettledSamples))
            yield return new Finding(Severity.Potential, Name,
                "claimed arrival inside the tool's reach box while its activity still asked for the stand",
                $"{Span(span)}. Body centre {s["npc_px"].Text[span.Start]}, stand {s["region_anchor_px"].Text[span.Start]}, working tile {s["region_work_tile"].Text[span.Start]}; terrain revision {s["region_terrain"].Text[span.Start]}→{s["region_terrain"].Text[span.End]}. "
                + "A tool activity asks for its stand only when a swing from the current centre cannot reach, and inside the box the remaining half of reach is a line to an exposed face, which the box does not hold and which is not verified between the poses a stand was proven at.",
                s.Tick(span.Start), s.Tick(span.End), span.Length);

        foreach (var span in FindStretches.Where(s.Count, i => Claimed(i) && Kind(i) == "firing-position" && s["fire"].Text[i] == "no-arc", SettledSamples))
            yield return new Finding(Severity.Potential, Name,
                "claimed arrival at an admitted firing position from which no arc solves",
                $"{Span(span)}; destination tile {s["spot"].Text[span.Start]}, anchor {s["region_anchor_px"].Text[span.Start]}. The destination was admitted with a solved arc to a target that moves, so this is not a contradiction; it names the interval in which the purpose of the position was not being served.",
                s.Tick(span.Start), s.Tick(span.End), span.Length);
    }
}

/// <summary>A selected fight that neither moves the body a pixel nor fires for two seconds.
/// Matches `combat` as well as `hunt`: the orb hovers rather than standing still, so a
/// pixel-identical stretch is a stuck body under either side of the merged stance.</summary>
public sealed class HuntingProducesAnOutcome : ICheck
{
    public string Name => "does hunting move or attack";
    public string[] Needs => new[] { "action", "brain_fresh", "fire", "npc_px", "control_source", "wall_elapsed_ms" };
    public IEnumerable<Finding> Run(Session s)
    {
        foreach (var span in FindStretches.Where(s.Count, i => i > 0 && s["action"].Text[i] is "hunt" or "combat" && s["brain_fresh"].Number[i] == 1
            && s["fire"].Text[i] is not ("fired" or "cooldown")
            // The centre is written in whole pixels, so an unchanged cell is an unmoved body and there is
            // no sub-pixel drift to threshold away.
            && string.Equals(s["npc_px"].Text[i], s["npc_px"].Text[i - 1], StringComparison.Ordinal), 120))
            yield return new Finding(Severity.Potential, Name, "hunting remained selected without movement or an attack",
                $"{span.Length} stationary samples; fire outcomes {FindStretches.Summarise(s["fire"], span)}, control owners {FindStretches.Summarise(s["control_source"], span)}. "
                + $"At {s["npc_px"].Text[span.Start]}. Inspect position and target rejection evidence; a selected hunt alone does not prove a feasible shot or route.",
                s.Tick(span.Start), s.Tick(span.End), span.Length);
    }
}
