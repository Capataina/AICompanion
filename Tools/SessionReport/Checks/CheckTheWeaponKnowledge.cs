#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether the weapon knowledge was calibrated on this capture: every companion shot paired with
/// the flight its projectile actually flew, per projectile type.
///
/// A shot record carries what the simulator predicted — the tick of flight its use was to land on
/// — and the shot-event written at that projectile's death carries what happened: the first body
/// hit and the update it landed on. The two join on the projectile's stable id. Per type the check
/// grades the pairing rate and the median gap between the predicted impact tick and the first hit's,
/// and every finding names the flight law standing behind the type, so a bad law is found from the
/// capture rather than from a feeling in play.
///
/// What this cannot convict on its own is deliberate. A first hit landing late on another body than
/// the aimed one is a forecast error — the target moved — wearing a timing error's clothes, so the
/// finding carries the share of first hits that landed on the aimed type beside the median gap: a
/// high share with a wide gap points at the law, a low share points at the motion. Volley siblings
/// have shot-events but no shot record of their own, so only the pairing direction from shots to
/// events is graded; events without a shot are expected and ignored.
/// </summary>
public sealed class WeaponKnowledgeIsCalibrated : ICheck, ICheckCoverage
{
    public string Name => "is the weapon knowledge calibrated on this capture";
    public string[] Needs => new[] { "tick" };

    /// <summary>How many paired first hits a type needs before its timing is graded rather than reported.</summary>
    private const int MinPairedHits = 5;

    /// <summary>How far the median first hit may land from its predicted impact tick, in flight updates.</summary>
    private const int TimingToleranceUpdates = 10;

    /// <summary>How many shots a weapon needs before its pairing rate is graded rather than reported.</summary>
    private const int MinShotsForPairing = 5;

    /// <summary>The share of a weapon's shots that must meet their shot-event.</summary>
    private const double PairingFloor = 0.8;

    public string? Missing(Session session)
        => CheckEvents.SidecarUnavailable(session, "the shot and shot-event records");

    public IEnumerable<Finding> Run(Session session)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        var shots = log.Events.Where(e => e.kind == "shot").ToList();
        if (shots.Count == 0)
            yield break;
        var events = log.Events.Where(e => e.kind == "shot-event").ToList();
        var byProjectile = new Dictionary<int, GodsEyeEvent>();
        foreach (GodsEyeEvent e in events)
            byProjectile[e.subject] = e;
        var npcTypeByStable = new Dictionary<int, int>();
        foreach (GodsEyeEvent e in log.Events.Where(e => e.kind == "npc-spawn"))
            if (int.TryParse(e.related, out int type))
                npcTypeByStable[e.subject] = type;
        var lawByType = new Dictionary<int, GodsEyeEvent>();
        foreach (GodsEyeEvent e in log.Events.Where(e => e.kind == "flight-law"))
            if (int.TryParse(e.label, out int type))
                lawByType[type] = e;

        // The pairing rate, per weapon: a shot record names no projectile type, so this half groups
        // by the weapon that fired it.
        foreach (var weapon in shots.GroupBy(s => s.label))
        {
            var unpaired = weapon.Where(s => !byProjectile.ContainsKey(ProjectileOf(s))).ToList();
            if (weapon.Count() < MinShotsForPairing || (double)(weapon.Count() - unpaired.Count) / weapon.Count() >= PairingFloor)
                continue;
            yield return CheckEvents.Aggregate(
                Severity.Potential,
                Name,
                $"{weapon.Key} paired {weapon.Count() - unpaired.Count} of {weapon.Count()} shots with their flights",
                unpaired,
                $"A shot without its shot-event is a flight the knowledge cannot be graded on, so a weapon that loses " +
                $"more than a fifth of its shots is hiding whatever its model does wrong. The floor is {PairingFloor:0%} over " +
                $"at least {MinShotsForPairing} shots. Volley siblings are not the gap: they own shot-events without shot " +
                $"records, which is the pairing direction this half never grades.");
        }

        // The timing gap, per projectile type: the predicted impact tick against the first hit's.
        var paired = new List<(GodsEyeEvent Shot, GodsEyeEvent Event)>();
        foreach (GodsEyeEvent shot in shots)
        {
            int projectile = ProjectileOf(shot);
            if (projectile >= 0 && byProjectile.TryGetValue(projectile, out GodsEyeEvent? e))
                paired.Add((shot, e));
        }
        foreach (var type in paired.GroupBy(p => p.Event.label))
        {
            var gaps = new List<int>();
            int aimed = 0, aimedKnown = 0, hits = 0;
            foreach ((GodsEyeEvent shot, GodsEyeEvent e) in type)
            {
                if (e.amount <= 0)
                    continue;
                hits++;
                if (FirstHit(e.detail) is (_, int hitTick)
                    && int.TryParse(shot.Field("expected-flight-ticks"), out int predicted) && predicted >= 0)
                    gaps.Add(Math.Abs(predicted - hitTick));
                if (int.TryParse(shot.related, out int aimedStable)
                    && npcTypeByStable.TryGetValue(aimedStable, out int aimedType)
                    && FirstHit(e.detail) is (int landedType, _))
                {
                    aimedKnown++;
                    if (landedType == aimedType)
                        aimed++;
                }
            }
            if (gaps.Count < MinPairedHits)
                continue;
            gaps.Sort();
            double median = gaps.Count % 2 == 1
                ? gaps[gaps.Count / 2]
                : (gaps[gaps.Count / 2 - 1] + gaps[gaps.Count / 2]) / 2.0;
            if (median <= TimingToleranceUpdates)
                continue;
            string law = "no flight law was recorded for this type, so the prior flew every shot";
            if (int.TryParse(type.Key, out int typeId) && lawByType.TryGetValue(typeId, out GodsEyeEvent? lawEvent))
                law = $"law revision {lawEvent.related} ({lawEvent.channel}, residual {lawEvent.Field("residual")}, " +
                    $"evidence {lawEvent.Field("evidence")}, terms {lawEvent.Field("terms")})";
            var weapons = string.Join(", ", type.Select(p => p.Shot.label).Distinct().OrderBy(w => w));
            yield return CheckEvents.Aggregate(
                Severity.Potential,
                Name,
                $"projectile type {type.Key} lands a median {median:0} updates from its predicted tick ({gaps.Count} first hits)",
                type.Select(p => p.Event).OrderBy(e => e.tick).ToList(),
                $"Fired as {weapons}; {hits} of {type.Count()} paired shots hit anything, and {aimed} of {aimedKnown} first hits " +
                $"with a known aimed body landed on its type. The tolerance is a median gap of {TimingToleranceUpdates} updates " +
                $"over at least {MinPairedHits} first hits. {Capitalize(law)}. Read the aimed share beside the gap: a high share " +
                $"with a wide gap points at the law, a low share points at bodies that moved.");
        }
    }

    /// <summary>The projectile stable id a shot record points at, or -1 where the link is absent or malformed.</summary>
    private static int ProjectileOf(GodsEyeEvent shot)
        => shot.ChannelField("projectile") is { } link && int.TryParse(link, out int projectile) ? projectile : -1;

    /// <summary>
    /// The first hit's NPC type and flight update. Parsed by hand rather than through the payload
    /// reader because the value nests semicolons: <c>first-hit=type=3;tick=41;damage=10</c> sits inside
    /// a semicolon-joined detail beside <c>first-wall=tick=..</c>, so the payload reader's first-wins
    /// <c>tick</c> is the wall's whenever a wall came first. Everything past <c>first-hit=</c> holds no
    /// other tick, so the first tick past it is the hit's.
    /// </summary>
    private static (int Type, int Tick)? FirstHit(string detail)
    {
        int start = detail.IndexOf("first-hit=", StringComparison.Ordinal);
        if (start < 0)
            return null;
        // Past the prefix itself, because the payload reader matches keys at segment starts and the
        // type sits glued to it; what follows holds no other tick, so the first tick past it is the hit's.
        string tail = detail.Substring(start + "first-hit=".Length);
        string? type = ReadGodsEyeEvents.Field(tail, "type");
        string? tick = ReadGodsEyeEvents.Field(tail, "tick");
        return type != null && tick != null && int.TryParse(type, out int t) && int.TryParse(tick, out int u)
            ? (t, u)
            : null;
    }

    private static string Capitalize(string text)
        => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
}
