extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;

using PositionRequest = live::AICompanion.Companion.Brain.PositionSelection.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.PositionSelection.RequestKind;
using T = live::AICompanion.Companion.Brain.WorldObservation.ThreatRecord;
using C = live::AICompanion.Companion.Brain.Behaviours.ActionContext;

/// <summary>
/// The firing-spot shortlist has to carry line-of-fire information before it is cut to the few
/// candidates that can afford a real trajectory solve. The cheap ranking pass used to pass a
/// literal 1 for every candidate's shot factor, so the eight that paid for a solve were chosen by
/// band, danger and openness alone — none of which knows whether a shot exists. The 2026-09-11
/// session recorded 810 hunt ticks standing on a spot the positioner had itself scored as having
/// no arc.
///
/// This builds the smallest geometry that separates the two rankings: an enemy in a pit, whose
/// rim is the only reachable place with a line to it, surrounded by open floor whose distant
/// spots win every cheap factor and cannot see into the pit at all.
/// </summary>
internal static class VerifyFiringPosition
{
    public static int Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "aic-method-assessment-" + Guid.NewGuid().ToString("N") + ".jsonl");
        Type events = typeof(live::AICompanion.Companion.Brain.BehaviourDiagnostics.GodsEyeEvents);
        var close = events.GetMethod("Close", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            events.GetMethod("Open", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { path });
            int comparisons = VerifyShortlistPrefersSpotsThatCanActuallyShoot();
            close.Invoke(null, null);
            var records = File.ReadLines(path).Select(line =>
                {
                    using var document = System.Text.Json.JsonDocument.Parse(line);
                    return document.RootElement.Clone();
                })
                .Where(row => row.GetProperty("kind").GetString() == "method-assessment"
                    && row.GetProperty("label").GetString() == "guard").ToArray();
            Require(records.Length == comparisons, "every queried guard method must produce exactly one occurrence record");
            Require(records.Count(row => row.GetProperty("channel").GetString() == "not-established") >= 3,
                "unchanged unavailable guarding must be recorded across comparisons, not only on an activity-label transition");
            Require(records.Last().GetProperty("channel").GetString() == "admitted", "opening must record admission separately from rejection");
            for (int i = 0; i < records.Length; i++)
            {
                string detail = records[i].GetProperty("detail").GetString()!;
                // The fourth comparison is the damageable sealed threat, whose guard offer is worth nothing and is never
                // queried, so it takes a comparison identity and writes no record.
                int choice = i < 3 ? i + 1 : i + 2;
                Require(detail.Contains($"choice-id={choice};") && detail.Contains("choice-phase=pre-activation;")
                    && detail.Contains("target-slot=30;") && detail.Contains("native-effect=unobserved"),
                    $"method occurrences need comparison/target identity and explicit execution limits; expected choice-id={choice}, detail={detail}");
                bool rejected = records[i].GetProperty("channel").GetString() == "not-established";
                // The first three are the undamageable sealed threat, which admission refuses before solving; any later
                // rejection is the reopened shaft's tested arc.
                string failure = i < 3 ? "attack-target-not-attackable" : "no-arc";
                Require(!rejected || detail.Contains("selectable=0;") && detail.Contains(failure),
                    $"a rejection must preserve the method's own failure ({failure}) rather than claim low desirability or native success; detail={detail}");
            }
            Console.WriteLine("firing position: usable method admission, terrain invalidation and every-query occurrence evidence pass");
            return 0;
        }
        finally
        {
            close.Invoke(null, null);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private const int FloorY = 80;
    // A narrow, deep shaft rather than a shallow bowl. A shallow one puts the clear/blocked
    // boundary in the middle of the distance ranking, so a clear spot reaches the shortlist by
    // accident and the check passes whatever the ranking knows. The shaft makes the separation
    // sharp: only the two lip tiles have a line down it, and they are the closest candidates of
    // all, which is exactly where a ranking that prefers standoff distance will never look.
    // The mouth is wide enough that a tile with solid floor under it can see down the shaft.
    // Terraria's CanHitLine tests three tile rows at each step, not one, so a mouth only as wide
    // as the shaft leaves every solid-floored lip blocked by the rock beside it and the fixture
    // would be asserting that an impossible shot should have been found.
    private const int ShaftLeft = 49, ShaftRight = 53, ShaftFloorY = 86;
    private const int EnemyTileX = 51;

    private static int VerifyShortlistPrefersSpotsThatCanActuallyShoot()
    {
        BuildPitWorld();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        // The player stands with the companion: this fixture is about where to shoot from, so the
        // follow band must not be the thing separating candidates.
        player.position = new Vector2(30 * 16f, FloorY * 16f - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.position = new Vector2(30 * 16f, FloorY * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;

        var enemy = new NPC();
        enemy.SetDefaults(Terraria.ID.NPCID.Zombie);
        enemy.whoAmI = 30;
        enemy.active = true;
        // Stationary, so its predicted path does not smear danger across the rim and decide the
        // comparison through a factor this check is not about.
        enemy.velocity = Vector2.Zero;
        enemy.Bottom = new Vector2(EnemyTileX * 16f + 8f, ShaftFloorY * 16f);
        Main.npc[30] = enemy;

        companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
        var threats = companion.Brain.Senses.Threats.Threats;
        threats.Clear();
        threats.Add(new T
        {
            Npc = enemy,
            DistanceToCompanion = Vector2.Distance(companion.NPC.Bottom, enemy.Bottom),
            DistanceToPlayer = Vector2.Distance(player.Bottom, enemy.Bottom),
        });

        var ctx = new C(companion, companion.Brain.Senses);
        var profile = companion.Arsenal.ProfileFor(ctx, enemy);
        Require(profile != null, "the firing-position fixture needs an equipped weapon profile to solve with");

        var request = new PositionRequest(RequestKind.LineOfFire, enemy.Center, enemy);
        // The reachable region is flooded incrementally across rescores rather than in one call, so
        // a single Resolve sees a region a few tiles wide and would exclude the lip on reachability
        // alone. Resolving repeatedly is what the brain does — the positioner rescores every twelfth
        // tick and advances the flood on its own cadence — and it is the only way this fixture asks
        // the ranking question rather than a question about flood budget.
        Vector2? chosen = null;
        for (int tick = 0; tick < 400; tick++)
            chosen = companion.Brain.Positioner.Resolve(request, companion.Brain.Senses, profile);

        string evidence = companion.Brain.Positioner.CandidateEvidence;
        // The geometry has to stay discriminating, or a green result would mean nothing: there must
        // be blind reachable spots for a sight-free ranking to prefer, and reachable spots with a
        // line for a sight-aware one to find. Both are asserted rather than assumed, because a
        // terrain edit that quietly removed either would leave this check passing for no reason.
        Require(companion.Brain.Positioner.ReachableCandidateCount > 8,
            $"the fixture needs more reachable candidates than the solve budget, or nothing is being shortlisted: reachable={companion.Brain.Positioner.ReachableCandidateCount}");
        Require(!Clear(46, enemy) && Clear(48, enemy),
            "the shaft must leave the near floor blind and its lip sighted, or the two rankings cannot disagree");
        Require(chosen != null,
            $"the pit fixture must produce a standing spot; evidence={evidence}");
        Require(evidence.Length > 0,
            "the positioner recorded no evaluated candidates, so the fixture proves nothing about the shortlist");

        // CandidateEvidence is "tileX,tileY:score:shot", strongest first, so the first entry is the
        // spot that won. A shortlist chosen without line of sight fills every slot with distant
        // floor spots that outscore the rim on standoff and cannot see the pit, and every one of
        // them reads no-arc.
        string best = evidence.Split('|')[0];
        Require(best.EndsWith(":clear-arc"),
            $"the chosen firing spot has no solved arc, so the shortlist was ranked without line of sight: best={best}; all={evidence}");

        bool anyClear = evidence.Contains(":clear-arc");
        Require(anyClear,
            $"no evaluated candidate could shoot the target, so the solve budget was spent entirely on blind spots: {evidence}");

        // Seal the same target beneath continuous rock. Reachable floor remains available,
        // but arriving anywhere on it cannot fulfil an attack request.
        for (int x = ShaftLeft; x <= ShaftRight; x++) Solid(x, FloorY);
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        Require(companion.Brain.Positioner.PrepareOffer(request, companion.Brain.Senses, profile).Destination == null,
            "changed terrain must invalidate a cached attack method before its rescore interval expires");
        foreach (RequestKind kind in new[] { RequestKind.LineOfFire, RequestKind.Guard })
        {
            var blocked = new PositionRequest(kind, enemy.Center, enemy);
            for (int tick = 0; tick < 400; tick++)
                chosen = companion.Brain.Positioner.Resolve(blocked, companion.Brain.Senses, profile);
            Require(companion.Brain.Positioner.EvaluatedCandidates > 0
                && companion.Brain.Positioner.CandidateEvidence.Contains(":no-arc"),
                "the sealed fixture must actually evaluate blocked attack candidates");
            Require(chosen == null,
                $"{kind} must remain unresolved when every tested destination lacks a shot; got {chosen}, evidence={companion.Brain.Positioner.CandidateEvidence}");
            Require(companion.Brain.Positioner.ChoiceReason == "no-usable-destination-established",
                "bounded failure to establish a shot must not report a retained destination or proven impossibility");
        }

        // A wall-crossing threat may endanger the player while the companion's weapons
        // cannot shoot through the covering rock. Urgency and an available intervention
        // are separate facts; no enemy AI is simulated by this selection fixture.
        enemy.noTileCollide = true;
        var urgent = threats[0];
        urgent.CanReachPlayer = true;
        urgent.Urgency = 1f;
        urgent.EffectiveTicksToPlayer = 0f;
        typeof(live::AICompanion.Companion.Brain.WorldObservation.ThreatSense)
            .GetProperty("MostUrgent")!.SetValue(companion.Brain.Senses.Threats, urgent);
        companion.Brain.Senses.SetInterventionEstimate(float.PositiveInfinity);
        var guard = companion.Brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.PurposeFamilies.Combat.ProtectPlayer>().Single();
        Vector2? heldDestination = companion.Brain.Positioner.Resolve(PositionRequest.ExactAt(companion.NPC.Bottom),
            companion.Brain.Senses, profile);
        Require(heldDestination != null, "the admission fixture needs an existing ordinary destination to preserve");
        string heldExplanation = companion.Brain.Positioner.CandidateEvidence;

        // Positive desirability beside a rejected method needs a threat guarding still values. Under the sealed rock
        // a damageable zombie is a proven absence of any firing position once the flood is settled — and it is settled
        // here from the first comparison — so guarding is worth nothing against it and is never nominated or queried.
        // An undamageable one has no removal to estimate, keeps its full share without asking whether it can be shot,
        // and still has to establish a destination, so it carries the rejection and occurrence contract.
        enemy.dontTakeDamage = true;
        for (int tick = 0; tick < 3; tick++)
        {
            var selected = companion.Brain.Chooser.Choose(ctx);
            var guardScore = companion.Brain.Chooser.LastScores.Single(s => ReferenceEquals(s.Action, guard));
            Require(guard.Access == null && float.IsPositiveInfinity(guard.RemovalTicks),
                $"the rejection phase needs a threat no weapon can damage, which guarding never asks the firing query about; access={guard.Access}, removal={guard.RemovalTicks}");
            Require(guardScore.Raw > 0f, "the sealed undamageable threat must remain worth protecting against");
            Require(!ReferenceEquals(selected, guard) && guardScore.Final == 0f && guardScore.Error.Length > 0,
                $"unestablished protective access must reject the method without erasing urgency: selected={selected?.Name}, raw={guardScore.Raw}, final={guardScore.Final}, reason={guardScore.Error}");
            // Admission refuses a target no weapon can damage before solving any candidate, so the retained evidence
            // names that refusal rather than a tested arc; the sealed shaft's no-arc evidence is required above, on the
            // positioner's own guard resolution, and again after reopening.
            Require(guardScore.MethodEvidence.Contains("attack-target-not-attackable") && guardScore.Error == "method-attack-target-not-attackable",
                $"the rejected offer must retain the method's own refusal separately from its raw value; evidence={guardScore.MethodEvidence}, error={guardScore.Error}");
            Require(companion.Brain.Positioner.Chosen == heldDestination
                && companion.Brain.Positioner.CandidateEvidence == heldExplanation,
                "a rejected nomination must preserve the previous ordinary destination and explanation");
        }

        // The same threat damageable again: nothing the companion can reach has a line to it, so protection is access
        // that never arrives. Guarding yields — getting away from such a threat is shared safety's job — without a
        // method query, so this comparison writes no method occurrence and is not counted in the returned total.
        enemy.dontTakeDamage = false;
        {
            var selected = companion.Brain.Chooser.Choose(ctx);
            var guardScore = companion.Brain.Chooser.LastScores.Single(s => ReferenceEquals(s.Action, guard));
            Require(guard.Access == live::AICompanion.Companion.Brain.PurposeFamilies.Combat.FiringAccess.None && float.IsFinite(guard.RemovalTicks),
                $"the damageable sealed threat must be a proven absence of firing positions from a settled flood; access={guard.Access}, removal={guard.RemovalTicks}, reach-complete={companion.Brain.Positioner.ReachComplete}");
            Require(guardScore.Raw == 0f && !ReferenceEquals(selected, guard)
                && guardScore.Eligibility == live::AICompanion.Companion.Brain.Behaviours.OfferEligibility.KnownUnusable
                && guardScore.EligibilityReason == "no-reachable-firing-position",
                $"a threat no reachable position can shoot must be worth no protection and named unusable; selected={selected?.Name}, raw={guardScore.Raw}, eligibility={guardScore.Eligibility}/{guardScore.EligibilityReason}");
            Require(guardScore.MethodEvidence.Length == 0,
                $"a guard offer worth nothing must not be queried for a destination; evidence={guardScore.MethodEvidence}");
            Require(companion.Brain.Positioner.Chosen == heldDestination
                && companion.Brain.Positioner.CandidateEvidence == heldExplanation,
                "an unqueried guard offer must preserve the previous ordinary destination and explanation");
        }

        // Reopening is fresh evidence, not a permanent unreachable verdict on the enemy.
        for (int x = ShaftLeft; x <= ShaftRight; x++) Open(x, FloorY);
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        for (int tick = 0; tick < 400; tick++)
            chosen = companion.Brain.Positioner.Resolve(request, companion.Brain.Senses, profile);
        Require(chosen != null && companion.Brain.Positioner.CandidateEvidence.Contains(":clear-arc"),
            "opening the shot must restore a useful firing destination");
        bool protectionRestored = false;
        int comparisons = 3;
        for (int tick = 0; tick < 80 && !protectionRestored; tick++)
        {
            protectionRestored = ReferenceEquals(companion.Brain.Chooser.Choose(ctx), guard);
            comparisons++;
        }
        Require(protectionRestored, "opening the shot must allow the still-useful guard offer to win again");
        Require(companion.Brain.Chooser.LastScores.Single(s => ReferenceEquals(s.Action, guard))
            .MethodEvidence.Contains("clear-arc"), "accepted protection must retain the method that admitted it");
        Require(companion.Brain.Positioner.Resolve(request, companion.Brain.Senses, null) == null,
            "removing the weapon profile must invalidate a retained firing position immediately");
        return comparisons;
    }

    /// <summary>
    /// A long open floor with a narrow shaft cut down into it. The enemy sits on the shaft floor,
    /// so a straight line from anywhere out on the main floor is stopped by the solid rock beside
    /// the shaft mouth, while the two tiles at the lip look straight down it. Both lips are on the
    /// main floor, so they are reachable by the same walk as every blind spot and cannot be removed
    /// by the reachability tier; the shaft's own interior is a one-way drop and is excluded by it.
    /// </summary>
    private static void BuildPitWorld()
    {
        Main.maxTilesX = Main.maxTilesY = 120;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)120, (ushort)120 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= ShaftFloorY + 2; y++)
                Solid(x, y);
        // Hollow the shaft, leaving its floor intact.
        for (int x = ShaftLeft; x <= ShaftRight; x++)
            for (int y = FloorY; y < ShaftFloorY; y++)
                Open(x, y);
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
    }

    /// <summary>Whether a body standing on the main floor at this tile column has a straight line to the target.</summary>
    private static bool Clear(int tileX, NPC target)
        => Collision.CanHitLine(new Vector2(tileX * 16f + 8f, FloorY * 16f - 30f), 1, 1,
            target.position, target.width, target.height);

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = true;
        tile.TileType = 1;
    }

    private static void Open(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = false;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
