#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>
/// What a navigator may keep believing about a goal after the search that answered it has finished, and what must make
/// it stop. A finished search that ended without a route is kept so the same question is not asked every tick
/// (<c>spent</c>), and a route that ended at the free corner beside a goal the body cannot slide to is kept as "as close
/// as it gets" (<c>settledShort</c>). A sentinel's review of 59b184a found both could outlive the goal they were about:
/// the drift branch re-aims the held goal a little every tick and the new-goal test compared with last tick's goal, so a
/// goal that moved smoothly never counted as new and a kept conclusion survived any distance of drift; and
/// <c>settledShort</c> was inferred from <c>spent == null</c>, which a tile edit invalidating a node-limit answer also
/// produces, so an edit anywhere in a large search parked the body short of its goal for good.
///
/// <para>Five scenes, each run against the unfixed navigator first (the sentinel's probe, 15 September 2026, at 530b16d).
/// Three are the review's findings and were red there: a settled-short answer drifted 600 px ended 583 px from the goal
/// with no plan after the drift; a proven absence drifted into a reachable chamber ended 531 px away still reading proven;
/// and a node-limit search with one tile edited elsewhere ended 4,478 px short after a single search, against 93 px with
/// no edit. The other two hold mechanisms that the fix keeps and that the review's mutants removed: an edit that opens a
/// proven-sealed pocket must be searched again, and a goal just behind a surface must be searched once and then hovered
/// at rather than asked every tick.</para>
/// </summary>
internal static class VerifyNavigatorConclusions
{
    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        int failures = 0;
        try
        {
            failures += ASettledShortAnswerIsForgottenOnceItsGoalHasDrifted();
            failures += AProvenAbsenceIsForgottenOnceItsGoalHasDrifted();
            failures += AnEditElsewhereDoesNotParkTheBodyShort();
            failures += AnEditThatOpensAProvenPocketIsSearchedAgain();
            failures += AGoalBehindASurfaceIsSearchedOnceAndHoveredAt();
            failures += AnInterruptedNodeLimitGoalIsAskedAgain();
            failures += AJitteringGoalDoesNotReaimAPartialRouteIntoRock();
        }
        finally
        {
            LimitPlanningWork.Unbounded = false;
            FreeSpaceSearch.WorldOverride = null;
            if (previous != null) MovementQueries.World = previous;
            ClearanceField.Shared.Invalidate();
        }
        return failures;
    }

    /// <summary>
    /// A room over a thick floor, and a goal one tile into the floor's surface: a free corner sits beside it, so the search
    /// finds that corner, the body cannot slide the last few pixels into rock, and the navigator settles short. The goal then
    /// drifts 600 px along the floor at a pace a following anchor moves at. The answer "as close as it gets" was about the
    /// goal where it was, so the body must plan again once the goal has moved past the replan distance, and end within the
    /// progress distance of where the goal now is, horizontally.
    /// </summary>
    private static int ASettledShortAnswerIsForgottenOnceItsGoalHasDrifted()
    {
        var world = Build(60, 40, (x, y) => y == 0 || y == 39 || x == 0 || x == 59 || y >= 20 ? '#' : '.');
        Plug(world);
        Vector2 goal = new(40 * 16 + 8, 20 * 16 + 8), centre = new(15 * 16 + 8, 160), velocity = Vector2.Zero;
        var navigator = new Navigator();
        for (int t = 0; t < 240; t++) Tick(navigator, world, goal, ref centre, ref velocity);
        string settled = $"settled at {centre.X:0},{centre.Y:0} reason {navigator.ProgressReason} searches {navigator.SearchId}";
        if (navigator.ProgressReason != "as-close-as-it-can-get")
            return Report($"settled-short drift premise: the body must settle short of a goal inside the floor's surface; {settled}");
        int planned = 0;
        for (int t = 0; t < 600; t++)
        {
            if (t < 400) goal.X -= 1.5f;
            Tick(navigator, world, goal, ref centre, ref velocity);
            if (navigator.PlannedThisTick) planned++;
        }
        float dx = MathF.Abs(centre.X - goal.X);
        string ledger = $"{settled}; after 600 px of drift: centre {centre.X:0},{centre.Y:0} goal {goal.X:0},{goal.Y:0} dx {dx:0} planned ticks {planned} searches {navigator.SearchId} reason {navigator.ProgressReason}";
        Console.WriteLine($"settled-short drift: {ledger}");
        int failures = 0;
        if (planned == 0) failures += Report($"settled-short drift: the navigator must plan again once its goal has drifted past the replan distance; {ledger}");
        if (dx > Weights.ObjectiveProgressPixels) failures += Report($"settled-short drift: the body must end within {Weights.ObjectiveProgressPixels:0} px of the drifted goal horizontally; {ledger}");
        return failures;
    }

    /// <summary>
    /// A wall down the middle of a closed world with a gap at its foot, and a sealed pocket inside the wall. The goal starts in
    /// the pocket, which the search proves unreachable; it then drifts 240 px into the right-hand chamber, which the body can
    /// reach through the gap. A proven absence was about the pocket, so once the goal has left it the navigator must search
    /// again, stop reporting the goal proven unreachable, and arrive. A fresh navigator arrives at tick 201.
    /// </summary>
    private static int AProvenAbsenceIsForgottenOnceItsGoalHasDrifted()
    {
        var world = PocketInAWall(open: false);
        Plug(world);
        Vector2 goal = new(29 * 16, 5 * 16), centre = new(10 * 16 + 8, 6 * 16), velocity = Vector2.Zero;
        var navigator = new Navigator();
        for (int t = 0; t < 120; t++) Tick(navigator, world, goal, ref centre, ref velocity);
        if (!navigator.GoalProvenUnreachable)
            return Report($"proven-absence drift premise: the pocket goal must be proven unreachable first; status {navigator.Status} searches {navigator.SearchId}");
        int arrivedAt = -1;
        for (int t = 0; t < 1500 && arrivedAt < 0; t++)
        {
            if (t < 60) goal.X += 4f;
            Tick(navigator, world, goal, ref centre, ref velocity);
            if (t >= 60 && navigator.Arrived) arrivedAt = t;
        }
        string ledger = $"goal {goal.X:0},{goal.Y:0} centre {centre.X:0},{centre.Y:0} distance {Vector2.Distance(centre, goal):0} proven {navigator.GoalProvenUnreachable} arrived at {arrivedAt} searches {navigator.SearchId} reason {navigator.ProgressReason}";
        Console.WriteLine($"proven-absence drift: {ledger}");
        int failures = 0;
        if (navigator.GoalProvenUnreachable) failures += Report($"proven-absence drift: a goal that left the sealed pocket must stop reading proven unreachable; {ledger}");
        if (arrivedAt < 0) failures += Report($"proven-absence drift: the body must arrive at the drifted goal within 1500 ticks; {ledger}");
        return failures;
    }

    /// <summary>
    /// An open world larger than one search may close, with the goal in a sealed pocket in its far corner, flown twice: once
    /// untouched, and once with a single tile edited near the start the first time a search stops at its node limit. That edit
    /// invalidates the kept node-limit answer without the navigator having concluded anything about the goal, so the body must
    /// go on searching from the partial routes and end about as close as the untouched run does; declared as within 160 px,
    /// against 93 px untouched, and with more than one search.
    /// </summary>
    private static int AnEditElsewhereDoesNotParkTheBodyShort()
    {
        const int width = 330, height = 270;
        float untouched = 0f;
        int failures = 0;
        for (int mode = 0; mode < 2; mode++)
        {
            var world = Build(width, height, (x, y) =>
            {
                bool border = y == 0 || y == height - 1 || x == 0 || x == width - 1;
                bool shell = x is >= 300 and <= 309 && y is >= 250 and <= 259;
                bool pocket = x is >= 302 and <= 307 && y is >= 252 and <= 257;
                return border || (shell && !pocket) ? '#' : '.';
            });
            Plug(world);
            Vector2 goal = new(304 * 16 + 8, 254 * 16 + 8), centre = new(20 * 16 + 8, 20 * 16 + 8), velocity = Vector2.Zero;
            var navigator = new Navigator();
            bool edited = false;
            for (int t = 0; t < 1200; t++)
            {
                Tick(navigator, world, goal, ref centre, ref velocity);
                if (mode == 1 && !edited && navigator.PlannedThisTick && navigator.LastSearchStop == FreeSpaceSearch.StopReason.NodeLimit)
                {
                    world.Set(3, 3, '#');
                    edited = true;
                }
            }
            float end = Vector2.Distance(centre, goal);
            string ledger = $"end distance {end:0} searches {navigator.SearchId} reason {navigator.ProgressReason} strikes {navigator.StuckStrikes}";
            if (mode == 0)
            {
                untouched = end;
                Console.WriteLine($"edit elsewhere, untouched run: {ledger}");
                continue;
            }
            Console.WriteLine($"edit elsewhere, one tile edited after the first node limit: edited {edited}; {ledger}; untouched ended {untouched:0}");
            if (!edited) failures += Report($"edit elsewhere premise: a search must stop at its node limit so the edit lands; {ledger}");
            if (end > 160f) failures += Report($"edit elsewhere: an edit that invalidates a node-limit answer must not park the body short; {ledger}; untouched ended {untouched:0}");
            if (navigator.SearchId <= 1) failures += Report($"edit elsewhere: the body must search again after the edit; {ledger}");
        }
        return failures;
    }

    /// <summary>
    /// The pocket scene with the goal left in the pocket, proven unreachable, and then one shell tile removed so the pocket
    /// opens onto the right-hand chamber. A proven absence is a statement about terrain, so an edit to the terrain it read
    /// must send the body searching again, and it must arrive.
    /// </summary>
    private static int AnEditThatOpensAProvenPocketIsSearchedAgain()
    {
        var world = PocketInAWall(open: false);
        Plug(world);
        Vector2 goal = new(29 * 16 + 8, 5 * 16 + 8), centre = new(10 * 16 + 8, 6 * 16), velocity = Vector2.Zero;
        var navigator = new Navigator();
        for (int t = 0; t < 120; t++) Tick(navigator, world, goal, ref centre, ref velocity);
        if (!navigator.GoalProvenUnreachable)
            return Report($"opened pocket premise: the pocket goal must be proven unreachable first; status {navigator.Status}");
        // Open the pocket's right wall across to the right-hand chamber, two rows tall so the orb fits.
        for (int x = 32; x <= 33; x++)
            for (int y = 4; y <= 6; y++)
                world.Set(x, y, '.');
        int arrivedAt = -1;
        for (int t = 0; t < 1500 && arrivedAt < 0; t++)
        {
            Tick(navigator, world, goal, ref centre, ref velocity);
            if (navigator.Arrived) arrivedAt = t;
        }
        string ledger = $"arrived at {arrivedAt} centre {centre.X:0},{centre.Y:0} distance {Vector2.Distance(centre, goal):0} proven {navigator.GoalProvenUnreachable} searches {navigator.SearchId}";
        Console.WriteLine($"opened pocket: {ledger}");
        return arrivedAt < 0 ? Report($"opened pocket: an edit opening a proven-sealed pocket must be searched again and reached; {ledger}") : 0;
    }

    /// <summary>
    /// A goal one tile into a floor's surface, held still. The search finds the free corner beside it and the body stops
    /// there; asking again from the same place finds the same corner, so the navigator must search at most twice in 300 ticks
    /// and keep the body near the route's end.
    /// </summary>
    private static int AGoalBehindASurfaceIsSearchedOnceAndHoveredAt()
    {
        var world = Build(60, 40, (x, y) => y == 0 || y == 39 || x == 0 || x == 59 || y >= 20 ? '#' : '.');
        Plug(world);
        Vector2 goal = new(40 * 16 + 8, 20 * 16 + 8), centre = new(15 * 16 + 8, 160), velocity = Vector2.Zero;
        var navigator = new Navigator();
        for (int t = 0; t < 300; t++) Tick(navigator, world, goal, ref centre, ref velocity);
        string ledger = $"searches {navigator.SearchId} centre {centre.X:0},{centre.Y:0} distance {Vector2.Distance(centre, goal):0} reason {navigator.ProgressReason}";
        Console.WriteLine($"goal behind a surface: {ledger}");
        int failures = 0;
        if (navigator.SearchId > 2) failures += Report($"goal behind a surface: an unchanged goal the body has settled short of must not be searched again every tick; {ledger}");
        if (Vector2.Distance(centre, goal) > 48f) failures += Report($"goal behind a surface: the body must stay by the route's end beside the goal; {ledger}");
        return failures;
    }

    /// <summary>
    /// The open world whose pocket a search cannot close within its node limit, with the request interrupted on the very
    /// tick the first search stops there and the same goal asked for again at once. An interrupt ends the attempt, and a
    /// node-limit answer proves nothing about the goal, so the resumed attempt must search again and fly the partial route
    /// it lends: kept across the interrupt, the answer is never earned from a place the body has not left, no partial route
    /// is lent, and the body hovers where it was for good. Declared as the same bound as the edit scene: within 160 px after
    /// 1200 ticks, with more than one search.
    /// </summary>
    private static int AnInterruptedNodeLimitGoalIsAskedAgain()
    {
        const int width = 330, height = 270;
        var world = Build(width, height, (x, y) =>
        {
            bool border = y == 0 || y == height - 1 || x == 0 || x == width - 1;
            bool shell = x is >= 300 and <= 309 && y is >= 250 and <= 259;
            bool pocket = x is >= 302 and <= 307 && y is >= 252 and <= 257;
            return border || (shell && !pocket) ? '#' : '.';
        });
        Plug(world);
        Vector2 goal = new(304 * 16 + 8, 254 * 16 + 8), centre = new(20 * 16 + 8, 20 * 16 + 8), velocity = Vector2.Zero;
        var navigator = new Navigator();
        bool interrupted = false;
        for (int t = 0; t < 1200; t++)
        {
            // The body is held where the search began until the interrupt, so the resumed ask comes from the place the answer
            // was reached at. Left to fly the pending search's partial route, it is already past the progress distance by the
            // node limit, the resumed ask is earned on distance alone, and the scene cannot tell a kept answer from a dropped one.
            if (interrupted) Tick(navigator, world, goal, ref centre, ref velocity);
            else navigator.MoveTo(new OrbState(centre, Vector2.Zero), goal);
            if (!interrupted && navigator.PlannedThisTick && navigator.LastSearchStop == FreeSpaceSearch.StopReason.NodeLimit)
            {
                navigator.Interrupt(new OrbState(centre, velocity), AttemptEnding.Cancelled, "fixture");
                interrupted = true;
            }
        }
        float end = Vector2.Distance(centre, goal);
        string ledger = $"interrupted {interrupted}; end distance {end:0} searches {navigator.SearchId} reason {navigator.ProgressReason}";
        Console.WriteLine($"interrupted node limit: {ledger}");
        int failures = 0;
        if (!interrupted) failures += Report($"interrupted node limit premise: a search must stop at its node limit so the interrupt lands; {ledger}");
        if (end > 160f) failures += Report($"interrupted node limit: a resumed attempt at a goal a search stopped short of must search again and keep flying; {ledger}");
        if (navigator.SearchId <= 1) failures += Report($"interrupted node limit: the resumed attempt must search again; {ledger}");
        return failures;
    }

    /// <summary>
    /// The goal one tile into the floor's surface, jittering a few pixels either side of its place while the body approaches,
    /// which is what an anchor re-derived every tick from a moving player does. The search's route ends at the free corner
    /// beside the goal, because the body cannot slide into rock; re-aiming that route's last point at the jittered goal would
    /// put the route's end inside the floor, block its last segment, and throw the route away every tick it is re-aimed. The
    /// jitter stays inside the replan distance, so nothing about the goal has changed: at most three searches in 300 ticks,
    /// and the body ends by the route's end.
    /// </summary>
    private static int AJitteringGoalDoesNotReaimAPartialRouteIntoRock()
    {
        var world = Build(60, 40, (x, y) => y == 0 || y == 39 || x == 0 || x == 59 || y >= 20 ? '#' : '.');
        Plug(world);
        Vector2 place = new(40 * 16 + 8, 20 * 16 + 8), centre = new(15 * 16 + 8, 160), velocity = Vector2.Zero;
        var navigator = new Navigator();
        for (int t = 0; t < 300; t++)
        {
            Vector2 goal = place + new Vector2(MathF.Sin(t * 0.3f) * 8f, 0f);
            Tick(navigator, world, goal, ref centre, ref velocity);
        }
        string ledger = $"searches {navigator.SearchId} centre {centre.X:0},{centre.Y:0} distance {Vector2.Distance(centre, place):0} reason {navigator.ProgressReason}";
        Console.WriteLine($"jittering goal: {ledger}");
        int failures = 0;
        if (navigator.SearchId > 3) failures += Report($"jittering goal: a goal moving inside the replan distance must not re-aim a partial route into rock and restart its search; {ledger}");
        if (Vector2.Distance(centre, place) > 48f) failures += Report($"jittering goal: the body must end by the route's end beside the goal; {ledger}");
        return failures;
    }

    private static TextTileWorld PocketInAWall(bool open) => Build(60, 40, (x, y) =>
    {
        bool border = y == 0 || y == 39 || x == 0 || x == 59;
        bool wall = x is >= 26 and <= 33 && !(y is >= 35 and <= 38);
        bool pocket = x is >= 28 and <= 31 && y is >= 3 and <= 6;
        return border || (wall && !pocket) ? '#' : '.';
    });

    private static TextTileWorld Build(int width, int height, Func<int, int, char> glyph)
    {
        var rows = new List<string>();
        for (int y = 0; y < height; y++)
        {
            var row = new char[width];
            for (int x = 0; x < width; x++) row[x] = glyph(x, y);
            rows.Add(new string(row));
        }
        return new TextTileWorld(0, 0, rows);
    }

    private static void Plug(TextTileWorld world)
    {
        MovementQueries.World = world;
        FreeSpaceSearch.WorldOverride = world;
        ClearanceField.Shared.Invalidate();
    }

    private static void Tick(Navigator navigator, ITileWorld world, Vector2 goal, ref Vector2 centre, ref Vector2 velocity)
    {
        Controls controls = navigator.MoveTo(new OrbState(centre, velocity), goal);
        velocity = OrbPace.Step(velocity, controls.Desired, controls.Burst);
        centre += velocity;
        CircleContact.Resolve(world, ref centre, ref velocity);
    }

    private static int Report(string message)
    {
        Console.WriteLine(message);
        return 1;
    }
}
