extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;

using PositionRequest = live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;
using PositionReasons = live::AICompanion.Companion.Brain.Infrastructure.Position.PositionReasons;
using SuccessRegionKind = live::AICompanion.Companion.Brain.Infrastructure.Position.SuccessRegionKind;
using Navigator = live::AICompanion.Companion.Brain.Infrastructure.Movement.Navigator;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using WeaponProfile = live::AICompanion.Companion.Brain.Infrastructure.Aiming.WeaponProfile;
using T = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatRecord;
using C = live::AICompanion.Companion.Brain.Activities.ActionContext;

/// <summary>
/// Offer validity is three-valued and time-aware, and a chosen destination holds.
///
/// Three defects share one shape and this fixture holds one row for each. A bounded stand search that runs out of
/// solves or milliseconds reported a proven impossibility, so a hunt was vetoed by its own budget and keeping
/// company took the body on the tick the budget expired. A stand's shot was solved against the target's current
/// centre, so the companion was sent to a place chosen for a slime that would no longer be there when it arrived.
/// And the follow fallback compared tile-quantised positions against a twelve-pixel arrival radius, so it could
/// name the tile the body was already standing on and hold there while the player walked away.
/// </summary>
internal static class VerifyOfferValidity
{
    public static int Run()
    {
        TheShotWindowIsTheTripsOwnLength();
        ACutSearchIsUndecidedAndTheIncumbentSurvives();
        AnExhaustedSearchIsAProvenNegative();
        AnExhaustedSearchIsProvableWhileTheBodyWalks();
        AStandWhoseShotClosesBeforeArrivalIsRefused();
        AMovedTargetIsAskedAboutAgain();
        PartialProgressNeverNamesTheTileTheBodyStandsOn();
        Console.WriteLine("offer validity: the shot window is the trip's length, cut searches stay undecided, exhausted ones prove a negative while the body walks, arrival-time shots and the partial-progress fixed point pass");
        return 0;
    }

    private const int FloorY = 80;
    private const int ShaftLeft = 49, ShaftRight = 53, ShaftFloorY = 86;

    // ---- rows -----------------------------------------------------------------------------------------

    /// <summary>
    /// The window a firing stand's arc must hold for is the trip's own length, bounded — asked of the rule
    /// directly, because the rule is arithmetic and a scene is the wrong instrument for arithmetic.
    ///
    /// <para>Two things are under test and the first one has no scene that could show it cheaply. A trip shorter
    /// than one sample interval used to be sampled <b>nowhere</b>: the loop began at one whole interval, so any
    /// trip under it ran no iterations and the stand was accepted on the arrival solve alone. A short walk is
    /// still a walk and the shot still has to survive it, so that is the case the samples exist for, silently
    /// skipped. The second is that the trip was read backwards — it chose between one sample and two, while the
    /// requirement itself was a fixed twenty or forty ticks after arrival however long the walk was, so a long
    /// trip was held to a middling trip's window.</para>
    ///
    /// <para>The expected sets are written out rather than recomputed from the constants, because a table derived
    /// from the same expression it is checking passes whatever that expression does.</para>
    /// </summary>
    private static void TheShotWindowIsTheTripsOwnLength()
    {
        int interval = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.ShotWindowSampleTicks;
        int cap = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.ShotWindowCapTicks;
        Require(interval == 20 && cap == 40, FormattableString.Invariant(
            $"the table below is written for a 20-tick interval and a 40-tick cap; they now read {interval} and {cap}, so rewrite the expected sets rather than deriving them from the constants they are checking"));
        (float Trip, int[] Expected, string Why)[] table =
        {
            (0f,   new int[0],        "a stand already underfoot has no walk to survive"),
            (1f,   new[] { 1 },       "a one-tick trip is still a trip and used to be sampled nowhere"),
            (5f,   new[] { 5 },       "shorter than one interval: the window's own end is the only sample"),
            (19f,  new[] { 19 },      "one tick under the interval, the old rule's blind spot at its widest"),
            (20f,  new[] { 20 },      "exactly one interval, which the interval lands on"),
            (30f,  new[] { 20, 30 },  "the interval, then the window's end, which the interval does not divide"),
            (40f,  new[] { 20, 40 },  "two intervals, both landed on"),
            (200f, new[] { 20, 40 },  "past the cap: the window stops, and a long walk is not asked for an eternal arc"),
        };
        foreach (var (trip, expected, why) in table)
        {
            int[] actual = live::AICompanion.Companion.Brain.Infrastructure.Position.Positioner
                .ShotWindowOffsets(trip).ToArray();
            int window = live::AICompanion.Companion.Brain.Infrastructure.Position.Positioner.ShotWindow(trip);
            Require(actual.SequenceEqual(expected), FormattableString.Invariant(
                $"a trip of {trip} must be sampled at [{string.Join(",", expected)}] ({why}); it was sampled at [{string.Join(",", actual)}]"));
            Require(window == (int)MathF.Min(trip, cap), FormattableString.Invariant(
                $"a trip of {trip} must measure a window of {MathF.Min(trip, cap)}; it measured {window}"));
            // The window's end is always asked about, whatever the interval does. That is the property the
            // sampling exists for, and the one a loop starting at a whole interval cannot hold.
            if (window > 0)
            {
                string last = actual.Length == 0 ? "none at all" : actual[^1].ToString();
                Require(actual.Length > 0 && actual[^1] == window, FormattableString.Invariant(
                    $"a trip of {trip} must be asked about at the end of its own window ({window}); last sample was {last}"));
            }
        }
        Console.WriteLine(FormattableString.Invariant(
            $"offer validity: shot windows sampled at interval {interval} to a cap of {cap} — trip 5 -> [5], 19 -> [19], 30 -> [20,30], 200 -> [20,40]"));
    }

    /// <summary>
    /// The same proven absence as the row above, with the body walking the whole time. This is the one the
    /// stationary row cannot reach, and the defect it holds is invisible from a scene where nothing moves.
    ///
    /// <para>A refusal is remembered against where the body was standing, because a stand can be refused for the
    /// length of the walk to it and that is a fact about where the walk starts. The exhaustion count was keyed the
    /// same way, and those are different questions: whether a verdict may be reused is about the body, while
    /// whether the sweep has been all the way round is about the stands and the target. Keyed together, a body
    /// that changed bucket — which a following companion does every few rescores — made the whole memory stop
    /// counting, so the shortlist re-cut in the same place every pass and the stands past the cut were never
    /// reached. The search stayed permanently unfinished for as long as the companion was moving, which is most
    /// of the time a hunt matters.</para>
    /// </summary>
    private static void AnExhaustedSearchIsProvableWhileTheBodyWalks()
    {
        var (companion, enemy, profile, _) = PitScene();
        // Filled rather than capped, for the same reason the stationary row fills it: a cap leaves the shaft floor
        // standable with a clear line to the enemy beside it, and choosing that pocket is correct behaviour and the
        // wrong scene. Filled, every candidate is open floor with rock in the way and the only question left is
        // whether the shortlist can finish.
        for (int x = ShaftLeft; x <= ShaftRight; x++)
            for (int y = FloorY; y < ShaftFloorY; y++)
                Solid(x, y);
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        var positioner = companion.Brain.Positioner;
        var request = new PositionRequest(RequestKind.LineOfFire, enemy.Center, enemy);

        var settle = new PositionRequest(RequestKind.WithPlayer, Main.player[0].Bottom);
        for (int i = 0; i < 1200 && !positioner.ReachComplete; i++)
            positioner.Resolve(settle, companion.Brain.Senses, null);
        Require(positioner.ReachComplete, "the walking row needs a settled reachable region before it counts candidates");

        // The body walks a few tiles back and forth along the floor while the search runs. The refusal memory's
        // bucket is four tiles wide, so this crosses it repeatedly rather than incidentally — and it stays near
        // enough that the reach flood is not being re-rooted across the world instead.
        // A triangle wave one whole bucket a pass across sixteen tiles, so the body is in a different bucket on
        // every pass the search runs. That rate is deliberate and it is not a stand-in for a walking speed: the
        // sweep finishes in single-figure passes, and how many it takes varies with how warm the suite is, so a
        // rate slow enough to be typical makes the number of crossings before exhaustion depend on JIT — which is
        // how the first version of this row passed alone and failed inside the full suite at three crossings. The
        // key moves on the stride rather than on the speed, so pinning the crossing rate to one per pass makes the
        // premise hold however many passes the sweep needs.
        const int Home = 30, Swing = 8, Period = 4, Stride = 4;
        int buckets = 0, previousBucket = int.MinValue;
        string reason = "";
        int settledAt = -1;
        for (int tick = 0; tick < 600; tick++)
        {
            int phase = tick % Period;
            int column = Home - Swing + Stride * (phase <= Period / 2 ? phase : Period - phase);
            companion.NPC.position = new Vector2(column * 16f, FloorY * 16f - companion.NPC.height);
            companion.NPC.velocity = Vector2.Zero;
            companion.Brain.Senses.Update(companion.NPC, Main.player[0], companion.Breath);
            int bucket = MovementQueries.FeetTile(companion.NPC.Bottom).X >> 2;
            if (bucket != previousBucket) { buckets++; previousBucket = bucket; }
            positioner.Resolve(request, companion.Brain.Senses, profile);
            if (tick == 0)
                // The same premise the stationary row makes: more candidates than one pass may solve, or the
                // shortlist fits inside its budget and there is no unfinished search to finish.
                Require(positioner.ReachableCandidateCount > 8, FormattableString.Invariant(
                    $"the sealed scene must offer more candidates than one pass can solve; reachable={positioner.ReachableCandidateCount}"));
            reason = positioner.ChoiceReason;
            if (reason == PositionReasons.NoUsableDestination) { settledAt = tick; break; }
        }
        Console.WriteLine(FormattableString.Invariant(
            $"offer validity: a sealed enemy became a proven absence on pass {settledAt + 1} with the body walking through {buckets} refusal buckets"));
        // The premise: the body really did move across the memory's own scoping stride. Without that this row is
        // the stationary one with extra steps, and would pass against the code it exists to reject.
        Require(buckets >= 3 && buckets >= settledAt, FormattableString.Invariant(
            $"the body must be in a different bucket on every pass, or this row is the stationary one with drift; buckets entered={buckets} over {settledAt + 1} passes"));
        Require(settledAt >= 0, FormattableString.Invariant(
            $"a sealed enemy must become a proven absence while the body walks, not stay undecided for ever; last reason={reason} after 600 passes"));
    }

    /// <summary>
    /// A hunt whose shortlist is larger than its solve budget must report that the search did not finish, never
    /// that no destination exists; and while it is undecided the destination already held must survive. This is
    /// the row that proves a budget cut alone cannot flip the companion off a hunt.
    /// </summary>
    private static void ACutSearchIsUndecidedAndTheIncumbentSurvives()
    {
        var (companion, enemy, profile, ctx) = PitScene();
        var request = new PositionRequest(RequestKind.LineOfFire, enemy.Center, enemy);
        var positioner = companion.Brain.Positioner;

        // Settle the flood first, or the early rescores are measuring flood budget rather than the shortlist.
        for (int i = 0; i < 600 && !positioner.ReachComplete; i++)
            positioner.Resolve(request, companion.Brain.Senses, profile);
        Require(positioner.ReachComplete, "the cut row needs a settled reachable region before it reads any reason");

        Vector2? open = null;
        for (int tick = 0; tick < 12; tick++)
            open = positioner.Resolve(request, companion.Brain.Senses, profile);
        Require(open != null, "the pit scene must find the lip while the shaft is open, or there is no incumbent to defend");

        // The incumbent survives a pass that cannot improve on it, and it survives it *without* entering the
        // shortlist at all: retention re-proves the held arc before any budget is spent, so no solve count and no
        // millisecond limit can drop a stand that still works. The evaluated-candidate count standing still across
        // twenty rescores is what says the wide search never ran.
        long revision = positioner.ChosenRevision;
        int evaluated = positioner.EvaluatedCandidates;
        Vector2 heldStand = open!.Value;
        for (int rescore = 0; rescore < 20; rescore++)
            for (int tick = 0; tick < 12; tick++)
                positioner.Resolve(request, companion.Brain.Senses, profile);
        Require(positioner.EvaluatedCandidates == evaluated,
            $"retention must not re-enter the shortlist; evaluated went {evaluated} to {positioner.EvaluatedCandidates}");
        Require(positioner.Chosen == heldStand,
            $"a held firing stand whose arc still solves must be kept, not resampled; held={heldStand}, now={positioner.Chosen}");
        Require(positioner.ChosenRevision == revision,
            $"retaining a destination must not advance its revision; was {revision}, now {positioner.ChosenRevision}");
        Require(positioner.ChoiceReason == PositionReasons.Retained,
            $"a retained destination must say so; reason={positioner.ChoiceReason}");

        // And the offer a chooser would read from that state is a usable one carrying the held destination.
        var offer = positioner.PrepareOffer(request, companion.Brain.Senses, profile);
        Require(offer.Destination == heldStand && !offer.Undecided,
            $"the admission query must return the retained stand as a settled answer; destination={offer.Destination}, reason={offer.Reason}");
    }

    /// <summary>A search that solved or remembered every candidate and found nothing is a proven negative, and
    /// stays one: this is the half the three-valued split must not weaken.</summary>
    private static void AnExhaustedSearchIsAProvenNegative()
    {
        var (companion, enemy, profile, _) = PitScene();
        // Fill the shaft rather than roofing it. A cap alone leaves the shaft floor standable with a clear line to
        // the enemy beside it, and a flood that has not yet proven that pocket unreachable will legitimately choose
        // and then retain it — which is correct behaviour and the wrong scene for this row. Filled, every candidate
        // is open floor with rock between it and the target, so the only question left is the shortlist's.
        for (int x = ShaftLeft; x <= ShaftRight; x++)
            for (int y = FloorY; y < ShaftFloorY; y++)
                Solid(x, y);
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        var request = new PositionRequest(RequestKind.LineOfFire, enemy.Center, enemy);
        var positioner = companion.Brain.Positioner;

        // The flood is settled first, and it is settled with a following request rather than this one. Settling
        // with the firing request would walk the shortlist and remember every refusal, so the search would already
        // be exhausted before observation began; not settling at all makes the row depend on the machine, because
        // how far the flood grows per resolve is bounded by a millisecond budget and a warm suite reaches a
        // different set from a cold standalone run — which showed up as four surviving candidates in the suite
        // against twenty-nine alone, and four cannot exceed a budget of eight. A following request refreshes the
        // same sense and spends no solves, so the region is complete and the shortlist untouched.
        var settle = new PositionRequest(RequestKind.WithPlayer, Main.player[0].Bottom);
        for (int i = 0; i < 1200 && !positioner.ReachComplete; i++)
            positioner.Resolve(settle, companion.Brain.Senses, null);
        Require(positioner.ReachComplete, "the exhaustion row needs a settled reachable region before it counts candidates");

        // This is where a cut is forced by construction: there are far more standable candidates around the sealed
        // shaft than the eight solves a rescore may pay for, and not one of them has an arc. The old code answered
        // "no usable destination established" on the very first pass and the chooser branded the hunt impossible on
        // it; the answer now walks the shortlist across rescores, remembering each refusal as the fact it is, and
        // only says the absence is proven once every candidate has been answered.
        // Every resolve, not every twelfth. With nothing held the rescore gate does not fire, so each tick runs a
        // whole scored pass; sampling one tick in twelve reads the twelfth pass, by which time the memory has
        // absorbed the shortlist and the cut it was meant to observe has already happened.
        var reasons = new List<string>();
        Vector2? chosen = null;
        bool undecidedOffer = false;
        for (int tick = 0; tick < 400; tick++)
        {
            chosen = positioner.Resolve(request, companion.Brain.Senses, profile);
            reasons.Add(positioner.ChoiceReason);
            if (tick == 0)
                // The scene must present more candidates than a pass may solve, or there is no cut to observe and
                // a green result would mean only that the shortlist happened to fit inside its budget.
                Require(positioner.ReachableCandidateCount > 8,
                    $"the sealed scene must offer more candidates than one pass can solve; sampled "
                    + $"{positioner.CandidateCount}, reachable {positioner.ReachableCandidateCount}, answered {positioner.EvaluatedCandidates}");
            if (positioner.ChoiceReason == PositionReasons.SearchUnfinished && !undecidedOffer)
            {
                // Read one undecided pass exactly as the chooser reads it: no destination, and an offer that says
                // the search was cut rather than that nothing exists, so the activity is not branded impossible.
                var cut = positioner.PrepareOffer(request, companion.Brain.Senses, profile);
                Require(cut.Destination == null && cut.Undecided,
                    $"a cut search must present an undecided offer; destination={cut.Destination}, reason={cut.Reason}, undecided={cut.Undecided}");
                undecidedOffer = true;
            }
            if (positioner.ChoiceReason == PositionReasons.NoUsableDestination) break;
        }
        int settledAt = reasons.IndexOf(PositionReasons.NoUsableDestination);
        Console.WriteLine($"offer validity: a sealed enemy took {settledAt + 1} passes to become a proven absence, "
            + $"undecided on the {reasons.Count(r => r == PositionReasons.SearchUnfinished)} before it "
            + $"({positioner.CandidateCount} sampled, {positioner.EvaluatedCandidates} answered on the last)");
        Require(undecidedOffer, "no cut pass was ever presented to the chooser, so the undecided offer is untested");

        Require(reasons.Contains(PositionReasons.SearchUnfinished),
            $"a shortlist larger than its solve budget must report an unfinished search at least once; reasons={string.Join(",", reasons)}");
        Require(settledAt >= 0, $"the sealed shaft must eventually prove the absence; reasons={string.Join(",", reasons)}");
        Require(reasons.Take(settledAt).All(r => r == PositionReasons.SearchUnfinished),
            $"every pass before the proven absence must say the search was cut, never that none exists; reasons={string.Join(",", reasons)}");

        // The undecided passes are the ones that must not brand the activity. Read one as the chooser reads it.
        Require(settledAt > 0, "the cut row needs at least one undecided pass to inspect");

        Require(chosen == null, $"a sealed enemy must yield no firing destination; chosen={chosen}");
        Require(positioner.ChoiceReason == PositionReasons.NoUsableDestination,
            $"a search that answered every candidate must report a proven absence, not an unfinished one; reason={positioner.ChoiceReason}");
        var offer = positioner.PrepareOffer(request, companion.Brain.Senses, profile);
        Require(offer.Destination == null && !offer.Undecided,
            $"an exhausted search must be a settled refusal the chooser may brand on; reason={offer.Reason}, undecided={offer.Undecided}");
        Require(positioner.CandidateEvidence.Contains(":no-arc"),
            $"the exhausted row must have actually solved candidates; evidence={positioner.CandidateEvidence}");
    }

    /// <summary>
    /// A stand is worth walking to only if the shot is still there on arrival. The target here is moving fast
    /// enough that its forecast leaves the shaft's line of fire well before a body could cross the floor to the
    /// lip, so the stand that a current-position solve would have accepted is refused with a reason naming the
    /// window rather than the geometry.
    /// </summary>
    private static void AStandWhoseShotClosesBeforeArrivalIsRefused()
    {
        var (companion, enemy, profile) = PillarScene();
        var positioner = companion.Brain.Positioner;
        var request = new PositionRequest(RequestKind.LineOfFire, enemy.Center, enemy);

        // The scene has to be discriminating before its result means anything: there must be a stand whose shot
        // exists against the target's current centre and not against its forecast at arrival, or a solve that
        // ignored the forecast entirely would pass this row.
        Vector2 eyeBehind = MuzzleAt(new Point(60, FloorY - 1));
        Vector2 eyeBeyond = MuzzleAt(new Point(70, FloorY - 1));
        bool behindNow = live::AICompanion.Companion.Brain.Infrastructure.Aiming.TrajectoryAimer.Solve(eyeBehind, enemy, profile!.Value) != null;
        bool behindLater = live::AICompanion.Companion.Brain.Infrastructure.Aiming.TrajectoryAimer.Solve(eyeBehind, enemy, profile.Value, ArrivalTicks) != null;
        bool beyondLater = live::AICompanion.Companion.Brain.Infrastructure.Aiming.TrajectoryAimer.Solve(eyeBeyond, enemy, profile.Value, ArrivalTicks) != null;
        Console.WriteLine($"offer validity: behind the pillar now={behindNow} at+{ArrivalTicks}={behindLater}, beyond it at+{ArrivalTicks}={beyondLater}");
        Require(behindNow && !behindLater,
            $"the pillar scene must offer a stand whose shot closes before arrival; now={behindNow}, later={behindLater}");
        Require(beyondLater, "the pillar scene must leave somewhere that can still shoot on arrival, or refusing is the only answer");

        // Settled with a following request for the same reason the exhaustion row is: a firing request spends
        // solves and remembers refusals while it waits, and an unsettled region leaves a handful of candidates
        // that differ between a warm suite and a cold run.
        var settle = new PositionRequest(RequestKind.WithPlayer, Main.player[0].Bottom);
        for (int i = 0; i < 1200 && !positioner.ReachComplete; i++)
            positioner.Resolve(settle, companion.Brain.Senses, null);
        Require(positioner.ReachComplete, "the pillar row needs a settled reachable region before it chooses a stand");
        // Resolve until the search settles rather than a fixed number of times. How deep one pass gets is bounded
        // by a millisecond budget, so a warm suite and a cold standalone run cut the shortlist in different places
        // and a fixed count makes this row depend on the machine. Waiting for the undecided state to clear is not
        // a workaround for that: it is the state existing precisely so a cut can be waited out.
        Vector2? chosen = null;
        for (int tick = 0; tick < 400; tick++)
        {
            chosen = positioner.Resolve(request, companion.Brain.Senses, profile);
            if (chosen != null || positioner.ChoiceReason == PositionReasons.NoUsableDestination) break;
        }

        Require(chosen != null, $"the pillar scene must produce a stand; reason={positioner.ChoiceReason}, evidence={positioner.CandidateEvidence}");
        // The forecast must actually have been consulted. A solve that fell back to the current position records
        // clear-arc-unforecast, and a row full of those would pass on stands that happen to work at arrival while
        // proving nothing about whether arrival was ever asked about.
        Require(positioner.CandidateEvidence.Contains(":clear-arc|") || positioner.CandidateEvidence.EndsWith(":clear-arc")
                || positioner.CandidateEvidence.Contains(":" + PositionReasons.ShotWindowShorterThanTrip),
            $"no candidate was judged against the forecast, so this row tested the low-confidence fallback; "
            + $"evidence={positioner.CandidateEvidence}");
        Point stand = MovementQueries.FeetTile(chosen!.Value);
        Console.WriteLine($"offer validity: chose {stand.X},{stand.Y} against a target walking from 58 toward 74; evidence={positioner.CandidateEvidence}");
        // The whole claim, in one assertion: the stand the companion walks to is one that can shoot the target
        // where it will be when the body arrives, not one chosen for where it was when the choice was made. The
        // probe asks at the stand's own estimated trip rather than at a fixed horizon, because that is the horizon
        // the choice was made at — asking at any other one tests a question the positioner was never posed.
        Point feet = MovementQueries.FeetTile(companion.NPC.Bottom);
        float trip = positioner.EstimatedTravelTicks(feet, stand)
            ?? Vector2.Distance(companion.NPC.Bottom, chosen.Value) / live::AICompanion.Companion.CompanionMotor.WalkSpeed;
        Require(live::AICompanion.Companion.Brain.Infrastructure.Aiming.TrajectoryAimer
                .Solve(MuzzleAt(stand), enemy, profile.Value, (int)MathF.Min(180f, trip)) != null,
            $"the chosen stand cannot shoot the target at its forecast arrival, so the shot was solved against a "
            + $"position the target will have left; stand={stand.X},{stand.Y}, trip={trip:0.0}, evidence={positioner.CandidateEvidence}");
    }

    /// <summary>
    /// An answer about where a target can be shot from goes stale when the target moves, not only when the body
    /// does — and `ResolveFiringOpportunity` was scoped to one end of that line.
    ///
    /// <para>Both of its stores carried the companion's position and the terrain revision and neither carried the
    /// target's. The cache therefore served a verdict taken while the enemy was behind a pillar for the whole of
    /// its tick window after the enemy had walked out from behind it, and the sweep accumulated its examined count
    /// across scans while the stands themselves are rebuilt around the enemy's own feet every scan — so the count
    /// could reach the set's size having asked only about stands around places the enemy had left. That returns a
    /// proven absence of any firing position, which guarding reads as a threat it cannot shoot and hunting as a
    /// target not worth approaching. It is the same exhausted-bound-reported-as-a-fact defect this class already
    /// closed once, arriving through the target's motion instead of through the budget.</para>
    ///
    /// <para>The row moves the enemy from behind the pillar to the companion's own feet without advancing a tick,
    /// so the only thing that has changed is the one thing the key was missing.</para>
    /// </summary>
    private static void AMovedTargetIsAskedAboutAgain()
    {
        var (companion, enemy, _) = PillarScene();
        var ctx = new C(companion, companion.Brain.Senses);
        var firing = new live::AICompanion.Companion.Brain.Activities.Combat.ResolveFiringOpportunity();

        // Settle the flood, or the first verdicts are measuring how far it has grown rather than the geometry.
        var settle = new PositionRequest(RequestKind.WithPlayer, Main.player[0].Bottom);
        for (int i = 0; i < 1200 && !companion.Brain.Positioner.ReachComplete; i++)
            companion.Brain.Positioner.Resolve(settle, companion.Brain.Senses, null);

        // Behind the pillar, well out of a shot from where the body stands.
        enemy.Bottom = new Vector2((PillarRight + 6) * 16f + 8f, FloorY * 16f);
        companion.Brain.Senses.Update(companion.NPC, Main.player[0], companion.Breath);
        var (behind, _) = firing.Resolve(ctx, enemy);
        Require(behind != live::AICompanion.Companion.Brain.Activities.Combat.FiringAccess.FromHere, FormattableString.Invariant(
            $"the premise fails: the enemy behind the pillar must not already be shootable from where the body stands, or moving it proves nothing; verdict={behind}"));

        // Now beside the companion, in the open, on the same tick. Nothing about the body or the world has
        // changed — only the target — so an answer that does not move is an answer keyed on the wrong things.
        enemy.Bottom = companion.NPC.Bottom + new Vector2(32f, 0f);
        float moved = Vector2.Distance(enemy.Center, companion.NPC.Center);
        companion.Brain.Senses.Update(companion.NPC, Main.player[0], companion.Breath);
        var (beside, _) = firing.Resolve(ctx, enemy);
        Console.WriteLine(FormattableString.Invariant(
            $"offer validity: a target behind the pillar read {behind}; walked to {moved:F0}px from the muzzle on the same tick it reads {beside}"));
        Require(beside == live::AICompanion.Companion.Brain.Activities.Combat.FiringAccess.FromHere, FormattableString.Invariant(
            $"a target that has walked into the open must be asked about again rather than answered from where it was; it read {beside}, and behind the pillar it read {behind}"));
        // And the absence in particular must never be the stale one, because a proven absence is the verdict the
        // rest of combat is entitled to act on.
        Require(beside != live::AICompanion.Companion.Brain.Activities.Combat.FiringAccess.None,
            $"a moved target must never be reported as a proven absence of firing positions on evidence gathered about where it used to be; verdict={beside}");
    }

    /// <summary>
    /// The follow fallback must never hand back a tile the navigator would immediately call arrived at. The body
    /// is placed exactly on a partial-progress answer with the player still out of reach above; the next answer
    /// has to be somewhere else, or nothing at all, and never the tile under the feet.
    /// </summary>
    private static void PartialProgressNeverNamesTheTileTheBodyStandsOn()
    {
        var (companion, _, _, _) = PitScene();
        var positioner = companion.Brain.Positioner;
        Player player = Main.player[0];
        // The player stands on a shelf the companion cannot reach, so no candidate is ever accepted and every
        // answer is the fallback's. The shelf is above the sealed ceiling of the world's floor slab.
        player.position = new Vector2(60 * 16f, (FloorY - 20) * 16f - player.height);
        player.velocity = Vector2.Zero;
        companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);

        var request = new PositionRequest(RequestKind.WithPlayer, player.Bottom);
        Vector2? chosen = null;
        for (int i = 0; i < 900 && !positioner.ReachComplete; i++)
            chosen = positioner.Resolve(request, companion.Brain.Senses, null);
        for (int tick = 0; tick < 24; tick++)
            chosen = positioner.Resolve(request, companion.Brain.Senses, null);

        Require(chosen != null && positioner.ChoiceReason == "partial-progress-candidate",
            $"a player on an unreachable shelf must be answered by the fallback; reason={positioner.ChoiceReason}, chosen={chosen}");
        Require(positioner.Region.Kind == SuccessRegionKind.PartialProgress,
            $"the fallback must declare the region it can meet; kind={positioner.Region.Kind}");

        // Stand the body exactly on that answer and ask again. Every later answer must be a real step away.
        for (int round = 0; round < 6; round++)
        {
            companion.NPC.Bottom = chosen!.Value;
            companion.NPC.velocity = Vector2.Zero;
            companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
            Vector2 feet = companion.NPC.Bottom;
            Vector2? next = null;
            for (int tick = 0; tick < 24; tick++)
                next = positioner.Resolve(request, companion.Brain.Senses, null);
            Require(next == null || Vector2.Distance(next.Value, feet) > Navigator.ArriveDistance,
                $"round {round}: the fallback named a tile the navigator is already arrived at; feet={feet}, next={next}, "
                + $"distance={(next == null ? -1f : Vector2.Distance(next.Value, feet))}, arrive={Navigator.ArriveDistance}");
            if (next == null) break;
            chosen = next;
        }
    }

    // ---- scene ----------------------------------------------------------------------------------------

    /// <summary>How far ahead the arrival probes look. A walk of a handful of tiles at the companion's own speed,
    /// which is short enough that the measured forecast confidence is still above the floor the solve requires —
    /// past roughly ninety ticks the forecast decays below it and the solve honestly falls back to the current
    /// position, so a scene built on a long walk would be testing the fallback rather than the forecast.</summary>
    private const int ArrivalTicks = 45;

    private static Vector2 MuzzleAt(Point tile)
        => live::AICompanion.Companion.Weapons.Arsenal.MuzzleAtFeet(MovementQueries.FeetWorld(tile));

    /// <summary>
    /// A flat floor with a pillar on it and an enemy walking past that pillar. Stands short of the pillar can see
    /// the enemy where it is now; by the time a body could walk to one, the enemy is beyond the pillar and the
    /// line is rock. Stands past the pillar are the mirror. The enemy is walked across real game ticks so the
    /// shared motion track accumulates error samples and its forecast carries measured confidence: without that
    /// the solve correctly declines to judge on a guess and falls back to the current position.
    /// </summary>
    private static (CompanionNPC Companion, NPC Enemy, WeaponProfile? Profile) PillarScene()
    {
        BuildFlatWorldWithPillar();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        // The body starts a real walk away from the stands that matter. Standing on top of them makes every trip
        // zero ticks, at which arrival and now are the same instant and the forecast has nothing to disagree with;
        // it is also far enough back that the walk is still inside the span the forecast is confident over.
        player.position = new Vector2(54 * 16f, FloorY * 16f - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.position = new Vector2(54 * 16f, FloorY * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;

        var enemy = new NPC();
        enemy.SetDefaults(Terraria.ID.NPCID.Zombie);
        enemy.whoAmI = 31;
        enemy.active = true;
        enemy.Bottom = new Vector2(52 * 16f + 8f, FloorY * 16f);
        enemy.velocity = new Vector2(EnemyWalkPx, 0f);
        // It phases, and that is the scene rather than an escape from building a better one. The forecast applies
        // Terraria's own tile collision, so an ordinary walker stops dead against the pillar and its predicted
        // position never leaves the side it started on — which makes every stand's answer the same now as later
        // and the row untestable. A phasing hostile crosses, so "where it is" and "where it will be" sit on
        // opposite sides of a wall, which is the whole distinction under test.
        // It flies as well as phases: the forecast applies the NPC's own gravity, so a phasing walker with gravity
        // sinks through the floor it no longer collides with and its predicted arrival position is inside rock,
        // where nothing can shoot it and the row degenerates into refusing every stand.
        enemy.noTileCollide = true;
        enemy.noGravity = true;
        Main.npc[31] = enemy;

        // Real ticks with a consistent step, so the track reads the motion as continuing rather than as a jump.
        // The clock setter is the observed-motion fixture's, not a second one: Terraria's counter is a private
        // field under one of two names depending on the build, and one place should know which.
        live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Clear();
        // Move, then observe, and stop on an observation. Advancing the position after the last observation leaves
        // the track one step behind the body, so the next observation is not consecutive with it, the error history
        // resets to zero samples, and the forecast carries no measured confidence — at which point every solve
        // honestly falls back to the target's current position and the row tests the fallback instead.
        for (ulong tick = 1; tick <= 17; tick++)
        {
            enemy.position += enemy.velocity;
            VerifyObservedMotion.SetTick(tick);
            live::AICompanion.Companion.Brain.Infrastructure.Observation.PredictObservedMotion.Observe(enemy);
        }

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
        Require(profile != null, "the pillar scene needs an equipped weapon profile to solve with");
        return (companion, enemy, profile);
    }

    private const float EnemyWalkPx = 6f;
    private const int PillarLeft = 65, PillarRight = 66, PillarTop = 6;

    private static void BuildFlatWorldWithPillar()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        Main.maxTilesX = Main.maxTilesY = 120;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)120, (ushort)120 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= FloorY + 4; y++)
                Solid(x, y);
        for (int x = PillarLeft; x <= PillarRight; x++)
            for (int y = FloorY - PillarTop; y < FloorY; y++)
                Solid(x, y);
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

    private static (CompanionNPC Companion, NPC Enemy, WeaponProfile? Profile, C Ctx) PitScene()
    {
        BuildPitWorld();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(30 * 16f, FloorY * 16f - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.position = new Vector2(30 * 16f, FloorY * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;

        var enemy = new NPC();
        enemy.SetDefaults(Terraria.ID.NPCID.Zombie);
        enemy.whoAmI = 30;
        enemy.active = true;
        enemy.velocity = Vector2.Zero;
        enemy.Bottom = new Vector2(51 * 16f + 8f, ShaftFloorY * 16f);
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
        Require(profile != null, "the offer-validity fixture needs an equipped weapon profile to solve with");
        return (companion, enemy, profile, ctx);
    }

    private static void BuildPitWorld()
    {
        // Main's static constructor reads Program.SavePath, which is null until something sets it. The default
        // suite does this before any fixture runs; the standalone flag has to do it itself or touching Main throws.
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        Main.maxTilesX = Main.maxTilesY = 120;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)120, (ushort)120 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= ShaftFloorY + 2; y++)
                Solid(x, y);
        for (int x = ShaftLeft; x <= ShaftRight; x++)
            for (int y = FloorY; y < ShaftFloorY; y++)
                Open(x, y);
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

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
