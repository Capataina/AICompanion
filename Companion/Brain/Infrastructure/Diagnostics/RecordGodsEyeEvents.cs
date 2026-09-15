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
    private static StreamWriter? writer;
    private static readonly Dictionary<int, int> npcGenerations = new();
    private static readonly Dictionary<int, int> projectileGenerations = new();
    private static readonly Dictionary<int, int> itemGenerations = new();
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
    internal static bool Active => writer != null;
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
        if (writer != null) return true;
        if (disabled) Dropped++;
        return false;
    }
    public static void RecordWorldInteraction(NPC companion, Point tile, string operation, string detail)
        => Write("world-interaction", companion.whoAmI, "", operation, "", companion.Bottom, Vector2.Zero,
            tile.ToWorldCoordinates(), 0, detail);

    /// <summary>One strike. <c>attempt=</c> is the tool instance's own counter and names nothing the activity owner knows;
    /// <paramref name="activityAttemptId"/> is the owner's attempt open when Execute struck, zero when none was, and is what a reader joins on.</summary>
    public static void RecordToolEffect(NPC companion, string tool, in Infrastructure.Interactions.TileToolObservation outcome, long choiceId, long activityId, long activityAttemptId)
    {
        if (!Accepting()) return;
        Write("tool-effect", Stable(npcGenerations, companion.whoAmI), "", tool,
            $"attempt={outcome.Attempt};choice-id={choiceId};activity-id={activityId};activity-attempt-id={activityAttemptId}", companion.Bottom, Vector2.Zero, outcome.Target.ToWorldCoordinates(),
            outcome.Effect == Infrastructure.Interactions.TileToolEffect.Damaged ? outcome.After.Damage - outcome.Before.Damage : 0,
            FormattableString.Invariant($"observation-tick={outcome.Tick};tool-item={outcome.ToolItem};effect={outcome.Effect};before-present={outcome.Before.Present};before-type={outcome.Before.Type};before-frame={outcome.Before.FrameX},{outcome.Before.FrameY};before-damage={outcome.Before.Damage};after-present={outcome.After.Present};after-type={outcome.After.Type};after-frame={outcome.After.FrameX},{outcome.After.FrameY};after-damage={outcome.After.Damage};damage-scope=tool-owned-hit-table;yield=unobserved"));
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

    internal static void Open(string path)
    {
        Close();
        // A telemetry stem is reserved by the TSV writer before its sidecars open. CreateNew is
        // still intentional here: a sidecar collision must fail loudly rather than turn a later
        // load retry into an apparently complete earlier session.
        writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        npcGenerations.Clear(); projectileGenerations.Clear(); itemGenerations.Clear(); sequence = 0;
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
        try { writer?.Flush(); writer?.Dispose(); }
        catch (Exception error) { Disable(error); }
        finally { writer = null; }
    }

    internal static void Flush()
    {
        try { writer?.Flush(); }
        catch (Exception error) { Disable(error); }
    }

    public static void RecordNpcSpawn(NPC npc) => Write("npc-spawn", Next(npcGenerations, npc.whoAmI), npc.type.ToString(CultureInfo.InvariantCulture), npc.TypeName, "", npc.Center, npc.velocity, Vector2.Zero, npc.life, "");
    public static void RecordNpcDeath(NPC npc) => Write("npc-death", Stable(npcGenerations, npc.whoAmI), npc.type.ToString(CultureInfo.InvariantCulture), npc.TypeName, "", npc.Center, npc.velocity, Vector2.Zero, npc.life, "");

    public static void RecordShot(NPC shooter, NPC? target, int projectileIndex, Vector2 muzzle, Vector2 launchVelocity, Vector2 expectedImpact, string weapon, int impactTicks = -1, float attackValue = 0f, int expectedKills = 0, float preventedHarm = 0f)
    {
        int projectile = Stable(projectileGenerations, projectileIndex);
        Write("shot", Stable(npcGenerations, shooter.whoAmI), target == null ? "" : Stable(npcGenerations, target.whoAmI).ToString(CultureInfo.InvariantCulture), weapon, $"projectile={projectile}", muzzle, launchVelocity, expectedImpact, 0, FormattableString.Invariant($"expected-flight-ticks={impactTicks};sequence-value={attackValue:0.000};sequence-kills={expectedKills};sequence-prevented-harm={preventedHarm:0.000}"));
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
    /// the drop that attempt walked toward, zero for every other pickup, so a reader sums an attempt's received quantity by identity.</summary>
    public static void RecordPickup(NPC companion, Item item, int amount, string destination, long collectionAttemptId)
        => Write("pickup", Stable(npcGenerations, companion.whoAmI), Stable(itemGenerations, item.whoAmI).ToString(CultureInfo.InvariantCulture), item.type.ToString(CultureInfo.InvariantCulture), destination, item.Center, Vector2.Zero, Vector2.Zero, amount, $"stack={item.stack};collection-attempt-id={collectionAttemptId}");

    public static void RecordItemSpawn(Item item) => Next(itemGenerations, item.whoAmI);

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

    private static void Write(string kind, int subject, string related, string label, string channel, Vector2 position, Vector2 velocity, Vector2 expected, int amount, string detail)
    {
        if (!Accepting()) return;
        try
        {
            long tick = Main.GameUpdateCount; double elapsed = BrainTelemetry.ElapsedMilliseconds;
            writer.WriteLine(JsonSerializer.Serialize(new EventRecord(1, sequence++, tick, elapsed, kind, subject, related, label, channel,
                position.X, position.Y, velocity.X, velocity.Y, expected.X, expected.Y, amount, detail)));
        }
        catch (Exception error) { Disable(error); }
    }

    private static void Disable(Exception error)
    {
        var failed = writer;
        writer = null;
        disabled = true;
        try { failed?.Dispose(); } catch (IOException) { }
        Terraria.ModLoader.ModContent.GetInstance<AICompanion>().Logger.Error($"GodsEyeEvents: recording stopped: {error.GetType().Name}: {error.Message}");
    }

    private readonly record struct EventRecord(int v, int seq, long tick, double wall_elapsed_ms, string kind, int subject, string related, string label, string channel,
        float pos_x, float pos_y, float vel_x, float vel_y, float expected_x, float expected_y, int amount, string detail);
}
