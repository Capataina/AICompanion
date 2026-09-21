extern alias live;

using Microsoft.Xna.Framework;
using Terraria;

using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using Navigator = live::AICompanion.Companion.Brain.Infrastructure.Movement.Navigator;

/// <summary>
/// Keeping the player company is moving about his whole region with him, never flying to a spot and stopping. The owner ruled
/// it on 15 September 2026: inside the region there is no place to be, only the region to move through at roughly his pace,
/// easing to the front, falling back, rising and dipping, busy or not, and never behind him as a policy.
///
/// <para>Four rows, their pass lines declared before the first run, each driven through the whole brain and the native body
/// on an empty floor, so nothing but keeping company has anything to offer:</para>
/// <list type="bullet">
/// <item>an idle player for six hundred ticks: the body is never under the session reader's still speed for ten ticks running,
/// is outside the region on at most thirty ticks after it first enters, is moved by the accompanying owner on at least nine
/// tenths of those ticks, and visits both outer thirds of the region across it and both halves of it up and down;</item>
/// <item>a player walking at two pixels a tick for three hundred ticks: never still for ten ticks running, outside the region
/// on at most a tenth of the ticks, more than three tiles behind him on at most a tenth, and behind his centre at all on at
/// most a quarter;</item>
/// <item>a player walking at one pixel a tick for seven hundred and twenty ticks, long enough for the walk to cross the box
/// and come back: the same four lines;</item>
/// <item>a companion that begins inside the region: its first brain tick is the accompanying owner's, and it is moving faster
/// than the still speed within ten ticks.</item>
/// </list>
///
/// <para>The still speed is the session reader's own (`MeasureStillnessAndMotion.BodyStillPxPerTick`), because "never still"
/// is the play measure these rows exist to hold headless.</para>
/// </summary>
internal static class VerifyAccompanyingThePlayer
{
    private const float StillSpeed = 0.3f;

    public static int Run()
    {
        using var screen = PlaySizedScreen.Declare();
        int red = 0;
        void Each(string name, Action fixture)
        {
            LimitPlanningWork.Unbounded = true;
            try { fixture(); Console.WriteLine($"GREEN {name}"); }
            catch (InvalidOperationException e) { red++; Console.WriteLine($"RED {name}: {e.Message}"); }
            catch (Exception e) { red++; Console.WriteLine($"RED {name}: {e}"); }
            finally { LimitPlanningWork.Unbounded = false; }
        }
        Each("an idle player's companion moves about his whole region and is never still", AnIdlePlayersCompanionMovesAboutTheWholeRegion);
        Each("a travelling player's companion keeps up from inside his region and does not trail", ATravellingPlayersCompanionKeepsUpWithoutTrailing);
        Each("a slow player walked a long way has his companion level or ahead, not behind", ASlowWalkAcrossTheWholeBoxStaysLevelOrAhead);
        Each("a companion that begins inside the region moves from its first ticks", ACompanionThatBeginsInsideMovesAtOnce);
        if (red == 0) Console.WriteLine("accompanying the player: moving about the whole region, keeping up without trailing, and moving from the first tick");
        return red;
    }

    private static void AnIdlePlayersCompanionMovesAboutTheWholeRegion()
    {
        ActionContext ctx = VerifyCompanyLocalMotion.BuildNeighbourhood(hazards: false);
        VerifyUsefulAssistance.ClearMeasuredLight();
        var brain = ctx.Companion.Brain;
        int stillRun = 0, longestStill = 0, enteredAt = -1, outsideAfterEntry = 0, accompanyAfterEntry = 0, ticksAfterEntry = 0;
        bool left = false, right = false, above = false, below = false;
        // How far across the box the body got, as a share of the room either side, so a row that fails on a third it never
        // reached says whether the walk was confined or merely slow.
        float leftmost = float.MaxValue, rightmost = float.MinValue;
        var owners = new SortedDictionary<string, int>();
        for (int tick = 0; tick < 600; tick++)
        {
            ctx.Player.velocity = Vector2.Zero;
            VerifyOreWork.AdvanceBrain(ctx);
            float speed = ctx.Companion.Motor.State.Velocity.Length();
            stillRun = speed < StillSpeed ? stillRun + 1 : 0;
            longestStill = Math.Max(longestStill, stillRun);
            string owner = brain.ControlGrants.Last?.AppliedOwner ?? "-";
            owners[owner] = owners.TryGetValue(owner, out int n) ? n + 1 : 1;
            bool inside = brain.Senses.Intent.Inside;
            if (enteredAt < 0 && inside) enteredAt = tick;
            if (enteredAt < 0) continue;
            ticksAfterEntry++;
            if (!inside) outsideAfterEntry++;
            if (owner == "accompany") accompanyAfterEntry++;
            var region = brain.Senses.Intent.Region;
            Vector2 room = region.HalfSize - new Vector2(Navigator.SettleRadius);
            Vector2 offset = ctx.Npc.Center - region.Centre;
            left |= offset.X < -room.X / 3f;
            right |= offset.X > room.X / 3f;
            above |= offset.Y < 0f;
            below |= offset.Y > 0f;
            leftmost = MathF.Min(leftmost, offset.X / MathF.Max(1f, room.X));
            rightmost = MathF.Max(rightmost, offset.X / MathF.Max(1f, room.X));
        }
        var last = brain.Senses.Intent.Region;
        string ledger = $"entered at tick {enteredAt}; longest still run {longestStill}; outside after entry {outsideAfterEntry} of {ticksAfterEntry}; "
            + $"accompanying {accompanyAfterEntry} of {ticksAfterEntry}; visited left third {left}, right third {right}, upper half {above}, lower half {below}; "
            + $"across the room from {leftmost:0.00} to {rightmost:0.00}; "
            + $"owners {string.Join(", ", owners.Select(o => $"{o.Key}={o.Value}"))}; region {last.Centre} half {last.HalfSize}; body {ctx.Npc.Center}; player {ctx.Player.Bottom}";
        Console.WriteLine($"idle accompanying: {ledger}");
        Require(enteredAt >= 0, $"the companion must be inside an idle player's region at some tick; {ledger}");
        Require(longestStill < 10, $"an idle player's companion must never be still for ten ticks; {ledger}");
        Require(outsideAfterEntry <= 30, $"an idle player's companion must stay inside his region once it is there; {ledger}");
        Require(accompanyAfterEntry * 10 >= ticksAfterEntry * 9, $"inside the region the accompanying owner must move the body; {ledger}");
        // This row asserts a wander and the code now parks, and the difference is a decision the owner
        // has not made yet rather than a defect anybody introduced.
        //
        // `0a2a98e` replaced "move about the region" with "park at the usable corner holding the most
        // combined wall-and-enemy clearance, inside the box", because the play before it had the
        // companion sitting on the dirt at player height with the positioner choosing no place at all.
        // Its own body calls that change an experiment — *the experiment is the wander park and the
        // route heat* — and leaves `combat stands after the wander look is judged` open in its decision
        // graph. So the park is deliberate, is pending a look, and is what this row measures against a
        // contract written for what it replaced.
        //
        // The row is left asserting the wander rather than relaxed to fit the park, because relaxing it
        // would quietly settle a question the owner said he wanted to see first. Everything else in the
        // ledger holds under both designs and still passes: the body enters the region, never goes
        // still for more than a couple of ticks, never leaves after entering, and accompanies on every
        // tick. What fails is coverage alone, and the numbers say how far the park drifts.
        //
        // It has also been flaky across this boundary — one commit filed a pass and a fail — which is
        // what a coverage test of a seeded local motion does when its step budget is near the span it
        // has to cover. Whichever way the look is judged, the replacement assertion should be about the
        // motion rather than about the ground it happens to cover in six hundred ticks.
        Require(left && right && above && below,
            $"an idle player's companion must move through the whole region, both outer thirds across it "
            + $"and both halves up and down. NOTE: this asserts the wander that 0a2a98e replaced with the "
            + $"clearest-air park as a deliberate experiment pending the owner's look, so a red here is "
            + $"that open question rather than a regression; {ledger}");
    }

    private static void ATravellingPlayersCompanionKeepsUpWithoutTrailing() => WalkAlongside("travelling", pace: 2f, Ticks: 300);

    /// <summary>
    /// The travelling row does not reach the closed rear: in three hundred ticks the walk crosses to the box's front, reflects
    /// and has not yet come back past its middle, and with the rear left open the row measured the same trajectory to the
    /// pixel. A slower player walked for longer lets the walk cross the box and come back, which is where closing the rear
    /// is the only thing between the companion and a place behind him.
    /// </summary>
    private static void ASlowWalkAcrossTheWholeBoxStaysLevelOrAhead() => WalkAlongside("slow long walk", pace: 1f, Ticks: 720);

    /// <summary>A player walking right at <paramref name="pace"/> pixels a tick for <paramref name="Ticks"/> ticks from column 12,
    /// with the companion beside him, and the four pass lines both walking rows share.</summary>
    private static void WalkAlongside(string label, float pace, int Ticks)
    {
        ActionContext ctx = VerifyCompanyLocalMotion.BuildNeighbourhood(hazards: false);
        VerifyUsefulAssistance.ClearMeasuredLight();
        var brain = ctx.Companion.Brain;
        // The walk starts at column 12 so the region's front edge stays on the floor, which ends at column 94. The first build
        // started at the neighbourhood's column 40 and walked 360 ticks: the region led off the end of the world, the body was
        // held against its edge at x 1510, and a fifteen-tick still run measured the world's edge, not the motion.
        const int StartColumn = 12, FloorEndColumn = 94;
        ctx.Player.Bottom = new Vector2(StartColumn * 16 + 8, ctx.Player.Bottom.Y);
        ctx.Npc.Center = new Vector2(StartColumn * 16 + 8, ctx.Npc.Center.Y);
        ctx.Npc.velocity = Vector2.Zero;
        brain.Senses.Update(ctx.Npc, ctx.Player);
        float frontmost = 0f;
        int stillRun = 0, longestStill = 0, outside = 0, trailing = 0, behind = 0;
        // The ticks leading into the longest still run, so a failure names what the body was asked for while it stood.
        var recent = new Queue<string>();
        string stillTrace = "";
        for (int tick = 0; tick < Ticks; tick++)
        {
            ctx.Player.velocity = new Vector2(pace, 0f);
            ctx.Player.position += ctx.Player.velocity;
            VerifyOreWork.AdvanceBrain(ctx);
            float speed = ctx.Companion.Motor.State.Velocity.Length();
            stillRun = speed < StillSpeed ? stillRun + 1 : 0;
            var r = brain.Senses.Intent.Region;
            Vector2 v = ctx.Companion.Motor.State.Velocity, d = ctx.Companion.Motor.DesiredVelocity, t = brain.Navigator.Hover.LastTarget;
            recent.Enqueue(FormattableString.Invariant(
                $"t{tick} {brain.ControlGrants.Last?.AppliedOwner ?? "-"} body {ctx.Npc.Center.X:0},{ctx.Npc.Center.Y:0} vel {v.X:0.00},{v.Y:0.00} desired {d.X:0.00},{d.Y:0.00} target {t.X - r.Centre.X:0},{t.Y - r.Centre.Y:0} centre {r.Centre.X:0},{r.Centre.Y:0} lead {r.Lead.X:0}"));
            if (recent.Count > 22) recent.Dequeue();
            if (stillRun > longestStill) stillTrace = string.Join(" | ", recent);
            longestStill = Math.Max(longestStill, stillRun);
            if (!brain.Senses.Intent.Inside) outside++;
            if (ctx.Npc.Center.X < ctx.Player.Center.X - 3 * 16f) trailing++;
            if (ctx.Npc.Center.X < ctx.Player.Center.X) behind++;
            frontmost = MathF.Max(frontmost, r.Centre.X + r.HalfSize.X);
        }
        Require(frontmost <= FloorEndColumn * 16f,
            $"the premise: the region's front edge must stay on the floor, or the row measures the world's edge; frontmost {frontmost:0} px, floor ends {FloorEndColumn * 16} px");
        var region = brain.Senses.Intent.Region;
        if (longestStill >= 10) Console.WriteLine($"{label} accompanying still trace: {stillTrace}");
        string ledger = $"{label}: longest still run {longestStill}; outside {outside} of {Ticks}; more than three tiles behind {trailing} of {Ticks}; behind him at all {behind} of {Ticks}; "
            + $"region {region.Centre} half {region.HalfSize} lead {region.Lead}; body {ctx.Npc.Center}; player {ctx.Player.Center}; last action {brain.LastAction?.Name}";
        Console.WriteLine($"travelling accompanying: {ledger}");
        Require(longestStill < 10, $"a travelling player's companion must never be still for ten ticks; {ledger}");
        Require(outside * 10 <= Ticks, $"a travelling player's companion must keep up from inside his region; {ledger}");
        Require(trailing * 10 <= Ticks, $"a travelling player's companion must not trail him as a policy; {ledger}");
        // Three tiles is further back than the closed rear lets the target go, so the trailing share alone passed with the rear
        // open as well. Behind his centre at all, on at most a quarter of the ticks, is what closing the rear buys.
        Require(behind * 4 <= Ticks, $"a travelling player's companion must be level with him or ahead on most ticks; {ledger}");
    }

    private static void ACompanionThatBeginsInsideMovesAtOnce()
    {
        ActionContext ctx = VerifyCompanyLocalMotion.BuildNeighbourhood(hazards: false);
        VerifyUsefulAssistance.ClearMeasuredLight();
        var brain = ctx.Companion.Brain;
        Require(brain.Senses.Intent.Region.Contains(ctx.Npc.Center), $"the premise: the companion must begin inside the region; body {ctx.Npc.Center} region {brain.Senses.Intent.Region.Centre} half {brain.Senses.Intent.Region.HalfSize}");
        string firstOwner = "";
        int movingAt = -1;
        for (int tick = 0; tick < 10; tick++)
        {
            ctx.Player.velocity = Vector2.Zero;
            VerifyOreWork.AdvanceBrain(ctx);
            if (tick == 0) firstOwner = brain.ControlGrants.Last?.AppliedOwner ?? "-";
            if (movingAt < 0 && ctx.Companion.Motor.State.Velocity.Length() >= StillSpeed) movingAt = tick;
        }
        string ledger = $"first owner {firstOwner}; first moving tick {movingAt}; reach flood refloods {brain.Senses.Reach.Refloods}; body {ctx.Npc.Center}";
        Console.WriteLine($"cold start accompanying: {ledger}");
        Require(firstOwner == "accompany", $"a companion that begins inside the region must be accompanying the player on its first tick; {ledger}");
        Require(movingAt >= 0, $"a companion that begins inside the region must be moving within ten ticks; {ledger}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
