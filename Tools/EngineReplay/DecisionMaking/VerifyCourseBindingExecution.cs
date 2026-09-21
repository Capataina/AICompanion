extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Tools.Ledger;
using Microsoft.Xna.Framework;
using live::AICompanion.Companion.Brain.Infrastructure.Position;
using live::AICompanion.Companion.Brain.Infrastructure.Selection;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
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
        Row("G01 every bound purpose names an activity the chooser registers", EveryPurposeHasAnExecutor);
        Row("G03 every discovered domain has a binder that can turn it into a step", EveryDomainCanBind);
        Row("G01 a firing stand keeps its own request kind", StandsAndTilesKeepTheirKinds);
        Row("G01 an unmapped purpose refuses rather than defaulting", UnknownPurposeRefuses);
        return red;
    }

    /// <summary>
    /// The purposes the sources actually mint, read off their own construction sites rather than
    /// imagined: combat mints "fire" per use, gathering carries the site's own "mine"/"chop", and the
    /// assistance adapter maps its three domains to "collect", "light" and "break-pot".
    /// </summary>
    private static readonly string[] MintedPurposes = { "fire", "mine", "chop", "collect", "light", "break-pot" };

    private static void EveryPurposeHasAnExecutor()
    {
        var brain = VerifyCompanionLifecycle.Create().Brain;
        HashSet<string> registered = brain.Chooser.Actions.Select(action => action.Name).ToHashSet(StringComparer.Ordinal);
        Require(registered.Count > 0, "the chooser registered no activities, so this row proves nothing");

        foreach (string purpose in MintedPurposes)
        {
            string executor = ExecuteCourseBinding.ActivityFor(purpose);
            if (executor.Length == 0) continue; // a pot is broken in passing and names no activity
            Require(registered.Contains(executor),
                $"the purpose '{purpose}' maps to the activity '{executor}', which the chooser does not register — bound work of that kind would publish and never run. Registered: {string.Join(", ", registered.OrderBy(name => name))}");
        }
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
        var binders = new live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities.IOpportunityBinder[]
        {
            new live::AICompanion.Companion.Brain.Activities.Gathering.GatheringOpportunityBinder("mine-target"),
            new live::AICompanion.Companion.Brain.Activities.Gathering.GatheringOpportunityBinder("chop-target"),
            new live::AICompanion.Companion.Brain.Activities.Combat.CombatOpportunityBinder(),
            new AssistanceOpportunityBinder("collect-target"),
            new AssistanceOpportunityBinder("light-target"),
            new AssistanceOpportunityBinder("pot-target"),
        };
        HashSet<string> implemented = binders.Select(b => b.Domain).ToHashSet(StringComparer.Ordinal);

        // The other side of the comparison, asked of the sources themselves. Combat's source names its
        // own domain; the other two take theirs, exactly as production constructs them. Keeping company
        // is the sixth job and deliberately has no source at all: an empty order *is* companionship.
        var sources = new live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities.IOpportunitySource[]
        {
            new live::AICompanion.Companion.Brain.Activities.Gathering.GatheringOpportunitySource("mine-target"),
            new live::AICompanion.Companion.Brain.Activities.Gathering.GatheringOpportunitySource("chop-target"),
            new live::AICompanion.Companion.Brain.Activities.Combat.CombatOpportunitySource(),
            new DiscoverAssistanceOpportunities("collect-target"),
            new DiscoverAssistanceOpportunities("light-target"),
            new DiscoverAssistanceOpportunities("pot-target"),
        };
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

    private static void UnknownPurposeRefuses()
    {
        bool refused = false;
        try { ExecuteCourseBinding.ActivityFor("forage"); }
        catch (ArgumentOutOfRangeException) { refused = true; }
        Require(refused, "an unmapped purpose was silently given an executor, so a new domain would inherit somebody else's activity");
    }

    private static StepBinding Binding(string purpose, double x, double y, string target = "target")
        => new(1, new OpportunityKey("fixture-domain", purpose, target, 0), "method", new CoursePoint(x, y),
            "tool", 1, 1, 0, 0, 0, Array.Empty<ResourcePhase>(), Array.Empty<PredictedEffect>(),
            Array.Empty<long>(), DependencyManifest.Empty, useProven: true);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
