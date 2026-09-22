extern alias live;

using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using AuditDecisionContracts = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.AuditDecisionContracts;
using BrainTelemetry = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using DecisionInputs = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.DecisionInputs;
using ReadLiveCourseForAudit = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.ReadLiveCourseForAudit;

/// <summary>
/// The one way a fixture opens a recording, because there are four things a recorded session needs and
/// every fixture that assembled them by hand got the same two of them wrong.
///
/// <b>What was wrong.</b> Thirteen sites across seven files wrote the same three lines — construct a
/// <c>BrainTelemetry</c>, hand it a mod, call <c>OnWorldLoad</c> — and not one of them put the
/// companion's body into <c>Main.npc</c>. <c>ReadLiveCourseForAudit.Read</c>, which is the audit's own
/// source, reaches the course through <c>CompanionNPC.Instance</c>, and that is
/// <c>Main.ActiveNPCs</c> scanned for the registered type; a fixture's companion is an object with both
/// halves of the <c>ModNPC</c> attachment and no slot, so the scan found nothing and the source returned
/// null on every call. Nothing went red: the audit counts every decision at the top of its own method,
/// before the source is consulted, so a session in that state reports every decision audited and zero
/// observations read, and four of the six decision contracts cannot fire while every row above them
/// reads green. Measured on the first run of
/// <c>Tools/EngineReplay/DecisionMaking/FuzzTheDecisionContracts.cs</c>: 1,200 decisions audited, 0
/// observations read, five contract rows green.
///
/// <b>Why it is a helper rather than thirteen repairs.</b> Setting the missing line at each site fixes the
/// sites somebody remembered and leaves the next one to be written wrong in the same way — this suite's
/// own rule about the preferences instance, one folder up. There is one seam now, and
/// <see cref="VerifyEveryRecordedFixtureIsVisibleToTheAudit"/> pins it against the source tree, so a new
/// fixture that constructs a recorder without coming through here is a red row rather than a silent
/// second instance.
///
/// <b>The fourth thing, which no fixture ever did.</b> <c>ReadLiveCourseForAudit.Install</c> is called in
/// play from <c>BrainTelemetry.Load</c>, a <c>ModSystem</c> override the loader invokes and no fixture
/// does. So even a body in a slot is not enough: the source has to be installed as well, and the two
/// halves fail identically from outside. <see cref="Open"/> does both.
/// </summary>
internal static class OpenTheRecorderOnACompanion
{
    /// <summary>
    /// Where the companion's body stands while a recorded fixture runs.
    ///
    /// Zero rather than a high slot, and the reason is <c>CompanionNPC.Find</c> rather than tidiness: it
    /// returns the *first* active NPC whose type matches, so a body placed above another active slot
    /// carrying the same type would lose the scan to it. Slot zero is also the slot
    /// <c>VerifyCompanionLifecycle.Create</c> leaves inactive and the one every combat scene seeds
    /// hostiles well above.
    /// </summary>
    public const int Slot = 0;

    /// <summary>
    /// Attach the mod, put the body where the audit looks, install the audit's source, and open the
    /// recording — in that order, because the recorder's own metadata reads the player and the audit's
    /// source reads the body, and a recorder opened before either closes itself and writes a capture of
    /// five header lines.
    /// </summary>
    public static void Open(BrainTelemetry recorder, CompanionNPC companion)
    {
        AttachWithoutOpening(recorder, companion);
        recorder.OnWorldLoad();
    }

    /// <summary>
    /// A recorded session with no brain in it, and the reason it has none, written down.
    ///
    /// Two rows here are about the recorder's own file lifecycle — a reserved stem retried inside one
    /// second, and the recording switch — and drive no tick at all, so there is nothing for the audit to
    /// read and placing a body would be ceremony. The reason is a parameter rather than a comment so that
    /// choosing this entry point is a written decision: the alternative is a site that quietly wants
    /// <see cref="Open"/> and reads identically from outside, which is the defect this file exists for.
    /// </summary>
    public static void OpenWithNoCompanion(BrainTelemetry recorder, string whyNoBodyIsNeeded)
    {
        _ = whyNoBodyIsNeeded;
        AttachTheMod(recorder);
        recorder.OnWorldLoad();
    }

    /// <summary>Everything <see cref="Open"/> does except opening the recording, for the one caller that
    /// decides whether to record after it already has a companion.</summary>
    public static void AttachWithoutOpening(BrainTelemetry recorder, CompanionNPC companion)
    {
        AttachTheMod(recorder);
        Place(companion);
    }

    /// <summary>
    /// The body into a live slot under the registered type, and the audit's source installed.
    ///
    /// Separate from <see cref="Open"/> for the one caller that decides whether to record after it has a
    /// companion — a fixture measuring the cost of recording against not recording still wants the body
    /// visible in both arms, or its two runs differ by more than the recorder.
    /// </summary>
    public static void Place(CompanionNPC companion)
    {
        sourceBeforeThisFixture = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics
            .AuditDecisionContracts.Source;
        if (ModContent.GetInstance<CompanionNPC>() == null) ContentInstance.Register(companion);
        companion.NPC.type = ModContent.NPCType<CompanionNPC>();
        companion.NPC.active = true;
        companion.NPC.whoAmI = Slot;
        Main.npc[Slot] = companion.NPC;
        // Installed here rather than left to the caller, because a body in a slot with no source reads
        // exactly like a source with no body: the capture closes `audit-observations-read=0` either way.
        ReadLiveCourseForAudit.Install();
    }

    /// <summary>
    /// Hands the recorder the mod this host pretends to have.
    ///
    /// <c>BrainTelemetry.OnWorldLoad</c> ends in <c>Mod.Logger.Info</c> and its failure path in
    /// <c>Mod.Logger.Error</c>, so a recorder with a null <c>Mod</c> throws out of the call rather than
    /// recording. One template is registered per content type, as the loader does: registering a fresh
    /// instance per fixture makes <c>ContentInstance&lt;T&gt;.Instance</c> null once more than one exists,
    /// which is a trap this suite's guide already carries.
    /// </summary>
    private static void AttachTheMod(BrainTelemetry recorder)
    {
        var mod = ModContent.GetInstance<live::AICompanion.AICompanion>() ?? new live::AICompanion.AICompanion();
        typeof(Mod).GetProperty("Logger")!.SetValue(mod, log4net.LogManager.GetLogger(typeof(OpenTheRecorderOnACompanion)));
        if (ModContent.GetInstance<live::AICompanion.AICompanion>() == null) ContentInstance.Register(mod);
        if (ModContent.GetInstance<BrainTelemetry>() == null) ContentInstance.Register(recorder);
        typeof(ModType).GetProperty("Mod")!.SetValue(recorder, mod);
    }

    /// <summary>What <see cref="AuditDecisionContracts.Source"/> held before this fixture placed a body,
    /// so <see cref="Clear"/> can give it back rather than leaving the live reader wired for whoever runs
    /// next. Null is the ordinary prior value and restoring null is the point.</summary>
    private static Func<live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.DecisionInputs?>? sourceBeforeThisFixture;

    /// <summary>
    /// Takes the body back out and gives the audit's source back.
    ///
    /// A companion left in a slot under the registered type is a live companion to anything that scans for
    /// one, and the per-case reset does not deactivate a slot. The source matters for the same reason one
    /// step along: a fixture that installed the live reader and did not remove it leaves the next case's
    /// audit reading through a body that case never placed, which is a pass or a fail decided by run
    /// order. The slot goes back as a blank inactive NPC rather than as the previous occupant — restoring
    /// that is the per-case reset's job and `ResetProcessState.cs` is not this file's to edit.
    /// </summary>
    public static void Clear()
    {
        Main.npc[Slot] = new NPC { whoAmI = Slot, active = false };
        live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.AuditDecisionContracts.Source
            = sourceBeforeThisFixture;
        sourceBeforeThisFixture = null;
    }

    /// <summary>The recorder's own close, which takes the reason it writes into the end marker; a fixture
    /// closing it directly names itself rather than borrowing a gameplay reason.</summary>
    public static void Close()
        => typeof(BrainTelemetry).GetMethod("Close", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { "fixture-close" });

    /// <summary>
    /// Whether the audit could read a frozen observation right now, and the companion it would read it
    /// from. Null for a body no scan can find, which is the state this whole file exists to make
    /// impossible by accident.
    /// </summary>
    public static CompanionNPC? WhatTheAuditWouldSee() => CompanionNPC.Instance;

    /// <summary>
    /// Refuses to let a fixture drive a recorded session the audit cannot see a body from.
    ///
    /// Thrown rather than logged, because the failure it replaces is silent by construction and a warning
    /// in a suite that prints thousands of lines is the same silence with more characters.
    /// </summary>
    public static void RequireTheAuditCanSeeTheBody(string fixture, CompanionNPC companion)
    {
        CompanionNPC? seen = CompanionNPC.Instance;
        if (ReferenceEquals(seen, companion)) return;
        throw new InvalidOperationException(
            $"{fixture} is about to record with a body the audit cannot read: CompanionNPC.Instance is "
            + (seen == null ? "null" : "a different companion")
            + $", so ReadLiveCourseForAudit.Read returns null, every decision is audited against no "
            + "observation, and four of the six decision contracts cannot fire while every row still "
            + $"passes. Open the recorder through {nameof(OpenTheRecorderOnACompanion)}.{nameof(Open)}.");
    }
}

/// <summary>
/// The row that makes the class above stay closed: every fixture that records comes through the one
/// seam, and the seam's guard fires when the body is not where the audit looks.
///
/// <b>Why a source pin rather than a runtime check alone.</b> The defect is not that a session fails —
/// it is that a session *succeeds* with four of its six contracts inert. A runtime guard only fires on
/// a fixture that already calls it, which is exactly the fixture that did not have the bug. So the
/// enforcement is the same shape as this folder's producer pin on the two refusal literals: read the
/// suite's own source and require that every file constructing a recorder names the helper.
/// </summary>
internal static class VerifyEveryRecordedFixtureIsVisibleToTheAudit
{
    private const string Family = "recorded-fixture visibility";

    /// <summary>Files allowed to construct a <c>BrainTelemetry</c> without naming the helper, each for a
    /// reason rather than as an exemption list that grows.</summary>
    private static readonly (string File, string Why)[] Excused =
    {
        ("Observation/GodsEyeTestStubs.cs",
            "its BrainTelemetry is a test-local stub of its own, not the mod's recorder"),
    };

    public static int Run()
    {
        int failed = 0;
        failed += RunOneRow.Case("a body no scan can find is refused before a fixture records it",
            TheGuardFiresOnAnInvisibleBody, Family);
        failed += RunOneRow.Case("the audit reads the very companion the helper placed",
            ThePlacedBodyIsTheOneTheAuditReads, Family);
        failed += RunOneRow.Case("every fixture that constructs a recorder opens it through the one seam",
            EveryRecorderConstructionNamesTheHelper, Family);
        return failed;
    }

    /// <summary>
    /// The guard, driven from the state it exists to refuse. This is the row's mutation and it is run
    /// rather than described: with the slot cleared, <c>CompanionNPC.Instance</c> must be null, the
    /// installed source must hand back nothing, and the guard must throw.
    /// </summary>
    private static void TheGuardFiresOnAnInvisibleBody()
    {
        var companion = VerifyCompanionLifecycle.Create();
        OpenTheRecorderOnACompanion.Clear();
        ReadLiveCourseForAudit.Install();
        try
        {
            Require(OpenTheRecorderOnACompanion.WhatTheAuditWouldSee() == null,
                "premise: with no body in a slot the audit must see no companion, and it sees one — so this "
                + "row is measuring a slot somebody else left behind rather than the guard");
            DecisionInputs? read = AuditDecisionContracts.Source?.Invoke();
            Require(read == null,
                "premise: the audit's own source must return null for a body no scan can find, which is the "
                + $"whole defect; it returned {(read == null ? "null" : "inputs")}");

            bool threw = false;
            try { OpenTheRecorderOnACompanion.RequireTheAuditCanSeeTheBody("this row", companion); }
            catch (InvalidOperationException error)
            {
                threw = true;
                Require(error.Message.Contains("CompanionNPC.Instance is null", StringComparison.Ordinal),
                    "the refusal must name what is wrong rather than that something is: " + error.Message);
            }
            Require(threw, "a fixture about to record with a body the audit cannot read was allowed through");
        }
        finally { OpenTheRecorderOnACompanion.Clear(); }
    }

    /// <summary>
    /// The positive half, and it asserts identity rather than non-nullness on purpose.
    /// <c>CompanionNPC.Find</c> returns the first active NPC of the registered type, so a row happy with
    /// "something was found" would pass against a body some earlier case left in a lower slot — which is
    /// the same class of defect one layer along.
    /// </summary>
    private static void ThePlacedBodyIsTheOneTheAuditReads()
    {
        var companion = VerifyCompanionLifecycle.Create();
        try
        {
            OpenTheRecorderOnACompanion.Place(companion);
            Require(ReferenceEquals(OpenTheRecorderOnACompanion.WhatTheAuditWouldSee(), companion),
                "the audit must read the companion the helper placed, not merely some companion; "
                + $"Instance is {(OpenTheRecorderOnACompanion.WhatTheAuditWouldSee() == null ? "null" : "another body")}");
            DecisionInputs? read = AuditDecisionContracts.Source?.Invoke();
            Require(read != null,
                "with the body placed and the source installed, the audit's source must hand back inputs; "
                + "it returned null, which is the state a capture reports as audit-observations-read=0");
            OpenTheRecorderOnACompanion.RequireTheAuditCanSeeTheBody("this row", companion);
        }
        finally { OpenTheRecorderOnACompanion.Clear(); }
    }

    /// <summary>
    /// The pin. Every <c>.cs</c> file under this project that constructs the mod's recorder must name the
    /// helper, so the thirteen sites cannot quietly become fourteen.
    /// </summary>
    private static void EveryRecorderConstructionNamesTheHelper()
    {
        string root = Path.Combine(RepositoryRoot(), "Tools", "EngineReplay");
        Require(Directory.Exists(root), $"the suite's own source is not under {root}, so this pin cannot be asked");
        var offenders = new List<string>();
        int constructing = 0;
        foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal)) continue;
            if (relative == "OpenTheRecorderOnACompanion.cs") continue;
            string text = File.ReadAllText(file);
            // Both spellings a fixture reaches the recorder by: the `using` alias most files take, and the
            // fully alias-qualified form one combat fixture writes inline.
            if (!text.Contains("new BrainTelemetry()", StringComparison.Ordinal)
                && !text.Contains("Diagnostics.BrainTelemetry()", StringComparison.Ordinal)) continue;
            constructing++;
            if (Excused.Any(e => e.File == relative)) continue;
            if (!text.Contains(nameof(OpenTheRecorderOnACompanion), StringComparison.Ordinal)) offenders.Add(relative);
        }
        Require(constructing > 0,
            "premise: no file under Tools/EngineReplay constructs a recorder at all, so this pin matched nothing "
            + "and would stay green through any change — the construction spelling has moved");
        Require(offenders.Count == 0,
            $"{offenders.Count} fixture(s) construct the recorder without opening it through "
            + $"{nameof(OpenTheRecorderOnACompanion)}, so their sessions audit every decision against no frozen "
            + $"observation and four of the six contracts cannot fire: {string.Join(", ", offenders)}");
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail(
            $"{Family}: {constructing} file(s) construct the mod's recorder, {Excused.Length} excused by name, "
            + "all the rest through the one seam");
    }

    /// <summary>The repository root, walked up from the running assembly rather than read from the
    /// working directory, so the pin does not depend on where the harness was launched from.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AICompanion.csproj")))
            directory = directory.Parent;
        Require(directory != null, $"no AICompanion.csproj above {AppContext.BaseDirectory}");
        return directory!.FullName;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
