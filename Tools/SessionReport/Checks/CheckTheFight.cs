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
        // `hurting` exists only in captures written before 15 September 2026, when a liquid still hurt the orb on
        // contact; every liquid is air to it since, and the recorder stopped writing the column. It is still read so an
        // older capture keeps its exclusion. `npc_hit` is required to be absent beside it for the reason the old rule
        // refused to exclude on submersion alone: a hit by something alive while the body happens to be in water is a
        // true finding, and excluding on the liquid alone would suppress it exactly as the breath suffix once suppressed two.
        Column? hurting = session.Find("hurting");
        Column? hitEvent = session.Find("npc_hit");

        var hits = new List<(int Row, float Lost, float Danger)>();
        int environmental = 0;
        for (int i = 1; i < session.Count; i++)
        {
            float before = life.Number[i - 1], after = life.Number[i];
            if (float.IsNaN(before) || float.IsNaN(after) || after >= before)
                continue;
            // Lava, fire and a hurting liquid all take life with no hostile in the world, so the
            // threat sense is *correct* to read zero on those ticks and counting them as unseen hits
            // turns a cave session into a page of definitive findings. This is the same exclusion
            // ScenarioCapture makes for the same reason, plus the liquid contact, which it does not
            // take damage from. Touching water is not the test on its own: a recorded hit event on the
            // same tick means something alive dealt it, and that is a true finding wherever the body
            // was floating at the time.
            bool burning = selfDanger != null && (selfDanger.Text[i].EndsWith('L') || selfDanger.Text[i].EndsWith('f'));
            bool scalded = hurting != null && hurting.Number[i] == 1f && (hitEvent == null || hitEvent.Text[i] == "-");
            if (burning || scalded)
            {
                environmental++;
                continue;
            }
            float reading = sense == null ? float.NaN : sense.Number[i];
            hits.Add((i, before - after, reading));
        }
        // Only a missing self_danger blinds the exclusion now: a capture with no `hurting` column is simply one written
        // after liquids stopped hurting the orb, and saying otherwise would print a false caveat on every new session.
        string aside = environmental == 0
            ? (selfDanger == null
                ? " This file carries no self_danger column, so a hit from fire cannot be told from a hit by something alive."
                : "")
            : $" A further {environmental} life loss(es) came from fire, or in an older capture from lava or a hurting liquid, and are excluded, "
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
        Column? huntReason = session.Find("hunt_reason");
        Column? planReason = session.Find("plan_reason");

        // No gap allowance, because a tick that fired *is* the break in the stretch. With one, a
        // healthy fight reads as one enormous finding: the reload ticks between two shots satisfy the
        // condition and a single "fired" row between them falls inside the allowance, so a two-minute
        // exchange folds into a single "nothing fired for 7,000 ticks" whose own tally shows the shots
        // it fired. The gap allowance is for a condition that flickers, and this one does not.
        //
        // `reachable` is hostiles that can reach the player, not hostiles hunting can shoot. A sealed
        // enemy the hunt already refused as no-reachable-firing-position is still reachable in that
        // column; counting it as "the target chooser refused everything" is the wrong question. The
        // stance's search refuses the same sealed enemy as no-use-reaches-target or
        // no-reachable-stand, read the same way; stands-undecided is not excluded, because an
        // unanswered search is not a proven absence and a stretch of it is the defect.
        var quiet = FindStretches.Where(session.Count, i =>
            reachable.Number[i] > 0f && shot.Number[i] == 0f && (fire == null || fire.Text[i] != "fired")
                && (fresh != null ? fresh.Number[i] > 0f : playerDead?.Text[i] != "1") && state?.Text[i] != "downed"
                && (huntReason == null || huntReason.Text[i].IndexOf("no-reachable-firing-position", StringComparison.Ordinal) < 0)
                && (planReason == null || (planReason.Text[i] != "no-use-reaches-target" && planReason.Text[i] != "no-reachable-stand")),
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
/// Whether Combat took the body while he was in danger. The stance's eagerness rows (F2, F3) say a
/// damageable hostile in reach makes Combat beat keeping company, and an enemy on him takes the
/// body from a vein; this is the capture-side grading of both. "In danger" is the brain's own
/// unsafe line — PlayerDanger at or above 0.25, where PlayerIsSafe stops holding — read back at it
/// rather than a threshold of this reader's choosing, and the finding carries what ran instead,
/// what Combat scored, and whether he was actually hit, so a selection loss reads differently from
/// a stance that offered nothing from range. A capture whose preamble disabled combat is excused
/// whole: not fighting when told not to is obedience, not reluctance.
/// </summary>
public sealed class CombatIsEagerWhenHeIsInDanger : ICheck
{
    /// <summary>The brain's own unsafe line: PlayerDanger below this is PlayerIsSafe.</summary>
    private const float Unsafe = 0.25f;

    /// <summary>One second. Shorter than any rescore cadence worth suspecting; a lost tick or two while the choice flips is noise.</summary>
    private const int MinTicks = 60;

    public string Name => "did combat take the body while he was in danger";
    public string[] Needs => new[] { "action", "danger" };

    public IEnumerable<Finding> Run(Session session)
    {
        if (FightPreference.CombatDisabled(session))
            yield break;
        Column action = session["action"], danger = session["danger"];
        Column? state = session.Find("state");
        Column? fresh = session.Find("brain_fresh");
        Column? playerDead = session.Find("player_dead");
        Column? playerHit = session.Find("player_hit");
        Column? nearThreat = session.Find("near_threat");
        Column? weaponReach = session.Find("weapon_reach");
        Column? combatScore = session.Find("combat_fin")
            ?? session.Find("hunt_fin") ?? session.Find("guard_fin");

        // The gap allowance is for the threshold boundary, not for flicker in the choice: danger
        // hovering at 0.25 crosses it every few ticks, and without an allowance one afternoon of
        // reluctance reads as a dozen one-line findings. Five ticks cannot hide a real takeover.
        foreach (var stretch in FindStretches.Where(session.Count, i =>
            danger.Number[i] >= Unsafe && !IsFighting(action.Text[i])
                && state?.Text[i] != "downed"
                && (fresh != null ? fresh.Number[i] > 0f : playerDead?.Text[i] != "1"),
            MinTicks, allowGap: 5))
        {
            int hits = 0;
            if (playerHit != null)
                for (int i = stretch.Start; i <= stretch.End; i++)
                    if (playerHit.Text[i] != "-")
                        hits++;
            string score = combatScore == null
                ? "no combat score column, so whether the stance offered anything is unanswerable"
                : $"combat scored {FindStretches.Mean(combatScore, stretch):0.00} on average, " +
                  $"{FindStretches.Max(combatScore, stretch):0.00} at best";
            string reach = nearThreat != null && weaponReach != null
                ? $" Nearest reachable hostile {FindStretches.Mean(nearThreat, stretch):0.0} tiles, " +
                  $"hands throw {FindStretches.Mean(weaponReach, stretch):0.0}."
                : "";
            string hurt = hits == 0 ? "He took no recorded hit in the stretch."
                : $"He took {hits} recorded hit(s) in the stretch while nothing fought for him.";
            yield return new Finding(
                Severity.Potential,
                Name,
                $"{stretch.Length} ticks with him in danger while {FindStretches.Summarise(action, stretch)} ran instead of combat",
                $"Danger to him peaked at {FindStretches.Max(danger, stretch):0.00} over the stretch; {score}.{reach} " +
                    $"{hurt} A high combat score beside another action is a selection loss; a score near zero is " +
                    "the stance offering nothing, which range explains and reluctance does not.",
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }

    internal static bool IsFighting(string label) => label is "combat" or "hunt" or "guard";
}

/// <summary>
/// Whether the ticks that read not-fighting had nothing to shoot. The fire column reads
/// not-fighting exactly when Combat is not the running activity, so a stretch of it beside a
/// reachable hostile inside weapon range is the eagerness row F1 failing in the file: something
/// shootable was there and no fight ran. The finding carries what ran instead and the best plan
/// value Combat offered over the stretch, which separates the two failures — a plan offered and
/// outscored is a selection loss, no plan at all beside a target in range is the stance not
/// seeing the shot. No gear column exists, so an unarmed stretch reads the same as a blind one;
/// the finding says so rather than guessing.
/// </summary>
public sealed class NotFightingMeansNothingToShoot : ICheck
{
    /// <summary>One second. A lost comparison that Combat wins back on the next rescore is not reluctance.</summary>
    private const int MinTicks = 60;

    public string Name => "was there something to shoot on the ticks it was not fighting";
    public string[] Needs => new[] { "fire", "near_threat", "weapon_reach" };

    public IEnumerable<Finding> Run(Session session)
    {
        if (FightPreference.CombatDisabled(session))
            yield break;
        Column fire = session["fire"], near = session["near_threat"], reach = session["weapon_reach"];
        Column? action = session.Find("action");
        Column? state = session.Find("state");
        Column? fresh = session.Find("brain_fresh");
        Column? playerDead = session.Find("player_dead");
        Column? planValue = session.Find("plan_value");
        Column? weapon = session.Find("weapon");
        Column? engage = session.Find("engage");

        // Unmeasured distances write "-", which parses to NaN and fails both comparisons, so a row
        // nobody measured is excluded without a special case. The gap allowance is the range
        // boundary's: a hostile pacing at the edge of the throw crosses it every few ticks.
        foreach (var stretch in FindStretches.Where(session.Count, i =>
            fire.Text[i] == "not-fighting" && near.Number[i] <= reach.Number[i]
                && state?.Text[i] != "downed"
                && (fresh != null ? fresh.Number[i] > 0f : playerDead?.Text[i] != "1"),
            MinTicks, allowGap: 5))
        {
            string running = action == null ? "an unrecorded activity" : FindStretches.Summarise(action, stretch);
            string offered = planValue == null ? "no plan value column"
                : FindStretches.Max(planValue, stretch) > 0f
                    ? $"combat offered a plan worth {FindStretches.Max(planValue, stretch):0.000}" +
                      (weapon == null ? "" : $" ({FindStretches.Summarise(weapon, stretch)})") +
                      ", and selection still ran something else"
                    : "combat offered no plan at all beside a target in range";
            string engaging = engage == null ? "" : $" Engage target: {FindStretches.Summarise(engage, stretch, 3)}.";
            yield return new Finding(
                Severity.Potential,
                Name,
                $"{stretch.Length} ticks not fighting while a reachable hostile stood inside weapon range",
                $"Nearest hostile {FindStretches.Mean(near, stretch):0.0} tiles against a throw of " +
                    $"{FindStretches.Mean(reach, stretch):0.0}; {running} ran instead and {offered}.{engaging} " +
                    "If the weapon slots held nothing this stretch, not-fighting is correct and the file " +
                    "cannot say: no gear column exists, so an unarmed stretch reads the same as a blind one.",
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }
}

/// <summary>
/// Whether a committed plan was actually performed: the body reached its stand and a planned use
/// fired from there. A combat stretch with a named stand that never arrives and never fires is the
/// hunt-arrived-and-did-nothing class, now graded on the plan columns rather than on hunt labels.
/// Captures without plan_id skip rather than reading clean.
/// </summary>
public sealed class TheCommittedPlanWasPerformed : ICheck, ICheckCoverage
{
    private const int MinTicks = 120;
    private const float ArrivalPixels = 64f;

    public string Name => "was the committed plan performed";
    public string[] Needs => new[] { "action", "plan_id", "plan_stand", "npc_px", "fire" };

    public string? Missing(Session session)
        => session.Find("plan_id") == null ? "plan_id" : null;

    public IEnumerable<Finding> Run(Session session)
    {
        Column action = session["action"], planId = session["plan_id"], stand = session["plan_stand"],
            body = session["npc_px"], fire = session["fire"];
        foreach (var stretch in FindStretches.Where(session.Count, i =>
            CombatIsEagerWhenHeIsInDanger.IsFighting(action.Text[i]) && planId.Number[i] > 0
                && stand.Text[i] != "-",
            MinTicks, allowGap: 5))
        {
            bool arrived = false, fired = false;
            for (int i = stretch.Start; i <= stretch.End; i++)
            {
                if (fire.Text[i] == "fired")
                    fired = true;
                if (Distance(body.Text[i], stand.Text[i]) <= ArrivalPixels)
                    arrived = true;
            }
            if (arrived && fired)
                continue;
            yield return new Finding(
                Severity.Potential,
                Name,
                $"{stretch.Length} ticks of combat on a named stand that {(arrived ? "was reached" : "was never reached")} and {(fired ? "fired" : "never fired")}",
                "A plan is performed when the body arrives at its stand and a planned use fires. " +
                    "Arriving and not shooting is the old arrived-hunt stall; shooting only while travelling is the wait the ordained hold exists to keep.",
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }

    internal static float Distance(string npcPx, string stand)
    {
        if (!TryParsePoint(npcPx, out float ax, out float ay) || !TryParsePoint(stand, out float bx, out float by))
            return float.PositiveInfinity;
        float dx = ax - bx, dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static bool TryParsePoint(string text, out float x, out float y)
    {
        x = y = 0f;
        int comma = text.IndexOf(',');
        if (comma <= 0)
            return false;
        return float.TryParse(text.AsSpan(0, comma), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out x)
            && float.TryParse(text.AsSpan(comma + 1), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out y);
    }
}

/// <summary>
/// Whether combat flickered: a new plan id every few ticks is B11 failing in the file. Counted per
/// minute of combat ticks, so a long quiet fight does not hide a burst of churn and a short fight
/// does not invent one. Captures without plan_id skip.
/// </summary>
public sealed class CombatDoesNotFlicker : ICheck, ICheckCoverage
{
    /// <summary>More than one new plan every two seconds of fighting, over a minute of combat, is churn rather than a changing fight.</summary>
    private const float MaxChangesPerMinute = 30f;
    private const int MinCombatTicks = 360;

    public string Name => "did combat flicker between plans";
    public string[] Needs => new[] { "action", "plan_id" };

    public string? Missing(Session session)
        => session.Find("plan_id") == null ? "plan_id" : null;

    public IEnumerable<Finding> Run(Session session)
    {
        Column action = session["action"], planId = session["plan_id"];
        int combatTicks = 0, changes = 0;
        float last = float.NaN;
        for (int i = 0; i < session.Count; i++)
        {
            if (!CombatIsEagerWhenHeIsInDanger.IsFighting(action.Text[i]) || planId.Number[i] <= 0)
            {
                last = float.NaN;
                continue;
            }
            combatTicks++;
            float id = planId.Number[i];
            if (!float.IsNaN(last) && id != last)
                changes++;
            last = id;
        }
        if (combatTicks < MinCombatTicks)
            yield break;
        float perMinute = changes * 3600f / combatTicks;
        if (perMinute <= MaxChangesPerMinute)
            yield break;
        yield return new Finding(
            Severity.Potential,
            Name,
            $"{changes} plan changes over {combatTicks} combat ticks ({perMinute:0.0} per minute)",
            "Fighting is a held episode. A new plan every couple of seconds is the companion flickering in place, not adapting to a changing fight.",
            session.Tick(0), session.Tick(session.Count - 1), combatTicks);
    }
}

/// <summary>
/// Whether a hitting fight kept a positive final while another job ran. Capture
/// 2026-09-18_16-35-26-353 dumped combat at raw 1.02 to final 0.00 via reunion on the tick after it
/// won, and company 0.05 took the body. The evaluator no longer zeros a ServesPlayerDirectly fight
/// for a long path to its stand; a stretch of this shape is that dump surviving in the file.
/// </summary>
public sealed class AHittingFightKeptItsScore : ICheck, ICheckCoverage
{
    private const float RawHeld = 0.5f;
    private const float FinalGone = 0.02f;
    private const int MinTicks = 30;

    public string Name => "did a hitting fight keep its score while another job ran";
    public string[] Needs => new[] { "action", "combat_raw", "combat_fin" };

    public string? Missing(Session session)
        => session.Find("combat_raw") == null ? "combat_raw" : session.Find("combat_fin") == null ? "combat_fin" : null;

    public IEnumerable<Finding> Run(Session session)
    {
        Column action = session["action"], raw = session["combat_raw"], fin = session["combat_fin"];
        foreach (var stretch in FindStretches.Where(session.Count, i =>
            !CombatIsEagerWhenHeIsInDanger.IsFighting(action.Text[i])
                && raw.Number[i] >= RawHeld && fin.Number[i] < FinalGone,
            MinTicks, allowGap: 5))
        {
            yield return new Finding(
                Severity.Potential,
                Name,
                $"{stretch.Length} ticks with combat raw {FindStretches.Mean(raw, stretch):0.00} collapsed to final {FindStretches.Mean(fin, stretch):0.00} while {FindStretches.Summarise(action, stretch)} ran",
                "A hitting plan that still serves him must keep a positive final. Raw held and final near zero is the reunion (or protection) excursion zero that dumps the fight to company the next tick.",
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }
}

/// <summary>
/// Whether combat stayed Unresolved:budget-cut while he was in danger with several hostiles in
/// range. The 15-slime window of 2026-09-18_16-35-26-353 was 922 of 934 ticks unpriced, four Yellow
/// Slime hits, life 100 to 40. A cut that priced nothing now offers from here; a stretch of this
/// shape is the crowd still deleting the fight.
/// </summary>
public sealed class CombatWasPricedInACrowd : ICheck, ICheckCoverage
{
    private const float Unsafe = 0.25f;
    private const int MinThreats = 3;
    private const int MinTicks = 60;

    public string Name => "was combat priced while he was in a crowd";
    public string[] Needs => new[] { "action", "danger", "plan_reason", "threats" };

    public string? Missing(Session session)
        => session.Find("plan_reason") == null ? "plan_reason" : session.Find("threats") == null ? "threats" : null;

    public IEnumerable<Finding> Run(Session session)
    {
        if (FightPreference.CombatDisabled(session))
            yield break;
        Column action = session["action"], danger = session["danger"], reason = session["plan_reason"],
            threats = session["threats"];
        foreach (var stretch in FindStretches.Where(session.Count, i =>
            reason.Text[i] == "budget-cut"
                && danger.Number[i] >= Unsafe
                && threats.Number[i] >= MinThreats
                && !CombatIsEagerWhenHeIsInDanger.IsFighting(action.Text[i]),
            MinTicks, allowGap: 5))
        {
            yield return new Finding(
                Severity.Potential,
                Name,
                $"{stretch.Length} ticks of budget-cut with {FindStretches.Max(threats, stretch):0} hostiles while {FindStretches.Summarise(action, stretch)} ran",
                "A crowd that exhausts the search must still offer a from-here shot. Unresolved:budget-cut here is combat deleted, not a fight that lost a comparison.",
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }
}

/// <summary>
/// Whether the capture's preamble disabled fighting, in either label era: `combat=false` now,
/// `hunting=false` before 0.39.0. A mid-session flip from the profile card is an occurrence, not
/// preamble, so a session that toggles combat halfway reads under the starting value; the fight
/// checks document that rather than joining the events stream for a toggle almost nobody makes.
/// </summary>
internal static class FightPreference
{
    internal static bool CombatDisabled(Session session)
    {
        if (!session.Metadata.TryGetValue("config", out string? config) || config == null)
            return false;
        foreach (string part in config.Split(';'))
        {
            string[] kv = part.Split('=');
            if (kv.Length != 2)
                continue;
            if ((kv[0] == "combat" || kv[0] == "hunting")
                && kv[1].Equals("false", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
        // Outcome-aware arsenals can deliberately choose less immediate damage to
        // remove an urgent threat. The old damage-only comparison cannot judge them.
        if (session.Has("attack_value")) yield break;
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
/// Finds sustained fresh range-only rejections even while the body moves. Arsenal.BestTarget
/// records a bounded shortlist, independent of the pursuit target; this evidence can suggest an
/// unproductive approach but cannot establish that all possible attacks or destinations failed.
/// Matches `combat` as well as `hunt`: the merged stance closes either side for a proven absence
/// of any firing position, so a sustained stretch under it is the same shape whatever side runs.
/// </summary>
public sealed class HuntingHadAWeaponThatCouldReach : ICheck
{
    /// <summary>300 consecutive samples warrant inspection; they do not certify physical impossibility.</summary>
    private const int Sustained = 300;

    public string Name => "could it reach what it was hunting";
    public string[] Needs => new[] { "action", "brain_fresh", "fire", "target_evidence", "target_evidence_age" };

    public IEnumerable<Finding> Run(Session s)
    {
        foreach (var span in FindStretches.Where(s.Count, i => s["action"].Text[i] is "hunt" or "combat"
            && s["brain_fresh"].Number[i] == 1
            && s["fire"].Text[i] is not ("fired" or "cooldown")
            && s["target_evidence_age"].Number[i] == 0
            && RecordedPairsAreOutsideReach(s["target_evidence"].Text[i]), Sustained))
            yield return new Finding(Severity.Potential, Name,
                "hunting persisted while every freshly recorded attack pair was outside reach",
                $"{span.Length} consecutive samples (inspection threshold {Sustained}) contain only outside-reach "
                + $"rejections in the recorded shortlist, with no shot taken. Fire outcomes {FindStretches.Summarise(s["fire"], span)}. "
                + "The shortlist is bounded and the arsenal's targets need not be the pursuit target. This does not "
                + "establish that every weapon-target pair was considered or that a useful firing position was impossible. "
                + "Compare pursuit identity, destination validity, route progress and granted controls to distinguish "
                + "a legitimate long approach from invalid positioning, interruption or failed execution.",
                s.Tick(span.Start), s.Tick(span.End), span.Length);
    }

    private static bool RecordedPairsAreOutsideReach(string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return false;
        foreach (string pair in evidence.Split('|'))
        {
            // Rejection wire format from Arsenal.BestTarget. Accepted outcomes have a different
            // shape. Reject incomplete/unknown formats rather than extract a matching substring.
            string[] fields = pair.Split(':');
            if (fields.Length != 6 || !int.TryParse(fields[0], out int target) || target < 0
                || !long.TryParse(fields[1], out long generation) || generation < 0
                || fields[2] != "0" || fields[3] != "0"
                || !fields[4].StartsWith("weapon=", StringComparison.Ordinal)
                || !int.TryParse(fields[4].AsSpan(7), out int weapon) || weapon < 0
                || fields[5] != "outside-reach") return false;
        }
        return true;
    }
}

/// <summary>
/// Counts transitions into downing rather than samples spent downed. A recorded down is an
/// outcome to investigate; this observation alone cannot identify its cause or avoidability.
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
                    $"the session opens with the companion already downed for {rowsDown} samples",
                    "No transition into the downed state was recorded, so the down happened before this file opened.",
                    session.Tick(0), session.Tick(0), rowsDown);
            yield break;
        }

        foreach (int row in downs)
        {
            // Bound the inspected history by samples. Sparse captures do not justify turning
            // this count into seconds, or treating an unsampled interval as fully observed.
            int from = Math.Max(0, row - 1800);
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
                Severity.Potential,
                Name,
                "the companion went down; whether it could have avoided this needs investigation",
                $"In the preceding {row - from} recorded samples (ticks {session.Tick(from)}..{session.Tick(row - 1)}) "
                    + $"it was doing {acting}, {far}, and {danger}. "
                    + "Downing is observed, but these fields do not establish enemy progression, safe escape access, "
                    + "available attacks or whether the damage was avoidable. Compare victim-specific threat forecasts, "
                    + "actual damage, granted safety controls and native movement outcomes before attributing the result "
                    + "to perception, selection or execution. Missing evidence remains an investigation limit.",
                session.Tick(row), session.Tick(row), 1);
        }

        if (downs.Count > 0)
            yield return new Finding(
                Severity.Oddity,
                Name,
                $"{downs.Count} down(s) across {rowsDown} samples recorded downed",
                "Downs count observed transitions; repeated downed samples are not additional deaths or a wall-time measurement.",
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
