extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Tools.Ledger;
using Microsoft.Xna.Framework;
using live::AICompanion.Companion.Brain.Infrastructure.Position;
using live::AICompanion.Companion.Brain.Infrastructure.Selection;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

/// <summary>
/// The bridge from a published course binding to the two things the rest of the tick already carries
/// out: which activity performs the work, and where the body is asked to be.
///
/// The load-bearing row is the coverage one. Every purpose the five opportunity sources mint has to
/// name an activity the chooser actually registers, because a purpose with no executor is not an error
/// anyone would see — the course would bind work, publish it, and nothing would happen, which reads in
/// play as a companion that decided to do something and then stood there.
/// </summary>
internal static class VerifyCourseBindingExecution
{
    public static int Run()
    {
        int red = 0;
        void Row(string name, Action test)
        {
            try { test(); Console.WriteLine("GREEN " + name); }
            catch (Exception error) { red++; Console.WriteLine("RED " + name + ": " + error.Message); }
        }
        Row("G01 every domain discovery can publish is performed by exactly one registered activity", EveryDomainHasAPerformer);
        Row("G01 a domain no activity declares is refused before the search can order it", AnUndeclaredDomainIsRefusedBeforeItIsOrdered);
        Row("G03 every discovered domain has a binder that can turn it into a step", EveryDomainCanBind);
        Row("G01 a firing stand keeps its own request kind", StandsAndTilesKeepTheirKinds);
        Row("G01 an undeclared domain refuses rather than defaulting", UnknownDomainRefuses);
        return red;
    }

    /// <summary>
    /// The purposes this row expects to find declared, each named beside the file that mints it, so the
    /// table is a claim the row *checks* rather than a copy it trusts.
    ///
    /// **It is no longer what makes the row complete, and that is the change of 22 September 2026.** The
    /// row used to sweep `Companion/` for the literal `new OpportunityKey(` and trust this table for the
    /// files it did not find — so `chop`, minted as a literal inside a `GatheringOpportunityFact` in a
    /// file that constructs no key, was covered only because somebody had typed it here. A sentinel
    /// added a seventh purpose the same way and the row stayed green; it stayed green again for a key
    /// written as `new Infrastructure.Selection.Opportunities.OpportunityKey(…)`. Completeness now comes
    /// from `OpportunityPurposes.All` and from `Opportunity`'s own constructor refusing an undeclared
    /// purpose, which no spelling can walk past; this table survives as the *producer* map, because
    /// knowing which file to open when a purpose loses its executor is worth keeping and a declaration
    /// alone does not say it.
    /// </summary>
    private static readonly (string Purpose, string Producer)[] MintedPurposes =
    {
        ("fire", "Companion/Brain/Activities/Combat/CombatCourseOpportunity.cs"),
        ("mine", "Companion/Brain/Activities/Gathering/GatheringCourseOpportunities.cs"),
        ("chop", "Companion/Brain/Activities/Gathering/CaptureTreeOpportunities.cs"),
        ("collect", "Companion/Brain/Infrastructure/Selection/Opportunities/DiscoverAssistanceOpportunities.cs"),
        ("light", "Companion/Brain/Infrastructure/Selection/Opportunities/DiscoverAssistanceOpportunities.cs"),
        ("break-pot", "Companion/Brain/Infrastructure/Selection/Opportunities/DiscoverAssistanceOpportunities.cs"),
    };

    /// <summary>
    /// Every domain the production sources publish is performed by exactly one activity the brain
    /// registers, and every declared purpose is one the opportunity constructor accepts.
    ///
    /// This row pinned a hand-written purpose table until 23 September 2026, with pots on a written
    /// exemption list — and the table was the defect: `CollectNearbyItems` declared `pot-target` and
    /// carried the pot method while the table left the purpose out, so a bound pot reached the tick with
    /// no activity. The executor map is derived from the activities' own `CourseDomains` now, so what
    /// this row checks is the production source list against the production activity list, with no
    /// third list of its own in between. A domain that gains discovery before any activity declares it
    /// fails this row by name, which is the silent class the pot was an instance of.
    /// </summary>
    private static void EveryDomainHasAPerformer()
    {
        // **The set under test is the declaration, not a sweep and not this file's table.** Every
        // opportunity a course can hold is built through `Opportunity`, which refuses a purpose outside
        // `OpportunityPurposes.All`, so that collection is the complete list by construction rather than
        // by anybody's search succeeding. The row's first job is therefore to check the declaration
        // against the executor map; its second is to prove the refusal is live, below.
        string[] declared = OpportunityPurposes.All.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Require(declared.Length > 0, "no opportunity purpose is declared at all, so this row checks nothing");

        // The table is the producer map rather than the source of truth, so what is asserted about it is
        // that it has not drifted *out* of the declaration — a purpose named here that nothing declares
        // is a stale row pointing whoever reads it at a file that no longer mints anything.
        string[] strayInTable = MintedPurposes.Select(p => p.Purpose).Distinct(StringComparer.Ordinal)
            .Except(declared, StringComparer.Ordinal).ToArray();
        Require(strayInTable.Length == 0,
            $"this row's producer table names purposes nothing declares, so it is pointing a reader at the wrong "
            + $"files: {string.Join(", ", strayInTable)}");
        string root = RepositoryRoot();
        foreach ((string purpose, string producer) in MintedPurposes)
            Require(File.ReadAllText(Path.Combine(root, producer)).Contains(
                    "OpportunityPurposes." , StringComparison.Ordinal),
                $"premise: {producer} is named as the producer of '{purpose}' and no longer names a declared "
                + "purpose at all, so the table points at a file that mints nothing");

        // A declared purpose with no producer is the other drift, and it is reported rather than refused:
        // a purpose can legitimately be declared a moment before its producer lands. It is printed so the
        // gap is visible in a run rather than silent.
        string[] undocumented = declared.Except(MintedPurposes.Select(p => p.Purpose), StringComparer.Ordinal).ToArray();
        if (undocumented.Length > 0)
            Console.WriteLine($"  purposes declared with no producer named in this row: {string.Join(", ", undocumented)}");

        var brain = VerifyCompanionLifecycle.Create().Brain;
        HashSet<string> registered = brain.Actions.Select(action => action.Name).ToHashSet(StringComparer.Ordinal);
        Require(registered.Count > 0, "the brain registered no activities, so this row proves nothing");

        string[] published = DecideCourseEachTick.ProductionSources().Select(s => s.Name).Distinct(StringComparer.Ordinal).ToArray();
        Require(published.Length > 0, "premise: the production source list is empty, so this row proves nothing");
        foreach (string domain in published)
        {
            Require(ExecuteCourseBinding.HasExecutor(domain),
                $"the domain '{domain}' is published by a production source and no registered activity declares it in "
                + "CourseDomains — a course would bind it, the tick would carry it, and nothing would perform it");
            string executor = ExecuteCourseBinding.ActivityFor(domain);
            Require(registered.Contains(executor),
                $"the domain '{domain}' is performed by '{executor}', which the brain does not register — bound work of that kind would publish and never run. Registered: {string.Join(", ", registered.OrderBy(name => name))}");
            int declaring = brain.Actions.Count(action => action.CourseDomains.Contains(domain, StringComparer.Ordinal));
            Require(declaring == 1,
                $"the domain '{domain}' is declared by {declaring} registered activities; one domain has one performer");
        }

        // **The arm that makes the list above complete, driven rather than searched for.** A seventh
        // purpose is exactly what the old sweep could not see: a sentinel minted one into a fact record
        // in a file with no key construction and this row stayed green, and it stayed green a second time
        // for the same key construction spelled out in full. Neither spelling matters now, because every
        // opportunity is built through one constructor and that constructor refuses a purpose the tree
        // does not declare — so an undeclared purpose cannot become a candidate, let alone a step. The
        // refusal is asserted by driving it, with the exception type named, so a guard downgraded to a
        // log line or a silent skip reds here.
        bool refusedUndeclared = false;
        try
        {
            _ = new Opportunity(new OpportunityKey("smash-target", "smash", "tile:10,10", 1), 1, default,
                OpportunityAdmission.KnownUsable, "fixture",
                new[] { new UsefulNeed(new NeedKey(NeedKind.Loot, "smash-target"), 1, 1, 1) },
                new[] { "smash" }, DependencyManifest.Empty, default);
        }
        catch (ArgumentOutOfRangeException) { refusedUndeclared = true; }
        Require(refusedUndeclared,
            "an opportunity was built with a purpose nothing declares, so a new domain can mint work no activity "
            + "performs and no list in this row would ever notice — which is the exact silence this pin exists for");

        foreach (string purpose in declared)
        {
            bool accepted = true;
            try
            {
                _ = new Opportunity(new OpportunityKey("fixture-domain", purpose, "tile:10,10", 1), 1, default,
                    OpportunityAdmission.KnownUsable, "fixture",
                    new[] { new UsefulNeed(new NeedKey(NeedKind.Loot, "fixture-domain"), 1, 1, 1) },
                    new[] { purpose }, DependencyManifest.Empty, default);
            }
            catch (ArgumentOutOfRangeException) { accepted = false; }
            Require(accepted,
                $"the declared purpose '{purpose}' is refused by the constructor every producer builds through, so "
                + "the domain that mints it discovers nothing at all");
        }
    }

    /// <summary>
    /// A domain no activity declares is refused by name before the search can order it, which is the
    /// structural half: the row above says every production domain has a performer, and this one says
    /// the search acts on a domain without one rather than binding work nothing performs.
    ///
    /// It is a *proven* refusal and not the middle value: the executor map is derived from the registered
    /// activities, a fact of this tree, so no later slice, no larger allowance and nothing about the
    /// world can turn the answer into a yes. Reading it as unresolved would park the opportunity for
    /// ever, re-offering it on every observation, which is the starvation shape this tree keeps refusing
    /// everywhere else. The fixture domain carries a declared purpose, so the refusal can only be about
    /// the domain.
    /// </summary>
    private static void AnUndeclaredDomainIsRefusedBeforeItIsOrdered()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(),
            "Companion/Brain/Infrastructure/Selection/Courses/SearchCourseOrders.cs"));
        Require(source.Contains($"\"{SearchCourseOrders.StepPurposeHasNoExecutor}\"", StringComparison.Ordinal),
            $"premise: '{SearchCourseOrders.StepPurposeHasNoExecutor}' is no longer written by the search, so a "
            + "capture's refusal tally cannot name this reason and nothing downstream can count it");
        Require(!ExecuteCourseBinding.HasExecutor("orphan-target"),
            "premise: the fixture's orphan domain has a performer, so this row cannot show the refusal");

        var search = new SearchCourseOrders(3);
        search.Begin(EmptyFacts(), Episode(), new[] { Usable("orphan-target", "collect"), Usable("light-target", "light") },
            Array.Empty<OpportunityKey>(), new RefuseEverything());
        // The structural tally rather than the evidence one. A refusal proved by the executor map is kept
        // apart from refusals about what an observation holds, because the audit's contracts ask whether
        // every *evidence* refusal was a not-observed one and a structural reason mixed into that answer
        // silences them — so this row reads the field the search actually writes it to, and a reason that
        // moved back into the evidence tally reds here rather than silently disarming contract two.
        Require(search.StructuralRefusals.TryGetValue(SearchCourseOrders.StepPurposeHasNoExecutor, out int refused) && refused == 1,
            $"the undeclared domain was not refused for having no performer before enumeration; structural refusals: "
            + $"{string.Join(", ", search.StructuralRefusals.Select(r => r.Key + "=" + r.Value))}; evidence refusals: "
            + $"{string.Join(", ", search.Refusals.Select(r => r.Key + "=" + r.Value))}");
        Require(!search.Refusals.ContainsKey(SearchCourseOrders.StepPurposeHasNoExecutor),
            "the executor refusal is also in the evidence tally, which is what puts contract two out of reach on "
            + "every decision that admits usable work nothing performs");
    }

    /// <summary>
    /// Discovery and binding are two halves of one migration and a domain can pass the first while
    /// failing the second in total silence. A source with no binder produces opportunities the search
    /// enumerates, prices and then cannot turn into a step, so that work simply never appears in any
    /// course — the same silent-never failure as a missing coverage fact, one layer up.
    ///
    /// The domain list is taken from the **sources**, by constructing every one the tree has and asking
    /// its own <c>Name</c>, and only then checked against the binders. The first version of this row
    /// built the binders and asserted the resulting set contained the domains it had just used to build
    /// them, which is <c>assert(observed, THE_CONSTANT)</c>: it could not fail, it would have stayed
    /// green if a sixth source landed with no binder, and its name advertised a capability it never
    /// touched. A coverage row whose two sides come from the same place covers nothing.
    /// </summary>
    private static void EveryDomainCanBind()
    {
        // Built the way production would build them: the gathering and assistance binders take their
        // domain exactly as their sources do, and combat's names its own. Reflection over constructors
        // was tried first and quietly under-reported — it skipped every binder without a parameterless
        // constructor and so claimed gathering had none, which is the instrument lying in the direction
        // that looks like a finding. Constructing them explicitly cannot do that.
        // Both sides are the lists the live brain itself constructs, asked through the public factories
        // on `DecideCourseEachTick`, so this row cannot drift from production by keeping its own copy —
        // which is the shape of defect the executor table had with the activities over pots.
        HashSet<string> implemented = DecideCourseEachTick.ProductionBinders().Select(b => b.Domain).ToHashSet(StringComparer.Ordinal);

        // Keeping company is the sixth job and deliberately has no source at all: an empty order *is*
        // companionship.
        var sources = DecideCourseEachTick.ProductionSources();
        Require(sources.Length > 0, "no source was constructed, so this row proves nothing");

        foreach (string domain in sources.Select(s => s.Name).Distinct(StringComparer.Ordinal))
            Require(implemented.Contains(domain),
                $"the source '{domain}' has no binder, so its opportunities would be discovered, priced and never become a step. Binders: {string.Join(", ", implemented.OrderBy(d => d))}");
    }

    private static void StandsAndTilesKeepTheirKinds()
    {
        PositionRequest fire = ExecuteCourseBinding.RequestFor(Binding("fire", 320, 160));
        Require(fire.Kind == RequestKind.FireFrom,
            $"a firing stand asked for {fire.Kind}, which routes around the combat stance's own admission and its rock fallback");

        // The pose here is 320,160, whose own tile is 20,10 — deliberately a different point from the
        // tile the opportunity names, because that difference is the whole row. The work tile used to be
        // derived from the pose, which is a hover *beside* the work and therefore in air; a row asserting
        // 20,10 was asserting the defect. Both minted shapes are read, so a parser that handles the
        // gathering form and drops the light form cannot pass here.
        PositionRequest ore = ExecuteCourseBinding.RequestFor(Binding("mine", 320, 160, "tile:copper:25,59"));
        Require(ore.Kind == RequestKind.Exact && ore.WorkTile == new Microsoft.Xna.Framework.Point(25, 59),
            $"tile work named the wrong tile, so the positioner applies its tool-reach proof to a place the hand never acts on; kind={ore.Kind} tile={ore.WorkTile}");
        Require(ore.WorkTile != new Microsoft.Xna.Framework.Point(20, 10),
            "the work tile is the body's own pose tile again, which is air beside the work rather than the work");

        PositionRequest lit = ExecuteCourseBinding.RequestFor(Binding("light", 320, 160, "tile:7,3"));
        Require(lit.WorkTile == new Microsoft.Xna.Framework.Point(7, 3),
            $"a torch site minted as 'tile:x,y' lost its tile, so only the gathering shape is actually parsed; tile={lit.WorkTile}");

        bool refusedNameless = false;
        try { ExecuteCourseBinding.RequestFor(Binding("mine", 320, 160, "target")); }
        catch (ArgumentOutOfRangeException) { refusedNameless = true; }
        Require(refusedNameless,
            "a tile-work opportunity whose target names no tile was given one anyway, which is the silently-wrong work tile this replaced");

        PositionRequest drop = ExecuteCourseBinding.RequestFor(Binding("collect", 320, 160, "item:3"));
        Require(drop.Kind == RequestKind.Exact && drop.WorkTile == null,
            "a drop is taken by contact and named a work tile, which would ask for a tool-reach proof nothing needs");

        var companionship = ExecuteCourseBinding.Companionship(new Vector2(1280f, 960f));
        Require(companionship.Kind == RequestKind.WithPlayer,
            "an empty course asked the body to hold, which makes 'nothing worth doing' look identical to 'freeze'");
        // The anchor is only read where the positioner admits no candidate, so a wrong one is invisible on
        // every ordinary tick and flies the body at it on the tick that matters. It shipped as Vector2.Zero,
        // the world's top-left corner, and the locked-door scene measured the companion climbing thirty
        // tiles away from the player to reach it. The row asserts the anchor is the caller's, not a default.
        Require(companionship.Anchor == new Vector2(1280f, 960f),
            $"an empty course's companionship anchored at {companionship.Anchor} rather than the region centre it was "
            + "given; an anchor the positioner falls back to must be where the player is, because SeekDestination flies at it");
    }

    private static void UnknownDomainRefuses()
    {
        bool refused = false;
        try { ExecuteCourseBinding.ActivityFor("forage-target"); }
        catch (ArgumentOutOfRangeException) { refused = true; }
        Require(refused, "an undeclared domain was silently given a performer, so a new domain would inherit somebody else's activity");
    }

    private static StepBinding Binding(string purpose, double x, double y, string target = "target")
        => new(1, new OpportunityKey("fixture-domain", purpose, target, 0), "method", new CoursePoint(x, y),
            "tool", 1, 1, 0, 0, 0, Array.Empty<ResourcePhase>(), Array.Empty<PredictedEffect>(),
            Array.Empty<long>(), DependencyManifest.Empty, useProven: true);

    /// <summary>The repository root, found by walking up for `Companion/Brain` rather than by counting
    /// parents, because the working directory differs between `run-case.sh`, `verify.sh` and a hand run
    /// and a fixed number of parents is a silent skip on two of the three.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Companion", "Brain")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException(
            "the repository root was not found by walking up for Companion/Brain, so the source sweep read nothing");
    }

    private static Opportunity Usable(string domain, string purpose)
        => new(new OpportunityKey(domain, purpose, "tile:10,10", 1), 1, default, OpportunityAdmission.KnownUsable,
            "fixture", new[] { new UsefulNeed(new NeedKey(NeedKind.Loot, domain), 1, 1, 1) },
            new[] { purpose }, DependencyManifest.Empty, default);

    private static DecisionFactSnapshot EmptyFacts() => new(1, 1, 100, 1, 0, Array.Empty<DecisionFact>());

    private static CourseComparisonEpisode Episode()
        => new(1, 1, 100, new[] { new UsefulNeed(new NeedKey(NeedKind.Loot, "fixture"), 1, 1, 1) }, true, 0, "fixture");

    /// <summary>A projector the refusal row never reaches, because the filter it is about runs inside
    /// `Begin` before any order is projected. It throws rather than returning a rejection, so a filter
    /// that stopped filtering fails loudly here instead of passing the pot through to a stub that
    /// refuses everything anyway.</summary>
    private sealed class RefuseEverything : ICourseProjector
    {
        public CourseProjectionResult Continue(IReadOnlyList<OpportunityKey> order, DecisionFactSnapshot facts,
            CourseComparisonEpisode episode, DecisionWorkCursor cursor, DecisionWorkBudget budget)
            => throw new InvalidOperationException("the refusal row projected an order; Begin should have filtered first");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
