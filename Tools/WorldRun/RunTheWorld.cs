extern alias live;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.CharacterBody;

/// <summary>
/// The tick loop: the whole brain and the native body, in a real world, behind a player who moves.
///
/// One tick here is the same three things a live frame is, in the same order. The player is placed
/// where the recording says it was, so the senses observe a player that is actually travelling.
/// The brain runs, chooses and asks the motor for controls. Then the engine's own gravity and
/// collision finish the move. The light engine is advanced one phase alongside, because that is
/// what a frame does and because the lighting behaviour reads the result.
///
/// The output is a track and a trace. The track is where the body's centre went, which is what the
/// recorded comparison and the checkpoint scorer both measure. The trace is one line per tick naming the
/// decision as well as the position, and it exists because determinism has to be checked on what
/// the brain *decided* and not only on where the body ended up: two runs can agree on position for
/// a hundred ticks while disagreeing about why, and the disagreement is the thing that matters.
/// </summary>
internal static class RunTheWorld
{
    /// <summary>
    /// How wide an area the light engine is driven over, in tiles either side of the player.
    ///
    /// It is the screen-sized neighbourhood a playing game lights, rather than a number tuned here:
    /// lighting decisions are about the room the companion is standing in, and a window much larger
    /// than the screen would light places the live game never lit and make the run's light field
    /// more generous than the one the capture was recorded under.
    /// </summary>
    private const int LightHalfWidth = 80, LightHalfHeight = 60;

    /// <summary>
    /// What the combat variant saw, per tick. The zombie is the instrument's own staging — frozen,
    /// retired by the instrument after sustained fire — so this record exists to grade the
    /// companion's decisions around a fight (eagerness, persistence, recovery), never its lethality,
    /// which no headless tool simulates. Null on an ordinary run, which places no zombie.
    /// </summary>
    internal sealed record FightTrace(
        IReadOnlyList<bool> CombatCurrent,
        IReadOnlyList<string> Fire,
        IReadOnlyList<bool> Threatened,
        /// <summary>Step index the instrument retired the zombie, or -1 when the companion never fired the sustained burst that retires it.</summary>
        int KillStep,
        /// <summary>How many ticks read fired, cumulative, which is what retires the zombie at five.</summary>
        int FiredTicks);

    /// <summary>
    /// One tick, as the play measures read it: what the course decided, what its discovery admitted,
    /// what its ordering refused, what the tick cost, and what stood in the world while it happened.
    ///
    /// It is a plain record rather than a reach back into the brain because the grading is done
    /// after the run, and a scorer that read live brain state would be reading the last tick's
    /// answer for every row. Everything on it is public brain surface sampled at the one moment it
    /// means what the row says it means.
    /// </summary>
    internal readonly record struct PlayTick(
        int Tick,
        string Reason,
        string Activity,
        bool HasStep,
        bool Settled,
        int UsableAdmitted,
        int UnresolvedAdmitted,
        int Refused,
        IReadOnlyDictionary<string, int> Refusals,
        double DecideMs,
        double BrainMs,
        int Gen2Collections,
        bool Fired,
        int HostilesAlive,
        int DropsPresent,
        /// <summary>Whether the tick's bound activity was the fighting stance.</summary>
        bool Fighting,
        /// <summary>
        /// Hostiles standing inside the player's own intent region — the space README says the
        /// companion moves about with him, so a hostile in it is one that is already on somebody.
        /// </summary>
        int HostilesInRegion,
        /// <summary>
        /// Hostiles the threat sense forecasts reaching <em>the player</em> inside the harm horizon,
        /// by its own <c>TicksToPlayer</c> and only where it believes the hostile can reach him.
        /// </summary>
        int HostilesArrivingAtPlayer,
        /// <summary>
        /// The same forecast about the companion, counted and reported but deliberately **not** part
        /// of whether a fight is wanted. The reason is circularity rather than doctrine: the threat
        /// sense's arrival is <c>Distance / ObservedSpeed</c>, so a hostile "reaching the companion in
        /// ten seconds" is very often a fact about where the orb chose to fly rather than about
        /// anything coming for anybody — and a denominator the companion can enlarge by wandering
        /// toward hostiles is one it can also pass, because it is already fighting on those ticks.
        /// Measured on the capture of 22 September: counting it made README want a fight on 1,115 of
        /// 2,340 ticks against 138 without it, with **zero** hostiles ever inside the player's region.
        /// A hostile genuinely on the companion is inside the player's region too, because the orb
        /// lives in that region, so the case README means by "already on top of one of you" is kept
        /// by the region test rather than lost.
        /// </summary>
        int HostilesArrivingAtCompanion,
        /// <summary>The soonest arrival at the player the threat sense forecast this tick, or infinity when nothing is coming.</summary>
        float SoonestArrivalTicks,
        /// <summary>
        /// The identity of the decision standing on this tick, and how many facts its frozen observation
        /// carried. Both are per *decision* rather than per tick, so a carried course repeats them: the
        /// growth verdict samples where this value changes, which is the same rule the soak samples on.
        /// They are read here rather than reconstructed because an observation is frozen for the life of
        /// one decision and is gone by the time any grader runs.
        /// </summary>
        long DecisionId,
        int Facts,
        /// <summary>
        /// The best order each domain led in the decision standing on this tick, as its total and the
        /// terms it is made of, so a losing fight says which term it lost on rather than only that it
        /// lost. `task_order_runner_up` in the recorder carries the second-best total alone, and a total
        /// cannot tell a fight that was too far away from one that could never kill anything.
        /// </summary>
        string Leaders)
    {
        /// <summary>
        /// Whether README's fight scenes want a fight on this tick.
        ///
        /// Two qualifiers, and both come out of the 2:00 scene rather than out of a distance. A
        /// hostile inside the player's own region is one that is on the pair — the scene's "already
        /// on top of one of you". A hostile the forecast has reaching the player soon is the bat that
        /// "will be here in a second or two", and the horizon is wide enough to hold the slow heavy
        /// thing "that will take five seconds to arrive". The scene's declined slime fails both, which
        /// is the whole point: it "was never going to reach either of you".
        ///
        /// What neither qualifier holds is named where the horizon is declared, and the companion-side
        /// arrival is excluded for the reason given on its own field.
        /// </summary>
        public bool AFightIsWanted => HostilesInRegion > 0 || HostilesArrivingAtPlayer > 0;
    }

    internal sealed record Outcome(
        IReadOnlyList<Vector2> CompanionCentres,
        IReadOnlyList<string> Trace,
        /// <summary>
        /// What the reach sense said about the tile the player's body occupied, on the tick he stood
        /// there. The tile of his body and not the tile under his feet: a standing player's feet rest
        /// on a solid tile, which has no usable corner by construction and so can never be in a flood
        /// of free space, and asking about it printed "not yet" on nineteen of twenty checkpoints
        /// whatever the run did. This is the "planner claim" half of the checkpoint matrix, and it is
        /// taken here rather than recomputed afterwards for two reasons: it is free, because the flood
        /// it reads has already been run for this tick's decisions, and it is the claim the brain
        /// actually held at the time rather than one reconstructed from a later state.
        /// </summary>
        IReadOnlyList<live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict> PlannerClaim,
        /// <summary>
        /// Whether the body's centre sat inside the player's intent region on each tick, by the region's own geometry
        /// rather than the sense's inside latch, and whether the sense read the body connected to him. Together they are
        /// what the brain itself calls being with the player — inside his region, with a way to him inside it — and they
        /// are taken here for the planner claim's reason: they are the region and the connection the brain held that
        /// tick, which no later state can reconstruct.
        /// </summary>
        IReadOnlyList<bool> InsideRegion,
        IReadOnlyList<bool> ConnectedToPlayer,
        int Ticks,
        double Seconds,
        string WorldSource,
        /// <summary>
        /// What the light sense held at the end of the run. It is carried out of the loop because
        /// driving the light engine is a claim this instrument makes, and an engine that reaches
        /// nothing is indistinguishable from a dark world unless the sense's own sample count is
        /// read: zero samples means the sense never read anything the engine presented.
        /// </summary>
        ulong? LightReadTick,
        int LightMeasuredSamples,
        float LightAtCompanion,
        float LightAtPlayer,
        /// <summary>Ticks the body's own tile sat outside the reach sense's known radius, so nothing near it could be proven absent.</summary>
        int TicksOutsideKnownRadius,
        /// <summary>Ticks the reach flood read complete.</summary>
        int TicksReachComplete,
        /// <summary>The combat variant's fight record, or null on an ordinary run, which places no zombie.</summary>
        FightTrace? Fight,
        /// <summary>
        /// The decision audit's own two counts at the end of the run, taken from the mod's statics
        /// rather than parsed back out of a capture, so the row that grades them holds whether or not
        /// a recorder was attached.
        ///
        /// They are two numbers rather than one because <c>Audited</c> increments at the top of the
        /// audit before its source is consulted, so a session whose installer never ran reports every
        /// decision audited and would look healthy under one count. <c>ObservationsRead</c> is lower
        /// than <c>Audited</c> by design and not by fault: the observation is read once per *decision*
        /// ordinal, and a carried course repeats its ordinal on every tick it holds the body.
        /// </summary>
        long DecisionsAudited,
        long AuditObservationsRead,
        /// <summary>Every contract violation the audit counted this run, by kind, whether or not the
        /// recorder's coalescing kept it. Empty on a healthy run, and empty in exactly the same way on
        /// a run whose audit was never wired — which is why the two counts above are graded first.</summary>
        IReadOnlyDictionary<string, long> ContractViolations,
        /// <summary>One entry per tick for the play measures, always filled: the cost of keeping it is a struct a tick.</summary>
        IReadOnlyList<PlayTick> Play)
    {
        /// <summary>
        /// One number standing for the whole run's decisions and positions, so two runs can be
        /// compared in a row rather than in a paragraph. It is order-sensitive on purpose: two runs
        /// that made the same set of decisions in a different order are not the same run.
        /// </summary>
        public string TraceHash
        {
            get
            {
                ulong hash = 14695981039346656037UL;
                foreach (string line in Trace)
                    foreach (char c in line) { hash ^= c; hash *= 1099511628211UL; }
                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Whether the light engine is driven at all.
    ///
    /// It exists to be turned off, which is the only way to find out whether driving it changes any
    /// decision in a given window. A run with it off whose trace is identical to a run with it on
    /// has proved that lighting reached nothing there — which is a fact about the window rather
    /// than a fault, and one worth being able to establish rather than assume.
    /// </summary>
    public static bool DriveLight { get; set; } = true;

    /// <summary>
    /// Whether this run is the combat variant: a zombie placed where the player will stand thirty
    /// ticks in, grading whether Combat takes the body, whether the hands keep firing while the
    /// zombie is on him, and whether the body rejoins the route after the kill.
    ///
    /// The zombie is staging, and the rows say so. It never moves — no headless tool runs NPC AI —
    /// and the kill is struck by the instrument after five cumulative fired ticks, because no
    /// headless tool simulates projectile damage either. What the variant grades is the companion's
    /// decisions around the fight, which are all real: the threat sense reads a live hostile, the
    /// stance must win selection, the hands must fire, and the body must come home afterwards.
    /// </summary>
    public static bool CombatVariant { get; set; } = false;

    /// <summary>
    /// The recording's own hostiles and drops, staged at their recorded ticks, or null for the
    /// empty-world run every row before 2026-09-22 was taken in.
    /// </summary>
    public static StageRecordedActors? Actors { get; set; }

    /// <summary>
    /// Whether this run keeps the game's own millisecond allowances instead of lifting them.
    ///
    /// Every other row here lifts them, because a determinism or divergence comparison under a wall
    /// clock is comparing two afternoons. The play measures are the exception and it is the whole
    /// point of them: what a player met was a brain being cut by its deadline, so a run that gave
    /// the brain all the time it wanted would be grading a brain nobody has ever played. A row taken
    /// this way says <c>production-clock</c> in its mode and is comparable only to another taken the
    /// same way.
    /// </summary>
    public static bool ProductionClock { get; set; } = false;

    /// <summary>
    /// Something to do once the companion exists and before the first tick.
    ///
    /// It exists for the recorder and the ordering is the whole reason. Attaching a recorder needs
    /// a world *and* a player: its metadata reads the player's mount, wings and boots for the
    /// capabilities line, and <c>Main.player[0]</c> is not built until <c>AttachCompanion</c> runs
    /// inside this loop. Opening the recorder before that reads a null player, and the recorder
    /// catches its own failure and closes — so the run produces a capture that is five header lines,
    /// zero rows and <c>end=recorder-initialization-failed</c>, which looks from outside like a
    /// recorder that was never asked for.
    /// </summary>
    public static Action? AfterTheCompanionIsAttached { get; set; }

    /// <summary>Which Main.npc slot the combat variant's zombie stands in. The companion is not in the array, so no slot collides.</summary>
    private const int ZombieSlot = 50;

    /// <summary>How many steps ahead of the opening the zombie waits, at the player's own recorded feet — free space by construction, beside the route by the plan's wording.</summary>
    private const int ZombieStepAhead = 30;

    /// <summary>How close the zombie must be to count as on the player: five tiles, near enough that the danger sense cannot miss it and far enough that the window spans his approach and his passing.</summary>
    private const float OnPlayerPx = 80f;

    /// <summary>
    /// How soon a hostile has to be reaching a body for README's scenes to want it fought, in ticks.
    ///
    /// Ten seconds, and the number is README's rather than this file's. The 2:00 scene declines a
    /// slime "eight hops from connecting" and wants the bat that "will be here in a second or two";
    /// between them it names the case that sets the horizon — "a slow, heavy thing … something that
    /// will take five seconds to arrive and an age to put down", which "matters more than the
    /// hop-count suggests". Five seconds is therefore inside the wanted set by name, and ten is that
    /// with headroom rather than a second rule.
    ///
    /// **Two things README wants fought sit outside this and outside the region test beside it**, and
    /// they are the cost of using a horizon at all. A thing slower than ten seconds to arrive but "an
    /// age to put down" is the same sentence's other half, and nothing here reads how long a fight
    /// would be. And the receding slime of line 51 — declined while the player walks, wanted the
    /// moment he "stops walking, turns to a tree and starts swinging", because then "the flight costs
    /// nothing" — turns on the *player's* motion rather than the hostile's, which neither qualifier
    /// looks at. So a row built on this under-counts in exactly those two scenes, and the rows say so.
    /// </summary>
    private const float HarmHorizonTicks = 600f;

    /// <summary>How many cumulative fired ticks retire the zombie: five arrows over several cooldown cycles, which proves the stance held the fight rather than firing once. Thirty was the first guess; the probe fired nine ticks in three hundred steps, the wooden bow's maximum rate, so thirty needs a thousand-tick slice for nothing the fifth shot does not prove.</summary>
    private const int FiredTicksToKill = 5;

    /// <summary>`domain=total(u useful h harm s self g gap)` per leader, idle first, unknowns named when any.</summary>
    private static string DescribeLeaders(IReadOnlyDictionary<string, live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.CourseValue> leaders)
        => string.Join(" ", leaders.OrderBy(pair => pair.Key == "(idle)" ? "" : pair.Key, StringComparer.Ordinal)
            .Select(pair => string.Create(CultureInfo.InvariantCulture,
                $"{pair.Key}={pair.Value.Total.Nominal:0.000}(u{pair.Value.UsefulEffects:0.000} h{pair.Value.Harm:0.000} s{pair.Value.CompanionHarm:0.000} g{pair.Value.Companionship:0.000})")
                + (pair.Value.Unknowns.Count == 0 ? "" : "[" + string.Join(",", pair.Value.Unknowns.Select(u => u.Split(':')[0]).Distinct()) + "]")));

    public static Outcome Play(ReadRecordedRoute.Route route, string worldSource, int seed)
    {
        // Unbounded planning, for the reason the suite already established and enforces: the live
        // allowances are a wall clock, so how much search fits in a tick depends on what else the
        // machine is doing, and a run compared against another run under them is comparing two
        // afternoons. The fixtures that test the deadline itself are the exception, and this is not
        // one of them.
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = !ProductionClock;
        try
        {
            return PlayWithPlanningUnbounded(route, worldSource, seed);
        }
        finally
        {
            live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false;
        }
    }

    private static Outcome PlayWithPlanningUnbounded(ReadRecordedRoute.Route route, string worldSource, int seed)
    {
        ReadRecordedRoute.Step opening = route[0];
        // The census counts for the whole process, so a pass that did not clear it would report its
        // own movement plus every pass before it.
        live::AICompanion.Companion.Brain.Infrastructure.Movement.BehaviourCensus.Reset();
        PrepareTheHeadlessEngine.PinEveryRandomSource(seed);
        // Before anything reads the clock, including the light warm-up: a pass that started its
        // route at a different world time from the pass before it is not a repeat of it.
        PrepareTheHeadlessEngine.StartTheWorldClockAt((ulong)opening.Tick);
        PrepareTheHeadlessEngine.PrepareLightServices();

        // The companion starts where the recording had it, which is what makes a run from a later
        // tick meaningful at all: the source has moved since these captures, so a run from tick one
        // has diverged long before it reaches anything worth asking about, and seeding the body at
        // the recorded pose is the only way to ask what this build does at that place.
        // The recorded position is the body's centre, and the orb is its centre, so it is placed as read.
        var companion = PrepareTheHeadlessEngine.AttachCompanion(opening.CompanionCentre, opening.PlayerFeet);

        Player player = Main.player[0];
        if (DriveLight) PrepareTheHeadlessEngine.WarmTheLightEngine(player.Bottom.ToTileCoordinates(), LightHalfWidth, LightHalfHeight);
        AfterTheCompanionIsAttached?.Invoke();

        // The combat variant's zombie, placed where the player will stand and retired by the
        // instrument. Re-placed every pass: the slot persists across passes in-process, and a pass
        // that inherited a dead zombie would grade a fight that never started.
        NPC? zombie = null;
        if (CombatVariant)
        {
            zombie = Main.npc[ZombieSlot];
            zombie.SetDefaults(NPCID.Zombie);
            zombie.Bottom = route[Math.Min(ZombieStepAhead, route.Count - 1)].PlayerFeet;
            zombie.velocity = Vector2.Zero;
            zombie.active = true;
            zombie.whoAmI = ZombieSlot;
        }

        Actors?.Reset();
        var play = new List<PlayTick>(route.Count);
        var centres = new List<Vector2>(route.Count);
        var trace = new List<string>(route.Count);
        var claims = new List<live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict>(route.Count);
        var inside = new List<bool>(route.Count);
        var connected = new List<bool>(route.Count);
        var combatCurrent = new List<bool>(route.Count);
        var fire = new List<string>(route.Count);
        var threatened = new List<bool>(route.Count);
        int killStep = -1, firedTicks = 0;
        int ticksOutsideKnownRadius = 0, ticksReachComplete = 0;
        var clock = Stopwatch.StartNew();

        for (int index = 0; index < route.Count; index++)
        {
            ReadRecordedRoute.Step step = route[index];

            // The player is placed rather than simulated. Its recorded velocity is written too,
            // because the follow behaviour reads predicted feet from velocity rather than from the
            // difference between two positions, so a player moved by position alone reads as a
            // player standing still in a new place every tick.
            player.Bottom = step.PlayerFeet;
            player.velocity = step.PlayerVelocity;
            player.dead = false;
            // The recorded life, placed rather than simulated, for the same reason the velocity is.
            // A staged hostile that reaches the replayed player takes his life down every tick and
            // would kill him inside a few hundred ticks, and a dead player is a player the senses
            // stop treating as somebody to keep company with — a difference this instrument would
            // have introduced, not one the recording held.
            if (step.PlayerLife > 0) player.statLife = step.PlayerLife;

            Actors?.BeforeTheBrain(step.Tick);

            PrepareTheHeadlessEngine.AdvanceTheWorldClock();
            if (DriveLight) PrepareTheHeadlessEngine.DriveLightOnce(player.Bottom.ToTileCoordinates(), LightHalfWidth, LightHalfHeight);

            // A throw from inside a game path — and the engine throws from several, because this
            // host runs none of the game's startup — arrives as a stack trace with no tick on it,
            // and finding the tick costs another whole-capture run. Naming it here turns the next
            // round into `--from-tick=<tick minus a few hundred> --ticks=400`, which is seconds.
            try
            {
                companion.AI();
                PrepareTheHeadlessEngine.AdvanceTheNativeBody(companion);
            }
            catch (Exception failure)
            {
                var b = companion.Brain;
                throw new InvalidOperationException(
                    $"the run threw at recorded tick {step.Tick} (step {index} of {route.Count}), "
                    + $"last action {b.LastAction?.Name ?? "-"}, request {b.LastRequest.Kind}, "
                    + $"navigator {b.Navigator.Status}, body at {companion.NPC.position.X:0},{companion.NPC.position.Y:0}, "
                    + $"player at {player.Bottom.X:0},{player.Bottom.Y:0}", failure);
            }

            // After the companion's own tick, because the engine updates NPCs in slot order and the
            // companion is an early slot: the brain therefore decides against where each hostile was
            // at the end of the previous tick, which is what it does in the game.
            Actors?.AfterTheBrain(step.Tick, player);

            centres.Add(companion.NPC.Center);
            var brain = companion.Brain;
            var course = brain.Course;
            // Unconditional now rather than gated on the probe's file, because the window property it
            // accumulates is graded as a row and a row that only runs when an environment variable is
            // set is a row nobody runs. The per-tick file is still the gated half.
            if (course.Facts is { } observed)
                CountTheFrozenObservationByKind.Observe(step.Tick, observed,
                    brain.Senses.Intent.Region.Heading.ToTileCoordinates());
            int usable = 0, unresolved = 0;
            foreach ((string _, int domainUsable, int domainUnresolved, int _, string _) in course.Admitted)
            {
                usable += domainUsable;
                unresolved += domainUnresolved;
            }
            // What README's fight scenes would say about this tick, read off the brain's own senses
            // rather than recomputed: the region is the one every "how far from the player" measure
            // in the tree already reads, and the arrival forecast is the threat sense's own, with its
            // own belief about whether the hostile can reach that body at all.
            int inRegion = 0, atPlayer = 0, atCompanion = 0;
            float soonest = float.PositiveInfinity;
            foreach (var threat in brain.Senses.Threats.Threats)
            {
                if (threat.Npc is not { active: true } hostile || hostile.life <= 0) continue;
                if (brain.Senses.Intent.Region.Contains(hostile.Center)) inRegion++;
                if (threat.CanReachPlayer && threat.TicksToPlayer <= HarmHorizonTicks) atPlayer++;
                if (threat.CanReachCompanion && threat.TicksToCompanion <= HarmHorizonTicks) atCompanion++;
                if (threat.CanReachPlayer) soonest = MathF.Min(soonest, threat.TicksToPlayer);
            }

            play.Add(new PlayTick(step.Tick, course.Last.Reason, course.Last.Activity,
                course.Last.Binding is not null, course.Last.Settled, usable, unresolved,
                course.LastRefusals.Values.Sum(), new Dictionary<string, int>(course.LastRefusals),
                brain.DecideMs, brain.TotalMs, GC.CollectionCount(2),
                companion.Combat.LastFireOutcome == "fired",
                Actors?.HostilesAlive ?? 0, Actors?.DropsPresent ?? 0,
                brain.LastAction?.Name == "combat", inRegion, atPlayer, atCompanion, soonest,
                course.DecisionId, course.Facts?.Facts.Count ?? 0, DescribeLeaders(course.LastLeaders)));
            // Asked after the tick's resolve, because the reach flood is advanced by the
            // positioner's resolve rather than by the senses' own update, so asking before it would
            // read the previous tick's region under the previous tick's rules.
            claims.Add(brain.Senses.Reach.Reachable(player.Center.ToTileCoordinates()));
            inside.Add(brain.Senses.Intent.Region.Contains(companion.NPC.Center));
            // Connected as the brain decides on it: not proven cut off. An unanswered question does not take the body out of the region.
            connected.Add(!brain.Senses.Intent.CutOff);
            // The reach sense's verdicts are only given inside its known radius of the flood's root, and
            // the root trails a travelling body; these count the ticks the body itself sat outside that
            // radius, where nothing near it could be proven absent, and the ticks the flood read complete.
            if (!brain.Senses.Reach.WithinKnownRadius(live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.Tile(companion.NPC.Center)))
                ticksOutsideKnownRadius++;
            if (brain.Senses.Reach.Complete) ticksReachComplete++;
            bool fighting = brain.LastAction?.Name == "combat";
            string fireOutcome = companion.Combat.LastFireOutcome;
            // On him means near him and still standing: a retired zombie threatens nothing, and a
            // window that outlived the kill would grade the companion for not fighting a corpse.
            bool onPlayer = zombie != null && zombie.active
                && Vector2.Distance(zombie.Center, player.Center) <= OnPlayerPx;
            if (zombie != null)
            {
                combatCurrent.Add(fighting);
                fire.Add(fireOutcome);
                threatened.Add(onPlayer);
                // The retirement: five cumulative fired ticks prove the fight, and the instrument
                // strikes the kill itself because no headless tool simulates projectile damage. Set
                // directly rather than through StrikeNPC, whose death path writes dust through
                // cosmetic slots no decision reads; the row states the staging either way.
                if (fireOutcome == "fired")
                    firedTicks++;
                if (killStep < 0 && firedTicks >= FiredTicksToKill)
                {
                    killStep = index;
                    zombie.life = 0;
                    zombie.active = false;
                }
            }
            string fightSuffix = zombie == null ? ""
                : $"|fight {(fighting ? "combat" : "other")} {fireOutcome} {(onPlayer ? "threatened" : "calm")} {(killStep < 0 ? "alive" : "retired")}";
            trace.Add(string.Create(CultureInfo.InvariantCulture,
                $"{step.Tick}|{brain.LastAction?.Name ?? "-"}|{brain.LastRequest.Kind}|{companion.Motor.AppliedControls}|{brain.Navigator.Status}|{companion.NPC.position.X:R},{companion.NPC.position.Y:R}"
                + $"|reach {(brain.Senses.Reach.Complete ? "complete" : "growing")} {brain.Senses.Reach.CornerCount} {(brain.Senses.Reach.WithinKnownRadius(live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.Tile(companion.NPC.Center)) ? "in" : "out")} reroots {brain.Senses.Reach.Reroots} refloods {brain.Senses.Reach.Refloods}") + fightSuffix);
        }

        clock.Stop();
        var light = companion.Brain.Senses.Light;
        FightTrace? fight = zombie == null ? null
            : new FightTrace(combatCurrent, fire, threatened, killStep, firedTicks);
        return new Outcome(centres, trace, claims, inside, connected, route.Count, clock.Elapsed.TotalSeconds, worldSource,
            light.ReadTick, light.MeasuredSamples, light.AtCompanion, light.AtPlayer,
            ticksOutsideKnownRadius, ticksReachComplete, fight,
            live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.AuditDecisionContracts.Audited,
            live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.AuditDecisionContracts.ObservationsRead,
            new Dictionary<string, long>(live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.AuditDecisionContracts.Counts),
            play);
    }
}
