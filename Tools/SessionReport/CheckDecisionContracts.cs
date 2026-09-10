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
            if (s["action"].Text[i] != "walk-with" || s["request"].Text[i] != "WithPlayer" || s["brain_fresh"].Number[i] != 1
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
