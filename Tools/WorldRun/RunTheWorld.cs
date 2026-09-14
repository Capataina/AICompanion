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
/// The output is a track and a trace. The track is where the body went, which is what the recorded
/// comparison and the checkpoint scorer both measure. The trace is one line per tick naming the
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
        IReadOnlyList<Vector2> CompanionFeet,
        IReadOnlyList<string> Trace,
        /// <summary>
        /// What the planner said about the tile the player was standing on, on the tick they stood
        /// there. This is the "planner claim" half of the checkpoint matrix, and it is taken here
        /// rather than recomputed afterwards for two reasons: it is free, because the flood it
        /// reads has already been run for this tick's decisions, and it is the claim the brain
        /// actually held at the time rather than one reconstructed from a later state.
        /// </summary>
        IReadOnlyList<live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict> PlannerClaim,
        int Ticks,
        double Seconds,
        string WorldSource)
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
        PrepareTheHeadlessEngine.PrepareLightServices();

        // The companion starts where the recording had it, which is what makes a run from a later
        // tick meaningful at all: the source has moved since these captures, so a run from tick one
        // has diverged long before it reaches anything worth asking about, and seeding the body at
        // the recorded pose is the only way to ask what this build does at that place.
        var companion = PrepareTheHeadlessEngine.AttachCompanion(
            new Vector2(opening.CompanionLeftBottom.X, opening.CompanionLeftBottom.Y),
            opening.PlayerFeet);
        // The recorded left edge is the body's left, not its centre, and the two differ by half a
        // body; placing a centre where a left edge belongs starts the run half a body out.
        companion.NPC.Bottom = new Vector2(opening.CompanionLeftBottom.X + companion.NPC.width / 2f, opening.CompanionLeftBottom.Y);

        Player player = Main.player[0];
        PrepareTheHeadlessEngine.WarmTheLightEngine(player.Bottom.ToTileCoordinates(), LightHalfWidth, LightHalfHeight);

        var feet = new List<Vector2>(route.Count);
        var trace = new List<string>(route.Count);
        var claims = new List<live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict>(route.Count);
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
            PrepareTheHeadlessEngine.DriveLightOnce(player.Bottom.ToTileCoordinates(), LightHalfWidth, LightHalfHeight);

            companion.AI();
            PrepareTheHeadlessEngine.AdvanceTheNativeBody(companion);

            feet.Add(companion.NPC.Bottom);
            var brain = companion.Brain;
            // Asked after the tick's resolve, because the reach flood is advanced by the
            // positioner's resolve rather than by the senses' own update, so asking before it would
            // read the previous tick's region under the previous tick's rules.
            claims.Add(brain.Senses.Reach.Reachable(player.Bottom.ToTileCoordinates()));
            trace.Add(string.Create(CultureInfo.InvariantCulture,
                $"{step.Tick}|{brain.LastAction?.Name ?? "-"}|{brain.LastRequest.Kind}|{companion.Motor.AppliedControls}|{brain.Navigator.Status}|{companion.NPC.position.X:R},{companion.NPC.position.Y:R}"));
        }

        clock.Stop();
        return new Outcome(feet, trace, claims, route.Count, clock.Elapsed.TotalSeconds, worldSource);
    }
}
