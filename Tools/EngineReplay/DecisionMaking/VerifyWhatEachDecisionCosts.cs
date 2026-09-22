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
        red += Row("a fact costs its own fields and never a serialise nobody asked for", AFactCostsNothingUntilRead);
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
        Serialised(Build(keys, values, 50).Last());

        long before = GC.GetTotalAllocatedBytes(precise: true);
        DecisionFact[] built = Build(keys, values, facts);
        long assembling = GC.GetTotalAllocatedBytes(precise: true) - before;

        // What the bound excludes, measured rather than named, because a bound stated as an absolute can
        // pass by the facts having got smaller. Serialising one fact is what the constructor used to do
        // to every fact, so the row asserts the pair: the per-fact cost is under the bound *and* it is
        // below the cost of the one operation it is claiming not to pay.
        long beforeOne = GC.GetTotalAllocatedBytes(precise: true);
        Serialised(built[0]);
        long oneSerialise = GC.GetTotalAllocatedBytes(precise: true) - beforeOne;

        double perFact = assembling / (double)facts;
        // Through the emitter rather than the console, because a measurement only a terminal saw is a
        // measurement the run file cannot be asked for afterwards.
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"  per-fact cost: assembling {facts} facts allocated "
            + $"{assembling:N0} bytes ({perFact:0} per fact); serialising one of them afterwards cost "
            + $"{oneSerialise:N0} bytes");

        // Measured 22 September 2026 on this machine: 112 bytes a fact against the 1,387 the constructor
        // charged while it serialised and hashed every fact — a JSON string, its UTF-8 bytes, a hash and
        // a hex string per fact nobody had asked about. The play of 0.38.13 carried 1,603 facts at about
        // thirty-eight decisions a second, so that difference **extrapolates** to roughly two megabytes a
        // decision, which is an arithmetic consequence of this row's per-fact figure and a fact count
        // from a capture nobody re-ran, not a rate anybody observed. The bound sits well above the
        // current figure and well below the old one, so it bounds the class of mistake rather than
        // fingerprinting one allocator.
        Require(perFact < 400,
            $"assembling an observation costs {perFact:0} bytes a fact before anything reads one, which is "
            + $"the serialise-per-fact the constructor used to do: {facts} facts allocated {assembling:N0} bytes");
        Require(oneSerialise > perFact,
            $"serialising one fact cost {oneSerialise:N0} bytes against {perFact:0} to assemble one, so the "
            + "operation this row claims a fact does not pay for is no longer more expensive than the fact "
            + "— the bound above would pass against a constructor that serialises");

        // The field comparison is the only equality a fact has now: two facts built the same way agree and
        // two built differently do not.
        var one = new DecisionFact(new FactKey("kind", "identity", 3), 7, new FactValue(1, 2, 3, "text"), FactEvidence.Observed);
        var same = new DecisionFact(new FactKey("kind", "identity", 3), 7, new FactValue(1, 2, 3, "text"), FactEvidence.Observed);
        var other = new DecisionFact(new FactKey("kind", "identity", 3), 8, new FactValue(1, 2, 3, "text"), FactEvidence.Observed);
        Require(one.SameObservationAs(same),
            "two facts carrying one observation no longer compare equal");
        Require(!one.SameObservationAs(other),
            "a changed version no longer changes the comparison, so a stale read would match a moved world");
    }

    /// <summary>One fact in the canonical form the constructor used to build for every fact, kept here
    /// rather than on <c>DecisionFact</c> because it is the cost this row measures against and nothing in
    /// production wants it.</summary>
    private static string Serialised(DecisionFact fact)
        => System.Text.Json.JsonSerializer.Serialize(new { fact.Key, fact.Version, fact.Value, fact.Evidence });

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
