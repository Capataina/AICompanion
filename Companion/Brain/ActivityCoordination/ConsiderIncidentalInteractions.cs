#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance;
using AICompanion.Companion.Brain.SharedMovementSystem;

namespace AICompanion.Companion.Brain.ActivityCoordination;

/// <summary>An interaction the companion made in passing: when, on which tile, by which method, and the activity executing at the time.
/// It is credited to no activity; the activity is recorded only so a reader can see what the body was doing.</summary>
public readonly record struct IncidentalInteraction(ulong Tick, Point Tile, string Method, string? DuringActivity, long ActivityId);

/// <summary>
/// Cheap compatible actions taken from where the body already is, at the grant boundary, so a lighting trip that passes a pot breaks it
/// without a second movement owner or a detour. The whole contract is what this refuses: it acts only on an ordinary execution tick whose
/// final grant left the hand Available and whose arm did not just fire; only within actual reach of the current pose; only on a target
/// the method's own permission, policy, supply, measurement and home-protection checks allow; never on the executing activity's own
/// method or target, since that activity will do it and counting it here would count one benefit twice; and it never requests a
/// position, writes movement or records productive work, so no activity's value or attempt conclusion includes it. A detour that would
/// need a new destination is not incidental; it is an ordinary activity choice.
/// </summary>
public sealed class ConsiderIncidentalInteractions
{
    // Library instances of each method's rules, never the chooser's activities, so asking them cannot disturb an activity's discovery.
    private readonly PerformNearbyWorldWork[] methods = { new CollectNearbyItems(), new LightUsefulArea() };
    private ulong nextScan;

    /// <summary>The last incidental interaction that produced its native effect, or none.</summary>
    public IncidentalInteraction? Last { get; private set; }

    public void Consider(in ActionContext ctx, HandGrant hand, bool armUsed, CompanionAction? executing, long activityId)
    {
        if (hand != HandGrant.Available || armUsed || ctx.Player.dead) return;
        ulong now = Main.GameUpdateCount;
        if (now < nextScan) return;
        // Optional work never extends a tick planning already filled: a tick whose shared allowance is spent skips the scan without
        // using up its turn, so the next tick with time left scans instead. The cadence limits scans per tick; this limits when one
        // may start, and the check between methods stops a scan that ran the allowance out part way.
        if (LimitPlanningWork.Expired) return;
        // The scan reads every tile in reach through each method's candidate rules, which for lighting include a light measurement and
        // the native torch selector, so it runs on a cadence rather than every tick.
        nextScan = now + (ulong)Weights.IncidentalScanTicks;
        foreach (PerformNearbyWorldWork method in methods)
        {
            if (LimitPlanningWork.Expired) return;
            if (executing != null && executing.GetType() == method.GetType()) continue;
            if (method.FindIncidentalTarget(ctx, executing?.ActivityIdentity) is not Point tile) continue;
            string note = $"incidental;during={executing?.Name ?? "none"};activity-id={activityId};credited-to=none;";
            if (!method.PerformIncidental(ctx, tile, note)) continue;
            Last = new IncidentalInteraction(now, tile, method.Name, executing?.Name, activityId);
            return;
        }
    }
}
