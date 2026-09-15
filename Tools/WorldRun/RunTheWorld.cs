extern alias live;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
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
        /// <summary>
        /// The orb's liquid immunities, read off the live body inside the run because the body is
        /// gone by the time the scorer runs: `CompanionNPC.Find()` answered null on every checkpoint
        /// of the only instrument that asked it, so a rule reading the flags there was reading a default.
        /// </summary>
        bool ImmuneToWater,
        bool ImmuneToLava,
        /// <summary>Ticks the body's own tile sat outside the reach sense's known radius, so nothing near it could be proven absent.</summary>
        int TicksOutsideKnownRadius,
        /// <summary>Ticks the reach flood read complete.</summary>
        int TicksReachComplete)
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

    public static Outcome Play(ReadRecordedRoute.Route route, string worldSource, int seed)
    {
        // Unbounded planning, for the reason the suite already established and enforces: the live
        // allowances are a wall clock, so how much search fits in a tick depends on what else the
        // machine is doing, and a run compared against another run under them is comparing two
        // afternoons. The fixtures that test the deadline itself are the exception, and this is not
        // one of them.
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
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

        var centres = new List<Vector2>(route.Count);
        var trace = new List<string>(route.Count);
        var claims = new List<live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict>(route.Count);
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

            centres.Add(companion.NPC.Center);
            var brain = companion.Brain;
            // Asked after the tick's resolve, because the reach flood is advanced by the
            // positioner's resolve rather than by the senses' own update, so asking before it would
            // read the previous tick's region under the previous tick's rules.
            claims.Add(brain.Senses.Reach.Reachable(player.Center.ToTileCoordinates()));
            // The reach sense's verdicts are only given inside its known radius of the flood's root, and
            // the root trails a travelling body; these count the ticks the body itself sat outside that
            // radius, where nothing near it could be proven absent, and the ticks the flood read complete.
            if (!brain.Senses.Reach.WithinKnownRadius(live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.Tile(companion.NPC.Center)))
                ticksOutsideKnownRadius++;
            if (brain.Senses.Reach.Complete) ticksReachComplete++;
            trace.Add(string.Create(CultureInfo.InvariantCulture,
                $"{step.Tick}|{brain.LastAction?.Name ?? "-"}|{brain.LastRequest.Kind}|{companion.Motor.AppliedControls}|{brain.Navigator.Status}|{companion.NPC.position.X:R},{companion.NPC.position.Y:R}"
                + $"|reach {(brain.Senses.Reach.Complete ? "complete" : "growing")} {brain.Senses.Reach.CornerCount} {(brain.Senses.Reach.WithinKnownRadius(live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.Tile(companion.NPC.Center)) ? "in" : "out")} reroots {brain.Senses.Reach.Reroots} refloods {brain.Senses.Reach.Refloods}"));
        }

        clock.Stop();
        var light = companion.Brain.Senses.Light;
        return new Outcome(centres, trace, claims, route.Count, clock.Elapsed.TotalSeconds, worldSource,
            light.ReadTick, light.MeasuredSamples, light.AtCompanion, light.AtPlayer,
            companion.ImmuneToWater, companion.ImmuneToLava, ticksOutsideKnownRadius, ticksReachComplete);
    }
}
