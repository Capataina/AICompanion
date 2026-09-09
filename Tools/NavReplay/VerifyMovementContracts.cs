using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.SharedMovementSystem;

internal static class VerifyMovementContracts
{
    public static int Run()
    {
        var world = new FloorWorld();
        NavGrid.World = world;
        AStar.InvalidateEdges();
        var live = new BodyState(100, 160, 0, 0, true);
        var local = new PlanLocalMovement();
        var target = new Vector2(300, 160);
        Require(local.Choose(world, live, target, new Controls(4, Jump: true), MovementCapabilities.Basic, (_, _) => true) == Controls.None,
            "all unsafe candidates must not execute the preferred jump");
        Controls first = local.Choose(world, live, target, new Controls(4), MovementCapabilities.Basic);
        live = BodyMotion.Step(world, live, first);
        Require(local.Choose(world, live, target, new Controls(4), MovementCapabilities.Basic, (_, _) => true) == Controls.None,
            "new threats invalidate retained repair controls");

        var capabilities = new MovementCapabilities(AirJumpCount: 2);
        var airborne = new BodyState(100, 100, 0, 1, false, Mobility: new MobilityState(2), Capabilities: capabilities);
        var one = BodyMotion.Step(world, airborne, new Controls(1, Jump: true));
        Require(one.Mobility.AirJumpsLeft == 1 && one.Capabilities == capabilities, "an air jump consumes one resource and preserves the kit");
        var two = BodyMotion.Step(world, one, new Controls(1, Jump: true));
        Require(two.Mobility.AirJumpsLeft == 0 && two.Capabilities == capabilities, "a chained jump consumes the remaining resource");
        var dry = BodyMotion.Step(world, live with { Wet = true }, Controls.None);
        Require(!dry.Wet, "leaving liquid cannot retain wet state forever in text replay");

        var policy = new CountingTraversal();
        var step = new NavStep(new Point(20, 9), MoveKind.Walk);
        var execution = new TraversalExecution(policy, step, null);
        var copy = execution.Copy();
        Controls copyFirst = copy.NextControls(live, out _);
        copy.NextControls(live, out _);
        Require(execution.NextControls(live, out _) == copyFirst, "probing a copied macro must not advance live policy state");
        Require(execution.Ticks == 1 && copy.Ticks == 2, "macro copies own their clocks");

        var walk = new WalkTraversal();
        walk.Begin(step);
        TraversalFault fault = TraversalFault.None;
        for (int tick = 0; tick < 60; tick++) fault = walk.Check(live, step, tick);
        Require(fault == TraversalFault.Stuck, "a walk with no displacement must report a fault");

        AStar.MsBudget = 0;
        AStar.Find(new Point(6, 9), new Point(80, 9), 0, out _, out var stop);
        Require(stop == AStar.SearchStopReason.ExpansionBudget, "a spent search budget is not exhausted terrain");
        AStar.MsBudget = .000001;
        AStar.Find(new Point(6, 9), new Point(80, 9), 10000, out _, out stop);
        Require(stop == AStar.SearchStopReason.Deadline, "a search deadline is not exhausted terrain");
        AStar.MsBudget = 0;

        var navigator = new Navigator();
        live = new BodyState(100, 160, 0, 0, true);
        navigator.MoveTo(live, target);
        int beforeInterrupt = navigator.EdgeCount;
        navigator.Interrupt(live);
        Require(navigator.EdgeCount == beforeInterrupt + 1 && navigator.LastEdge?.Outcome == TraversalFault.Interrupted,
            "an active movement attempt must record its interruption");
        navigator.Clear();
        Require(navigator.EdgeCount == beforeInterrupt + 1, "clearing an interrupted attempt must not report it twice");
        navigator = new Navigator();
        AStar.Avoid.Clear();
        for (int tick = 0; tick < 150; tick++) navigator.MoveTo(live, target);
        Require(navigator.FaultCount > 0, "replanning cannot hide a stationary body's failed movement request");
        Require(navigator.EdgeCount >= navigator.FaultCount, "retrying a failed edge starts a separately recorded attempt");
        Require(AStar.Avoid.Count == 0, "navigator-local failure prices must not leak into the caller's obstacle list");

        var immobile = new CountingStillWorld();
        var rejectedExecution = new TraversalExecution(new WalkTraversal(), step, null);
        local = new PlanLocalMovement();
        Require(!local.TryExecute(immobile, rejectedExecution, live, null, out _, out _), "an immobile macro must fail its proof");
        int simulations = immobile.Simulations;
        local.TryExecute(immobile, rejectedExecution, live, null, out _, out _);
        Require(immobile.Simulations == simulations, "an identical failed physical proof must not rerun every frame");
        immobile.Revision++;
        local.TryExecute(immobile, rejectedExecution, live, null, out _, out _);
        Require(immobile.Simulations > simulations, "terrain change invalidates a failed proof");

        Console.WriteLine("movement contracts: unsafe controls, retained threats, capability chains, liquid exit, macro isolation, walk stalls, search limits and interruption outcomes passed");
        return 0;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private class FloorWorld : ITileWorld
    {
        public bool InWorld(int x, int y) => x >= 0 && x < 100 && y >= 0 && y < 100;
        public TileShape Shape(int x, int y) => y >= 10 ? TileShape.Solid : TileShape.Air;
        public bool PassThrough(int x, int y) => false;
        public bool Water(int x, int y) => false;
        public bool Lava(int x, int y) => false;
    }

    private sealed class CountingStillWorld : FloorWorld, ITileWorld, IBodySimulationWorld
    {
        public int Simulations { get; private set; }
        public int Revision { get; set; }
        public BodyState Simulate(BodyState state, Controls controls, MovementCapabilities capabilities)
        {
            Simulations++;
            return state;
        }
    }

    private sealed class CountingTraversal : Traversal
    {
        private int count;
        public override MoveKind Kind => MoveKind.Walk;
        public override IEnumerable<NavEdge> Candidates(NavNode node, BodyPhysics.Pose? here, bool lava) => Array.Empty<NavEdge>();
        public override Controls Steer(BodyState live, NavStep step, NavStep? next) => new(++count);
        public override object CaptureExecutionState() => count;
        public override void RestoreExecutionState(object? state) => count = (int)state!;
    }
}
