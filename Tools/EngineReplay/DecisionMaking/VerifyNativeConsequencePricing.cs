extern alias live;

using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Tools.Ledger;
using live::AICompanion.Companion.Brain.Infrastructure.Movement;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

/// <summary>
/// The native consequence provider's honesty, which is the only thing between a partially built cost
/// model and a companion that prefers danger.
///
/// `ForecastCourseConsequences` prices companionship, the return leg, and predicted contact harm to the
/// companion over the trajectory that companionship produces. The dangerous way to ship a partly-built
/// cost model is to return Complete with an empty harm list: every comparison would read "no predicted
/// harm" and actively prefer the course that flies through the most hostiles. So two properties are held
/// here at once, and they pull in opposite directions, which is exactly why neither is safe to assume.
///
/// Harm must actually be produced — a hostile sitting on the flight path has to become a `PredictedHarm`
/// event, or the safety term is decoration and a route through a pack prices the same as a route around
/// it. And the tail must stay unresolved anyway, because only the companion's body is modelled and the
/// player's is not, so no course here can ever be certified harm-free; by the Courses contract an
/// unresolved tail cannot certify strict superiority, so a course may be chosen as a legal first action
/// and can never be proven better than a safer rival.
///
/// Nothing about the code's shape says which of those it does, which is why these rows exist.
/// </summary>
internal static class VerifyNativeConsequencePricing
{
    public static int Run()
    {
        int red = 0;
        void Row(string name, Action test)
        {
            try { test(); Console.WriteLine("GREEN " + name); }
            catch (Exception error) { red++; Console.WriteLine("RED " + name + ": " + error.Message); }
        }
        Row("G12 unanswered travel suspends the pricing and forwards its request", PendingForwardsItsTravelRequest);
        Row("G12 an unpriced harm is unresolved, never zero", HostilesLeaveTheTailUnresolved);
        Row("G12 harm stays unresolved even with an empty census", HarmIsNeverProvenAbsent);
        Row("G12 a second candidate order is priced from its own pose, not the first's", EachOrderIsPricedFromItsOwnStart);
        Row("G12 an unanswered enemy motion suspends the pricing and forwards its request", PendingForwardsItsMotionRequest);
        Row("G12 a hostile on the flight path becomes a predicted harm", AHostileOnThePathIsPriced);
        Row("G12 the same hostile off the path costs nothing", AHostileOffThePathIsNotPriced);
        Row("G12 a fully modelled census still leaves the tail unresolved", ModelledHarmStillCannotCertifySafety);
        return red;
    }

    private static TextTileWorld World() => new(0, 0, new[]
    {
        "####################", "#..................#", "#..................#", "#..................#",
        "#..................#", "#..................#", "#..................#", "#..................#",
        "#..................#", "####################"
    });

    /// <summary>The frozen observation these rows price against. It is hand-built rather than taken
    /// from `AssembleCourseSnapshot` because the travel below is solved in the text world above, and a
    /// snapshot whose region came from a native floor would be priced against geometry it never saw.
    /// The assembler has its own rows in `Observation/VerifyCourseSnapshotAssembly.cs`.</summary>
    private static DecisionFactSnapshot Snapshot() => new(1, 1, 100, 1, 0, new[]
    {
        new DecisionFact(CapturedCompanionshipRegion.Key, 1, new(Text: JsonSerializer.Serialize(
            new CapturedCompanionshipRegion(new(48, 80), new(20, 20), default, 100, 100, true))), FactEvidence.Observed)
    });

    private static ForecastCourseConsequences Forecast() => new(new CoursePoint(48, 80));

    private static CourseComparisonEpisode Episode()
        => new(1, 1, 10, Array.Empty<UsefulNeed>(), true, 0, "consequence-fixture");

    private static ProjectedCourseState Successor(DecisionFactSnapshot facts)
        => new(new CoursePoint(240, 80), facts);

    /// <summary>
    /// Prices an empty order, driving the observation owner's model queue exactly the way the live
    /// coordinator will have to: the forecast suspends on a travel it cannot compute inside hypothetical
    /// evaluation, the owner answers it against the frozen world, and the same forecast resumes on the
    /// model-extended snapshot rather than restarting.
    /// </summary>
    private static CourseProjectionResult Price()
    {
        var owner = new RetainCourseModelQueries(Snapshot(), World(), 2, 4);
        return Settle(Forecast(), owner, new CoursePoint(240, 80));
    }

    /// <summary>A suspended pricing must name what it waits on. The domain cursor stays on the candidate
    /// needing the answer, so a request that is never forwarded parks that candidate for ever.</summary>
    private static void PendingForwardsItsTravelRequest()
    {
        DecisionFactSnapshot facts = Snapshot();
        CourseProjectionResult first = Forecast().Continue(Array.Empty<StepBinding>(), Successor(facts),
            facts, Episode(), new DecisionWorkCursor(), new(double.PositiveInfinity));
        Require(first.Status == ProjectionStatus.Pending,
            $"a pricing with no captured travel should suspend; status={first.Status}");
        Require(first.RequiredTravel is { Count: > 0 },
            "the suspended pricing forwarded no travel request, so the observation owner has nothing to answer");
        Require(first.Projection == null, "a suspended pricing published a projection");
    }

    /// <summary>A snapshot carrying no census at all. Harm cannot be priced from a fact that does not
    /// exist, and the one thing that must not happen is that absence reading as safety.</summary>
    private static void HostilesLeaveTheTailUnresolved()
    {
        CourseProjectionResult result = Price();
        Require(result.Status == ProjectionStatus.Complete,
            $"answered travel should complete the pricing; status={result.Status} reason={result.Reason}");
        Require(result.Projection is { } priced && priced.TailUnresolved,
            "a pricing with no census read resolved its tail, so an unmodelled harm reads as no harm");
        Require(result.Reason.Contains("contact-census-unread", StringComparison.Ordinal),
            $"an absent census was not named as the reason harm went unpriced, so a silently empty harm list is indistinguishable from a modelled one; reason={result.Reason}");
        Require(result.Projection!.Harm.Count == 0, "a harm was reported that no model produced");
        Require(result.Projection!.Companionship.Count > 0,
            "an empty order produced no companionship interval, so idling would be free");
        Require(result.Projection!.ConsequenceDependencies.Reads.Count > 0,
            "the pricing published an empty consequence manifest, which asserts a calculation with no captured inputs and can never be dirtied when they change");
    }

    /// <summary>
    /// Harm is never proven absent, and a census with nothing in it is not the exception it looks like.
    ///
    /// An earlier version of this provider resolved the tail when a finished census held no hostile.
    /// That read proof about one instant of `Main.npc[]` as proof about a whole projected journey
    /// including the return leg, and it certified zero harm to the *player* too, about which a
    /// hostile-slot census says nothing. The second half of that is still true now that the companion's
    /// own harm is modelled, which is what `ModelledHarmStillCannotCertifySafety` holds from the
    /// other side: a complete census and every enemy modelled still cannot resolve this tail.
    /// </summary>
    private static void HarmIsNeverProvenAbsent()
    {
        CourseProjectionResult empty = Price();
        Require(empty.Projection is { } priced && priced.TailUnresolved,
            "an empty census resolved the tail, which reads a moment's observation as proof about a horizon and says nothing about player harm at all");
        Require(empty.Projection!.Harm.Count == 0, "a harm was reported that no model produced");
    }

    /// <summary>
    /// The row the `BeginOrder` leak lived under. One forecast object prices two different candidate
    /// orders in a search, and each must be priced from its own starting state.
    ///
    /// It exists because the two fixtures either side of this seam each used a stand-in for the other:
    /// this file drove the real forecast against a hand-built snapshot and never through `BindCourseOrder`,
    /// while `VerifyCourseOrderProjection` drove the real binder against a fixture forecast. The leak sat
    /// exactly in the gap — a retained companionship leg handed to every later order verbatim, priced from
    /// a pose it had never seen and asking for no travel to reach it. With harm unresolved, companionship
    /// is the only cost term, so every candidate order cost the same.
    /// </summary>
    private static void EachOrderIsPricedFromItsOwnStart()
    {
        DecisionFactSnapshot facts = Snapshot();
        var owner = new RetainCourseModelQueries(facts, World(), 2, 4);
        var forecast = new ForecastCourseConsequences(new CoursePoint(48, 80));

        CourseProjectionResult first = Settle(forecast, owner, new CoursePoint(240, 80));
        CourseProjectionResult second = Settle(forecast, owner, new CoursePoint(64, 88));

        Require(first.Projection != null && second.Projection != null,
            "one of the two candidate orders never priced at all");
        Require(first.Projection!.Companionship.Count > 0 && second.Projection!.Companionship.Count > 0,
            "a candidate order priced with no companionship interval");
        // Two starting poses at genuinely different distances from the same reunion point cannot honestly
        // produce one identical cost. Equality here is the retained leg being reused, not a coincidence.
        Require(first.Projection!.ReunionTick != second.Projection!.ReunionTick
                || !first.Projection!.Companionship.SequenceEqual(second.Projection!.Companionship),
            $"two orders starting {240 - 64} px apart priced identically, so the second was handed the first's retained leg; end={first.Projection!.ReunionTick}");
    }

    // The flight priced by every row here runs from x=240 to the reunion point at x=48, along y=80.
    //
    // The two y values are chosen to straddle the boundary by less than half a body, which is a
    // deliberate tightening rather than a detail. A hostile 32 px tall at y=64 covers 64..96, and the
    // 20 px orb centred on y=80 covers 70..90, so it overlaps. At y=91 the hostile covers 91..123 and
    // clears the body by a single pixel. An earlier pair used 64 against 120, separated by 56 px of
    // centre — far wider than the 10 px that anchoring the body's box at its pose rather than centring
    // it would move — so both rows read the same either way and a mutation swapping centred anchoring
    // for top-left anchoring left the whole file green. A control is only a control when the thing it
    // varies is the smallest thing that could be wrong.
    private const double OnThePath = 64, OffThePath = 91;

    /// <summary>
    /// One hostile in a frozen census, built to sit still for the whole horizon.
    ///
    /// Both native motion flags are set deliberately: with gravity and tile collision live, the simulated
    /// enemy would fall to the floor and the row's geometry would depend on how far it fell rather than on
    /// where the census put it. A stationary hostile is the one scene in which "the body flew into it" and
    /// "the body did not" differ by nothing but the one coordinate the row varies.
    /// </summary>
    private static CapturedContactEnemy Hostile(double y)
    {
        var track = new PredictObservedMotion.ExportedTrack(Slot: 3, Type: 1, Tick: Main.GameUpdateCount,
            Position: new((float)140, (float)y), Velocity: Vector2.Zero, Acceleration: Vector2.Zero,
            Gravity: 0, MaxFallSpeed: 10, WaterSpeed: 1, LavaSpeed: 1, HoneySpeed: 1, ShimmerSpeed: 1,
            NoGravity: true, NoTileCollide: true, Wet: false, LavaWet: false, HoneyWet: false,
            ShimmerWet: false, MeanError: 0, ErrorSamples: 0);
        var shape = new CapturedMeleeEnemy(1, new(140, y), 32, 32, 1, 1, 0, 0, 0, 0);
        return new(3, 7, shape, Damage: 20, track);
    }

    /// <summary>The companion as a contact victim, taken through the real capture rather than hand-built,
    /// because its defence value is the game's own arithmetic frozen into a record and a fixture writing
    /// that by hand would be asserting against its own copy of the formula.</summary>
    private static CapturedContactVictim CompanionVictim()
    {
        var npc = new NPC();
        npc.SetDefaults(0);
        npc.active = true; npc.life = 200; npc.width = 20; npc.height = 20;
        npc.position = new Vector2(240 - 10, 80 - 10);
        npc.immune[255] = 0;
        return CaptureContactVictim.Capture(npc, new(double.PositiveInfinity))
            ?? throw new InvalidOperationException("the companion victim capture refused an unbounded allowance");
    }

    /// <summary>
    /// The player as a contact victim and his own motion track, for a snapshot that exercises the live
    /// pricing path rather than the degraded one.
    ///
    /// Every row in this file used to run without these, which meant every row ran the branch taken when
    /// the player's facts are missing — companion priced alone, his geometry an unsupported empty, no
    /// player actor. A review of `e63375d` measured that: the mechanism that commit added had no
    /// reachable coverage anywhere, and the row guarding "a course cannot certify it harms nobody"
    /// passed only because its fixture withheld the facts its own comment said did not exist yet.
    ///
    /// He stands still at the origin of the scene's own geometry, so a row that wants him hit places a
    /// hostile on him rather than relying on his motion, and a row that does not is unaffected by him.
    /// </summary>
    private static (CapturedContactVictim Victim, CapturedPlayerMotion Motion) PlayerFacts()
    {
        Player player = Main.player[0];
        player.active = true; player.dead = false;
        player.width = 20; player.height = 42;
        player.position = new Vector2(48 - 10, 80 - 21);
        player.velocity = Vector2.Zero;
        player.statLife = 100; player.immune = false; player.immuneTime = 0;
        var victim = CaptureContactVictim.Capture(player, new(double.PositiveInfinity))
            ?? throw new InvalidOperationException("the player victim capture refused an unbounded allowance");
        var motion = CapturedPlayerMotion.Capture(player, new(double.PositiveInfinity))
            ?? throw new InvalidOperationException("the player motion capture refused an unbounded allowance");
        return (victim, motion);
    }

    /// <summary>The region snapshot every row prices against, plus a census and both victims so the harm
    /// half has something to read. Ticks come from the live counter because the model scheduler refuses an
    /// enemy query whose capture tick is not its observation's.</summary>
    private static DecisionFactSnapshot HostileSnapshot(double y)
    {
        CapturedContactEnemy enemy = Hostile(y);
        var player = PlayerFacts();
        var census = new CapturedContactCensus(Main.GameUpdateCount, Main.maxNPCs, Main.maxNPCs, new[] { enemy }, true);
        // The snapshot's own tick, the census tick and every enemy track tick are one value by contract —
        // `RetainCourseModelQueries` refuses the three disagreeing, because a census from one frame indexed
        // under another frame's observation is exactly the mixed-observation defect the freeze exists to stop.
        return new(1, 1, (long)Main.GameUpdateCount, 1, 0, new[]
        {
            new DecisionFact(CapturedCompanionshipRegion.Key, 1, new(Text: JsonSerializer.Serialize(
                new CapturedCompanionshipRegion(new(48, 80), new(20, 20), default, 100, 100, true))), FactEvidence.Observed),
            census.ToFact(1),
            CompanionVictim().ToFact(1),
            player.Victim.ToFact(1),
            player.Motion.ToFact(1),
        });
    }

    private static CourseProjectionResult PriceAgainst(double y)
    {
        var owner = new RetainCourseModelQueries(HostileSnapshot(y), World(), 2, 8);
        return Settle(Forecast(), owner, new CoursePoint(240, 80));
    }

    /// <summary>The motion half of the two-kind suspension. Travel already had this row; without its twin
    /// an absent motion answer could be silently read as an enemy that cannot hurt anybody.</summary>
    private static void PendingForwardsItsMotionRequest()
    {
        var owner = new RetainCourseModelQueries(HostileSnapshot(OnThePath), World(), 2, 8);
        var forecast = Forecast();
        // Travel settles first, so the motion request is whatever the pricing still wants after it.
        for (int slice = 0; slice < 200; slice++)
        {
            CourseProjectionResult result = forecast.Continue(Array.Empty<StepBinding>(),
                new ProjectedCourseState(new CoursePoint(240, 80), owner.Snapshot), owner.Snapshot,
                Episode(), new DecisionWorkCursor(), new(double.PositiveInfinity));
            if (result.RequiredEnemyMotion is { Count: > 0 } wanted)
            {
                Require(result.Status == ProjectionStatus.Pending && result.Projection == null,
                    "a pricing waiting on enemy motion published a projection anyway");
                Require(wanted.Any(request => request.Slot == 3 && request.Generation == 7),
                    "the forwarded request does not name the hostile the census actually holds, so nothing could answer it");
                return;
            }
            Require(result.Status == ProjectionStatus.Pending,
                $"the pricing finished without ever asking for the census hostile's motion; reason={result.Reason}");
            foreach (CourseTravelRequest request in result.RequiredTravel ?? Array.Empty<CourseTravelRequest>())
                owner.RequestTravel(request);
            var budget = new DecisionWorkBudget(double.PositiveInfinity, 64);
            using (LimitPlanningWork.Own(budget)) owner.Continue(budget);
        }
        throw new InvalidOperationException("the pricing never asked for enemy motion within 200 slices");
    }

    /// <summary>The load-bearing row of the harm half. Everything else here can pass against a forecast
    /// that produces no harm at all; this one cannot.</summary>
    private static void AHostileOnThePathIsPriced()
    {
        CourseProjectionResult result = PriceAgainst(OnThePath);
        Require(result.Status == ProjectionStatus.Complete,
            $"the hostile scene never settled; status={result.Status} reason={result.Reason}");
        Require(result.Projection!.Harm.Count > 0,
            $"a hostile parked on the flight path produced no predicted harm, so the safety term cannot separate a course through a pack from one around it; reason={result.Reason}");
        PredictedHarm first = result.Projection!.Harm[0];
        Require(first.Actor == HarmActor.Companion && first.Damage > 0,
            $"the predicted harm names no companion damage; actor={first.Actor} damage={first.Damage}");
        Require(result.Reason.Contains("every-census-enemy-modelled", StringComparison.Ordinal),
            $"the pricing reported harm while some enemy motion was unresolved, so this row would pass on a scene it never modelled; reason={result.Reason}");
        // The manifest must carry every input the price consumed, and the motion fact is the one that
        // goes missing: the census is re-versioned every observation, so a manifest holding only the
        // census still looks dirty-able and a missing motion read hides behind that. A mutation that
        // recorded no motion read at all left every other row in this file green.
        var manifest = result.Projection!.ConsequenceDependencies.Reads;
        Require(manifest.Any(read => read.Key == CapturedContactCensus.Key),
            "the census the harm was priced from is absent from the manifest, so a changed census could never dirty this cost");
        Require(manifest.Any(read => read.Key == CapturedContactVictim.Key(HarmActor.Companion)),
            "the victim capture the harm was priced from is absent from the manifest");
        Require(manifest.Any(read => read.Key.Kind == "enemy-course-motion"),
            "no enemy-motion read reached the manifest, so the modelled motion this harm was computed from can never dirty it");
    }

    /// <summary>The control. Without it the row above passes against arithmetic that reports a hit for any
    /// hostile the census holds, wherever it is.</summary>
    private static void AHostileOffThePathIsNotPriced()
    {
        CourseProjectionResult result = PriceAgainst(OffThePath);
        Require(result.Status == ProjectionStatus.Complete,
            $"the clear scene never settled; status={result.Status} reason={result.Reason}");
        Require(result.Projection!.Harm.Count == 0,
            $"a hostile clearing the flight path by one pixel was priced as a hit, so the body's box is wrong by at least half its own size; harm={result.Projection!.Harm.Count}");
        Require(result.Reason.Contains("every-census-enemy-modelled", StringComparison.Ordinal),
            $"the clear scene reported no harm because nothing was modelled, which proves nothing; reason={result.Reason}");
    }

    /// <summary>
    /// The property that survives harm being priced, and the one most likely to be "tidied up" later.
    ///
    /// Every hostile in this census is modelled and the census itself is complete, so the obvious reading
    /// is that the harm list is now exhaustive and the tail may resolve. It may not: only the companion's
    /// body is modelled here, and nothing models the player's, so a resolved tail would certify that a
    /// course harms nobody on evidence that covers one of the two actors.
    /// </summary>
    /// <summary>
    /// A course must not be certified harm-free, and <b>the reason it is not has changed</b>, which is
    /// why this row is worth reading before it is trusted.
    ///
    /// It used to pass because nothing modelled the player, so `ForecastContactHarm` was built with an
    /// incomplete census on purpose. That stopped being true in `e63375d` — and this fixture kept
    /// passing anyway, because it withheld the player's victim and motion facts and so ran the degraded
    /// branch. A review caught exactly that: an instrument agreeing with its own stale premise.
    ///
    /// Both facts are in the snapshot now, so the tail is unresolved for the reason that actually holds:
    /// the harm window is the course's own duration and these orders are empty, so the window is far
    /// shorter than the forecast's reach and nothing beyond it was examined. A course genuinely covering
    /// the whole window may resolve, which is the behaviour unpinning the tail exists for; one that ends
    /// early says so rather than banking the silence as proof it is the safer course.
    /// </summary>
    private static void ModelledHarmStillCannotCertifySafety()
    {
        foreach (double y in new[] { OnThePath, OffThePath })
        {
            CourseProjectionResult result = PriceAgainst(y);
            Require(result.Projection is { } priced && priced.TailUnresolved,
                $"a course whose harm window is shorter than the forecast's reach resolved its tail at "
                + $"y={y}, so it was certified harm-free over ticks nobody examined; reason={result.Reason}");
            Require(result.Reason.Contains("both-bodies-priced", StringComparison.Ordinal),
                $"this row must exercise the live pricing path rather than the branch taken when the "
                + $"player's facts are absent, or it agrees with its own premise; reason={result.Reason}");
        }
    }

    /// <summary>Drives one order to a terminal answer through the model queue, from a given start.</summary>
    private static CourseProjectionResult Settle(ForecastCourseConsequences forecast,
        RetainCourseModelQueries owner, CoursePoint start)
    {
        for (int slice = 0; slice < 200; slice++)
        {
            var successor = new ProjectedCourseState(start, owner.Snapshot);
            CourseProjectionResult result = forecast.Continue(Array.Empty<StepBinding>(), successor,
                owner.Snapshot, Episode(), new DecisionWorkCursor(), new(double.PositiveInfinity));
            if (result.Status != ProjectionStatus.Pending) return result;
            foreach (CourseTravelRequest request in result.RequiredTravel ?? Array.Empty<CourseTravelRequest>())
                owner.RequestTravel(request);
            foreach (CourseEnemyMotionRequest request in result.RequiredEnemyMotion ?? Array.Empty<CourseEnemyMotionRequest>())
                owner.RequestEnemyMotion(request);
            var budget = new DecisionWorkBudget(double.PositiveInfinity, 64);
            using (LimitPlanningWork.Own(budget)) owner.Continue(budget);
        }
        throw new InvalidOperationException("an order never settled within 200 model slices");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
