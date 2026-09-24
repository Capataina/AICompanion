#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Movement;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// Sparse, occurrence-time event companion to the per-tick TSV. Every method snapshots its
/// native arguments before returning: Terraria slots and entity state are mutable and may be
/// reused before a buffered file write happens.
/// </summary>
public static class GodsEyeEvents
{
    private static readonly Dictionary<int, int> npcGenerations = new();
    private static readonly Dictionary<int, int> projectileGenerations = new();
    private static readonly Dictionary<int, int> itemGenerations = new();
    /// <summary>What was last seen in each item slot, so a slot reused by a different drop opens a new
    /// generation. See <see cref="ItemIdentity"/> for why the slot alone cannot say.</summary>
    private static readonly Dictionary<int, (int Type, int Age)> itemOccupants = new();
    private static int sequence;
    private static int lastMovementSteps = -1;
    private static Navigator.ExecutionStatus lastMovementStatus;
    private static AttemptEnding? lastMovementEnding;
    private static string? lastNavigationEvidence;
    private static string? lastActivityEvidence;
    private static string? lastControlEvidence;
    private static long lastAttemptRecorded;
    private static int cosmeticContacts;
    private static Vector2 cosmeticFirst, cosmeticLast;
    private static long cosmeticFirstTick;
    internal static bool Active => active;
    private static bool active;
    // Set when a failed write stops the stream, cleared when a session opens: before Open and after a normal Close nothing
    // is being recorded, so an occurrence offered then is not a loss and is not counted as one.
    private static bool disabled;
    internal const int CosmeticContactsPerSummary = 128;
    /// <summary>Records handed to the writer this session, its opening and closing records included.</summary>
    internal static int Written => sequence;
    /// <summary>Occurrences refused this session because a failed write had stopped the stream; the sidecar cannot record its own loss.</summary>
    internal static long Dropped { get; private set; }
    /// <summary>Cosmetic terrain contacts folded into summary records this session.</summary>
    internal static long Coalesced { get; private set; }

    /// <summary>Whether an occurrence can be written, counting it as dropped when the stream stopped after a failed write.</summary>
    private static bool Accepting()
    {
        if (active) return true;
        if (disabled) Dropped++;
        return false;
    }
    // The `candidate-funnel` occurrence and its coalescing key went with the family chooser's telemetry
    // under schema 0.46.0, because the funnel's subject is a preparation-time shortlist the course does
    // not keep; `course-admitted:<domain>` beside `course-refused:<reason>` is the per-domain equivalent
    // and has been in the decision occurrence since 0.42.0. The funnel object itself is not gone: three
    // activities still fill one and seven fixtures read it, so it is a live in-process diagnostic with no
    // recorded form rather than dead work. Anything reviving the occurrence writes a new key here rather
    // than restoring this one, since a coalescer with no writer is state nobody clears.

    /// <summary>
    /// One window of frames that took longer than the engine's own fixed timestep, with the worst
    /// one's whole split beside it.
    ///
    /// It is a window rather than a frame because a session in trouble overruns on most of its frames
    /// — 84% of them on the 22 September 2026 capture — and an occurrence each would be two thousand
    /// records carrying one fact. The share across a session is read off the `frame_ms` column, where
    /// every row has one; this is what a reader opens when they want a hitch attributed. The amount is
    /// the count of overruns and the channel is the window they fell in, so a reader can take a rate
    /// without reconstructing the cadence.
    /// </summary>
    public static void RecordFrameOverrun(NPC companion, int overruns, int windowTicks, double worstMilliseconds, string split)
    {
        if (!Accepting()) return;
        Write("frame-overrun", Stable(npcGenerations, companion.whoAmI), "",
            worstMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
            windowTicks.ToString(CultureInfo.InvariantCulture), companion.Center, Vector2.Zero, Vector2.Zero,
            overruns, FormattableString.Invariant($"worst-frame-ms={worstMilliseconds:0.00};window-ticks={windowTicks};overruns={overruns};{split}"));
    }

    /// <summary>
    /// The worst brain tick of one spike window: a tick whose cost crossed the fence computed from the session's own
    /// recent ticks (<c>DetectCostSpikes</c>), with the whole account of where its time went. The label is its cost
    /// in milliseconds, the channel the window's length in ticks and the amount how many ticks in the window crossed,
    /// so a burst is one record that says how long it was; the detail is built by the recorder, which holds the tree.
    /// </summary>
    public static void RecordCostSpike(NPC? companion, double costMilliseconds, int spikesInWindow, int windowTicks, string detail)
    {
        if (!Accepting()) return;
        Write("cost-spike", companion == null ? 0 : Stable(npcGenerations, companion.whoAmI), "",
            costMilliseconds.ToString("0.000", CultureInfo.InvariantCulture),
            windowTicks.ToString(CultureInfo.InvariantCulture), companion?.Center ?? Vector2.Zero, Vector2.Zero, Vector2.Zero,
            spikesInWindow, FormattableString.Invariant($"spikes-in-window={spikesInWindow};window-ticks={windowTicks};{detail}"));
    }

    /// <summary>
    /// One world interaction. The two operations that are native effects — a torch placed, a pot broken —
    /// are audited against the accepted step (<c>AuditDecisionContracts.ObserveEffect</c>) before the stream
    /// gate, so the contract counts with no session open; every occurrence then carries the step it was
    /// performed under (schema 0.47.0). A refusal or an abandoned approach did nothing to the world and
    /// carries <c>binding-verdict=not-an-effect</c> beside the step that was held.
    /// </summary>
    public static void RecordWorldInteraction(NPC companion, Point tile, string operation, string detail)
    {
        EffectVerdict verdict = operation is AuditDecisionContracts.PlaceTorchOperation or AuditDecisionContracts.BreakPotOperation
            ? AuditDecisionContracts.ObserveEffect("world-interaction", operation, tile.X, tile.Y, null, Main.GameUpdateCount)
            : new EffectVerdict(0, "not-an-effect", "not-an-effect");
        Write("world-interaction", companion.whoAmI, "", operation, "", companion.Bottom, Vector2.Zero,
            tile.ToWorldCoordinates(), 0, detail + verdict.Fields);
    }

    /// <summary>One strike. <c>attempt=</c> is the tool instance's own counter and names nothing the activity owner knows;
    /// <paramref name="activityAttemptId"/> is the owner's attempt open when Execute struck, zero when none was, and is what a reader joins on.
    /// Every strike is audited against the accepted step before the stream gate, whether or not it damaged the tile:
    /// the hand acting beside its step is the defect, and a strike that did nothing is still the hand acting.</summary>
    public static void RecordToolEffect(NPC companion, string tool, in Infrastructure.Interactions.TileToolObservation outcome, long choiceId, long activityId, long activityAttemptId)
    {
        EffectVerdict verdict = AuditDecisionContracts.ObserveEffect("tool-effect", tool, outcome.Target.X, outcome.Target.Y, null, Main.GameUpdateCount);
        if (!Accepting()) return;
        Write("tool-effect", Stable(npcGenerations, companion.whoAmI), "", tool,
            $"attempt={outcome.Attempt};choice-id={choiceId};activity-id={activityId};activity-attempt-id={activityAttemptId}", companion.Bottom, Vector2.Zero, outcome.Target.ToWorldCoordinates(),
            outcome.Effect == Infrastructure.Interactions.TileToolEffect.Damaged ? outcome.After.Damage - outcome.Before.Damage : 0,
            FormattableString.Invariant($"observation-tick={outcome.Tick};tool-item={outcome.ToolItem};effect={outcome.Effect};before-present={outcome.Before.Present};before-type={outcome.Before.Type};before-frame={outcome.Before.FrameX},{outcome.Before.FrameY};before-damage={outcome.Before.Damage};after-present={outcome.After.Present};after-type={outcome.After.Type};after-frame={outcome.After.FrameX},{outcome.After.FrameY};after-damage={outcome.After.Damage};damage-scope=tool-owned-hit-table;yield=unobserved")
                + verdict.Fields);
    }

    public static void RecordActivity(NPC companion, long id, string name, string phase, string reason,
        long endedId, string endReason, ulong changedAt)
    {
        if (!Accepting()) return;
        int actor = Stable(npcGenerations, companion.whoAmI);
        string detail = $"activity-id={id};phase={phase};reason={reason};last-ended-id={endedId};last-end-reason={endReason};changed-at={changedAt}";
        string key = $"{actor}:{name}:{detail}";
        if (key == lastActivityEvidence) return;
        lastActivityEvidence = key;
        Write("activity-state", actor, "", name, phase, companion.Bottom, Vector2.Zero, Vector2.Zero, 0, detail);
    }

    public static void RecordControlGrant(NPC companion, long id, ulong tick, long activityId, string activityPhase,
        string requestedOwner, string appliedOwner, string requestedControls, string appliedControls,
        string hand, Vector2 appliedVelocity, long motorApplications, long attemptId = 0)
    {
        if (!Accepting()) return;
        int actor = Stable(npcGenerations, companion.whoAmI);
        // IDs advance every tick. Emit ownership transitions here; the TSV retains every grant.
        string key = $"{actor}:{activityId}:{attemptId}:{activityPhase}:{requestedOwner}:{appliedOwner}:{hand}:{motorApplications}";
        if (key == lastControlEvidence) return;
        lastControlEvidence = key;
        Write("control-grant", actor, "", appliedOwner, hand, companion.Bottom, appliedVelocity, Vector2.Zero, 0,
            $"grant-id={id};grant-tick={tick};activity-id={activityId};attempt-id={attemptId};activity-phase={activityPhase};requested-owner={requestedOwner};applied-owner={appliedOwner};requested-controls={requestedControls};applied-controls={appliedControls};hand={hand};motor-applications={motorApplications};scope=ai-phase-before-engine;hand-effect=unobserved");
    }

    /// <summary>One concluded attempt, written once. The caller offers every retained outcome and
    /// the cursor skips those already written, so two conclusions inside one tick both reach the
    /// record. Plain fields keep this writer compilable beside the replay stubs without the brain.</summary>
    public static void RecordAttemptOutcome(NPC companion, long attemptId, long activityId, string activity, string family,
        ulong startTick, ulong endTick, string status, string cause, int productiveEffects, string attribution,
        int claimedYieldType, int claimedYieldQuantity)
    {
        if (!Accepting() || attemptId <= lastAttemptRecorded) return;
        lastAttemptRecorded = attemptId;
        // The channel carries attribution beside status wherever one applies, so a reader grouping
        // by channel cannot fold a shared or unattributed completion into the companion's own.
        string channel = attribution == "NotApplicable" ? status : status + ":" + attribution;
        Write("attempt-outcome", Stable(npcGenerations, companion.whoAmI), "", activity, channel,
            companion.Bottom, Vector2.Zero, Vector2.Zero, productiveEffects,
            $"attempt-id={attemptId};activity-id={activityId};family={family};start-tick={startTick};end-tick={endTick};status={status};attribution={attribution};cause={cause};productive-effects={productiveEffects};effect-scope=companion-credited-tool-or-interaction-effects;interruption-is-not-failure=true;claimed-yield-type={claimedYieldType};claimed-yield-quantity={claimedYieldQuantity}");
    }

    /// <summary>
    /// One experience credit: what earned it (enemy-kill, boss-fight, ore, tree, torch), who (companion, player), how much in
    /// displayed experience, the level before and after, where the bar stands, and both anchors with the one this credit
    /// moved, so a play capture shows the bar moving and why. The subject is the level after; the channel is the anchor
    /// changed. Plain values, because the headless tools compile this file without the progression folder.
    /// </summary>
    public static void RecordExperienceCredit(string source, string earner, double earned, int levelBefore, int levelAfter, double into,
        double required, double enemyAnchorLife, int enemyAnchorLevel, double bossAnchorLife, int bossAnchorLevel, string anchorChanged,
        Vector2 where, string detail)
    {
        if (!Accepting()) return;
        Write("experience-credit", levelAfter, earner, source, anchorChanged, where, Vector2.Zero, Vector2.Zero,
            (int)Math.Min(int.MaxValue, Math.Round(earned)),
            FormattableString.Invariant($"source={source};earner={earner};earned={earned:0.###};level-before={levelBefore};level-after={levelAfter};into={into:0.###};required={required:0.###};enemy-anchor={enemyAnchorLife:0}@{enemyAnchorLevel};boss-anchor={bossAnchorLife:0}@{bossAnchorLevel};anchor-changed={anchorChanged};{detail}"));
    }

    internal static void Open(string path)
    {
        Close();
        // A telemetry stem is reserved by the TSV writer before its sidecars open. CreateNew is
        // still intentional here: a sidecar collision must fail loudly rather than turn a later
        // load retry into an apparently complete earlier session.
        active = true;
        npcGenerations.Clear(); projectileGenerations.Clear(); itemGenerations.Clear(); itemOccupants.Clear(); sequence = 0;
        RecordTerrainChunks.Reset();
        lastMovementSteps = -1;
        lastMovementEnding = null;
        lastNavigationEvidence = null;
        lastActivityEvidence = null;
        lastControlEvidence = null;
        lastAttemptRecorded = 0;
        cosmeticContacts = 0;
        disabled = false;
        Dropped = 0;
        Coalesced = 0;
        // Main.GameUpdateCount can survive a prior world in a host process. It is never claimed as
        // this session's start tick; occurrence ticks remain useful only relative to one another.
        Write("session", 0, "", "", "", Vector2.Zero, Vector2.Zero, Vector2.Zero, 0, "schema=1;start-tick=unknown;capture=sparse-events;terrain=rolling-local-16x16-chunks;radius=3-chunks;nominal-scan=49-ticks;max-chunks-per-tick=2;unseen=unknown");
    }

    /// <summary>
    /// One companion tick's replay inputs: what the tick found in the world and what it was allowed, with the
    /// decision it made beside them. `ReplayInputs` (`RecordReplayInputs.cs`, which the headless tools do not compile)
    /// owns the format and is the only producer. Returns whether the line reached the writer's queue, because the line is
    /// delta-encoded and its producer has to start the next one from nothing when it did not.
    /// </summary>
    internal static bool RecordReplayInputs(NPC companion, string detail)
        => Write("replay-inputs", 0, "", "", "", companion.Center, companion.velocity, Vector2.Zero, 0, detail);

    /// <summary>
    /// Records only the callback this mod observed. A returned ModSystem callback is useful
    /// lifecycle evidence, but cannot establish that Terraria completed the outer operation.
    /// </summary>
    internal static void RecordLifecycle(string phase, string evidence)
        => Write("lifecycle", 0, "", phase, "", Vector2.Zero, Vector2.Zero, Vector2.Zero, 0, evidence);

    /// <summary>The preferences and diagnostics switches changed mid-session; the detail has the TSV preamble's <c>config</c> shape.</summary>
    internal static void RecordConfiguration(string detail)
        => Write("configuration", 0, "", "changed", "", Vector2.Zero, Vector2.Zero, Vector2.Zero, 0, detail);

    internal static void Close()
    {
        FlushCosmeticContacts();
        Write("session-end", 0, "", "", "", Vector2.Zero, Vector2.Zero, Vector2.Zero, sequence, "normal-close");
        active = false;
    }

    internal static void Flush()
    {
        // The worker flushes after every bounded drain. Producers deliberately have no stream operation here.
    }

    /// <summary>Queues a typed retained-course occurrence on the same ordered sidecar as native receipts.</summary>
    internal static bool RecordCourse(CourseTracePhase phase, CourseTraceContext context, CourseTracePayload payload,
        bool required, CourseDecisionSnapshot? snapshot)
    {
        if (!Accepting()) { if (required) disabled = true; return false; }
        var record = new EventRecord(1, sequence++, context.SourceTick, BrainTelemetry.ElapsedMilliseconds, "course-" + payload.Kind,
            0, "", context.Producer, "", 0, 0, 0, 0, 0, 0, 0, "",
            payload.Kind, payload.Version, phase.ToString(), context.ObservationOrdinal, context.ReceiptWatermark, context, payload, snapshot);
        return QueueDiagnosticRecords.TryEnqueueCourse(record, required, QueueDiagnosticRecords.Estimate(context, payload, snapshot));
    }

    public static void RecordNpcSpawn(NPC npc) => Write("npc-spawn", Next(npcGenerations, npc.whoAmI), npc.type.ToString(CultureInfo.InvariantCulture), npc.TypeName, "", npc.Center, npc.velocity, Vector2.Zero, npc.life, FormattableString.Invariant($"slot={npc.whoAmI}"));
    public static void RecordNpcDeath(NPC npc) => Write("npc-death", Stable(npcGenerations, npc.whoAmI), npc.type.ToString(CultureInfo.InvariantCulture), npc.TypeName, "", npc.Center, npc.velocity, Vector2.Zero, npc.life, FormattableString.Invariant($"slot={npc.whoAmI}"));

    public static void RecordShot(NPC shooter, NPC? target, int projectileIndex, Vector2 muzzle, Vector2 launchVelocity, Vector2 expectedImpact, string weapon, int impactTicks = -1, float attackValue = 0f, int expectedKills = 0, float preventedHarm = 0f,
        int planId = -1, int planSegment = -1, int planUse = -1, string predicted = "", string sim = "")
    {
        int projectile = Stable(projectileGenerations, projectileIndex);
        Write("shot", Stable(npcGenerations, shooter.whoAmI), target == null ? "" : Stable(npcGenerations, target.whoAmI).ToString(CultureInfo.InvariantCulture), weapon, $"projectile={projectile}", muzzle, launchVelocity, expectedImpact, 0, FormattableString.Invariant($"expected-flight-ticks={impactTicks};sequence-value={attackValue:0.000};sequence-kills={expectedKills};sequence-prevented-harm={preventedHarm:0.000};plan={planId};segment={planSegment};use={planUse};predicted={predicted};sim={sim}"));
    }

    /// <summary>
    /// One projectile use grouped, either shooter's: what the item put into the world this time. Plain values
    /// rather than the use itself, because the headless tools compile this file on its own. <paramref name="slots"/>
    /// is the use's measured decomposition, one <c>slot=i,type=,angle=,speed=,share=,origin=,delay=</c> entry per
    /// spawn ordered by delay then angle; <paramref name="buffs"/> the shooter's buffs at the use's start.
    /// </summary>
    public static void RecordVolleyObserved(int itemType, string shooter, int useId, bool complete, int spawnCount, string slots, string buffs)
    {
        if (!Accepting()) return;
        string name = Terraria.ID.ItemID.Search.TryGetName(itemType, out string? found) && found != null ? found : $"item-{itemType}";
        Write("volley-observed", 0, useId.ToString(CultureInfo.InvariantCulture), name, shooter,
            Vector2.Zero, Vector2.Zero, Vector2.Zero, spawnCount,
            FormattableString.Invariant($"item={itemType};complete={(complete ? 1 : 0)};buffs={buffs};slots={slots}"));
    }

    /// <summary>
    /// One companion projectile's whole flight in one record, written at its death: the bounded-per-shot form of
    /// the wall contacts, body hits, child spawns and death the trace holds. Counts for all three, firsts for the
    /// wall (tick, velocity in and out) and the hit (NPC stable id, tick, damage), types for the children.
    /// </summary>
    public static void RecordShotEvent(int projectileSlot, int projectileType, string death, int walls, string firstWall,
        int hits, string firstHit, int children, string childTypes)
    {
        if (!Accepting()) return;
        Write("shot-event", Stable(projectileGenerations, projectileSlot), "", projectileType.ToString(CultureInfo.InvariantCulture), death,
            Vector2.Zero, Vector2.Zero, Vector2.Zero, hits,
            FormattableString.Invariant($"death={death};walls={walls};first-wall={firstWall};hits={hits};first-hit={firstHit};children={children};child-types={childTypes}"));
    }

    /// <summary>
    /// One projectile type's flight law at a new revision: the terms kept and their parameters, the residual,
    /// the evidence behind it and whether it predicts. Plain values, because the headless tools compile this file
    /// on its own. <paramref name="terms"/> is the law's semicolon-joined description.
    /// </summary>
    public static void RecordFlightLaw(int projectileType, int revision, string terms, float residual, int evidence, bool predictable)
    {
        if (!Accepting()) return;
        Write("flight-law", 0, revision.ToString(CultureInfo.InvariantCulture), projectileType.ToString(CultureInfo.InvariantCulture),
            predictable ? "predictable" : "unpredictable", Vector2.Zero, Vector2.Zero, Vector2.Zero, evidence,
            FormattableString.Invariant($"residual={residual:0.0000};evidence={evidence};terms={terms}"));
    }

    /// <summary>
    /// One attack plan's life event: committed, advanced to its next segment, or invalidated. The detail is
    /// <c>DescribeAttackPlan</c>'s semicolon form — segments, vector, weighted value, front size, the three best
    /// rejected plans with dominated-or-weights and the objective each lost on, the budget cut and the
    /// invalidation reason — built by the caller, because the headless tools compile this file on its own.
    /// </summary>
    public static void RecordCombatPlan(NPC companion, int planId, string phase, Vector2 stand, int frontSize, string detail)
    {
        if (!Accepting()) return;
        Write("combat-plan", Stable(npcGenerations, companion.whoAmI), "", $"plan-{planId}", phase,
            stand, Vector2.Zero, Vector2.Zero, frontSize, detail);
    }

    /// <summary>
    /// One combat decision's complete input as a JSON document: the body, gear and knowledge, the enemy
    /// forecast with its motion history, hostile projectiles, the player and his intent region, the terrain
    /// window, the stand verdicts, the budget, the weights and the committed plan. Written on every commit,
    /// at a bounded rate while a plan is committed, and on the inspector's mark key; the audit replays the
    /// decision from this alone. Built by the caller, for the same standalone reason as above.
    /// </summary>
    public static void RecordCombatSnapshot(NPC companion, int planId, string trigger, string json)
    {
        if (!Accepting()) return;
        Write("combat-snapshot", Stable(npcGenerations, companion.whoAmI), "", $"plan-{planId}", trigger,
            Vector2.Zero, Vector2.Zero, Vector2.Zero, json.Length, json);
    }

    public static void RecordProjectileOutcome(Projectile projectile, NPC? hit, string outcome)
    {
        // Neutral, non-damaging projectiles create harmless terrain contacts continuously.
        // Companion/friendly, hostile, damaging and every hit/death outcome remain exact.
        if (outcome == "terrain-hit" && projectile.damage <= 0 && !projectile.friendly && !projectile.hostile)
        {
            if (cosmeticContacts == 0) { cosmeticFirst = projectile.Center; cosmeticFirstTick = Main.GameUpdateCount; }
            cosmeticLast = projectile.Center;
            cosmeticContacts++;
            if (cosmeticContacts >= CosmeticContactsPerSummary) FlushCosmeticContacts();
            return;
        }
        Write("projectile-" + outcome, outcome == "spawn" ? Next(projectileGenerations, projectile.whoAmI) : Stable(projectileGenerations, projectile.whoAmI), hit == null ? "" : Stable(npcGenerations, hit.whoAmI).ToString(CultureInfo.InvariantCulture), projectile.type.ToString(CultureInfo.InvariantCulture), $"owner={projectile.owner}", projectile.Center, projectile.velocity, Vector2.Zero, projectile.damage, "");
    }

    private static void FlushCosmeticContacts()
    {
        if (cosmeticContacts == 0) return;
        Write("projectile-terrain-contact-summary", 0, "", "cosmetic-glowstick", "coalesced", cosmeticLast, Vector2.Zero, cosmeticFirst, cosmeticContacts,
            $"first-tick={cosmeticFirstTick};first={cosmeticFirst.X:0.0},{cosmeticFirst.Y:0.0};last={cosmeticLast.X:0.0},{cosmeticLast.Y:0.0}");
        Coalesced += cosmeticContacts;
        cosmeticContacts = 0;
    }

    /// <summary>One accepted contact pickup. <paramref name="collectionAttemptId"/> is the open collection attempt when this item is
    /// the drop that attempt walked toward, zero for every other pickup, so a reader sums an attempt's received quantity by identity.
    /// A claimed pickup is the collection's native effect and is audited against the accepted step before the stream gate; a
    /// pickup no attempt claimed is the body brushing a drop in passing, which no decision chose, and carries
    /// <c>binding-verdict=not-claimed</c>.</summary>
    public static void RecordPickup(NPC companion, Item item, int amount, string destination, long collectionAttemptId)
    {
        EffectVerdict verdict = collectionAttemptId != 0
            ? AuditDecisionContracts.ObserveEffect("pickup", "claimed-pickup", null, null, item.whoAmI, Main.GameUpdateCount)
            : new EffectVerdict(0, "not-claimed", "not-claimed");
        Write("pickup", Stable(npcGenerations, companion.whoAmI), ItemIdentity(item).ToString(CultureInfo.InvariantCulture), item.type.ToString(CultureInfo.InvariantCulture), destination, item.Center, Vector2.Zero, Vector2.Zero, amount, $"stack={item.stack};collection-attempt-id={collectionAttemptId}" + verdict.Fields);
    }

    /// <summary>One drop the loot sense has just admitted for the first time, so a reader can stage a
    /// drop the companion saw and never reached. Before it, the only items a capture named were the
    /// ones a funnel considered or a pickup transferred, and the 22 September capture's tail carried
    /// three drops nameable nowhere at all.</summary>
    public static void RecordDropSighted(Item item, float distanceToCompanion, float value)
        => Write("drop-sighted", 0, ItemIdentity(item).ToString(CultureInfo.InvariantCulture),
            item.type.ToString(CultureInfo.InvariantCulture), "loot-sense", item.Center, item.velocity, Vector2.Zero, item.stack,
            $"slot={item.whoAmI};stack={item.stack};distance={distanceToCompanion:0.0};value={value:0.000}");

    /// <summary>A drop the engine has just spawned, which opens a new generation on that slot.</summary>
    public static void RecordItemSpawn(Item item)
    {
        itemOccupants[item.whoAmI] = (item.type, item.timeSinceItemSpawned);
        Next(itemGenerations, item.whoAmI);
    }

    /// <summary>
    /// The stable identity of the item in a slot, advancing the generation whenever the slot's occupant
    /// has changed since this recorder last looked.
    ///
    /// <b>Why the slot alone is not the item.</b> Generations used to advance only from
    /// <c>GlobalItem.OnSpawn</c>, which a drop staged directly into <c>Main.item</c> never fires — a
    /// world run, a fixture, or anything the engine does not route through its own spawn path — so slot
    /// 1 carried generation 1 through two entirely different items and `pickup.related` joined a
    /// transfer to whichever of them a reader assumed. Comparing what is in the slot now against what
    /// was in it last time catches the whole class without depending on which path put it there:
    /// a different type is a different item, and a lower <c>timeSinceItemSpawned</c> is a different
    /// item of the same type, because that counter only ever climbs while one item occupies a slot.
    /// </summary>
    internal static int ItemIdentity(Item item)
    {
        int slot = item.whoAmI, age = item.timeSinceItemSpawned;
        bool replaced = itemOccupants.TryGetValue(slot, out (int Type, int Age) seen)
            && (seen.Type != item.type || age < seen.Age);
        itemOccupants[slot] = (item.type, age);
        return replaced ? Next(itemGenerations, slot) : Stable(itemGenerations, slot);
    }

    public static void RecordEffectiveNpcDamage(NPC subject, NPC.HitInfo hit, int damageDone)
        => Write("npc-damage", Stable(npcGenerations, subject.whoAmI), "", subject.TypeName, "", subject.Center, subject.velocity, Vector2.Zero, Math.Max(0, damageDone), $"raw={hit.Damage};effective={Math.Max(0, damageDone)};life-now={subject.life};knockback={hit.Knockback:0.00}");

    // ModPlayer.OnHurt precedes health subtraction; PostHurt omits fatal hits. Preserve the
    // observed pre-state and label the computed successor as expected, never observed health.
    public static void RecordPlayerDamage(Player subject, Player.HurtInfo hit)
        => Write("player-damage", 0, "", "player", "before-health-subtraction", subject.Center, subject.velocity, Vector2.Zero,
            Math.Min(hit.Damage, Math.Max(0, subject.statLife)), $"resolved-damage={hit.Damage};life-before={subject.statLife};expected-life-after={Math.Max(0, subject.statLife - hit.Damage)};knockback={hit.Knockback:0.00}");

    public static void RecordMovementOutcome(NPC companion, string step, string outcome, string detail)
        => Write("movement-" + outcome, Stable(npcGenerations, companion.whoAmI), "", step, "", companion.Center, companion.velocity, Vector2.Zero, 0, detail);

    public static void RecordDecision(NPC companion, string winner, string board, string request, string controls)
        => Write("decision", Stable(npcGenerations, companion.whoAmI), "", winner, request, companion.Center, companion.velocity, Vector2.Zero, 0, $"scores={board};controls={controls}");

    public static void RecordMethodAssessment(NPC companion, NPC? target, int targetGeneration,
        long comparison, string activity, string family, string request, int evidenceTick,
        float raw, float compared, Vector2? destination, string reason, string candidates)
    {
        if (!Accepting()) return;
        // This occurrence precedes activation and comparison completion. A missing later
        // decision remains missing; an admitted method is not a completed activity.
        Write("method-assessment", Stable(npcGenerations, companion.whoAmI),
            target == null ? "" : Stable(npcGenerations, target.whoAmI).ToString(CultureInfo.InvariantCulture),
            activity, destination == null ? "not-established" : "admitted", companion.Bottom,
            Vector2.Zero, destination ?? Vector2.Zero, 0,
            FormattableString.Invariant($"choice-id={comparison};choice-phase=pre-activation;family={family};request={request};target-slot={target?.whoAmI ?? -1};target-observer-generation={targetGeneration};position-evidence-tick={evidenceTick};raw={raw:R};compared={compared:R};selectable={(destination == null ? 0f : compared):R};destination-present={destination != null};reason={reason};candidates={candidates};scope=bounded-position-query;travel=unobserved;native-effect=unobserved"));
    }

    public static void RecordMovementState(NPC companion, Navigator navigator)
    {
        if (navigator.Status == lastMovementStatus && navigator.RemainingRouteSteps == lastMovementSteps
            && navigator.LastEnding == lastMovementEnding && Main.GameUpdateCount % 60 != 0) return;
        lastMovementStatus = navigator.Status;
        lastMovementSteps = navigator.RemainingRouteSteps;
        lastMovementEnding = navigator.LastEnding;
        string detail = FormattableString.Invariant($"status={navigator.Status};search={navigator.LastSearchStop};goal={navigator.GoalTile};route-steps={navigator.Path?.Count ?? 0};route-index={navigator.Path?.Index ?? -1};remaining-steps={navigator.RemainingRouteSteps};progress={navigator.ProgressReason};stuck-ticks={navigator.StuckTicks};stuck-strikes={navigator.StuckStrikes};attempt-ending={navigator.LastEnding?.ToString() ?? "-"}");
        RecordMovementOutcome(companion, navigator.Path is { } path ? "segment-" + path.Index.ToString(CultureInfo.InvariantCulture) : "none", "state", detail);
    }

    /// <summary>
    /// A transition record from the navigation boundary. It is deliberately sampled by telemetry,
    /// rather than letting navigation write diagnostics, so the portable mover remains independent
    /// of game recording and the record can say whether this was a fresh brain decision.
    /// </summary>
    public static void RecordNavigationEvidence(NPC companion, bool brainExecuted, string action, string request, string controls,
        long searchId, long attemptId, int expansions, bool pending, string progressReason, int experienceRoutesUsed,
        int candidates, int reachableCandidates, int rejectedCandidates, string choiceReason, float interventionTicks, float protectionUrgency, float predictionConfidence, int predictionSamples, string localMovement = "")
    {
        string key = string.Join('|', brainExecuted, action, request, controls, searchId, attemptId, expansions, pending, progressReason, experienceRoutesUsed, candidates, reachableCandidates, rejectedCandidates, choiceReason, interventionTicks, protectionUrgency, predictionConfidence, predictionSamples, localMovement);
        if (key == lastNavigationEvidence && Main.GameUpdateCount % 60 != 0)
            return;
        lastNavigationEvidence = key;
        string freshness = brainExecuted ? "fresh" : "stale-or-not-executed";
        Write("navigation-state", Stable(npcGenerations, companion.whoAmI), "", action, request, companion.Center, companion.velocity, Vector2.Zero, 0,
            $"freshness={freshness};controls={controls};search-id={searchId};attempt-id={attemptId};search-pending={pending};search-expansions={expansions};progress={progressReason};experience-routes={experienceRoutesUsed};candidates={candidates};reachable-candidates={reachableCandidates};rejected-candidates={rejectedCandidates};choice={choiceReason};intervention-ticks={interventionTicks};protection={protectionUrgency:0.000};prediction-confidence={predictionConfidence:0.000};prediction-samples={predictionSamples};{localMovement}");
    }

    /// <summary>
    /// One finished journey: one continuous stretch of wanting one kind of place, from the tick it began to the tick it
    /// ended. The three times are deliberately separate and not one ratio, because they answer different questions — the
    /// planned total is the first route's length at the orb's top pace, the actual is what the body took, and the player's
    /// is what a body that definitely can do it took over the same ground. A player comparison of "-" means the player's
    /// recorded trail never covered both ends, which is missing coverage rather than a player who was slower.
    ///
    /// The actual is the span between the two tick stamps less the ticks the body spent downed, which travel beside it so
    /// the wall clock is still recoverable. A death inside a journey is not the journey being slow, and a downing lasts
    /// longer than most journeys, so charging it would make every death the loudest slow journey in the report.
    /// </summary>
    public static void RecordRouteEpisode(NPC companion, string request, string outcome, ulong startTick, ulong endTick,
        int plannedTicks, int actualTicks, int downedTicks, float straightTiles, float pathTiles, float meanSpeed, string playerTicks)
    {
        if (!Accepting()) return;
        Write("route-episode", Stable(npcGenerations, companion.whoAmI), "", request, outcome, companion.Bottom, Vector2.Zero, Vector2.Zero, actualTicks,
            FormattableString.Invariant($"start-tick={startTick};end-tick={endTick};outcome={outcome};planned-ticks={plannedTicks};actual-ticks={actualTicks};downed-ticks={downedTicks};player-ticks={playerTicks};straight-tiles={straightTiles:0.00};path-tiles={pathTiles:0.00};mean-speed-px-per-tick={meanSpeed:0.00};planned-scope=first-route-length-at-top-pace;actual-scope=span-less-downed-ticks;player-scope=tightest-recorded-trail-crossing-within-2-tiles-of-both-ends"));
    }

    /// <summary>
    /// A body on its own route that stopped moving, with the reason attributed from retained state at the moment it
    /// happened rather than inferred from rows afterwards. The evidence that produced the reason travels beside it, so a
    /// reader can disagree with the attribution without re-deriving the state it was made from, and the thresholds travel
    /// with it, so a finding built on this can be argued with.
    /// </summary>
    public static void RecordStop(NPC companion, ulong startTick, ulong endTick, int ticks, string reason,
        bool againstWall, bool sameSegment, bool replanned, float fastestSpeed,
        float stoppedPixelsPerTick, int stoppedTicks)
    {
        if (!Accepting()) return;
        Write("stop", Stable(npcGenerations, companion.whoAmI), "", reason, reason, companion.Center, companion.velocity, Vector2.Zero, ticks,
            FormattableString.Invariant($"start-tick={startTick};end-tick={endTick};ticks={ticks};reason={reason};against-wall-throughout={againstWall};same-segment-throughout={sameSegment};replanned-during={replanned};fastest-px-per-tick={fastestSpeed:0.00};threshold-px-per-tick={stoppedPixelsPerTick:0.00};threshold-ticks={stoppedTicks};scope=ordinary-travel-owner-with-an-executable-or-direct-route"));
    }

    public static void RecordTerrainSnapshot(int x, int y, string data)
        => Write("terrain-snapshot", 0, "", "local-world", "post-update", new Vector2(x * 16, y * 16), Vector2.Zero, Vector2.Zero, 0, data);

    private static int Next(Dictionary<int, int> map, int slot) { int generation = map.TryGetValue(slot, out int prior) ? prior + 1 : 1; map[slot] = generation; return slot * 1_000_000 + generation; }
    private static int Stable(Dictionary<int, int> map, int slot) => map.TryGetValue(slot, out int generation) ? slot * 1_000_000 + generation : Next(map, slot);

    /// <summary>Hand one occurrence to the writer's queue; false when the stream was not accepting or the queue refused it.</summary>
    private static bool Write(string kind, int subject, string related, string label, string channel, Vector2 position, Vector2 velocity, Vector2 expected, int amount, string detail)
    {
        if (!Accepting()) return false;
        var record = new EventRecord(1, sequence++, Main.GameUpdateCount, BrainTelemetry.ElapsedMilliseconds, kind, subject, related, label, channel,
            position.X, position.Y, velocity.X, velocity.Y, expected.X, expected.Y, amount, detail);
        if (QueueDiagnosticRecords.TryEnqueueLegacy(record, QueueDiagnosticRecords.EstimateLegacy(kind, related, label, channel, detail))) return true;
        Dropped++;
        disabled = true;
        return false;
    }

    private static void Disable(Exception error)
    {
        active = false;
        disabled = true;
        Terraria.ModLoader.ModContent.GetInstance<AICompanion>().Logger.Error($"GodsEyeEvents: recording stopped: {error.GetType().Name}: {error.Message}");
    }

    private readonly record struct EventRecord(int v, int seq, long tick, double wall_elapsed_ms, string kind, int subject, string related, string label, string channel,
        float pos_x, float pos_y, float vel_x, float vel_y, float expected_x, float expected_y, int amount, string detail,
        string? payload_kind = null, int? payload_version = null, string? phase = null, long? observation_ordinal = null,
        long? receipt_watermark = null, CourseTraceContext? context = null, CourseTracePayload? payload = null,
        CourseDecisionSnapshot? snapshot = null);

}
