#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether the companion saw the damage it took. Every danger term in the brain used to read the
/// danger to the *player*, so a companion eighty tiles away lived in a world with no danger in it
/// at all: on 2026-09-09 it lost sixty-five life across five hits between ticks 2753 and 3082 with
/// the danger column reading 0.00 at every one of them. A hit in a tick scored as perfectly safe is
/// wrong by construction whatever else was happening, which makes it the definitive form, and it is
/// the single most useful thing this reader can find because the brain cannot see it from inside.
/// </summary>
public sealed class DamageArrivesWhereDangerWasSeen : ICheck
{
    /// <summary>A danger reading this low is "nothing is happening", not "something small is happening".</summary>
    private const float Blind = 0.01f;

    /// <summary>An empty breath bar. Above this the head being under water is swimming, not drowning.</summary>
    private const float Suffocating = 0.01f;

    public string Name => "did it see the danger before the damage arrived";
    public string[] Needs => new[] { "life" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column life = session["life"];
        // The companion's own danger is the right column; the player's is the fallback for a file
        // written before the companion had a sense of its own peril, and a finding says which it read.
        Column? own = session.Find("self_threat");
        Column? player = session.Find("danger");
        Column? sense = own ?? player;
        string senseName = own != null ? "self_threat" : "danger";
        // Read rather than required, the same way the danger column is: a file without them still
        // answers the question, less precisely, and the finding says so instead of being skipped.
        Column? selfDanger = session.Find("self_danger");
        Column? breath = session.Find("breath");

        var hits = new List<(int Row, float Lost, float Danger)>();
        int environmental = 0;
        for (int i = 1; i < session.Count; i++)
        {
            float before = life.Number[i - 1], after = life.Number[i];
            if (float.IsNaN(before) || float.IsNaN(after) || after >= before)
                continue;
            // Lava, fire and a drained breath bar all take life with no hostile in the world, so the
            // threat sense is *correct* to read zero on those ticks and counting them as unseen hits
            // turns a cave session into a page of definitive findings. This is the same exclusion
            // ScenarioCapture makes for the same reason, plus drowning, which it does not take damage
            // from. Being under water is not the test: the session of 2026-09-08 holds two real hits
            // at breath 0.85 and 0.98, so the suffix alone would have suppressed two true findings.
            bool burning = selfDanger != null && (selfDanger.Text[i].EndsWith('L') || selfDanger.Text[i].EndsWith('f'));
            bool suffocating = breath != null && breath.Text[i].EndsWith('u') && breath.Number[i] <= Suffocating;
            if (burning || suffocating)
            {
                environmental++;
                continue;
            }
            float reading = sense == null ? float.NaN : sense.Number[i];
            hits.Add((i, before - after, reading));
        }
        string aside = environmental == 0
            ? (selfDanger == null || breath == null
                ? " This file carries no self_danger or breath column, so a hit from lava, fire or drowning cannot be told from a hit by something alive."
                : "")
            : $" A further {environmental} life loss(es) came from lava, fire or a drained breath bar and are excluded, "
              + "because a threat sense is right to read zero when nothing alive is in the room.";
        if (hits.Count == 0)
            yield break;

        var blind = new List<(int Row, float Lost, float Danger)>();
        float lostTotal = 0f, lostBlind = 0f;
        foreach (var hit in hits)
        {
            lostTotal += hit.Lost;
            if (!float.IsNaN(hit.Danger) && hit.Danger <= Blind)
            {
                blind.Add(hit);
                lostBlind += hit.Lost;
            }
        }

        if (sense == null)
        {
            yield return new Finding(
                Severity.Oddity,
                Name,
                $"{hits.Count} damage event(s) cost {lostTotal:0} life, and no danger column to read them against",
                "The file carries neither self_threat nor danger, so whether the companion saw what hit it is "
                    + "unanswerable from this session.",
                session.Tick(hits[0].Row), session.Tick(hits[^1].Row), hits.Count);
            yield break;
        }

        if (blind.Count > 0)
        {
            var stretch = new Stretch(blind[0].Row, blind[^1].Row);
            string far = session.Has("npc_tile", "player_tile")
                ? $" Distance to him at those ticks ran from {session.TileDistance("npc_tile", "player_tile", blind[0].Row):0} "
                  + $"to {session.TileDistance("npc_tile", "player_tile", blind[^1].Row):0} tiles."
                : "";
            string acting = session.Has("action") ? $" Action: {FindStretches.Summarise(session["action"], stretch, 3)}." : "";
            yield return new Finding(
                Severity.Definitive,
                Name,
                $"{blind.Count} of {hits.Count} hits landed while {senseName} read zero, costing {lostBlind:0} life",
                $"The companion was struck in ticks it scored as containing no danger at all.{far}{acting} "
                    + (own != null
                        ? "The column read is the companion's own danger, so this is not the old defect of measuring "
                          + "the player's peril and calling it the world's: something reachable and hitting it scored "
                          + "zero urgency, which points at the urgency factors — reachability, sight or closeness — "
                          + "rather than at which body the danger was measured for."
                        : "This file predates the companion's own danger sense, so the column read is the danger to "
                          + "the *player*: a hit taken at distance with this reading at zero is the known feedback "
                          + "loop where straying from him made every threat term vanish and hunting score higher.")
                    + aside,
                session.Tick(stretch.Start), session.Tick(stretch.End), blind.Count);
        }

        yield return new Finding(
            Severity.Oddity,
            Name,
            $"{hits.Count} damage event(s) over the session, {lostTotal:0} life lost in total",
            $"The heaviest single hit cost {Worst(hits):0} life. This is the baseline the other findings are read "
                + "against: a session with no damage proves nothing about kiting." + aside,
            session.Tick(hits[0].Row), session.Tick(hits[^1].Row), hits.Count);
    }

    private static float Worst(List<(int Row, float Lost, float Danger)> hits)
    {
        float worst = 0f;
        foreach (var hit in hits)
            worst = MathF.Max(worst, hit.Lost);
        return worst;
    }
}

/// <summary>
/// Whether the hands did anything while something was trying to kill it. Firing used to live inside
/// three actions, which made the companion structurally incapable of shooting while following,
/// looting, wandering or working; the fix moved it out, and this is the check that says whether it
/// took. A run of ticks with a reachable hostile present and nothing fired is a defect unless every
/// arc was genuinely blocked, and the fire column is what separates those two — which is exactly why
/// that column was added.
/// </summary>
public sealed class TheHandsWorkWhileThreatened : ICheck
{
    /// <summary>Three seconds. Longer than the slowest weapon's cooldown by a wide margin.</summary>
    private const int MinTicks = 180;

    public string Name => "did it shoot while something reachable was on it";
    public string[] Needs => new[] { "reachable", "shot" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column reachable = session["reachable"], shot = session["shot"];
        Column? fire = session.Find("fire");
        Column? engage = session.Find("engage");
        Column? fresh = session.Find("brain_fresh");
        Column? playerDead = session.Find("player_dead");
        Column? state = session.Find("state");

        // No gap allowance, because a tick that fired *is* the break in the stretch. With one, a
        // healthy fight reads as one enormous finding: the reload ticks between two shots satisfy the
        // condition and a single "fired" row between them falls inside the allowance, so a two-minute
        // exchange folds into a single "nothing fired for 7,000 ticks" whose own tally shows the shots
        // it fired. The gap allowance is for a condition that flickers, and this one does not.
        var quiet = FindStretches.Where(session.Count, i =>
            reachable.Number[i] > 0f && shot.Number[i] == 0f && (fire == null || fire.Text[i] != "fired")
                && (fresh != null ? fresh.Number[i] > 0f : playerDead?.Text[i] != "1") && state?.Text[i] != "downed",
            MinTicks, allowGap: 0);

        foreach (var stretch in quiet)
        {
            string why;
            Severity severity;
            if (fire == null)
            {
                why = "The file has no fire column, so whether this was a reload, a target with no reachable arc, or "
                    + "no target at all cannot be separated; that column exists from 0.6.3 onward.";
                severity = Severity.Potential;
            }
            else
            {
                var tally = FindStretches.Tally(fire, stretch);
                string top = tally.Count > 0 ? tally[0].Key : "-";
                why = $"The fire column reads {FindStretches.Summarise(fire, stretch)}. ";
                why += top switch
                {
                    "no-target" => "Nothing was picked to shoot at while a reachable hostile was present, which is the "
                        + "target chooser refusing everything the threat sense offered — read it against the engage column.",
                    "no-arc" => "A target was picked and no launch angle reached it for the whole stretch, so the body "
                        + "was standing somewhere with no shot; that is the positioner's job and its line-of-fire "
                        + "factor scored the spot it chose anyway.",
                    "hands-busy" => "The hands were driving a tool, which is the one thing that legitimately stops the "
                        + "shooting, so this is only a defect if the work should have lost to the fight.",
                    "cooldown" => "The whole stretch reads as a reload, which no weapon in the roster is long enough to "
                        + "produce, so the cooldown is being reset by something other than a shot.",
                    _ => "The dominant reason is not one the reader knows about.",
                };
                severity = top is "hands-busy" ? Severity.Oddity : Severity.Potential;
            }

            string what = engage == null ? "" : $" Engage target: {FindStretches.Summarise(engage, stretch, 3)}.";
            yield return new Finding(
                severity,
                Name,
                $"{stretch.Length} ticks with a reachable hostile present and nothing fired",
                $"Up to {FindStretches.Max(reachable, stretch):0} reachable hostile(s) over the stretch.{what} {why}",
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }
}

/// <summary>
/// Whether the weapon it fired was the one its own arithmetic scored higher. Both weapons' expected
/// damage is recorded, the loser included, precisely so this question has an answer in the file
/// instead of having to be re-derived by hand from the situation. A row where the rejected weapon
/// scored higher than the chosen one is a defect with no other tell anywhere in the record.
/// </summary>
public sealed class TheChosenWeaponIsTheBetterOne : ICheck
{
    /// <summary>Expected damage under this much apart is a tie, and a tie is not a defect.</summary>
    private const float Margin = 0.5f;

    public string Name => "was the weapon it fired the one that scored higher";
    public string[] Needs => new[] { "weapon", "exp_bow", "exp_knife" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column weapon = session["weapon"], bow = session["exp_bow"], knife = session["exp_knife"];

        int wrong = 0, worstRow = -1;
        float worstGap = 0f;
        for (int i = 0; i < session.Count; i++)
        {
            float mine = weapon.Text[i] switch { "bow" => bow.Number[i], "knife" => knife.Number[i], _ => float.NaN };
            float theirs = weapon.Text[i] switch { "bow" => knife.Number[i], "knife" => bow.Number[i], _ => float.NaN };
            if (float.IsNaN(mine) || float.IsNaN(theirs))
                continue;
            float gap = theirs - mine;
            if (gap <= Margin)
                continue;
            wrong++;
            if (gap > worstGap)
            {
                worstGap = gap;
                worstRow = i;
            }
        }
        if (wrong == 0)
            yield break;

        // Potential rather than definitive, by this reader's own boundary: a known legitimate cause
        // exists, because TryFire's fallback can swap the held weapon on the tick it fires without
        // rewriting either expected-damage column, so the row is the two facts having been recorded a
        // few ticks apart rather than the arsenal having chosen wrongly. What would settle it is the
        // fallback recording its own swap; until it does, the row cannot distinguish the two.
        yield return new Finding(
            Severity.Potential,
            Name,
            $"{wrong} row(s) held a weapon whose rejected alternative scored higher, the worst by {worstGap:0.0} damage",
            $"At the worst row the chosen weapon was {weapon.Text[worstRow]} with the bow at {bow.Number[worstRow]:0.0} "
                + $"and the knife at {knife.Number[worstRow]:0.0} expected damage. The arsenal picks the larger of the "
                + "two, so a row like this means the columns and the choice were written at different moments: the "
                + $"choice is cached for a dozen ticks and the fallback in TryFire may swap the weapon on the tick it "
                + "fires without rescoring, which would produce exactly this and is the first place to look. What would "
                + "settle it is a column recording the swap, which is why this is potential and not definitive.",
            session.Tick(worstRow), session.Tick(worstRow), wrong);
    }
}

/// <summary>
/// Whether the companion went down. Pre-boss enemies against a companion with the player's ranged
/// stats should be kiteable indefinitely, so a down is a design failure and not bad luck; the
/// finding carries the run of damage that led to it, because the interesting part is the thirty
/// seconds before, never the tick itself. It also counts *transitions* into the downed state rather
/// than rows in it: 613 rows reading downed is one down lasting ten seconds, and reporting it as
/// 613 downs is a mistake this reader is written not to repeat.
/// </summary>
public sealed class TheCompanionStaysUp : ICheck
{
    public string Name => "did it go down, and what happened in the minute before";
    public string[] Needs => new[] { "state" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column state = session["state"];
        var downs = new List<int>();
        for (int i = 1; i < session.Count; i++)
            if (state.Text[i] == "downed" && state.Text[i - 1] != "downed")
                downs.Add(i);

        int rowsDown = 0;
        for (int i = 0; i < session.Count; i++)
            if (state.Text[i] == "downed")
                rowsDown++;

        if (downs.Count == 0)
        {
            if (rowsDown > 0)
                yield return new Finding(
                    Severity.Oddity,
                    Name,
                    $"the session opens with the companion already downed for {rowsDown} ticks",
                    "No transition into the downed state was recorded, so the down happened before this file opened.",
                    session.Tick(0), session.Tick(0), rowsDown);
            yield break;
        }

        foreach (int row in downs)
        {
            int from = Math.Max(0, row - 1800); // the half-minute before, at sixty a second
            var before = new Stretch(from, row - 1);
            string acting = session.Has("action") ? FindStretches.Summarise(session["action"], before, 3) : "unrecorded";
            string far = session.Has("npc_tile", "player_tile")
                ? $"{FindStretches.Mean(DistanceColumn(session), before):0} tiles from him on average"
                : "at an unrecorded distance from him";
            string danger = session.Has("self_threat")
                ? $"its own danger averaged {FindStretches.Mean(session["self_threat"], before):0.00}"
                : session.Has("danger") ? $"the danger to him averaged {FindStretches.Mean(session["danger"], before):0.00}"
                : "no danger column";

            yield return new Finding(
                Severity.Definitive,
                Name,
                "the companion went down, which pre-boss enemies should never manage against a kiting body",
                $"For the half-minute before it went down it was doing {acting}, {far}, and {danger}. "
                    + "A companion that can shoot, retreat and outrange everything before a boss has no mechanism by "
                    + "which it should die, so the question is which of those three it was not doing: the action tally "
                    + "says whether it was fighting or fleeing, the distance says whether help was anywhere near, and "
                    + "the danger reading says whether it knew.",
                session.Tick(row), session.Tick(row), 1);
        }

        if (downs.Count > 0)
            yield return new Finding(
                Severity.Oddity,
                Name,
                $"{downs.Count} down(s) across {rowsDown} ticks spent downed",
                "The tick count is time spent on the floor, not a count of deaths.",
                session.Tick(downs[0]), session.Tick(downs[^1]), downs.Count);
    }

    /// <summary>Distance to him per row, as a synthetic column so the stretch helpers can average it.</summary>
    internal static Column DistanceColumn(Session session)
    {
        var number = new float[session.Count];
        var text = new string[session.Count];
        for (int i = 0; i < session.Count; i++)
        {
            number[i] = session.TileDistance("npc_tile", "player_tile", i);
            text[i] = float.IsNaN(number[i]) ? "-" : number[i].ToString("0.0");
        }
        return new Column { Name = "distance_tiles", Text = text, Number = number };
    }
}
