// The activities and their step hand-over are read through the live mod, because this project compiles
// the effect recorders and the audit a second time without `Selection/` or `Activities/` behind them.
extern alias live;

#nullable enable

using System.Reflection;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;
using Microsoft.Xna.Framework;
using Terraria;

/// <summary>
/// The effect contract: every native effect the companion causes names the accepted step it performed
/// and lands on that step's target.
///
/// <b>Every row is a pair, and the quiet half is the half that matters.</b> A tripwire that fires on its
/// own scene has only proved it can fire. So the off-step strike is followed by the on-step strike, the
/// stepless pot break by the same break under a step accepted on its own tick, the wrong drop by the right
/// one — and the quiet half asserts that no count moved, not only that the right kind stayed at zero.
///
/// <b>Each row goes in through the effect recorders</b> (`GodsEyeEvents.RecordToolEffect`,
/// `RecordWorldInteraction`, `RecordPickup`) rather than calling the audit, because the hook in the
/// recorder is the wiring: a tree whose recorders stopped calling `ObserveEffect` compiles and passes a
/// row that calls the audit directly. No recording is open, on purpose — the contract counts whether or
/// not a session is, and a row with a session would hide the day it stopped doing so.
///
/// The last row is not about the audit at all. It is the source pin against the class this contract
/// exists to catch: an activity that performs course work without adopting the step it was handed.
/// </summary>
internal static class VerifyEveryEffectIsTheBoundStep
{
    private const string Family = "effect contract";

    public static int Run()
    {
        int failed = 0;
        failed += RunOneRow.Case("a strike beside its accepted step is named effect-off-binding, and the strike on it is quiet", AStrikeBesideItsStepIsNamed, Family);
        failed += RunOneRow.Case("a native effect with no accepted step is named effect-without-binding, and an unwired reader is not one", AnEffectWithNoStepIsNamed, Family);
        failed += RunOneRow.Case("an in-passing effect is bound by the incidental step accepted on its own tick and by no older one", AnIncidentalStepBindsOnlyItsOwnTick, Family);
        failed += RunOneRow.Case("a claimed pickup lands on the drop its step names, and an unclaimed one is not audited", AClaimedPickupLandsOnItsStepsDrop, Family);
        failed += RunOneRow.Case("the world-interaction operations the effect contract audits are still the ones the performers write", TheEffectOperationsStillMatchTheirPerformers, Family);
        failed += RunOneRow.Case("every activity that performs course work adopts the step it was handed", EveryCourseActivityAdoptsItsStep, Family);
        return failed;
    }

    // ── the strike ────────────────────────────────────────────────────────────────────────────────

    private static void AStrikeBesideItsStepIsNamed()
    {
        using var scene = new Scene(new BoundStepForAudit(41, "mine-target", "mine", "tile:7:25,59", 25, 59));
        GodsEyeEvents.RecordToolEffect(scene.Companion, "pickaxe", Strike(26, 59), 1, 1, 1);
        Require(Count(AuditDecisionContracts.EffectOffBinding) == 1 && Count(AuditDecisionContracts.EffectWithoutBinding) == 0,
            $"a pickaxe strike on 26,59 under a step naming 25,59 must be one effect-off-binding; counted {Counts()}");

        GodsEyeEvents.RecordToolEffect(scene.Companion, "pickaxe", Strike(25, 59), 1, 1, 1);
        Require(Total() == 1 && AuditDecisionContracts.EffectsAudited == 2,
            $"a strike on the step's own work tile must move no count; counted {Counts()} over {AuditDecisionContracts.EffectsAudited} audited effect(s)");
    }

    // ── no step ───────────────────────────────────────────────────────────────────────────────────

    private static void AnEffectWithNoStepIsNamed()
    {
        using var scene = new Scene(null);
        GodsEyeEvents.RecordWorldInteraction(scene.Companion, new Point(40, 50), AuditDecisionContracts.BreakPotOperation, "fixture;");
        Require(Count(AuditDecisionContracts.EffectWithoutBinding) == 1 && Count(AuditDecisionContracts.EffectOffBinding) == 0,
            $"a pot broken with no step held and none accepted must be one effect-without-binding; counted {Counts()}");

        // A refusal did nothing to the world and is not an effect whatever step is held.
        GodsEyeEvents.RecordWorldInteraction(scene.Companion, new Point(40, 50), "placement-refused", "fixture;");
        // An unwired reader is the question going unasked, never the companion acting without a step.
        AuditDecisionContracts.BindingSource = null;
        GodsEyeEvents.RecordWorldInteraction(scene.Companion, new Point(40, 50), AuditDecisionContracts.PlaceTorchOperation, "fixture;");
        Require(Total() == 1 && AuditDecisionContracts.EffectsAudited == 1,
            $"a refusal and an effect with no binding reader must move no count; counted {Counts()} over {AuditDecisionContracts.EffectsAudited} audited effect(s)");
    }

    // ── in passing ────────────────────────────────────────────────────────────────────────────────

    private static void AnIncidentalStepBindsOnlyItsOwnTick()
    {
        // The activity holding the body is lighting somewhere else: in-passing work is judged against the
        // step the course accepted for it, not against the activity's.
        using var scene = new Scene(new BoundStepForAudit(50, "light-target", "light", "tile:10,10", 10, 10));
        var pot = new BoundStepForAudit(51, "collect-target", "break-pot", "tile:40,50", 40, 50);
        long now = Main.GameUpdateCount;

        // A stale acceptance first: one tick old, it must not excuse the break.
        AuditDecisionContracts.AcceptIncidental(pot, now - 1);
        GodsEyeEvents.RecordWorldInteraction(scene.Companion, new Point(41, 51), AuditDecisionContracts.BreakPotOperation, "fixture;");
        Require(Count(AuditDecisionContracts.EffectOffBinding) == 1,
            $"a pot broken under an incidental step accepted on an earlier tick is off the activity's step; counted {Counts()}");

        // Accepted on this tick, the same break on another tile of the two-by-two is bound.
        AuditDecisionContracts.AcceptIncidental(pot, now);
        GodsEyeEvents.RecordWorldInteraction(scene.Companion, new Point(41, 51), AuditDecisionContracts.BreakPotOperation, "fixture;");
        Require(Total() == 1,
            $"a pot broken on 41,51 under the step naming the pot at 40,50, accepted this tick, must move no count; counted {Counts()}");

        // And the two-by-two is the whole tolerance: a tile past it is off the step even under acceptance.
        GodsEyeEvents.RecordWorldInteraction(scene.Companion, new Point(42, 50), AuditDecisionContracts.BreakPotOperation, "fixture;");
        Require(Count(AuditDecisionContracts.EffectOffBinding) == 2,
            $"a break on 42,50 is outside the pot at 40,50 and must be named off its step; counted {Counts()}");
    }

    // ── the drop ──────────────────────────────────────────────────────────────────────────────────

    private static void AClaimedPickupLandsOnItsStepsDrop()
    {
        using var scene = new Scene(new BoundStepForAudit(60, "collect-target", "collect", "item:7", null, null));
        GodsEyeEvents.RecordPickup(scene.Companion, Drop(8), 1, "cargo", 99);
        Require(Count(AuditDecisionContracts.EffectOffBinding) == 1,
            $"a pickup of slot 8 claimed by a collection attempt whose step names item:7 must be one effect-off-binding; counted {Counts()}");

        GodsEyeEvents.RecordPickup(scene.Companion, Drop(7), 1, "cargo", 99);
        GodsEyeEvents.RecordPickup(scene.Companion, Drop(9), 1, "cargo", 0);
        Require(Total() == 1 && AuditDecisionContracts.EffectsAudited == 2,
            $"the claimed pickup of the step's own drop and an unclaimed brush past another must move no count; counted {Counts()} over {AuditDecisionContracts.EffectsAudited} audited effect(s)");
    }

    // ── the producer pin ──────────────────────────────────────────────────────────────────────────

    /// <summary>The contract audits two world-interaction operations by name, and both names are
    /// literals in performer files this project does not compile; a rename there would make the torch or
    /// the pot silently unaudited while every row above still passed.</summary>
    private static void TheEffectOperationsStillMatchTheirPerformers()
    {
        string root = RepositoryRoot();
        (string Path, string Literal)[] pins =
        {
            ("Companion/Brain/Activities/NearbyAssistance/LightUsefulArea.cs", AuditDecisionContracts.PlaceTorchOperation),
            ("Companion/Brain/Activities/NearbyAssistance/CollectNearbyItems.cs", AuditDecisionContracts.BreakPotOperation),
        };
        foreach ((string path, string literal) in pins)
        {
            string full = Path.Combine(root, path);
            Require(File.Exists(full), $"the effect contract's performer {path} is not where it expects it, under {root}");
            Require(File.ReadAllText(full).Contains($"\"{literal}\"", StringComparison.Ordinal),
                $"{path} no longer records \"{literal}\", so that effect is silently outside the effect contract");
        }
    }

    // ── the private chooser pin ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every registered activity that declares a course domain overrides <c>OnAccept</c>, the hook the
    /// tick hands the accepted step through.
    ///
    /// An activity that performs course work without adopting its step is a second chooser: the course
    /// binds one target, the body flies to its pose, and the hand works whatever the activity's own search
    /// found. The effect contract catches that in play once it happens; this catches it in the source
    /// before it can, including for an activity added later. It is structural rather than behavioural on
    /// purpose — overriding the hook does not prove the activity uses the step, and the effect rows are
    /// what prove that — but an activity that does not even receive the step cannot be using it.
    /// </summary>
    private static void EveryCourseActivityAdoptsItsStep()
    {
        var offenders = new List<string>();
        int declaring = 0;
        foreach (live::AICompanion.Companion.Brain.Activities.CompanionAction activity in
                 live::AICompanion.Companion.Brain.Infrastructure.Selection.RegisterActivities.All())
        {
            if (activity.CourseDomains.Length == 0) continue;
            declaring++;
            MethodInfo? hook = activity.GetType().GetMethod("OnAccept", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Require(hook != null, $"CompanionAction.OnAccept is gone, so {activity.Name} has no way to be handed its step and this row must be rewritten against whatever replaced it");
            if (hook!.DeclaringType == typeof(live::AICompanion.Companion.Brain.Activities.CompanionAction))
                offenders.Add($"{activity.GetType().Name} ({string.Join(",", activity.CourseDomains)})");
        }
        Require(declaring > 0, "no registered activity declares a course domain, so this row asked nothing");
        Require(offenders.Count == 0,
            $"{offenders.Count} of {declaring} activities performing course work never adopt the step they are handed "
            + $"(no OnAccept override), so each acts on a target of its own choosing: {string.Join("; ", offenders)}");
    }

    // ── scene ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One effect scene: a clean audit, a binding reader that answers the given step, and both
    /// put back on disposal so the next case reads whatever it read before.</summary>
    private sealed class Scene : IDisposable
    {
        private readonly Func<BoundStepForAudit?>? priorSource = AuditDecisionContracts.BindingSource;
        public NPC Companion { get; } = new() { whoAmI = 0, width = 20, height = 20, position = new Vector2(400f, 800f) };

        public Scene(BoundStepForAudit? held)
        {
            AuditDecisionContracts.Reset();
            AuditDecisionContracts.BindingSource = () => held;
        }

        public void Dispose()
        {
            AuditDecisionContracts.BindingSource = priorSource;
            AuditDecisionContracts.Reset();
        }
    }

    private static AICompanion.Companion.Brain.Infrastructure.Interactions.TileToolObservation Strike(int x, int y)
    {
        var state = new AICompanion.Companion.Brain.Infrastructure.Interactions.TileToolState(true, 7, 0, 0, 0);
        return new AICompanion.Companion.Brain.Infrastructure.Interactions.TileToolObservation(
            Main.GameUpdateCount, 1, new Point(x, y), Terraria.ID.ItemID.CopperPickaxe, state, state with { Damage = 30 });
    }

    private static Item Drop(int slot)
    {
        var item = new Item();
        item.SetDefaults(Terraria.ID.ItemID.CopperOre);
        item.whoAmI = slot;
        item.stack = 1;
        return item;
    }

    private static long Count(string kind)
        => AuditDecisionContracts.Counts.TryGetValue(kind, out long value) ? value : 0;

    private static long Total() => AuditDecisionContracts.Counts.Values.Sum();

    private static string Counts()
        => AuditDecisionContracts.Counts.Count == 0 ? "(nothing fired)"
            : string.Join(", ", AuditDecisionContracts.Counts.Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>The repository root, found by walking up from the running assembly until the mod's own
    /// project file is beside us.</summary>
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
