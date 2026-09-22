#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether the companion kept company while a dark tile the player's own smart cursor was offering him stayed unlit. The
/// owner's rule is that if his cursor can place a torch in the dark, the companion can place one too, and the unlit statue
/// area of 15 September was the case that broke it: keeping company won for minutes while his cursor offered torch spots
/// in the dark. The recorder writes, every row, the tile his cursor would offer from where he stands, that tile's light,
/// and the stage at which lighting refuses it, so this is a count of stretches rather than a reconstruction.
///
/// <para>A potential issue rather than a definitive one, because a stretch can be correct: a tile whose stand is sealed
/// is unreachable for the companion however dark it is. The stage is reported with it so the reader can tell a sealed
/// stand from a search that stopped short or a job outbid by keeping company.</para>
/// </summary>
public sealed class TorchesGoWhereHisCursorWould : ICheck
{
    /// <summary>Three seconds at sixty a tick: past the few rescores a search takes to catch up with a player who has just
    /// walked into the dark, and short enough that a person standing there would already be reaching for a torch.</summary>
    private const int MinTicks = 180;

    /// <summary>Rows a stretch may miss and still be one stretch: a tick or two of another action between comparisons.</summary>
    private const int AllowGap = 10;

    public string Name => "did lighting leave a dark tile his own cursor offered while keeping company won";
    public string[] Needs => new[] { "action", "torch_reference", "torch_reference_dark", "torch_reference_stage" };

    public IEnumerable<Finding> Run(Session session)
    {
        Column action = session["action"], tile = session["torch_reference"], dark = session["torch_reference_dark"], stage = session["torch_reference_stage"];
        // Only a tile known to be dark. A carried tile, one a light somebody is carrying outshines and the sense never read
        // without it, is not a torch site under the placement rule, and a sky tile never is; neither staying unlit is a miss.
        bool Holds(int i) => action.Text[i] == "keep-company" && tile.Text[i] != "-" && dark.Text[i] == "dark";
        foreach (Stretch stretch in FindStretches.Where(session.Count, Holds, MinTicks, AllowGap))
        {
            // One tile at a time: the cursor offering the same dark tile throughout is that tile staying unlit, and a stretch
            // whose tile keeps changing is a player walking through the dark, which the stretch alone cannot tell apart.
            int start = -1, last = -1;
            for (int i = stretch.Start; i <= stretch.End + 1; i++)
            {
                bool inside = i <= stretch.End && Holds(i);
                if (inside && start >= 0 && tile.Text[i] == tile.Text[start]) { last = i; continue; }
                if (i <= stretch.End && !inside) continue;
                if (start >= 0 && last - start + 1 >= MinTicks) yield return Report(session, tile, stage, new Stretch(start, last));
                start = last = inside ? i : -1;
            }
        }
    }

    private Finding Report(Session session, Column tile, Column stage, Stretch run)
    {
        var stages = FindStretches.Tally(stage, run).Take(3).Select(p => $"{p.Key} {100f * p.Value / run.Length:0}%");
        string light = session.Has("torch_reference_light") ? $" at a mean light of {FindStretches.Mean(session["torch_reference_light"], run):0.000}" : "";
        return new Finding(
            Severity.Potential,
            Name,
            $"{run.Length} ticks keeping company while his cursor offered the dark tile {tile.Text[run.Start]}",
            $"The player's own smart cursor offered a torch spot at {tile.Text[run.Start]}{light}, the tile stayed unlit, and "
                + $"keeping company held the body throughout. Lighting refused that tile at: {string.Join(", ", stages)}. A "
                + "stand-unreachable stage is a sealed tile and correct; lit, placer-refused or search-cut is a rule or a "
                + "search disagreeing with his cursor, and passed-every-stage is lighting losing the comparison.",
            session.Tick(run.Start), session.Tick(run.End), run.Length);
    }
}

/// <summary>
/// Whether keeping company won while the player stood idle beside a hostile hunting itself rated usable. Hunting is the
/// opportunistic thing the companion does when little else is going on, and an idle player with a killable enemy nearby is
/// exactly that; the 15 September capture kept company through stretches like it. Reported with hunting's own factors, so
/// a hunt that lost on its value can be told from one that lost on the time it would take or on the ordering. Reads the
/// `combat_*` columns with the `hunt_*` ones as fallback, because the merged stance renamed them; a capture with neither
/// has no offer to judge and the check stays quiet.
/// </summary>
public sealed class HuntsWorthTakingAreTaken : ICheck
{
    /// <summary>Three seconds at sixty a tick: long enough that a hunt which was going to win would have.</summary>
    private const int MinTicks = 180;

    /// <summary>About twenty tiles, in the recorder's tiles to the nearest reachable hostile: well inside one screen, near
    /// enough that a player would expect his companion to deal with it.</summary>
    private const float NearTiles = 20f;

    /// <summary>A player moving no faster than this many pixels a tick is standing or shuffling, not travelling.</summary>
    private const float IdleSpeed = 0.5f;

    private const int AllowGap = 10;

    public string Name => "did keeping company win while an idle player stood near a hunt rated usable";
    public string[] Needs => new[] { "action", "near_threat", "player_vel" };

    public IEnumerable<Finding> Run(Session session)
    {
        string prefix = session.Has("combat_offer") ? "combat" : "hunt";
        if (!session.Has(prefix + "_offer")) yield break;
        Column action = session["action"], offer = session[prefix + "_offer"], near = session["near_threat"], velocity = session["player_vel"];
        bool Holds(int i)
            => action.Text[i] == "keep-company"
                && offer.Text[i].StartsWith("Usable:", StringComparison.Ordinal)
                && !float.IsNaN(near.Number[i]) && near.Number[i] <= NearTiles
                && Session.TryPair(velocity.Text[i], out float vx, out float vy) && MathF.Sqrt(vx * vx + vy * vy) <= IdleSpeed;
        foreach (Stretch stretch in FindStretches.Where(session.Count, Holds, MinTicks, AllowGap))
        {
            // **This check's own evidence survives 0.46.0 and two of its decorations do not, so it
            // names their absence rather than skipping.** A named skip is for a question the capture
            // cannot answer; the question here — a usable hunt losing to keeping company beside an idle
            // player — is answered by `_offer`, `_raw` and `_fin`, all of which the course still writes.
            // What went with the family chooser is `_time`, its per-activity time discount, and
            // `_funnel`, its preparation-time shortlist. Dropping them from the sentence silently would
            // leave a reader comparing two findings from two schemas and concluding the factors moved.
            bool retired = !CompletedTransferClaimsWereReceived.SchemaBelow(session, CompletedTransferClaimsWereReceived.ChooserColumnsRetired);
            var factors = new List<string>();
            foreach (string column in new[] { prefix + "_raw", prefix + "_fin", prefix + "_time", "keep-company_fin" })
                if (session.Has(column)) factors.Add($"{column} {FindStretches.Mean(session[column], stretch):0.000}");
                else if (retired && column.EndsWith("_time", StringComparison.Ordinal))
                    factors.Add($"{column} retired at {CompletedTransferClaimsWereReceived.ChooserColumnsRetired} with the family chooser, "
                        + "having been a constant 1.000 by decision before that");
            string offers = string.Join(", ", FindStretches.Tally(offer, stretch).Take(2).Select(p => $"{p.Key} {100f * p.Value / stretch.Length:0}%"));
            string funnel = session.Has(prefix + "_funnel")
                ? $" Hunting's furthest candidate stopped at: {string.Join(", ", FindStretches.Tally(session[prefix + "_funnel"], stretch).Take(2).Select(p => $"{p.Key} {100f * p.Value / stretch.Length:0}%"))}."
                : retired
                    ? $" Where the candidate stopped is not in this capture: `{prefix}_funnel` was the family chooser's preparation-time "
                      + $"shortlist and went at schema {CompletedTransferClaimsWereReceived.ChooserColumnsRetired}; the course's nearest "
                      + "equivalent is the decision occurrence's `course-refused:<reason>` tally, which counts refusals per domain rather "
                      + "than naming the stage one candidate reached."
                    : "";
            yield return new Finding(
                Severity.Potential,
                Name,
                $"{stretch.Length} ticks keeping company with an idle player and a usable hunt within {FindStretches.Max(near, stretch):0.0} tiles",
                $"Hunting offered a usable shot ({offers}) at a hostile no further than {NearTiles:0} tiles away while the player "
                    + $"stood idle, and keeping company won every comparison. Hunting's factors, mean over the stretch: "
                    + $"{string.Join(", ", factors)}.{funnel} A hunt valued below keeping company is the scoring's call; a hunt "
                    + "whose final is far below its raw value lost on a factor, which the decision occurrence names.",
                session.Tick(stretch.Start), session.Tick(stretch.End), stretch.Length);
        }
    }
}
