#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Activities.NearbyAssistance;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;
using FindToolAccess = AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;

namespace AICompanion.Companion.Brain.Infrastructure.Grants;

/// <summary>An interaction the companion made in passing: when, on which tile, by which method, the activity executing at the time, and the
/// one-step binding the course accepted for it. It is credited to no activity; the activity is recorded only so a reader can see what the
/// body was doing, and the binding id is what joins the native effect to the acceptance that licensed it.</summary>
public readonly record struct IncidentalInteraction(ulong Tick, Point Tile, string Method, string? DuringActivity, long ActivityId, long BindingId);

/// <summary>
/// Cheap compatible uses from where the body already is, proposed to the course and performed only once it accepts them, so a lighting trip
/// that passes a pot breaks it without a second movement owner or a detour — and a fight that passes a pot does not.
///
/// <para>**It proposes; it does not choose.** Until 23 September 2026 this scanned every tile in reach through the methods' own candidate rules
/// and made the native call on the first that passed, pricing nothing — it read no encounter and no protection urgency, so it broke pots in the
/// middle of a boss fight the course had just refused to break a pot in. The plan's grants row names exactly that as the post-grant second chooser
/// to remove. Now the candidates are the census's own sites whose work tile is in reach of the body, each is put to
/// <see cref="DecideCourseEachTick.AcceptIncidental"/> — the domain's real binder with zero travel, the course's own next-use validation and the
/// relevance optional work is priced under — and only an accepted one-step binding reaches the native call, after the live world is asked once
/// more whether the use still holds.</para>
///
/// <para>The contract is still mostly what it refuses: an ordinary execution tick whose final grant left the hand Available and whose arm did not
/// just fire; only within actual reach of the current pose; never a site the published course holds — the executing step's own, since that
/// activity will do it and counting it here would count one benefit twice, nor a later step's, since breaking it early would replace the
/// running course; and never a position request, a movement write or productive work recorded, so no activity's value or
/// attempt conclusion includes it. A detour that would need a new destination is not incidental; it is a course step.</para>
/// </summary>
public sealed class ConsiderIncidentalInteractions
{
    // Library instances of each method's rules, never the brain's activities, so asking them cannot disturb an activity's own state.
    private readonly CollectNearbyItems pots = new();
    private readonly LightUsefulArea lighting = new();
    private ulong nextScan;
    private readonly List<(float Distance, FactKey Site, Point Tile, PerformNearbyWorldWork Method)> inReach = new();
    private readonly HashSet<(string Domain, string Target)> planned = new();

    /// <summary>The last incidental interaction that produced its native effect, or none.</summary>
    public IncidentalInteraction? Last { get; private set; }

    public void Consider(in ActionContext ctx, HandGrant hand, bool armUsed, CompanionAction? executing, long activityId)
    {
        if (hand != HandGrant.Available || armUsed || ctx.Player.dead) return;
        ulong now = Main.GameUpdateCount;
        if (now < nextScan) return;
        // Optional work never extends a tick planning already filled: a tick whose shared allowance is spent skips the scan without
        // using up its turn, so the next tick with time left scans instead. The cadence limits scans per tick; this limits when one
        // may start, and the check between proposals stops a scan that ran the allowance out part way.
        if (LimitPlanningWork.Expired) return;
        DecideCourseEachTick course = ctx.Companion.Brain.Course;
        if (course.Facts is not { } facts) return;
        nextScan = now + (ulong)Weights.IncidentalScanTicks;

        // Every site the published course holds is the course's to work, in its order: the executing step's because that activity
        // will do it, and a later step's because breaking it in passing would invalidate that step and replace the running course,
        // which acceptance is not allowed to do. The executing activity's own binding is added for an unsettled tick, where the
        // activity may carry a step the course has not published.
        planned.Clear();
        if (course.Course.Current is { } published)
            foreach (StepBinding step in published.Projection.Steps) planned.Add((step.Opportunity.Domain, step.Opportunity.Target));
        if (ctx.Companion.Brain.Activity.Binding is { } own) planned.Add((own.Opportunity.Domain, own.Opportunity.Target));
        inReach.Clear();
        foreach (DecisionFact fact in facts.Facts)
        {
            // The census's two tile uses: a torch site under lighting's domain and a pot under collection's, whose tile identity is the
            // `tile:x,y` shape `ExecuteCourseBinding.WorkTileOf` reads. A drop is collection's other act, taken by contact and named by
            // its item slot (`item:N`), so it has no tile to be in reach of and is never an incidental use.
            (PerformNearbyWorldWork Method, string Purpose)? use = fact.Key.Kind switch
            {
                "collect-target" when fact.Key.Identity.StartsWith("tile:", StringComparison.Ordinal) => (pots, OpportunityPurposes.BreakPot),
                "light-target" => (lighting, OpportunityPurposes.Light),
                _ => null,
            };
            if (use is not { } found) continue;
            PerformNearbyWorldWork method = found.Method;
            if (planned.Contains((fact.Key.Kind, fact.Key.Identity))) continue;
            Point tile = ExecuteCourseBinding.WorkTileOf(new OpportunityKey(fact.Key.Kind, found.Purpose, fact.Key.Identity, fact.Key.Generation));
            if (!FindToolAccess.InReach(ctx.Npc.Center, tile)) continue;
            inReach.Add((Vector2.DistanceSquared(ctx.Npc.Center, tile.ToWorldCoordinates()), fact.Key, tile, method));
        }
        inReach.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        foreach (var (_, site, tile, method) in inReach)
        {
            if (LimitPlanningWork.Expired) return;
            IncidentalAcceptance answer = course.AcceptIncidental(ctx, site);
            if (answer.Binding is not { } step) continue;
            // The acceptance was against the frozen observation; the native call is against the live world, which may have moved since.
            if (!method.MayActIncidentally(ctx, tile)) continue;
            string note = $"incidental;during={executing?.Name ?? "none"};activity-id={activityId};binding-id={step.Id};credited-to=none;";
            if (!method.PerformIncidental(ctx, tile, note)) continue;
            Last = new IncidentalInteraction(now, tile, method.Name, executing?.Name, activityId, step.Id);
            return;
        }
    }
}
