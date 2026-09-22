#nullable enable

using System.Collections.Generic;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Activities.Combat;
using AICompanion.Companion.Brain.Activities.Gathering;
using AICompanion.Companion.Brain.Activities.NearbyAssistance;

namespace AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>
/// The three families an activity declares itself into. Nothing compares by family any more — the
/// family chooser nominated the best child of each and that stage went with it on 22 September 2026 —
/// but the label survives because it is how a *reader* looks for a job: the recorder groups attempt
/// outcomes by it, the inspector's Execution page groups the course's worths by it, and the profile
/// card names it. A course's own vocabulary is the domain (`mine-target`, `combat`, `light-target` and
/// the rest), which is a different and finer set; `../Selection/Opportunities/CLAUDE.md` has the table.
/// </summary>
public enum PurposeFamily { Gathering, Combat, NearbyAssistance }

/// <summary>
/// The one list of activity instances the companion has, built once per brain.
///
/// It is a registration and not a decision, which is why it outlived the chooser that used to hold it:
/// every surface that has to enumerate the jobs reads this list — the telemetry header writes one column
/// family per registered activity, `ExecuteCourseBinding` maps a published purpose onto one of them,
/// `ReadCourseWorthPerActivity` asks the course what each is worth, and the tick prepares all of them
/// before the course decides anything. A fixture narrowing the companion to two jobs edits this list on
/// the brain; that is a supported thing to do and several dozen of them do it.
///
/// **The order is the registration order and it is load-bearing in one place**: a tie between two equal
/// candidates used to break on the index into this list, and the recorder's per-activity columns are
/// written in this order behind a header written once at world load, so an activity inserted mid-session
/// writes a row wider than its own header. Add at the end.
/// </summary>
public static class RegisterActivities
{
    public static List<CompanionAction> All() => new()
    {
        new FightEnemies(),
        new CollectNearbyItems(),
        new ChopTree(),
        new MineOre(),
        new LightUsefulArea(),
        new KeepCompany(),
    };
}
