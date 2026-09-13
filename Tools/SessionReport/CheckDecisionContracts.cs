using System;
using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>Checks the handoff between intention, destination, controls and measured motion.</summary>
public sealed class ArrivalDoesNotStrandFollowing : ICheck
{
    public string Name => "does arrival fulfil the follow objective";
    public string[] Needs => new[] { "action", "request", "brain_fresh", "recovery_active", "follow_objective_valid", "spot", "path_steps", "control", "observed_left", "observed_bottom", "npc_width", "follow_reason", "wall_elapsed_ms" };
    public IEnumerable<Finding> Run(Session s)
    {
        bool Contradiction(int i)
        {
            if (s["action"].Text[i] is not ("walk-with" or "keep-company") || s["request"].Text[i] != "WithPlayer" || s["brain_fresh"].Number[i] != 1
                || s["recovery_active"].Number[i] != 0 || s["follow_objective_valid"].Number[i] != 0
                || s["path_steps"].Number[i] != 0 || !s["control"].Text[i].Contains("move=0.00;jump=0")
                || !Session.TryPair(s["spot"].Text[i], out float x, out float y)) return false;
            float dx = x * 16 + 8 - (s["observed_left"].Number[i] + s["npc_width"].Number[i] / 2);
            float dy = (y + 1) * 16 - s["observed_bottom"].Number[i];
            // Older schemas lack navigator status; their twelve-pixel arrival contract
            // can still be checked from the recorded chosen stand and observed body.
            return s.Find("nav_status") is { } status ? status.Text[i] == "Arrived" : dx * dx + dy * dy <= 144.1f;
        }
        foreach (var span in FindStretches.Where(s.Count, Contradiction, 120))
            yield return new Finding(Severity.Definitive, Name, "the navigator stopped at its destination while following remained unsatisfied",
                $"Ticks {s.Tick(span.Start)}–{s.Tick(span.End)}, {s["wall_elapsed_ms"].Number[span.End] - s["wall_elapsed_ms"].Number[span.Start]:0} ms. "
                + $"No route or movement was issued for {span.Length} samples; objective reason {FindStretches.Summarise(s["follow_reason"], span)}. "
                + $"Observed body starts at {s["observed_left"].Text[span.Start]},{s["observed_bottom"].Text[span.Start]}, chosen stand {s["spot"].Text[span.Start]}. "
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
///   follow-comfort   AcceptsDestination admits a destination only inside the comfort box around the admission's
///                    player feet or anchor, with the navigator's arrival radius reserved on both axes, so while the
///                    comfort is at least that radius an arrival outside both boxes is Definitive
///   tool-reach       every stand a tool proof returns reaches its tile, and the box half of reach is arithmetic, so a
///                    stand declared outside its own box is Definitive while reach never changed in the capture.
///                    An arrival outside the box is Potential: the navigator's radius is wider than the band a searched
///                    stand is verified over, and a stand kept because the body already reached from it is verified
///                    at one pixel. An arrival inside the box on a row whose activity still asked for its stand is
///                    Potential too, because the line to an exposed face is not interval-shaped
///   firing-position  the arc belongs to a moving target, so arriving where no arc now solves is Potential only
///
/// Meeting places, roams and exact requests without a work tile declare no region and are not judged.
/// </summary>
public sealed class ClaimedArrivalsStayInsideTheirSuccessRegion : ICheck
{
    /// <summary>Navigator.ArriveDistance and FindToolAccess's eye height; ChronicleTests pins both against the producer.</summary>
    internal const float ArriveDistance = 12f, EyeHeight = 30f;
    // On arrival the navigator issues no controls, so a sample that is not held settles on the next tick; half a
    // second of claimed arrival is a body the navigator has stopped steering, not a landing in progress.
    private const int SettledSamples = 30;
    // Feet, anchors and player positions are written to two decimals; half a pixel covers that and nothing a tile
    // decision could turn on.
    private const float Slack = .5f;

    public string Name => "does a claimed arrival lie inside its purpose's success region";
    public string[] Needs => new[] { "tick", "wall_elapsed_ms", "region_kind", "region_revision", "region_terrain", "region_anchor_px", "region_player_px", "region_comfort",
        "region_work_tile", "region_reach", "region_arrival", "observed_left", "observed_bottom", "npc_width", "spot", "fire" };

    public IEnumerable<Finding> Run(Session s)
    {
        string Kind(int i) => s["region_kind"].Text[i];
        bool Claimed(int i) => s["region_arrival"].Text[i] != "-";
        float FeetX(int i) => s["observed_left"].Number[i] + s["npc_width"].Number[i] / 2;
        float FeetY(int i) => s["observed_bottom"].Number[i];
        bool Pair(string column, int i, out float x, out float y) => Session.TryPair(s[column].Text[i], out x, out y);
        string Span(Stretch span) => $"Ticks {s.Tick(span.Start)}–{s.Tick(span.End)}, {s["wall_elapsed_ms"].Number[span.End] - s["wall_elapsed_ms"].Number[span.Start]:0} ms, destination revision {s["region_revision"].Text[span.Start]}";

        bool OutsideFollow(int i)
        {
            if (!Claimed(i) || Kind(i) != "follow-comfort" || !Pair("region_player_px", i, out float px, out float py)
                || !Pair("region_anchor_px", i, out float ax, out float ay) || !Pair("region_comfort", i, out float cx, out float cy)) return false;
            bool Near(float x, float y) => Math.Abs(FeetX(i) - x) <= cx + Slack && Math.Abs(FeetY(i) - y) <= cy + Slack;
            return !Near(px, py) && !Near(ax, ay);
        }
        foreach (var span in FindStretches.Where(s.Count, OutsideFollow, 1))
        {
            Pair("region_comfort", span.Start, out float cx, out float cy);
            bool reserved = Math.Min(cx, cy) >= ArriveDistance;
            yield return new Finding(reserved ? Severity.Definitive : Severity.Potential, Name,
                "claimed purpose arrival outside its declared success region: following stopped outside the comfort box its destination was admitted against",
                $"{Span(span)}. Body feet {FeetX(span.Start):0.0},{FeetY(span.Start):0.0}; admitted against player feet {s["region_player_px"].Text[span.Start]} and anchor {s["region_anchor_px"].Text[span.Start]} "
                + $"with comfort {s["region_comfort"].Text[span.Start]}; destination tile {s["spot"].Text[span.Start]}. "
                + (reserved ? "Acceptance reserves the navigator's arrival radius inside the comfort box on both axes, so an arrival outside both boxes names a destination that was never admitted, or an arrival the navigator should not have claimed."
                    : "The comfort is below the navigator's arrival radius, so acceptance could not reserve it and an arrival just outside is possible without a defect.")
                + " Whether the player has since moved is a separate question, which the follow-objective check answers.", s.Tick(span.Start), s.Tick(span.End), span.Length);
        }

        bool Box(int i, float feetX, float feetY)
        {
            if (!Pair("region_work_tile", i, out float tx, out float ty) || !Pair("region_reach", i, out float rx, out float ry)) return true;
            return Math.Abs(feetX - (tx * 16 + 8)) <= rx * 16 + 8 + Slack && Math.Abs(feetY - EyeHeight - (ty * 16 + 8)) <= ry * 16 + 8 + Slack;
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

        foreach (var span in FindStretches.Where(s.Count, i => Claimed(i) && Kind(i) == "tool-reach" && !Box(i, FeetX(i), FeetY(i)), SettledSamples))
        {
            Pair("region_anchor_px", span.Start, out float sx, out float sy);
            yield return new Finding(Severity.Potential, Name,
                "claimed purpose arrival outside its declared success region: the navigator stopped outside the tool's reach box",
                $"{Span(span)}. Body feet {FeetX(span.Start):0.0},{FeetY(span.Start):0.0}, {FeetX(span.Start) - sx:0.0},{FeetY(span.Start) - sy:0.0} from the stand {s["region_anchor_px"].Text[span.Start]}, "
                + $"working tile {s["region_work_tile"].Text[span.Start]}, destination tile {s["spot"].Text[span.Start]}. The first contract that failed is a usable working position: the navigator accepts any grounded pose "
                + $"within {ArriveDistance:0} pixels of its destination and issues no controls there, while a searched stand is verified only at itself and eight pixels to each side, a stand kept because the body already reached is verified at one pixel, "
                + "and the destination is the standable tile nearest the stand rather than the stand itself.", s.Tick(span.Start), s.Tick(span.End), span.Length);
        }

        foreach (var span in FindStretches.Where(s.Count, i => Claimed(i) && Kind(i) == "tool-reach" && Box(i, FeetX(i), FeetY(i)), SettledSamples))
            yield return new Finding(Severity.Potential, Name,
                "claimed arrival inside the tool's reach box while its activity still asked for the stand",
                $"{Span(span)}. Body feet {FeetX(span.Start):0.0},{FeetY(span.Start):0.0}, stand {s["region_anchor_px"].Text[span.Start]}, working tile {s["region_work_tile"].Text[span.Start]}; terrain revision {s["region_terrain"].Text[span.Start]}→{s["region_terrain"].Text[span.End]}. "
                + "A tool activity asks for its stand only when a swing from the current feet cannot reach, and inside the box the remaining half of reach is a line to an exposed face, which the box does not hold and which is not verified between the poses a stand was proven at.",
                s.Tick(span.Start), s.Tick(span.End), span.Length);

        foreach (var span in FindStretches.Where(s.Count, i => Claimed(i) && Kind(i) == "firing-position" && s["fire"].Text[i] == "no-arc", SettledSamples))
            yield return new Finding(Severity.Potential, Name,
                "claimed arrival at an admitted firing position from which no arc solves",
                $"{Span(span)}; destination tile {s["spot"].Text[span.Start]}, anchor {s["region_anchor_px"].Text[span.Start]}. The destination was admitted with a solved arc to a target that moves, so this is not a contradiction; it names the interval in which the purpose of the position was not being served.",
                s.Tick(span.Start), s.Tick(span.End), span.Length);
    }
}

public sealed class HuntingProducesAnOutcome : ICheck
{
    public string Name => "does hunting move or attack";
    public string[] Needs => new[] { "action", "brain_fresh", "fire", "observed_left", "observed_bottom", "control_source", "wall_elapsed_ms" };
    public IEnumerable<Finding> Run(Session s)
    {
        foreach (var span in FindStretches.Where(s.Count, i => i > 0 && s["action"].Text[i] == "hunt" && s["brain_fresh"].Number[i] == 1
            && s["fire"].Text[i] is not ("fired" or "cooldown")
            && Math.Abs(s["observed_left"].Number[i] - s["observed_left"].Number[i - 1]) < .1f
            && Math.Abs(s["observed_bottom"].Number[i] - s["observed_bottom"].Number[i - 1]) < .1f, 120))
            yield return new Finding(Severity.Potential, Name, "hunting remained selected without movement or an attack",
                $"{span.Length} stationary samples; fire outcomes {FindStretches.Summarise(s["fire"], span)}, control owners {FindStretches.Summarise(s["control_source"], span)}. "
                + $"At {s["observed_left"].Text[span.Start]},{s["observed_bottom"].Text[span.Start]}. Inspect position and target rejection evidence; a selected hunt alone does not prove a feasible shot or route.",
                s.Tick(span.Start), s.Tick(span.End), span.Length);
    }
}

public sealed class SubmergedMotionGetsExplained : ICheck
{
    public string Name => "does submerged movement preserve a route to air";
    public string[] Needs => new[] { "breath", "brain_fresh", "observed_left", "observed_bottom", "action", "control_source", "control", "path_steps", "wall_elapsed_ms" };
    public IEnumerable<Finding> Run(Session s)
    {
        foreach (var span in FindStretches.Where(s.Count, i => i > 0 && s["brain_fresh"].Number[i] == 1
            && s["breath"].Text[i].EndsWith('u')
            && Math.Abs(s["observed_left"].Number[i] - s["observed_left"].Number[i - 1]) < .1f
            && Math.Abs(s["observed_bottom"].Number[i] - s["observed_bottom"].Number[i - 1]) < .1f, 120))
        {
            string escape = s.Has("escape_stage", "state_search_pending")
                ? $"Escape stages {FindStretches.Summarise(s["escape_stage"], span)}, pending search {FindStretches.Summarise(s["state_search_pending"], span)}. "
                : "This schema lacks escape-stage and pending-search evidence. ";
            yield return new Finding(Severity.Potential, Name, "the submerged body remained stationary while breath ran down",
                $"{span.Length} samples at {s["observed_left"].Text[span.Start]},{s["observed_bottom"].Text[span.Start]}; breath {s["breath"].Text[span.Start]}→{s["breath"].Text[span.End]}. "
                + $"Actions {FindStretches.Summarise(s["action"], span)}; control owners {FindStretches.Summarise(s["control_source"], span)}. "
                + escape + "Replay the liquid amounts, collision shapes and live body before deciding whether the exit was physically possible. Submersion alone does not establish a planner defect.",
                s.Tick(span.Start), s.Tick(span.End), span.Length);
        }
    }
}
