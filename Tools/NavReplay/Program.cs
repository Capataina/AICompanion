#nullable enable

using System;
using System.Globalization;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Tools.Ledger;

// The game-free side of the movement core: the corner graph, the flood, the route search, the
// smoother and the steering law compiled straight out of the mod tree and run over text worlds
// with no game. Three things live here now. The self-test, which is the ledger's own row set for
// this tool — the corridor-middle measurement and the corpus mirror's exactness. The extractor,
// which cuts a scenario file out of a capture at a tick. And the mirror transform the extractor's
// output can be reflected through. The walker's replays — one block at a time, jump comparisons,
// contract fixtures, the failing-scenario reducer — went with the walker; a scenario is replayed
// against the orb by Tools/WorldRun, which drives the whole brain.
// Exit code: 0 when every self-test case passed, 1 otherwise.

// The portable tier's reset. This process holds no game: what a case here can leave behind is the
// shared planning allowance, the search's world override, the clearance field and the census.
EmitLedgerRows.ResetBeforeCase = keepProductionAllowances =>
{
    LimitPlanningWork.Unbounded = !keepProductionAllowances;
    LimitPlanningWork.End();
    FreeSpaceSearch.WorldOverride = null;
    OrbTerrain.Immunity = LiquidImmunity.None;
    ClearanceField.Shared.Invalidate();
    BehaviourCensus.Reset();
};

if (args.Length == 1 && args[0] == "--self-test")
    return EmitLedgerRows.Case("nav-replay", "NavReplay", "the route through a corridor sits nearer its middle than its walls, and the steered body stays there",
            VerifyCorridorMiddle.Run,
            killedBy: "an edge cost that stops pricing clearance, a smoother that skips from one wall-hugging end to the other, or steering that cuts the corner back to the wall")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "a body the navigator has called arrived stays arrived, on one search, and comes to rest",
            VerifyArrivalHolds.Run,
            killedBy: "steering that brakes too late for the arrival radius, or an arrival that coasts out and replans")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "a hover anchor never pulls the body back behind a wall, and a dodge never flies it into lava",
            VerifyAnchorsAndEvade.Run,
            killedBy: "a wait anchor that outlives its goal or survives the body being carried across a wall, or an evade simulation that runs the solid contact without asking about liquid")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "the body flies toward a goal while its search runs, and a finished search is not run again for an unchanged goal",
            VerifyNavigatorKeepsMoving.Run,
            killedBy: "a navigator that hovers until a search returns, restarts a search that hit its node limit or proved an absence, or keeps a proven absence to itself instead of handing the goal back")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "the corpus mirror is exact",
            VerifyMirrorExactness.Run,
            killedBy: "a reflection about the wrong column, an unpadded short row, or a dropped glyph flip");

if (args.Length >= 3 && args[0] == "--extract-scenario" && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tick))
{
    int width = 64, height = 40;
    for (int i = 3; i + 1 < args.Length; i++)
        if (args[i] == "--size" && args[i + 1].Split('x') is [var sw, var sh] && int.TryParse(sw, out int swi) && int.TryParse(sh, out int shi))
        { width = swi; height = shi; }
    ExtractScenarioFromCapture.Extract cut = ExtractScenarioFromCapture.Run(args[1], tick, width, height, null, Console.WriteLine);
    Console.WriteLine($"extracted {cut.Path}");
    return 0;
}

Console.WriteLine("usage: dotnet run --project Tools/NavReplay -- --self-test");
Console.WriteLine("       dotnet run --project Tools/NavReplay -- --extract-scenario <capture.tsv> <tick> [--size WxH]");
return 2;
