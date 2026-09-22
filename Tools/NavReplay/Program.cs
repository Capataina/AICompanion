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
// shared planning allowance, the search's world override, the clearance field, the census and the
// published enemy boxes. The last is a fact one component writes for another to read within a tick,
// so whichever case last wrote it decides what every later case's stand generator and park climb away
// from; the engine tier's reset clears it for the same reason (ResetProcessState.cs), and the fixtures
// here that cleared it themselves were each fixing the one reader somebody remembered.
EmitLedgerRows.ResetBeforeCase = keepProductionAllowances =>
{
    LimitPlanningWork.Unbounded = !keepProductionAllowances;
    LimitPlanningWork.End();
    FreeSpaceSearch.WorldOverride = null;
    ClearanceField.Shared.Invalidate();
    BehaviourCensus.Reset();
    MovementQueries.Hazards = Array.Empty<Microsoft.Xna.Framework.Rectangle>();
};

if (args.Length == 1 && args[0] == "--self-test")
    return EmitLedgerRows.Case("nav-replay", "NavReplay", "the route through a corridor sits nearer its middle than its walls, and the steered body stays there",
            VerifyCorridorMiddle.Run,
            killedBy: "an edge cost that stops pricing clearance, a smoother that skips from one wall-hugging end to the other, or steering that cuts the corner back to the wall")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "a body the navigator has called arrived stays arrived, on one search, and comes to rest",
            VerifyArrivalHolds.Run,
            killedBy: "steering that brakes too late for the arrival radius, or an arrival that coasts out and replans")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "a hover anchor never pulls the body back behind a wall, and a dodge between two shots is free to use the lava below them",
            VerifyAnchorsAndEvade.Run,
            killedBy: "a wait anchor that outlives its goal or survives the body being carried across a wall, or a dodge that still treats liquid as something to stay out of")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "a dodge bends the job's flight only where the job's own flight would meet harm, and never presses the body into a wall",
            VerifyEvadeKeepsTheJob.Run,
            killedBy: "a keep test that holds one velocity in a straight line instead of flying the job's own steering, or a heading that goes nowhere scored above a stop")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "an accompanying body is bent only where its own walk across the region would be hit, and kept everywhere else",
            VerifyEvadeBendsAccompanying.Run,
            killedBy: "an accompanying walk that never declares itself the tick's producer, so the keep test flies an earlier request's steering, or a forecast that holds the walking target still")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "under sustained fire a dodge takes no more hits than the job alone and never keeps the body from arriving",
            VerifyEvadeUnderSustainedFire.Run,
            killedBy: "a dodge that only postpones a hit it cannot avoid, so a body in a corridor flees ahead of each shot and never reaches its goal")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "the accompanying target starts where the body is and never moves faster than the walk",
            VerifyAccompanyingTargetIsContinuous.Run,
            killedBy: "walk state that outlives a request which was not accompanying, a first place clamped into the open part of the box, or the open part of the box snapping when the player's lead changes side")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "the accompanying walk climbs off a floor toward clearer air",
            VerifyAccompanyPrefersClearance.Run,
            killedBy: "a walk that only reflects off walls and never reads combined clearance among its steps")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "the accompanying walk tours what it can reach rather than living on a wall inside its box",
            VerifyAccompanyGetsPastAnObstruction.Run,
            killedBy: "a tour that draws every leg from the far edge of the box whether or not the body has a way to it, so a wall standing inside the region holds the body against its face")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "the accompanying walk draws no leg below the floor its caller names, which in the game is the top of his head",
            VerifyAccompanyStaysAboveHisHead.Run,
            killedBy: "a tour drawing its legs from the whole box, whose lower part is the player's legs and the ground he stands on, so the companion keeps diving at his feet")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "a leg drawn from the whole box while the player travels stays inside his box",
            VerifyTheWideLegUnderATravellingPlayer.Run,
            killedBy: "a wide leg whose widened bounds and the leg they belong to come apart, so the walk is clamped against one part of the box while aiming at another")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "the tour's redraw under a footprint the player never lifts costs the same work every tick",
            VerifyTheEscapeCostsTheSameEveryTick.Run,
            killedBy: "a redraw whose per-tick work grows with how long the player holds the block, which is the half of that cost the code rather than the player decides")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "the body flies toward a goal while its search runs, and a finished search is not run again for an unchanged goal",
            VerifyNavigatorKeepsMoving.Run,
            killedBy: "a navigator that hovers until a search returns, restarts a search that hit its node limit or proved an absence, or keeps a proven absence to itself instead of handing the goal back")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "a kept conclusion about a goal is forgotten once its goal has moved or its terrain changed, and only a finished search settles the body short",
            VerifyNavigatorConclusions.Run,
            killedBy: "a conclusion compared against last tick's goal rather than the goal it concluded about, an edit that no longer invalidates a proven absence, a settled-short answer inferred from a missing conclusion, or a settled-short answer never kept")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "the corpus mirror is exact",
            VerifyMirrorExactness.Run,
            killedBy: "a reflection about the wrong column, an unpadded short row, or a dropped glyph flip")
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "a cut scenario says in its own header why its window was cut, without moving what the header already meant",
            VerifyTheCutReasonRidesInTheHeader.Run,
            killedBy: "a reason written ahead of the provenance instead of after it, so free text holding the word goal is read as the goal");

if (args.Length >= 3 && args[0] == "--extract-scenario" && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tick))
{
    int width = 64, height = 40;
    string? reason = null;
    for (int i = 3; i + 1 < args.Length; i++)
    {
        if (args[i] == "--size" && args[i + 1].Split('x') is [var sw, var sh] && int.TryParse(sw, out int swi) && int.TryParse(sh, out int shi))
        { width = swi; height = shi; }
        // Why this window was worth cutting, carried into the fixture's own header. A committed
        // scenario outlives its session and the file is all a later reader has, so a reason recorded
        // only in a folder guide is the half that goes stale.
        if (args[i] == "--reason") reason = args[i + 1];
    }
    ExtractScenarioFromCapture.Extract cut = ExtractScenarioFromCapture.Run(args[1], tick, width, height, null, Console.WriteLine, reason);
    Console.WriteLine($"extracted {cut.Path}");
    return 0;
}

Console.WriteLine("usage: dotnet run --project Tools/NavReplay -- --self-test");
Console.WriteLine("       dotnet run --project Tools/NavReplay -- --extract-scenario <capture.tsv> <tick> [--size WxH] [--reason \"<why this window>\"]");
return 2;
