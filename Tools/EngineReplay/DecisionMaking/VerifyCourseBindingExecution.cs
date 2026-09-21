extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Tools.Ledger;
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
    /// This row is a **known gap made visible**, not a guard over settled ground: collect, light and
    /// pot have sources and no binder as of 21 September 2026, so it names them rather than asserting
    /// they are absent. When a binder lands, move that domain out of the pending list and the row
    /// starts guarding it. The list is here rather than in prose because prose about an unfinished
    /// migration goes stale and a list the suite reads does not.
    /// </summary>
    private static void EveryDomainCanBind()
    {
        var pending = new[] { "collect-target", "light-target", "pot-target" };

        // Built the way production would build them: gathering's binder takes its domain exactly as its
        // source does, and combat's names its own. Reflection over constructors was tried first and
        // quietly under-reported — it skipped every binder without a parameterless constructor and so
        // claimed gathering had none, which is the instrument lying in the direction that looks like a
        // finding. Constructing them explicitly cannot do that.
        var binders = new live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities.IOpportunityBinder[]
        {
            new live::AICompanion.Companion.Brain.Activities.Gathering.GatheringOpportunityBinder("mine-target"),
            new live::AICompanion.Companion.Brain.Activities.Gathering.GatheringOpportunityBinder("chop-target"),
            new live::AICompanion.Companion.Brain.Activities.Combat.CombatOpportunityBinder(),
        };
        HashSet<string> implemented = binders.Select(b => b.Domain).ToHashSet(StringComparer.Ordinal);

        foreach (string domain in new[] { "mine-target", "chop-target" })
            Require(implemented.Contains(domain),
                $"'{domain}' has no binder, so its opportunities would be priced and never become a step. Binders: {string.Join(", ", implemented.OrderBy(d => d))}");

        Type binderType = typeof(live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities.IOpportunityBinder);
        string[] declared = binderType.Assembly.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsInterface && binderType.IsAssignableFrom(type))
            .Select(type => type.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Require(declared.Length == 2,
            $"the number of binder implementations changed to {declared.Length} ({string.Join(", ", declared)}); if one of {string.Join(", ", pending)} just gained a binder, guard it here and take it off the pending list");
    }

    private static void StandsAndTilesKeepTheirKinds()
    {
        PositionRequest fire = ExecuteCourseBinding.RequestFor(Binding("fire", 320, 160));
        Require(fire.Kind == RequestKind.FireFrom,
            $"a firing stand asked for {fire.Kind}, which routes around the combat stance's own admission and its rock fallback");

        PositionRequest ore = ExecuteCourseBinding.RequestFor(Binding("mine", 320, 160));
        Require(ore.Kind == RequestKind.Exact && ore.WorkTile == new Microsoft.Xna.Framework.Point(20, 10),
            $"tile work lost its work tile, so the positioner cannot apply the tool-reach proof that admitted the pose; kind={ore.Kind} tile={ore.WorkTile}");

        PositionRequest drop = ExecuteCourseBinding.RequestFor(Binding("collect", 320, 160));
        Require(drop.Kind == RequestKind.Exact && drop.WorkTile == null,
            "a drop is taken by contact and named a work tile, which would ask for a tool-reach proof nothing needs");

        Require(ExecuteCourseBinding.Companionship.Kind == RequestKind.WithPlayer,
            "an empty course asked the body to hold, which makes 'nothing worth doing' look identical to 'freeze'");
    }

    private static void UnknownPurposeRefuses()
    {
        bool refused = false;
        try { ExecuteCourseBinding.ActivityFor("forage"); }
        catch (ArgumentOutOfRangeException) { refused = true; }
        Require(refused, "an unmapped purpose was silently given an executor, so a new domain would inherit somebody else's activity");
    }

    private static StepBinding Binding(string purpose, double x, double y)
        => new(1, new OpportunityKey("fixture-domain", purpose, "target", 0), "method", new CoursePoint(x, y),
            "tool", 1, 1, 0, 0, 0, Array.Empty<ResourcePhase>(), Array.Empty<PredictedEffect>(),
            Array.Empty<long>(), DependencyManifest.Empty, useProven: true);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
