#nullable enable

using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether hunting kept the companion anywhere near him. The design's first priority is that the
/// companion stays on his screen, and a hunt is the opportunistic thing it does when little else is
/// going on; a hunt that walks it off the screen has inverted that. On 2026-09-09 the hunt action
/// held for half the session at a mean of thirty-five tiles and a maximum of eighty-eight, which is
/// two screens away, and it died out there.
/// </summary>
public sealed class HuntingStaysOnHisScreen : ICheck
{
    /// <summary>
    /// Half a screen at the game's default zoom, in tiles: a tile is sixteen pixels and the window
    /// is about seventeen hundred wide, so a companion past this is off the edge of what he can see.
    /// </summary>
    private const float OffScreenTiles = 55f;

    /// <summary>Five seconds. A hunt that crosses the screen edge briefly is a hunt returning.</summary>
    private const int MinTicks = 300;

    public string Name => "did hunting keep it on his screen";
    public string[] Needs => new[] { "action", "npc_tile", "player_tile" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column action = session["action"];
        Column distance = TheCompanionStaysUp.DistanceColumn(session);

        var away = FindStretches.Where(session.Count, i =>
            action.Text[i] == "hunt" && distance.Number[i] > OffScreenTiles,
            MinTicks, allowGap: 30);

        foreach (var stretch in away)
        {
            string danger = session.Has("self_threat")
                ? $" Its own danger over the stretch averaged {FindStretches.Mean(session["self_threat"], stretch):0.00}."
                : session.Has("danger")
                    ? $" The danger to him over the stretch averaged {FindStretches.Mean(session["danger"], stretch):0.00}, "
                      + "and that is the danger to *him*, which is not a reason for the companion to feel safe."
                    : "";
            yield return new Finding(
                Severity.Definitive,
                Name,
                $"{stretch.Length} ticks of hunting at up to {FindStretches.Max(distance, stretch):0} tiles from him",
                $"Mean distance over the stretch {FindStretches.Mean(distance, stretch):0} tiles, against a threshold of "
                    + $"{OffScreenTiles:0} tiles, which is half a screen at default zoom and therefore the point past "
                    + $"which he cannot see the companion at all.{danger} Hunting is meant to be the opportunistic "
                    + "action for when little else is going on, so a hunt that holds for this long at this range is the "
                    + "leash term either absent or beaten by the terms beside it.",
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }
}

/// <summary>
/// Whether the action board is being used at all. A scoring brain with nine actions that spends
/// almost the whole session in one of them is either being played in a situation only that action
/// fits, or has one action whose score no other can beat — and the second is indistinguishable from
/// a priority chain, which is the thing the design refuses. This is an oddity rather than a defect
/// because a session spent walking down one corridor genuinely is one action.
/// </summary>
public sealed class TheActionBoardGetsUsed : ICheck
{
    /// <summary>Past this share of the session, one action is the brain rather than a choice in it.</summary>
    private const float DominantShare = 0.55f;

    public string Name => "how the session divided between the actions";
    public string[] Needs => new[] { "action" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column action = session["action"];
        var whole = new Stretch(0, session.Count - 1);
        var tally = FindStretches.Tally(action, whole);

        var parts = new List<string>();
        foreach (var pair in tally)
            parts.Add($"{pair.Key} {100f * pair.Value / session.Count:0.0}%");
        string shares = string.Join(", ", parts);

        if (tally.Count > 0 && tally[0].Value > DominantShare * session.Count)
            yield return new Finding(
                Severity.Oddity,
                Name,
                $"{tally[0].Key} ran for {100f * tally[0].Value / session.Count:0.0}% of the session",
                $"Shares: {shares}. Past {DominantShare * 100f:0}% one action is doing the deciding, so the runner-up's "
                    + "raw score is the thing to read: if it never came close, the winner's score has a term the others "
                    + "cannot compete with and the board is a priority chain wearing a product of considerations.",
                session.Tick(0), session.Tick(session.Count - 1), tally[0].Value);
        else
            yield return new Finding(
                Severity.Oddity,
                Name,
                $"the session divided across {tally.Count} action(s), the busiest at {(tally.Count > 0 ? 100f * tally[0].Value / session.Count : 0f):0.0}%",
                $"Shares: {shares}.",
                session.Tick(0), session.Tick(session.Count - 1), session.Count);
    }
}

/// <summary>
/// Whether the torch was out in a fight. The torch is what the hand does when nothing else wants
/// it, so a shown torch with a reachable hostile present means the fight never asked for the hand:
/// either nothing was being fired, or the torch's hysteresis is holding the hand past the point the
/// weapon needed it. It is an oddity because a lit torch during a fight the companion is winning
/// from range is harmless.
/// </summary>
public sealed class TheTorchGivesUpTheHand : ICheck
{
    private const int MinTicks = 120;

    public string Name => "was the torch in the hand during a fight";
    public string[] Needs => new[] { "torch", "reachable" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column torch = session["torch"], reachable = session["reachable"];
        var stretches = FindStretches.Where(session.Count, i =>
            torch.Text[i] == "shown" && reachable.Number[i] > 0f, MinTicks, allowGap: 10);

        foreach (var stretch in stretches)
        {
            string fire = session.Has("fire") ? $" The fire column reads {FindStretches.Summarise(session["fire"], stretch)}." : "";
            yield return new Finding(
                Severity.Oddity,
                Name,
                $"{stretch.Length} ticks with the torch shown and up to {FindStretches.Max(reachable, stretch):0} reachable hostile(s)",
                $"The torch fills the hand only when no action has claimed it, so this says the shooting never asked "
                    + $"for the arm over that stretch.{fire} Harmless where the fight is being won from range; a defect "
                    + "where the same ticks also show nothing fired.",
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }
}

/// <summary>
/// Whether the brain fits in a frame. A tick is a sixtieth of a second, so the whole game has 16.67
/// milliseconds and the companion is one NPC in it; the sixth run of 2026-09-08 was called
/// unplayable and the phase columns exist to answer which part cost it. The reader states the share
/// and names the worst phase, so the profiling that took a session of column arithmetic is one line.
/// </summary>
public sealed class TheBrainFitsInAFrame : ICheck
{
    /// <summary>A frame at sixty a second, in milliseconds.</summary>
    private const float Frame = 16.67f;

    /// <summary>Half a frame for one NPC's mind is where the game starts losing frames elsewhere.</summary>
    private const float Worrying = 0.5f;

    /// <summary>A whole frame is a game that cannot run at speed no matter what else is cheap.</summary>
    private const float Fatal = 1.0f;

    public string Name => "did the brain fit in a frame";
    public string[] Needs => new[] { "brain_ms" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column total = session["brain_ms"];
        var whole = new Stretch(0, session.Count - 1);
        float mean = FindStretches.Mean(total, whole);
        float peak = FindStretches.Max(total, whole);
        float share = mean / Frame;

        var phases = new List<string>();
        foreach (string phase in new[] { "senses_ms", "reflex_ms", "decide_ms", "position_ms", "navigate_ms" })
            if (session.Has(phase))
                phases.Add($"{phase[..^3]} {FindStretches.Mean(session[phase], whole):0.00}");
        string breakdown = phases.Count > 0 ? $" Mean per phase in ms: {string.Join(", ", phases)}." : "";

        Severity severity = share >= Fatal ? Severity.Definitive : share >= Worrying ? Severity.Potential : Severity.Oddity;
        yield return new Finding(
            severity,
            Name,
            $"the brain averaged {mean:0.00} ms a tick, {share * 100f:0}% of a frame, peaking at {peak:0.00} ms",
            $"A frame is {Frame:0.00} ms and the companion is one NPC inside it.{breakdown} "
                + (severity == Severity.Oddity
                    ? "This is the baseline, not a finding."
                    : "The phase with the largest mean is where the cost is; the two searches are sticky columns "
                      + "(plan_ms and flood_ms hold the last search's cost until the next), so they overstate a "
                      + "per-tick share and the phase columns are the ones to read."),
            session.Tick(0), session.Tick(session.Count - 1), session.Count);
    }
}
