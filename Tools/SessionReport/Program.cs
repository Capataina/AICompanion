#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AICompanion.Tools.Ledger;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Reads a playtest's record and says what is wrong with it, sorted by how sure it is: definitive
/// issues that the design or the record's own arithmetic rules out, potential issues that are wrong
/// in every situation anyone has thought of, and oddities that are shapes in the numbers with no
/// rule behind them yet.
///
/// It exists because the record was never the problem and the reading was: a session is eleven
/// thousand rows of eighty-odd columns, every diagnosis this project has made from one was a
/// hand-rolled column sum, and the columns move whenever the brain grows a new fact — which on
/// 2026-09-09 produced a mean distance of three and a half thousand tiles from an index that had
/// shifted by two. Every check here is grown from a defect that actually happened, and each one
/// names the columns it needs so a file written before those columns exist reads as reduced
/// coverage rather than as a clean run.
///
///   dotnet run --project Tools/SessionReport -- Telemetry/&lt;stamp&gt;.tsv
///   dotnet run --project Tools/SessionReport -- Telemetry          # the newest session in the folder
///
/// Exit code 0 when nothing definitive was found, 1 when something was, so the run is a check and
/// not only a report.
/// </summary>
public static class Program
{
    /// <summary>Every check, in the order the report runs them. Internal because the behaviour parity
    /// table resolves a check's name from its type rather than repeating the string: a renamed class is
    /// then a compile error where a renamed string would be a row that silently stops matching.</summary>
    internal static readonly ICheck[] Checks =
    {
        // The instrument first: a finding here means the rest of the file is not yet evidence.
        new TicksAdvance(),
        new ReturnableFitsInsideReach(),
        new TheReachFloodSettles(),
        new ColumnsHoldWhatTheyClaim(),
        new TheCaptureWasClosed(),
        new NoOccurrenceWasDropped(),
        // The boundary check, which asks whether the record can be believed at all: a body held by
        // our own code rather than by the world. It comes before the behaviour checks because a
        // finding here means the behaviour below it was measured on a broken body. Its former
        // companion, the divergence between the offline motion rule and the collision that
        // performed it, went with the walker: there is one body and one contact now, run identically
        // in the mod and in every headless tool, so there is no second prediction to disagree with.
        new TheBodyIsNeverPinned(),
        // Whether the whole update fitted the engine's own timestep, and where it went when it did
        // not. It sits beside the instrument checks rather than among the behaviour ones because a
        // session the world could not keep up with is a session whose behaviour was measured on a
        // world running at a fraction of real time.
        new TheFrameFitsTheEnginesTimestep(),
        // The identity contracts: whether a selection, an attempt and a control grant are named
        // consistently across the rows and the occurrence sibling. Every one is a rule the producer
        // guarantees rather than a threshold, and a finding here means any later attribution of
        // effort or failure to an activity is being read across records that disagree.
        new SelectedActivitiesHadAnEligibleOffer(),
        new ARetainedChoiceKeepsItsSelection(),
        new TheBoundActivityHoldsWhileOneDecisionRuns(),
        new AttemptIdentitiesAgreeAcrossRecords(),
        new RepeatedFailedMethodsAreFindings(),
        new CompletedTransferClaimsWereReceived(),
        new ControlGrantsAreCompatible(),
        // Then the body, the fight and the choices. Three movement checks went with the walker and
        // are worth naming here, because each would have kept reporting a clean run for ever rather
        // than failing loudly: a move's proven ticks against its performed ticks, whether every kind
        // of offered move was ever made, and whether a refused step held the body. All three read a
        // proved edge with a kind, an outcome and a macro refusal in front of it, and the orb's
        // navigator carries a route of free-space corners with none of those things in it.
        new TheBodyMovesWhenDriven(),
        // The whole journey against its proven ticks and against the player's own, then the stops:
        // a route's segments can each be flown at pace while the journey costs four times as much,
        // because the time goes into the gaps, and the stops are where those gaps are.
        new JourneysTakeTheTimeTheyWereProven(),
        new TheBodyStopsOnItsOwnRoute(),
        // The course's own record, checked against what its producer guarantees rather than against a
        // threshold. The recording side ran ahead of the reading side for the whole migration, so until
        // this landed the companion could write a typed course trace that nothing read back.
        new EveryCourseDecisionAccountsForItsOwnSearch(),
        new ACensusAdmissionSurvivesItsBinder(),
        new TheDecisionAuditRanOnTheDecisionsTheCaptureHolds(),
        new EveryEffectWasTheAcceptedStep(),
        new BeingUnableToReachHimGetsNoticed(),
        new FollowingMakesRouteProgress(),
        new ArrivalDoesNotStrandFollowing(),
        new ClaimedArrivalsStayInsideTheirSuccessRegion(),
        new HuntingProducesAnOutcome(),
        new CombatHeldTargetsItsBinderCouldNotSee(),
        // A submerged body running its breath down was a check here and is not replaced. The orb has
        // no breath, and every liquid is air to it, so there is no submerged state for a check to watch.
        new FollowingRespondsAfterDeparture(),
        new DamageArrivesWhereDangerWasSeen(),
        new TheHandsWorkWhileThreatened(),
        new CombatIsEagerWhenHeIsInDanger(),
        new NotFightingMeansNothingToShoot(),
        new TheCommittedPlanWasPerformed(),
        new CombatDoesNotFlicker(),
        new AHittingFightKeptItsScore(),
        new CombatWasPricedInACrowd(),
        new TheChosenWeaponIsTheBetterOne(),
        new WeaponKnowledgeIsCalibrated(),
        new TheCompanionStaysUp(),
        new HuntingStaysOnHisScreen(),
        // How long a decision lasts, which sits beside the other choice checks because every one
        // of them reads a behaviour's outcome and none of them could see a behaviour that never
        // got one: an abandoned approach looks identical to an approach that was never worth much.
        new DecisionsSurviveLongEnoughToPayOff(),
        new TheActionBoardGetsUsed(),
        new TheTorchGivesUpTheHand(),
        // The player as the reference: his own smart cursor's dark tile left unlit, and a usable hunt beside him while he
        // stood idle, each while keeping company won. Both read columns only 0.35.0 and later write and skip an older capture by name.
        new TorchesGoWhereHisCursorWould(),
        new HuntsWorthTakingAreTaken(),
        new TheBrainFitsInAFrame(),
    };

    /// <summary>
    /// The numbers a play holds, as opposed to the rules it breaks. These do not decide anything:
    /// each emits a value into the ledger so the next capture is compared against this one rather
    /// than read from scratch, and the pass lines they are eventually judged against live in the
    /// verification plan and travel as tags on the rows.
    /// </summary>
    private static readonly IMeasure[] Measures =
    {
        new MeasureAheadShare(),
        // `cancelled-in-flight` and `arrived-with-follow-gap` were measures here and are deleted
        // rather than rewritten: the first read a proved edge released mid-arc, and the second read
        // the positioner's `partial-progress-candidate` choice reason, which the positioner stopped
        // writing when a spot became a corner node the flood reached. Neither quantity exists for
        // this body, and a measure left reading a name nobody writes reports zero for ever.
        new MeasureStopsByReason(),
        new MeasureCourseWork(),
        new MeasureValidityFlips(),
        new MeasureHuntKnownUnusableShare(),
        new MeasureReachCompleteShare(),
        new MeasureJourneysReached(),
        new MeasureHandsByActivity(),
        new MeasureTerrainRevisionRate(),
        // What the whole update cost and whose each part of it was, from schema 0.45.0's frame ledger.
        // Updates a second and frames a second are two rows here because an update is not a frame.
        new MeasureTheFrame(),
        // Where inside the brain the time and memory went, and which sections dominated the ticks that stood out
        // from the session's own recent ticks, from schema 0.48.0's section profile.
        new MeasureWhereTheTimeGoes(),
        // The orb's stillness and roughness, which the first orb play of 15 September made the
        // question: `unexplained-stops` read zero over a session spent mostly still, because a body
        // held by a safety response or an arrived hold has no route to stop on.
        new MeasureStillness(),
        new MeasureDistanceWhilePlayerMoves(),
        new MeasureMotionSmoothness(),
        new MeasureSafetyShare(),
    };

    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
        {
            // Both, always, and the play measures run even when the chronology tests fail: a
            // partial self-test that stops at the first red hides whatever the second half would
            // have said, which is the abort-on-first-failure shape the whole ledger exists to end.
            //
            // Both halves report a ledger row, which is what makes this instrument's failure
            // visible to the scoreboard at all. Before they did, a red self-test printed its
            // failures, returned 1, and contributed nothing the run file could hold — so verify.sh
            // scored a run whose reader's own tests were failing and exited 0. The chronology half
            // runs on fixtures it builds itself and so can be wrapped; the measures half files its
            // own row because its capture can legitimately be absent.
            int chronology = EmitLedgerRows.Case(PlayRow.Instrument, "SelfTest",
                "the chronology reader, its checks and its damaged-capture contracts hold",
                ChronicleTests.Run);
            int measures = PlayMeasureTests.Run();
            return chronology != 0 || measures != 0 ? 1 : 0;
        }

        // The measures alone, over any capture, which is how a recording made before the ledger
        // existed is still benchmarked: the run is opened under the capture's own source revision
        // rather than under whatever is checked out, so a comparison between two captures is a
        // comparison between the two builds that wrote them.
        if (args.Length >= 2 && args[0] == "--measures")
        {
            string? only = Resolve(args[1]);
            if (only == null) { Console.Error.WriteLine($"no session file at or under {args[1]}"); return 2; }
            Session measured;
            try { measured = Session.Load(only); }
            catch (Exception e) { Console.Error.WriteLine($"{only}: {e.Message}"); return 2; }
            Console.Write(RunMeasures(measured));
            return 0;
        }

        if (args.Length >= 2 && args[0] == "--multirun")
        {
            string[] paths = ResolveAll(args[1..]);
            if (paths.Length == 0)
            {
                Console.Error.WriteLine("--multirun needs at least one readable session file or Telemetry folder");
                return 2;
            }
            Console.Write(MultiRunReport.Of(paths));
            return MultiRunReport.HasDefinitive(paths) ? 1 : 0;
        }

        if (args.Length >= 3 && args[0] == "--html")
        {
            string[] paths = ResolveAll(args[2..]);
            if (paths.Length == 0)
            {
                Console.Error.WriteLine("--html needs an output .html path followed by at least one readable session");
                return 2;
            }
            try
            {
                WritePlaytestHtml.Write(args[1], paths);
                Console.WriteLine($"wrote recorded timeline {args[1]} for {paths.Length} session(s)");
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"could not write {args[1]}: {e.Message}");
                return 2;
            }
        }

        bool fullTimeline = args.Length > 0 && args[0] == "--timeline";
        if (fullTimeline)
            args = args[1..];
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: dotnet run --project Tools/SessionReport -- [--timeline] <session.tsv | Telemetry folder>\n       dotnet run --project Tools/SessionReport -- --multirun <session.tsv | Telemetry folder>...\n       dotnet run --project Tools/SessionReport -- --html <output.html> <session.tsv | Telemetry folder>...");
            return 2;
        }

        string? path = Resolve(args[0]);
        if (path == null)
        {
            Console.Error.WriteLine($"no session file at or under {args[0]}");
            return 2;
        }

        Session session;
        try
        {
            session = Session.Load(path);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"{path}: {e.Message}");
            return 2;
        }

        Console.Write(DescribeSession.Of(session));
        Console.Write(DescribeGodsEyeEvents.Of(path, fullTimeline));
        Console.Write(ReadCourseChronicle.Describe(session, path));
        // The chronicle line above says whether the course evidence can be trusted; this one says what
        // the companion decided with it. They are printed together and in that order deliberately, so a
        // story is never read without the coverage statement that says how much of it is there.
        Console.Write(DescribeCourseDecisions.Of(session, fullTimeline));
        // And the same decisions as a table keyed on the identity the rows carry, with the census, the
        // ordered course and the cost joined to each. The narration above answers "what was it doing at
        // tick N"; this answers "what was it given, what did it choose, and what did that cost".
        Console.Write(WriteCourseTimeline.Of(session, fullTimeline));
        Console.Write(JoinAttemptEvidence.Describe(path, session, fullTimeline));
        Console.Write(Chronicle.Of(session, fullTimeline));
        if (session.Count == 0)
            return 0;
        // The census opens the report, above every finding, because it is the one part that says
        // what did *not* happen. Every check below it fires on a threshold somebody chose and can
        // only find a failure somebody imagined, so a category nobody thought to threshold reads as
        // silence; a census prints a row per category whether or not anything happened in it, and a
        // zero in a row is loud where zero findings from a detector is invisible. The mod writes it
        // beside the session under the same stamp, so nobody has to be told where to look.
        Companion(path, "-census.txt", "behaviour census");
        Companion(path, "-map.txt", "session map");
        Console.Write(DescribeCombatAudit.Of(path));
        // Where the brain's time went, section by section, and the spike ticks with their whole trees from the
        // sidecar; above the measures, because the measures file the same numbers and this is the reading of them.
        Console.Write(DescribeWhereTheTimeGoes.Of(session, path));

        Console.Write(RunMeasures(session));

        var (findings, skipped, ran) = Evaluate(session);

        Console.WriteLine();
        Console.WriteLine($"coverage  {ran} of {Checks.Length} checks ran");
        foreach (var (name, missing) in skipped)
            Console.WriteLine($"  skipped  {name}  — the file has no {missing}");

        // Coverage against the specification rather than against the columns: which of the behaviours
        // the README says the companion is responsible for this capture said anything about at all.
        Console.WriteLine();
        Console.Write(WriteBehaviourParity.Of(session, findings, skipped));

        foreach (Severity severity in new[] { Severity.Definitive, Severity.Potential, Severity.Oddity })
        {
            Finding[] raw = findings.Where(f => f.Severity == severity)
                                    .OrderByDescending(f => f.Rows)
                                    .ToArray();
            var group = Fold(raw);
            Console.WriteLine();
            Console.WriteLine(raw.Length == group.Count
                ? $"{Label(severity)}  ({group.Count})"
                : $"{Label(severity)}  ({group.Count} of {raw.Length:n0} occurrence(s), folded by class)");
            if (group.Count == 0)
            {
                Console.WriteLine("  nothing");
                continue;
            }
            foreach (Finding finding in group)
            {
                string where = finding.FirstTick == finding.LastTick
                    ? $"tick {finding.FirstTick:n0}"
                    : $"ticks {finding.FirstTick:n0}..{finding.LastTick:n0}";
                Console.WriteLine();
                Console.WriteLine($"  {finding.Title}");
                Console.WriteLine($"    where  {where}");
                Console.WriteLine($"    asked  {finding.Check}");
                foreach (string line in Wrap(finding.Detail, 92))
                    Console.WriteLine($"    {line}");
            }
        }

        // The closing line counts the lines the report printed, with the occurrence total beside it.
        // It used to count raw findings while the header counted folded lines, so the 22 September 2026
        // capture closed on "1089 definitive issue(s)" under a header reading "DEFINITIVE ISSUES (8)" —
        // two numbers for two different things, and the larger one is the one a reader quotes.
        Finding[] definitiveFindings = findings.Where(f => f.Severity == Severity.Definitive).ToArray();
        int definitive = Fold(definitiveFindings).Count;
        Console.WriteLine();
        Console.WriteLine(definitive == 0
            ? "no definitive issue in this session."
            : definitive == definitiveFindings.Length
                ? $"{definitive} definitive issue(s): something in this session is wrong by construction."
                : $"{definitive} definitive issue(s) over {definitiveFindings.Length:n0} occurrence(s): something in this session is wrong by construction.");
        return definitive == 0 ? 0 : 1;
    }

    /// <summary>
    /// Run every measure, emit its rows to whatever run file is open, and render them for a person.
    ///
    /// A measure that throws becomes an <c>error</c> row rather than taking the report down, and
    /// the wording of that row matters: an error is the instrument breaking and says nothing about
    /// the thing under test, so it must never be read as a number of zero. A measure that cannot
    /// run because the capture predates a column it reads is skipped by name, for the same reason
    /// the checks are — zero coverage and a clean reading look identical once they are folded
    /// together.
    /// </summary>
    internal static string RunMeasures(Session session)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine();
        text.AppendLine("play measures — numbers, not verdicts; the ledger compares them against the last run");
        foreach (IMeasure measure in Measures)
        {
            string[] missing = measure.Needs.Where(n => !session.Has(n)).ToArray();
            if (missing.Length > 0)
            {
                EmitLedgerRows.Skipped(PlayRow.Instrument, PlayRow.Suite, measure.Name, $"the file has no {string.Join(", ", missing)}");
                text.AppendLine($"  skipped  {measure.Name} — the file has no {string.Join(", ", missing)}");
                continue;
            }
            if (measure.Missing(session) is { } absent)
            {
                EmitLedgerRows.Skipped(PlayRow.Instrument, PlayRow.Suite, measure.Name, $"the file has no {absent}");
                text.AppendLine($"  skipped  {measure.Name} — the file has no {absent}");
                continue;
            }
            try
            {
                foreach (LedgerRow row in measure.Rows(session))
                {
                    EmitLedgerRows.Row(row);
                    text.AppendLine(row.Verdict == "skipped"
                        ? $"  skipped  {row.Case} — {row.Message}"
                        : $"  {row.Value,10:0.##} {row.Unit,-12} {row.Case}");
                }
            }
            catch (Exception e)
            {
                EmitLedgerRows.Error(PlayRow.Instrument, PlayRow.Suite, measure.Name, $"{e.GetType().Name}: {e.Message}");
                text.AppendLine($"  ERROR    {measure.Name} — the measure itself failed: {e.GetType().Name}: {e.Message}. Nothing was measured, so this is no coverage rather than a clean number.");
            }
        }
        return text.ToString();
    }

    /// <summary>One evaluator for ordinary and multi-run reports; no second check policy may drift.</summary>
    internal static (List<Finding> Findings, List<(string Name, string Missing)> Skipped, int Ran) Evaluate(Session session)
    {
        var findings = new List<Finding>(); var skipped = new List<(string Name, string Missing)>(); int ran = 0;
        foreach (ICheck check in Checks)
        {
            string[] missing = check.Needs.Where(n => !session.Has(n)).ToArray();
            if (missing.Length > 0) { skipped.Add((check.Name, string.Join(", ", missing))); continue; }
            if (check is ICheckCoverage coverage && coverage.Missing(session) is { } absent) { skipped.Add((check.Name, absent)); continue; }
            ran++;
            try { findings.AddRange(check.Run(session)); }
            catch (Exception e)
            {
                findings.Add(new Finding(Severity.Potential, check.Name, $"the check itself failed: {e.GetType().Name}",
                    $"{e.Message}. Nothing was measured for this question, so treat it as no coverage rather than as a clean result.", 0, 0, 0));
            }
        }
        return (findings, skipped, ran);
    }

    /// <summary>How many occurrences of one class may be printed separately before they become a count.</summary>
    private const int RepeatsShown = 3;

    /// <summary>How many classes one check may print before the tail of them becomes a count.</summary>
    private const int ClassesShown = 6;

    /// <summary>How many tick spans a folded line names before the rest become a count of spans.</summary>
    private const int SpansNamed = 8;

    /// <summary>
    /// One line per class of finding, with the count and the span it covered. A condition that held
    /// five times in a session is one defect that recurred, and printing its paragraph five times moves
    /// the reading cost rather than removing it — which is the failure a reader built to replace
    /// hand-grepping must not commit itself.
    ///
    /// <para><b>The class is the fold key and the check is not</b>, which is the correction the 22
    /// September 2026 capture forced. Folding per check printed the worst three and one summary line
    /// whose detail then enumerated every one of 872 tick spans — the reading cost moved into the
    /// summary rather than out of the report. Folding per class prints one line for 875 occurrences of
    /// one contradiction while leaving two genuinely different findings from one check as two lines:
    /// "projectile type 1 lands a median 21 updates late" and "projectile type 3 lands a median 12
    /// updates late" are the whole content of that check and must not become one row.</para>
    ///
    /// <para>A class under <see cref="RepeatsShown"/> occurrences is left as its individual findings,
    /// because three paragraphs carrying three different sets of numbers are worth more than one
    /// paragraph carrying a count. Past it the class folds, and a check with more classes than
    /// <see cref="ClassesShown"/> folds the tail of them as well, so no check can flood the report
    /// however many distinct things it finds.</para>
    /// </summary>
    internal static List<Finding> Fold(Finding[] group)
    {
        var kept = new List<Finding>();
        foreach (var byCheck in group.GroupBy(f => f.Check))
        {
            var classes = new List<Finding>();
            foreach (var byClass in byCheck.GroupBy(ClassOf, StringComparer.Ordinal))
            {
                var ordered = byClass.OrderByDescending(f => f.Rows).ToArray();
                if (ordered.Length <= RepeatsShown) { classes.AddRange(ordered); continue; }
                classes.Add(FoldOne(ordered));
            }
            classes.Sort((a, b) => b.Rows.CompareTo(a.Rows));
            if (classes.Count <= ClassesShown) { kept.AddRange(classes); continue; }
            kept.AddRange(classes.Take(ClassesShown));
            var rest = classes.Skip(ClassesShown).ToArray();
            kept.Add(new Finding(rest[0].Severity, byCheck.Key,
                $"and {rest.Length} further class(es) of the same check, the largest over {rest[0].Rows:n0} row(s)",
                $"Ticks {Spans(rest)}. The {ClassesShown} above carry the reasoning; these are the same check "
                    + "finding different things, so run with --timeline for each one's own numbers.",
                rest.Min(f => f.FirstTick), rest.Max(f => f.LastTick), rest.Sum(f => f.Rows)));
        }
        return kept.OrderByDescending(f => f.Rows).ToList();
    }

    /// <summary>One class's occurrences as one finding: the representative's reasoning, then the count,
    /// the whole span and a bounded sample of the individual spans.</summary>
    private static Finding FoldOne(Finding[] ordered)
    {
        int first = ordered.Min(f => f.FirstTick), last = ordered.Max(f => f.LastTick);
        return new Finding(ordered[0].Severity, ordered[0].Check,
            $"{ordered[0].Title} ({ordered.Length:n0}×)",
            $"{ordered.Length:n0} occurrence(s) of one class between ticks {first:n0} and {last:n0}, over "
                + $"{ordered.Sum(f => f.Rows):n0} row(s); the largest spans {ordered[0].Rows:n0} row(s). "
                + $"Spans: {Spans(ordered)}. One line rather than {ordered.Length:n0}, because the same "
                + $"contradiction recurring is one defect. {ordered[0].Detail}",
            first, last, ordered.Sum(f => f.Rows), ClassOf(ordered[0]));
    }

    private static string Spans(Finding[] ordered)
    {
        var named = ordered.Take(SpansNamed)
            .Select(f => f.FirstTick == f.LastTick ? $"{f.FirstTick:n0}" : $"{f.FirstTick:n0}..{f.LastTick:n0}");
        string text = string.Join(", ", named);
        return ordered.Length <= SpansNamed ? text : $"{text} and {ordered.Length - SpansNamed:n0} more";
    }

    /// <summary>
    /// What two findings have to share to be one line. A check that can fire per tick declares its own
    /// class; everything else is its title with the digits masked, so two findings differing only in a
    /// count or a tick range fold and two differing in a name do not.
    /// </summary>
    internal static string ClassOf(Finding finding)
    {
        if (finding.Class is { } declared) return declared;
        var masked = new System.Text.StringBuilder(finding.Title.Length);
        bool inNumber = false;
        foreach (char c in finding.Title)
        {
            bool digit = char.IsAsciiDigit(c) || (inNumber && (c == ',' || c == '.'));
            if (!digit) { masked.Append(c); inNumber = false; continue; }
            if (!inNumber) masked.Append('#');
            inNumber = true;
        }
        return masked.ToString();
    }

    /// <summary>
    /// A whole-session artefact the mod wrote beside the .tsv under the same stamp, printed as it
    /// is. Absent is reported by name rather than passed over, because a missing census reads
    /// exactly like an empty one and the two mean opposite things: no file means the session
    /// predates the census or ended without a clean world unload, and an empty one would be a
    /// companion that never moved.
    /// </summary>
    private static void Companion(string sessionPath, string suffix, string what)
    {
        string path = Path.ChangeExtension(sessionPath, null) + suffix;
        Console.WriteLine();
        if (!File.Exists(path))
        {
            Console.WriteLine($"no {what} beside this session ({Path.GetFileName(path)} is absent), so treat its questions as unmeasured rather than clean");
            return;
        }
        Console.Write(File.ReadAllText(path));
    }

    /// <summary>A file as given, or the newest .tsv in a folder, so a report is one command after a playtest.</summary>
    /// <summary>A capture path, or the newest capture in a folder. Internal because the play measures
    /// resolve their default the same way the tool does, rather than naming one file nobody writes.</summary>
    internal static string? Resolve(string argument)
    {
        if (File.Exists(argument))
            return argument;
        if (!Directory.Exists(argument))
            return null;
        return Directory.EnumerateFiles(argument, "*.tsv")
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
    }

    /// <summary>
    /// Multi-run and HTML input expands every session in each directory. Ordinary reporting still
    /// calls <see cref="Resolve"/> and therefore keeps its deliberate newest-run convenience.
    /// A capture directory is evidence, not a request to silently discard every run but one.
    /// </summary>
    internal static string[] ResolveAll(IEnumerable<string> arguments)
        => arguments.SelectMany(argument => File.Exists(argument)
                ? (IEnumerable<string>)new[] { argument }
                : Directory.Exists(argument)
                    ? (IEnumerable<string>)Directory.EnumerateFiles(argument, "*.tsv").OrderBy(File.GetLastWriteTimeUtc).ThenBy(path => path, StringComparer.Ordinal)
                    : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new List<string>();
        int length = 0;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (length > 0 && length + 1 + word.Length > width)
            {
                yield return string.Join(' ', line);
                line.Clear();
                length = 0;
            }
            line.Add(word);
            length += (length > 0 ? 1 : 0) + word.Length;
        }
        if (line.Count > 0)
            yield return string.Join(' ', line);
    }

    private static string Label(Severity severity) => severity switch
    {
        Severity.Definitive => "DEFINITIVE ISSUES",
        Severity.Potential => "POTENTIAL ISSUES",
        _ => "ODDITIES AND BASELINES",
    };
}
