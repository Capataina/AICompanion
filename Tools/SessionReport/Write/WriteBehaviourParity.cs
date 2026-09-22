#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// One row per behaviour the README says the companion is responsible for, naming the fixture that
/// grades it headlessly and what this capture's own checks said about it.
///
/// <para><b>It is a coverage table before it is a comparison, and the empty rows are the point.</b>
/// Every check in this tool fires on a condition somebody imagined, so a behaviour nobody wrote a
/// fixture or a check for produces no finding and no skip — it is invisible, in the way the census
/// this folder already prints above the findings exists to stop for *categories of event*. The
/// README's Behaviour By Behaviour table is the closest thing this project has to a specification, so
/// it is the list to check coverage against, and a row here reading "no fixture" is a behaviour whose
/// only evidence is somebody watching the game.</para>
///
/// <para><b>The README is read at run time and the mapping is declared here.</b> Reading it means a
/// behaviour added to the specification appears as an unmapped row rather than being silently absent,
/// which is the same property the coverage block has for a missing column. Declaring the mapping means
/// it is a claim somebody made and can be argued with, rather than a keyword match that would pair a
/// row about protecting the player with every check whose name holds the word "player".</para>
///
/// <para><b>What this does not read is the fixture's own verdict.</b> A case's pass or fail lives in
/// `Tools/Ledger/runs/`, which is a store of whichever runs happen to be committed at whichever
/// commits, and a report about one capture that changed its verdict depending on the state of that
/// store would be reporting on the store. So the column is headed `cases N` and counts names, and
/// reading a named case's last verdict is the ledger's own scoreboard's job. The heading matters
/// beside the verdict column: read as "fixtures 2" next to "play agrees" it invites "two fixtures pass
/// and the play agrees with them", which is two claims this page does not make.</para>
///
/// <para><b>A check that ran and found nothing is three answers, and the verdict says which.</b> See
/// <see cref="Witness"/> for what went wrong when it was one.</para>
/// </summary>
public static class WriteBehaviourParity
{
    /// <summary>The heading the behaviour table sits under, and the marker its rows begin with.</summary>
    private const string Heading = "# Behaviour By Behaviour";

    /// <summary>
    /// One behaviour, the fixtures that grade it and the checks here that speak to it.
    ///
    /// <para>The behaviour is matched against the README's own bold name, so a renamed row in the
    /// specification shows up as an unmapped behaviour and a stale entry here shows up as a mapping
    /// naming nothing — both loud, neither silent. The fixture names are the case names the instruments
    /// register, pinned by the self-test against the source that registers them. The checks are types
    /// rather than names, so renaming a check class breaks the build here instead of quietly unpairing
    /// a row.</para>
    /// </summary>
    /// <summary>
    /// Whether this capture contains anything of the behaviour for a check to grade.
    ///
    /// <para><b>A silent check is not agreement, and treating it as agreement is the mirror of the
    /// false positives this page was built in the same change as.</b> Before this existed the table
    /// said the play agreed with <i>Self-preservation</i> on a session with zero damage events and zero
    /// downings, with <i>Recovering when it cannot follow</i> on one where `recovery_active` was false
    /// on all 2,340 rows, and with <i>Breaking containers</i> on one whose census never admitted a pot;
    /// the same thirteen agreements appeared on a different capture from four days earlier, which is
    /// the tell that the number was a function of which checks ran rather than of the play. A false
    /// negative in a coverage table is worse than a false positive in a findings block, because the
    /// findings block is read adversarially and the coverage table is read as reassurance.</para>
    ///
    /// <para>The answer is three-valued for the same reason the light and reach senses are: <c>true</c>
    /// the behaviour occurred, <c>false</c> it provably did not, and <c>null</c> this capture cannot
    /// say — either because the column or occurrence the witness reads is absent, or because no witness
    /// is declared for the row at all. A row with no witness reads as silent-and-unwitnessed rather
    /// than being promoted to agreement, so declaring one is what *earns* the word "agrees".</para>
    /// </summary>
    private sealed record Witness(string What, Func<Session, bool?> Occurred);

    private sealed record Mapped(string Behaviour, string[] Fixtures, Type[] Checks, Witness? Saw = null);

    /// <summary>A witness over a text column: absent column means the capture cannot say.</summary>
    private static Witness Column(string what, string column, Func<string, bool> holds)
        => new(what, session => session.Find(column) is not { } found ? null
            : Enumerable.Range(0, session.Count).Any(i => holds(found.Text[i])));

    /// <summary>A witness over the sidecar's occurrence kinds.</summary>
    private static Witness Occurrence(string what, string kind)
        => new(what, session =>
        {
            GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
            return !log.Present ? null : log.Events.Any(e => string.Equals(e.kind, kind, StringComparison.Ordinal));
        });

    /// <summary>A witness asking whether a domain was ever admitted usable by the course's own census.
    /// A domain the world never offered is a behaviour the play had no chance to exercise.</summary>
    private static Witness Admitted(string what, string domain)
        => new(what, session =>
        {
            GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
            if (!log.Present) return null;
            bool any = false;
            foreach (GodsEyeEvent e in log.Events)
            {
                if (!string.Equals(e.kind, "decision", StringComparison.Ordinal)) continue;
                var admitted = ACensusAdmissionSurvivesItsBinder.ReadAdmissions(e.detail);
                if (admitted.Count == 0) continue;
                any = true;
                if (admitted.TryGetValue(domain, out var entry) && entry.Usable > 0) return true;
            }
            return any ? false : (bool?)null;
        });

    private static readonly Mapped[] Mapping =
    {
        new("Getting out of the player's way",
            new[] { "courtesy stillness does not depend on what ran before" },
            Array.Empty<Type>()),
        new("Boss and event behaviour",
            Array.Empty<string>(),
            Array.Empty<Type>()),
        new("Reading where the player is going",
            new[]
            {
                "the player's intent region holds the player on every recorded row and has the shape the owner ruled",
                "keeping the player company is moving about his whole region: never still, never trailing, and moving from the first tick",
                "following responds to a player who departs",
            },
            new[] { typeof(FollowingRespondsAfterDeparture) }),
        new("Enemy selection",
            new[]
            {
                "weapon, target, stand and aim are valued by what the companion's own shots achieved",
                "the target hold survives ordinary motion and breaks on a change that should change the choice",
                "only the combat stance fires",
            },
            new[] { typeof(TheChosenWeaponIsTheBetterOne), typeof(CombatHeldTargetsItsBinderCouldNotSee), typeof(TheHandsWorkWhileThreatened), typeof(NotFightingMeansNothingToShoot), typeof(HuntsWorthTakingAreTaken) },
            Occurrence("a hostile appearing", "npc-spawn")),
        new("Chaining several jobs into one trip",
            new[] { "the search keeps the best order the winner beat", "collecting, lighting and pot breaking bind steps a course can hold" },
            Array.Empty<Type>()),
        new("Playing several steps ahead",
            new[] { "retained courses compare conserved futures and publish complete repairs", "a decision that spans ticks keeps the fight the body is already in" },
            new[] { typeof(TheBoundActivityHoldsWhileOneDecisionRuns), typeof(EveryCourseDecisionAccountsForItsOwnSearch) }),
        new("Choosing a place to work",
            new[] { "assistance is useful rather than merely nearby" },
            Array.Empty<Type>()),
        new("Knowing what it cannot do",
            new[]
            {
                "the light and reach senses answer in three values",
                "a route that ran out of budget is pending, and only an exhausted one is unreachable",
                "the way to the player is resumed until it is answered and never read as a proof before it is",
            },
            new[] { typeof(TheReachFloodSettles), typeof(ACensusAdmissionSurvivesItsBinder) }),
        new("Committing to a decision",
            new[] { "a decision that spans ticks keeps the fight the body is already in", "the target hold survives ordinary motion and breaks on a change that should change the choice" },
            new[] { typeof(DecisionsSurviveLongEnoughToPayOff), typeof(CombatDoesNotFlicker) }),
        new("Firing position",
            new[]
            {
                "a FireFrom stand holds the point the plan priced and refuses what the flood unclaims",
                "combat is admitted only where it can be executed",
                "combat makes progress toward its target",
            },
            new[] { typeof(TheCommittedPlanWasPerformed), typeof(HuntingProducesAnOutcome), typeof(HuntingStaysOnHisScreen) }),
        new("Self-preservation",
            new[] { "every liquid is air to the orb: it flies through water, honey, lava and shimmer at its air pace and is never hurt", "downing and revival keep life on the NPC" },
            new[] { typeof(TheCompanionStaysUp), typeof(DamageArrivesWhereDangerWasSeen) },
            Column("the companion being hit", "npc_hit", v => v.Length > 0 && v != "-")),
        new("Lighting the area",
            new[] { "torches go where his smart cursor would put one in the dark, and the record says why not", "native lighting projections preserve captured light and shared deficits" },
            new[] { typeof(TorchesGoWhereHisCursorWould), typeof(TheTorchGivesUpTheHand) },
            Admitted("anywhere dark admitted as work", "light-target")),
        new("Deciding what counts as a threat",
            new[]
            {
                "the threat sense reads danger from sealed chambers correctly",
                "a threat is anticipated from how it actually arrives",
                "an unthreatening enemy far away keeps off the vein",
            },
            new[] { typeof(CombatIsEagerWhenHeIsInDanger) }),
        new("Moving through the world",
            new[]
            {
                "contact pushes the orb out of a wall, kills the velocity into it and keeps the slide",
                "the orb flies its planned routes over native terrain and stops at what it cannot fit through",
                "a whole journey is recorded against its proven ticks",
            },
            new[] { typeof(TheBodyMovesWhenDriven), typeof(TheBodyIsNeverPinned), typeof(JourneysTakeTheTimeTheyWereProven), typeof(TheBodyStopsOnItsOwnRoute) }),
        new("Protecting the player",
            new[] { "danger lifts combat over work", "recovery flight and protection admit only what may start them" },
            new[] { typeof(AHittingFightKeptItsScore), typeof(CombatWasPricedInACrowd) }),
        new("Breaking containers",
            new[] { "collecting, lighting and pot breaking bind steps a course can hold" },
            new[] { typeof(CompletedTransferClaimsWereReceived) },
            Admitted("a pot admitted as work", "pot-target")),
        new("Dodging and kiting",
            new[] { "a dodge bends mining without stopping it", "safety bends the body inside its job and never takes it: an enemy beside a leaving player, firing on, a bent guard, an intervening hostile" },
            Array.Empty<Type>()),
        new("Opportunistic mining",
            new[]
            {
                "ore work breaks ore without excavating ordinary terrain",
                "the mining list decides which ores are work, and remembers every ore the player has held",
                "remaining work is accounted to whoever did it",
            },
            new[] { typeof(RepeatedFailedMethodsAreFindings) },
            Admitted("an ore admitted as work", "mine-target")),
        new("Reporting what it is doing",
            new[] { "a capture states the configuration it ran under and the course order it took", "every candidate a preparation refused is named with the stage and what it read" },
            new[] { typeof(ColumnsHoldWhatTheyClaim), typeof(TheCaptureWasClosed), typeof(NoOccurrenceWasDropped), typeof(TheDecisionAuditRanOnTheDecisionsTheCaptureHolds) }),
        new("Finding a route",
            new[]
            {
                "a two-wide corridor is open to the flood, a one-wide is closed, and a liquid across it is as open as air",
                "a route that ran out of budget is pending, and only an exhausted one is unreachable",
                "a closed door is opened rather than treated as a wall",
            },
            new[] { typeof(ClaimedArrivalsStayInsideTheirSuccessRegion) }),
        new("Staying with the player",
            new[]
            {
                "keeping company over pools stays returnable, never stands still, and meets a walking player",
                "the park is chosen about the player rather than about the body, and sits in the band above his head",
                "the park waits a few tiles over his head rather than at either end of a column clear all the way up",
                "being with the player needs a way to him: a body inside his region on the far side of a sealed wall comes round",
                "keeping company is the fallback: a slime worth hunting is hunted, a far one is not, rejoining is capped and sight is not distance",
            },
            new[] { typeof(FollowingMakesRouteProgress), typeof(ArrivalDoesNotStrandFollowing) }),
        new("Looting",
            new[] { "a collected drop is claimed only for what arrived", "assistance is useful rather than merely nearby" },
            new[] { typeof(CompletedTransferClaimsWereReceived) },
            Occurrence("anything picked up", "pickup")),
        new("Weapon selection",
            new[]
            {
                "the item in a weapon slot fires or swings through the arsenal with the item's own numbers",
                "weapon, target, stand and aim are valued by what the companion's own shots achieved",
                "a weapon's misses against one enemy type stay with that type",
            },
            new[] { typeof(TheChosenWeaponIsTheBetterOne), typeof(WeaponKnowledgeIsCalibrated) },
            Occurrence("a weapon used", "shot")),
        new("Recovering when it cannot follow",
            new[] { "recovery flight and protection admit only what may start them", "a companion sealed off from the player does not travel away from him" },
            new[] { typeof(BeingUnableToReachHimGetsNoticed) },
            Column("recovery flight running", "recovery_active", v => v is "True" or "true" or "1")),
        new("Changing the world",
            new[] { "ore work breaks ore without excavating ordinary terrain", "a closed door is opened rather than treated as a wall" },
            Array.Empty<Type>()),
        new("Speed",
            new[] { "the orb flies its planned routes over native terrain and stops at what it cannot fit through" },
            Array.Empty<Type>()),
        new("Doors",
            new[] { "a closed door is opened rather than treated as a wall" },
            Array.Empty<Type>()),
        new("Getting up after being downed",
            new[] { "downing and revival keep life on the NPC" },
            Array.Empty<Type>()),
        new("Opportunistic chopping",
            new[] { "native tree census retains work across cuts and observes axe effects", "gathering beside the player is cooperative rather than competing" },
            Array.Empty<Type>()),
        new("Growing stronger",
            new[]
            {
                "experience follows the game's own numbers: kills, boss fights and work level the companion alike in every difficulty",
                "the mastery preview is the owner's flat tree of ten-node lanes, and learning follows its edges and needs",
            },
            Array.Empty<Type>()),
        new("Using handed gear",
            new[] { "handed gear fits its slots and the tool power it reads is the game's own gate", "an unarmed companion offers no combat" },
            Array.Empty<Type>()),
        new("Tiring",
            new[] { "the companion's stats mirror the player's" },
            Array.Empty<Type>()),
    };

    /// <summary>Every fixture case this table names, for the self-test that pins them against the
    /// instruments that register them.</summary>
    internal static IEnumerable<string> NamedFixtures => Mapping.SelectMany(m => m.Fixtures).Distinct(StringComparer.Ordinal);

    /// <summary>Every behaviour this table claims to map, for the self-test that reads the README.</summary>
    internal static IEnumerable<string> MappedBehaviours => Mapping.Select(m => m.Behaviour);

    /// <summary>
    /// The behaviours the README's Behaviour By Behaviour table names, in its own order, read from the
    /// bold name that opens each row up to the em dash that separates it from its gloss.
    /// </summary>
    internal static List<string> BehavioursIn(string readme)
    {
        var names = new List<string>();
        int at = readme.IndexOf(Heading, StringComparison.Ordinal);
        if (at < 0) return names;
        foreach (string line in readme[at..].Split('\n'))
        {
            if (!line.StartsWith("| **", StringComparison.Ordinal)) continue;
            int close = line.IndexOf("**", 4, StringComparison.Ordinal);
            if (close < 0) continue;
            string name = line[4..close].Trim();
            if (name.Length > 0 && !names.Contains(name, StringComparer.Ordinal)) names.Add(name);
        }
        return names;
    }

    public static string Of(Session session, IReadOnlyList<Finding> findings,
        IReadOnlyList<(string Name, string Missing)> skipped, string? readmePath = null)
    {
        string path = readmePath ?? "README.md";
        if (!File.Exists(path))
            return $"behaviour parity  unavailable: no {path} to read the Behaviour By Behaviour table from, so the "
                + "behaviours this capture covers cannot be counted against the ones the project claims\n";
        var behaviours = BehavioursIn(File.ReadAllText(path));
        if (behaviours.Count == 0)
            return $"behaviour parity  unavailable: {path} carries no `{Heading}` table, so there is nothing to check coverage against\n";

        var byName = Mapping.ToDictionary(m => m.Behaviour, StringComparer.Ordinal);
        var skippedNames = skipped.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var rows = new List<(string Behaviour, string Fixtures, string Play, string Verdict)>();
        var extract = new List<(string Behaviour, int Tick)>();
        int noFixture = 0, disagrees = 0, agrees = 0, unmeasured = 0, notExercised = 0, unwitnessed = 0;

        foreach (string behaviour in behaviours)
        {
            if (!byName.TryGetValue(behaviour, out Mapped? mapped))
            {
                rows.Add((behaviour, "unmapped", "unmapped", "no mapping"));
                noFixture++;
                continue;
            }
            string fixtures = mapped.Fixtures.Length == 0 ? "none" : $"{mapped.Fixtures.Length}";

            var ran = new List<string>();
            var fired = new List<Finding>();
            int skippedHere = 0;
            foreach (Type type in mapped.Checks)
            {
                // A mapped type the report does not register is a row claiming a check nobody runs, and
                // it is reported rather than thrown on: the table's job is to name coverage that is not
                // there, so failing on its own missing coverage would be the one failure it cannot make.
                ICheck? check = Program.Checks.FirstOrDefault(c => c.GetType() == type);
                if (check is null) { skippedHere++; continue; }
                if (skippedNames.Contains(check.Name)) { skippedHere++; continue; }
                ran.Add(check.Name);
                fired.AddRange(findings.Where(f => string.Equals(f.Check, check.Name, StringComparison.Ordinal)
                    && f.Severity != Severity.Oddity));
            }

            string play;
            string verdict;
            if (mapped.Checks.Length == 0) { play = "no check reads it"; verdict = "unmeasured"; unmeasured++; }
            else if (ran.Count == 0) { play = $"{skippedHere} check(s), all skipped on this capture"; verdict = "unmeasured"; unmeasured++; }
            else if (fired.Count == 0)
            {
                // **Silence is three answers, and only one of them is agreement.** A check that ran and
                // found nothing has either watched the behaviour happen correctly, watched a session the
                // behaviour never occurred in, or run with nobody able to say which. The row's declared
                // witness is what separates them, and a row with none stays in the third.
                bool? saw = mapped.Saw?.Occurred(session);
                if (saw == true) { play = $"{ran.Count} check(s) silent, and the capture shows {mapped.Saw!.What}"; verdict = "play agrees"; agrees++; }
                else if (saw == false)
                {
                    play = $"{ran.Count} check(s) silent, and nothing shows {mapped.Saw!.What}";
                    verdict = "not exercised";
                    notExercised++;
                }
                else
                {
                    play = mapped.Saw is null
                        ? $"{ran.Count} check(s) silent; no witness says whether it occurred"
                        : $"{ran.Count} check(s) silent; the capture cannot say about {mapped.Saw.What}";
                    verdict = "silent";
                    unwitnessed++;
                }
            }
            else
            {
                Finding worst = fired.OrderByDescending(f => f.Rows).First();
                play = $"{fired.Count} finding(s) from {fired.Select(f => f.Check).Distinct(StringComparer.Ordinal).Count()} check(s), "
                    + $"worst at ticks {worst.FirstTick:n0}..{worst.LastTick:n0}";
                verdict = "play disagrees";
                disagrees++;
                // A finding spanning essentially the whole session names no *place*, so a terrain
                // window cut at its first tick would be a fixture of wherever the companion happened to
                // start. "the chosen behaviour changed every 5.4 ticks" is the live case: its span is
                // ticks 1..2,340 and cutting at tick 1 would commit the spawn point as evidence of churn.
                int span = worst.LastTick - worst.FirstTick;
                if (span * 10 < (session.Tick(session.Count - 1) - session.Tick(0)) * 9)
                    extract.Add((behaviour, worst.FirstTick));
            }
            if (mapped.Fixtures.Length == 0) noFixture++;
            rows.Add((behaviour, fixtures, play, verdict));
        }

        var text = new StringBuilder();
        text.Append($"behaviour parity  {behaviours.Count} behaviour(s) in {Path.GetFileName(path)}'s specification: "
            + $"{noFixture} named by no fixture at all, and in this capture "
            + $"{agrees} the play agrees with, {disagrees} it contradicts, {notExercised} this play never exercised, "
            + $"{unwitnessed} silent with nothing to say whether they occurred, {unmeasured} nothing here measures\n");
        text.Append("                  a check that ran and found nothing is not agreement: a row says `play agrees` only where a declared "
            + "witness saw the behaviour happen, `not exercised` where the witness proves it did not, and `silent` where nobody can say\n");
        text.Append("                  the fixture column counts named cases and reads none of their verdicts, so it is `cases N` rather than a "
            + "score: a case's pass or fail lives in the ledger, and a report that read it would report on which runs happen to be committed\n");
        foreach ((string behaviour, string fixtures, string play, string verdict) in rows)
            text.Append($"  {Fit(behaviour, 36)}  cases {Fit(fixtures, 5)}  {Fit(play, 70)}  {verdict}\n");
        foreach (Mapped stale in Mapping.Where(m => !behaviours.Contains(m.Behaviour, StringComparer.Ordinal)))
            text.Append($"  mapping for '{stale.Behaviour}' names no behaviour the specification still holds; the README row was renamed or removed\n");
        // The same coverage question asked backwards, which is the half a table of behaviours cannot
        // show: a check nobody could name a behaviour for is either an instrument check, which is
        // correct, or a question the specification has no row for, which is a gap in the specification
        // rather than in the reader. Both are worth seeing and neither is a fault.
        var claimed = Mapping.SelectMany(m => m.Checks).ToHashSet();
        string[] unclaimed = Program.Checks.Where(c => !claimed.Contains(c.GetType())).Select(c => c.Name).ToArray();
        if (unclaimed.Length > 0)
            text.Append($"  {unclaimed.Length} check(s) no behaviour row claims — instrument checks, or questions the specification has no row "
                + $"for: {string.Join("; ", unclaimed)}\n");

        // **A disagreement is worth a committed scenario, and this prints the command rather than
        // running it.** A report is a read of one file and the extractor writes a fixture into the
        // repository; a reader that wrote one as a side effect of being run would put a different
        // terrain window into the tree every time somebody opened a capture, named after whichever
        // finding happened to be largest that day. The commands are ready to paste, and several
        // disagreements naming the same tick want one file rather than one each — which on the 22
        // September capture they do, because the tail is one defect seen from four rows.
        if (extract.Count > 0)
        {
            text.Append($"  {extract.Select(e => e.Tick).Distinct().Count()} tick(s) worth cutting into a committed scenario for the "
                + $"{extract.Count} disagreement(s) above; run each from the repository root:\n");
            foreach (int tick in extract.Select(e => e.Tick).Distinct().OrderBy(t => t))
                text.Append($"    dotnet Tools/NavReplay/bin/Debug/net8.0/NavReplay.dll --extract-scenario {session.Path} {tick}"
                    + $"   # {string.Join(", ", extract.Where(e => e.Tick == tick).Select(e => e.Behaviour))}\n");
        }
        return text.ToString();
    }

    private static string Fit(string value, int width)
        => value.Length <= width ? value.PadRight(width) : value[..(width - 1)] + "…";
}
