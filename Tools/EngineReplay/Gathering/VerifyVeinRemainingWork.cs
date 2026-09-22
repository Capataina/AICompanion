#nullable enable

extern alias live;

using System;
using System.Linq;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Activities.Gathering;
using live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>
/// What a vein's admission says when the handed pickaxe has no remaining-work estimate for it.
///
/// Every ore in the 22 September 2026 capture read <c>native-remaining-unresolved</c> on all 2,340 ticks
/// — 2,321 rows of the TSV carry the string, and the events sidecar's funnel reads
/// <c>mine-target=usable:0,unknown:10,unusable:0,reason:native-remaining-unresolved</c> for the whole
/// session, so ten to thirteen veins were found and not one of them was ever usable or ever refused. The
/// capture cannot say why: it carries no pickaxe fact of any kind, and the one reason it does carry is the
/// one this file exists to remove, because it is the reason that erases every other.
///
/// <para>The mechanism is that a null estimate was read as an unanswered question. <c>TileMiner.
/// EstimateRemaining</c> answers null on exactly five conditions — outside the world, no longer ore, inside a
/// protected companion home, a tile the game refuses to kill, and a pickaxe with no power against it — and
/// every one of those is something observed rather than something a bounded search ran out of time on. The
/// admission ladder above it had already computed the precise refusal (<c>pickaxe-cannot-damage</c>, say) and
/// then overwrote it with <c>unknown</c>, which under this tree's standing rule that optional work does not
/// start on an unanswered search leaves the vein neither startable nor refusable: unknown is the one
/// admission a census can never resolve by looking harder at a fact that will not change.</para>
///
/// <para>The rows are a pair on purpose. The weak pick is the defect and the adequate pick is the control,
/// because an implementation that answered <c>unusable</c> to everything would satisfy the first alone.</para>
/// </summary>
internal static class VerifyVeinRemainingWork
{
    public static int Run()
        => RunOneRow.Case("G08 an ore the handed pickaxe can damage carries a remaining estimate when it is admitted",
               AnAdequatePickResolvesTheVein)
         + RunOneRow.Case("G08 a vein the handed pickaxe cannot damage is refused by name rather than left unknown",
               AWeakPickRefusesRatherThanWonders);

    /// <summary>
    /// The control, and the property the lane was asked to establish: copper under a copper pickaxe, three
    /// connected tiles so the vein is a vein rather than one cell, admitted on the tick it is captured with a
    /// remaining estimate that is a real number.
    /// </summary>
    private static void AnAdequatePickResolvesTheVein()
    {
        GatheringOpportunityFact vein = CaptureOneVein(TileID.Copper);
        Require(vein.Admission == "usable", $"copper under a copper pickaxe was admitted '{vein.Admission}' ({vein.Reason})");
        Require(vein.Reason == "observed-native-ore", $"an admitted copper vein gave the reason '{vein.Reason}'");
        Require(vein.Work is { DamageRemaining: > 0 },
            "an admitted vein carried no captured tool work, so nothing downstream can price the swing");
        Require(vein.RemainingAmount > 0,
            $"an admitted three-tile copper vein reported {vein.RemainingAmount} remaining damage");
    }

    /// <summary>
    /// The defect. Meteorite needs a pick power of 50 and the companion is handed a copper pickaxe at 35, so
    /// <c>CanMine</c> is false and the ladder's own answer is <c>unusable / pickaxe-cannot-damage</c> — a
    /// proven refusal about a tool and a tile, which is exactly the kind of answer a census is supposed to
    /// be able to reach. What a reader of the capture got instead was <c>unknown /
    /// native-remaining-unresolved</c>, which names no tile, no tool and no condition.
    /// </summary>
    private static void AWeakPickRefusesRatherThanWonders()
    {
        GatheringOpportunityFact vein = CaptureOneVein(TileID.Meteorite);
        Require(vein.Reason != "native-remaining-unresolved",
            "a vein whose every tile the handed pickaxe provably cannot damage still reports "
            + "'native-remaining-unresolved', which is the capture's own signature: a proven refusal reported as an "
            + "open question, so the vein can never be started and can never be written off");
        Require(vein.Reason == "pickaxe-cannot-damage",
            $"the refusal was named '{vein.Reason}' rather than naming the tool that cannot do the work");
        Require(vein.Admission == "unusable",
            $"a provably undiggable vein was admitted '{vein.Admission}'; only an unanswered question is unknown");
    }

    /// <summary>
    /// Three connected cells of one ore type on a floor, captured through the real native census with the
    /// real handed pickaxe. Three rather than one because the estimate is taken per tile and summed, so a
    /// single-cell scene cannot tell a whole-vein answer from a per-tile one.
    /// </summary>
    private static GatheringOpportunityFact CaptureOneVein(ushort ore)
    {
        (_, ActionContext context) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, ore,
            new(25, 59), new(26, 59), new(27, 59));
        var capture = new CaptureGatheringOpportunities();
        DecisionFact[] facts = capture.Capture(context, new(double.PositiveInfinity)).ToArray();
        DecisionFact[] sites = facts.Where(fact => fact.Key.Kind == "mine-target").ToArray();
        Require(sites.Length == 1,
            $"the scene must present exactly one vein for the row to be about, and it presented {sites.Length}");
        GatheringOpportunityFact vein = JsonSerializer.Deserialize<GatheringOpportunityFact>(sites[0].Value.Text)!;
        // A premise rather than an assertion: if the flood did not join the three cells, every row below is
        // about a one-tile vein and says nothing about the per-tile sum, while still passing or failing for
        // reasons that look like the subject.
        Require(vein.Detail.Contains("vein-complete=True", StringComparison.Ordinal),
            $"the scene's vein census did not finish, so its admission is about an unfinished scan: {vein.Detail}");
        return vein;
    }

    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
