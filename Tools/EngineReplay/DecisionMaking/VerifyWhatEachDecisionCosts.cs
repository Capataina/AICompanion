extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;

/// <summary>
/// What one decision costs to assemble and to carry, measured in operations and bytes rather than in
/// milliseconds.
///
/// The play of 0.38.13 ran at 38 updates a second against 60, the brain took 39% of the session's wall
/// clock and `decide` was 90% of the brain. Two things scaled with it. The frozen observation's fact
/// count climbed from 150 to 1,603 in thirty-three seconds and never fell, and every fact was serialised
/// to JSON and SHA-256 digested at construction whether or not anything ever read either. The session
/// counted 813 gen-0, 357 gen-1 and 50 gen-2 collections in a minute, with the allocation rate roughly
/// doubling as the fact count grew, and 29 of the 37 worst frames coinciding with a gen-2 collection at
/// 45 to 70 ms of brain each.
///
/// Bytes and counts rather than milliseconds is deliberate and is this repository's own rule: the suite
/// runs about 2.3 times slower per operation than a standalone run, so a wall-clock threshold here would
/// test how far through the suite the row sits as much as the code under it.
/// </summary>
internal static class VerifyWhatEachDecisionCosts
{
    public static int Run()
    {
        int red = 0;
        red += Row("a fact carries no digest until something asks for one", AFactCostsNothingUntilRead);
        red += Row("the frozen observation does not grow with the ground the player has covered", TheObservationStaysBounded);
        return red;
    }

    /// <summary>The ore seam is left in the tile map, which the per-case reset rebuilds, but the work
    /// policy this scene switches on is a process static the reset does not know about — so it goes back
    /// here rather than travelling to the next case.</summary>
    private static int Row(string name, Action test)
    {
        live::AICompanion.Companion.Brain.Activities.WorkPolicy policy
            = live::AICompanion.Companion.Brain.Activities.WorkPolicies.Mining;
        try { test(); Console.WriteLine("  GREEN " + name); return 0; }
        catch (Exception error) { Console.WriteLine("  RED " + name + ": " + error.Message); return 1; }
        finally { live::AICompanion.Companion.Brain.Activities.WorkPolicies.Mining = policy; }
    }

    /// <summary>
    /// The per-fact cost of assembling an observation, in bytes allocated.
    ///
    /// Two thousand facts is the size the play reached, and the measurement is what a snapshot costs
    /// before anybody reads anything out of it — which is the ordinary case, because a decision reads a
    /// bounded slice and carries the rest. The bound is stated as bytes per fact so it says something a
    /// reader can act on rather than being a number tied to the loop count.
    /// </summary>
    private static void AFactCostsNothingUntilRead()
    {
        const int facts = 2_000;
        // The keys and values are built first and outside the measurement, because a capture composes
        // facts out of values it already holds and the strings are the capture's cost rather than the
        // fact's. One pass is then thrown away: the JSON serialiser and the hash both warm reflection and
        // caches on first use, and charging that here measures start-up.
        FactKey[] keys = Enumerable.Range(0, facts)
            .Select(i => new FactKey("mine-target", "tile:1:" + i, i)).ToArray();
        FactValue[] values = Enumerable.Range(0, facts)
            .Select(i => new FactValue(i, i * 16, i * 16, "{\"site\":" + i + "}")).ToArray();
        Build(keys, values, 50).Last().Digest.GetHashCode();

        long before = GC.GetTotalAllocatedBytes(precise: true);
        DecisionFact[] built = Build(keys, values, facts);
        long assembling = GC.GetTotalAllocatedBytes(precise: true) - before;

        long beforeDigest = GC.GetTotalAllocatedBytes(precise: true);
        _ = built[0].Digest;
        long oneDigest = GC.GetTotalAllocatedBytes(precise: true) - beforeDigest;

        double perFact = assembling / (double)facts;
        Console.WriteLine($"  per-fact cost: assembling {facts} facts allocated {assembling:N0} bytes "
            + $"({perFact:0} per fact); one digest afterwards cost {oneDigest:N0} bytes");

        // Measured 22 September 2026 on this machine: 112 bytes a fact lazily against 1,387 eagerly, a
        // JSON string, its UTF-8 bytes, a hash and a hex string per fact nobody had asked about. At the
        // play's 1,600 facts and thirty-eight decisions a second that difference is about two megabytes
        // a decision and seventy-seven a second, which is the allocation rate behind the session's 50
        // gen-2 collections. The bound sits well above the lazy figure and well below the eager one, so
        // it bounds the class of mistake rather than fingerprinting one allocator.
        Require(perFact < 400,
            $"assembling an observation costs {perFact:0} bytes a fact before anything reads one, which is the "
            + $"eager digest: {facts} facts allocated {assembling:N0} bytes");
        Require(oneDigest > 0,
            "asking for a digest allocated nothing, so the digest is not being computed and this row is "
            + "measuring a field that no longer exists");

        // Laziness must not have changed what a digest is. Two facts built the same way agree, two built
        // differently do not, and a fact's digest is stable across reads.
        var one = new DecisionFact(new FactKey("kind", "identity", 3), 7, new FactValue(1, 2, 3, "text"), FactEvidence.Observed);
        var same = new DecisionFact(new FactKey("kind", "identity", 3), 7, new FactValue(1, 2, 3, "text"), FactEvidence.Observed);
        var other = new DecisionFact(new FactKey("kind", "identity", 3), 8, new FactValue(1, 2, 3, "text"), FactEvidence.Observed);
        Require(one.Digest == same.Digest && one.Digest == one.Digest,
            $"two identical facts no longer digest alike; {one.Digest} against {same.Digest}");
        Require(one.Digest != other.Digest,
            $"a changed version no longer changes the digest; both read {one.Digest}");
        Require(one.SameObservationAs(same) && !one.SameObservationAs(other),
            "the field comparison that replaced the digest comparison disagrees with the digest");
    }

    private static DecisionFact[] Build(FactKey[] keys, FactValue[] values, int count)
    {
        var built = new DecisionFact[count];
        for (int i = 0; i < count; i++) built[i] = new DecisionFact(keys[i], i, values[i], FactEvidence.Observed);
        return built;
    }

    /// <summary>
    /// A declared bound on the frozen observation, and this row is a guard rather than a repair — say so
    /// rather than letting a green read as a fix.
    ///
    /// The play's observation grew tenfold and never fell, and no scene reachable from this harness
    /// reproduces that. A seventy-tile ore seam walked end to end peaks at 46 facts and *falls* to 29 as
    /// the census window leaves the ground behind; a crowd of eight hostiles peaks at 54, with the combat
    /// front contributing at most 36 use facts. What grew in play is therefore neither the mining census
    /// nor the combat front, and it is most likely the honest size of a real world's light and ore
    /// censuses rather than an accumulation — which is a claim about what was eliminated, not an answer.
    ///
    /// What the row does hold is the property the growth would violate if it is ever a defect: the count
    /// does not rise monotonically with the ground the player covers.
    /// </summary>
    private static void TheObservationStaysBounded()
    {
        ActionContext ctx = OreSeam();
        DecideCourseEachTick owner = ctx.Companion.Brain.Course;
        var counts = new List<int>();
        for (int tick = 0; tick < 1200; tick++)
        {
            ctx.Player.velocity = new Vector2(2f, 0f);
            ctx.Player.controlRight = true;
            ctx.Player.position += ctx.Player.velocity;
            Tick(ctx);
            if (owner.Facts is { } facts) counts.Add(facts.Facts.Count);
        }
        int first = counts.Take(100).Max(), last = counts.Skip(counts.Count - 100).Max();
        Require(last <= first,
            $"the observation grew with the ground covered: {first} facts in the first hundred ticks against "
            + $"{last} in the last hundred, over a seam the player walked end to end");
        Console.WriteLine($"  observation size over 1,200 ticks of walking: first hundred max {first}, "
            + $"last hundred max {last}, whole-run min {counts.Min()} max {counts.Max()}");
    }

    /// <summary>A long seam of ore under a floor the player can walk along, which is the shape that makes
    /// the mining census sweep new ground: its window is carried ahead of the player.</summary>
    private static ActionContext OreSeam()
    {
        var vein = new List<Point>();
        for (int x = 30; x < 92; x += 2) vein.Add(new Point(x, 91));
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, vein.ToArray());
        foreach (Projectile projectile in Main.projectile) projectile.active = false;
        ctx.Player.Bottom = new Vector2(20 * 16, 92 * 16);
        ctx.Npc.Bottom = new Vector2(20 * 16, 91 * 16);
        ctx.Senses.Update(ctx.Npc, ctx.Player);
        return ctx;
    }

    /// <summary>The whole tick and then the engine's own move, because a brain tick alone writes a
    /// velocity and nothing integrates it: a body that never travels never reaches the seam, and a
    /// mining row on a body that never arrives measures an intention.</summary>
    private static void Tick(ActionContext ctx)
    {
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
        VerifyResponsiveFollowing.AdvanceNative(ctx.Companion);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
