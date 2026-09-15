extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Chooser = live::AICompanion.Companion.Brain.Infrastructure.Selection.Chooser;
using CircleContact = live::AICompanion.Companion.Brain.Infrastructure.Movement.CircleContact;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;

/// <summary>
/// The route home a job pays for is read from beside the player, which is where every job is chosen from while the companion keeps
/// him company. The reach flood is rooted at the body, so its costs to the player and to the job's stand are two distances from one
/// point, and the route between them is at least their difference. A sentinel's review of the company lane found that difference
/// taken one way only — the player's cost less the stand's, floored at zero — so with the body beside the player the detour read zero
/// for jobs whose true detour was 188 and 2,387 px, and the owner's ruling that the separation cost reads the route home applied only
/// once the orb had already flown to the job.
///
/// <para>The scene is the route-home row's own: ore on the corridor floor, the player five rows up on an upper floor whose way down is
/// either a gap near him or the ledge at the far end. The body is read once beside the ore and once beside the player. Pass lines:</para>
/// <list type="bullet">
/// <item>premise: read from the stand, the far way up is longer than the near one by more than two hundred pixels, or the scene
/// compares nothing;</item>
/// <item>read from beside the player, the detour is above zero on both floors, and the far way up is longer than the near one by
/// the same margin — the ordering the separation cost acts on, from the place the choice is made;</item>
/// <item>on both floors the two readings agree within half again of the smaller. They cannot agree exactly, and the reason is the
/// pricing rather than the fix: the flood prices each edge by the clearance at its far corner, so a flood rooted at one end prices
/// the way to the other differently from a flood rooted there pricing the way back, and the difference grows with the length of
/// corridor between them.</item>
/// </list>
///
/// <para>This row was first written to require the two readings within a fixed 160 px, then within a slack derived from each root's
/// offset from its end, and both were wrong about the same thing. With the body left five tiles short of the ore the far detour read
/// 673 px from there against 998 px from beside the player; with the body moved beside the ore, 817 against 998, a gap no root offset
/// accounts for. That remainder is the directional pricing, measured here at 16 and 22 percent, and it is recorded as a finding
/// rather than tuned into a tolerance.</para>
/// </summary>
internal static class VerifyRouteHomeFromEitherEnd
{
    private const float FarRouteLonger = 200f, DirectionalRatio = 1.5f;

    public static int Run()
    {
        bool lifted = LimitPlanningWork.Unbounded;
        LimitPlanningWork.Unbounded = true;
        try
        {
            var near = Detours(farRoute: false);
            var far = Detours(farRoute: true);
            string ledger = $"near way up: from the stand {near.AtStand:0} px, from beside the player {near.BesidePlayer:0} px; "
                + $"far way up: from the stand {far.AtStand:0} px, from beside the player {far.BesidePlayer:0} px";
            Console.WriteLine($"MEASURE route home from either end: {ledger}");
            Require(far.AtStand > near.AtStand + FarRouteLonger,
                $"premise: the far way up must lengthen the detour read from the stand by more than {FarRouteLonger} px, or nothing is compared; {ledger}");
            Require(near.BesidePlayer > 0f && far.BesidePlayer > 0f,
                $"the detour read from beside the player must be above zero on both floors; {ledger}");
            Require(far.BesidePlayer > near.BesidePlayer + FarRouteLonger,
                $"read from beside the player, the far way up must be longer than the near one by more than {FarRouteLonger} px; {ledger}");
            foreach (var (name, reading) in new[] { ("near", near), ("far", far) })
            {
                float low = MathF.Min(reading.AtStand, reading.BesidePlayer), high = MathF.Max(reading.AtStand, reading.BesidePlayer);
                Require(high <= low * DirectionalRatio,
                    $"the {name} way up must read within {DirectionalRatio} times from either end; {ledger}");
            }
            Console.WriteLine($"route home from either end: {ledger}");
            return 0;
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine($"RED route home from either end: {e.Message}");
            return 1;
        }
        finally { LimitPlanningWork.Unbounded = lifted; }
    }

    private readonly record struct Reading(float AtStand, float BesidePlayer);

    private static Reading Detours(bool farRoute)
    {
        Point ore = new(25, 89);
        var (_, ctx) = VerifyOreWork.SetUp(live::AICompanion.Companion.Brain.Activities.WorkPolicy.Opportunistic, TileID.Copper, ore);
        VerifyOreWork.BuildPlayersUpperFloor(90, gapNearPlayer: !farRoute);
        var brain = ctx.Companion.Brain;
        ctx.Player.velocity = Vector2.Zero;
        // The player stands on the upper floor, five rows above the companion's corridor floor, as in the route-home row.
        ctx.Player.Bottom = ctx.Npc.Center + new Vector2(576f, -5 * 16 + CircleContact.Radius);
        Vector2 stand = ore.ToWorldCoordinates();

        float Read()
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            brain.Senses.Update(ctx.Npc, ctx.Player);
            VerifyOreWork.ResettleReach(ctx);
            return Chooser.RouteDetour(ctx.Senses.Reach, ctx.Npc.Center, stand, ctx.Senses.Player.Position);
        }

        // At the stand: beside the ore, clear of the floor and the ore tile.
        ctx.Npc.Center = new Vector2(ore.X * 16 - CircleContact.Radius - 1f, (ore.Y + 1) * 16 - CircleContact.Radius - 1f);
        ctx.Npc.velocity = Vector2.Zero;
        float atStand = Read();
        // Beside the player: two tiles behind him and a little above his centre, clear of the upper floor he stands on.
        ctx.Npc.Center = ctx.Player.Center + new Vector2(-32f, -8f);
        ctx.Npc.velocity = Vector2.Zero;
        float beside = Read();
        return new Reading(atStand, beside);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
