extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;

using PositionRequest = live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;
using PositionReasons = live::AICompanion.Companion.Brain.Infrastructure.Position.PositionReasons;
using SuccessRegionKind = live::AICompanion.Companion.Brain.Infrastructure.Position.SuccessRegionKind;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;

/// <summary>
/// A FireFrom stand holds the point the plan priced and refuses what the flood unclaims.
///
/// The positioner no longer scores firing stands — the planner's generators propose them and the
/// search prices them — so what remains to prove is the hold: Resolve answers the anchor exactly,
/// PrepareOffer admits it as settled once the flood claims it, reports undecided while the flood
/// has not, and refuses rock and dead targets alike. The six rows this file used to carry tested
/// the scored path's shot windows, shortlist budgets, refusal memory and opportunity resolver, and
/// went with that machinery rather than being rewritten against behaviour that no longer exists.
/// </summary>
internal static class VerifyOfferValidity
{
    public static int Run()
    {
        // Every row runs and files a sub-row; the fixture fails afterwards if any did, rather than at the first.
        const string family = "offer validity";
        int failed = 0;
        failed += RunOneRow.Case(FireFromHoldsThePlannedPoint, family);
        failed += RunOneRow.Case(FireFromReportsUndecidedWhileTheFloodHasNotClaimedIt, family);
        failed += RunOneRow.Case(FireFromRefusesSolidGround, family);
        failed += RunOneRow.Case(FireFromRefusesADeadTarget, family);
        if (failed == 0)
            Console.WriteLine("offer validity: a FireFrom stand holds its point, reports undecided on unclaimed ground, falls back to here from rock, and refuses a dead target");
        return failed;
    }

    private const int FloorY = 80;
    private const int ShaftLeft = 49, ShaftRight = 53, ShaftFloorY = 86;

    private static void FireFromHoldsThePlannedPoint()
    {
        var (companion, enemy) = PitScene();
        var positioner = companion.Brain.Positioner;
        Vector2 stand = MovementQueries.HoverPoint(new Point(30, FloorY - 1));
        var request = new PositionRequest(RequestKind.FireFrom, stand, enemy);

        Vector2? chosen = positioner.Resolve(request, companion.Brain.Senses);
        Require(chosen == stand, $"a FireFrom stand must hold the planned point exactly; chose {chosen} for {stand}");
        Require(positioner.ChoiceReason == "fire-from-stand",
            $"a held stand must say so; reason={positioner.ChoiceReason}");
        Require(positioner.Region.Kind == SuccessRegionKind.Undeclared,
            $"a held stand declares no purpose geometry; region={positioner.Region.Kind}");
        Require(positioner.CandidateCount == 1 && positioner.RejectedCandidateCount == 0,
            "a held stand is one candidate admitted, not a lattice scored");

        var offer = positioner.PrepareOffer(request, companion.Brain.Senses);
        Require(offer.Destination == stand && !offer.Undecided,
            $"the admission query must return the held stand as a settled answer; destination={offer.Destination}, reason={offer.Reason}");
    }

    private static void FireFromReportsUndecidedWhileTheFloodHasNotClaimedIt()
    {
        var (companion, enemy) = PitScene();
        var positioner = companion.Brain.Positioner;
        // Open air far from the body's feet, with a flood that has never grown: unclaimed by
        // construction, which is NotYet rather than unreachable.
        Vector2 far = MovementQueries.HoverPoint(new Point(110, FloorY - 10));
        var request = new PositionRequest(RequestKind.FireFrom, far, enemy);

        var offer = positioner.PrepareOffer(request, companion.Brain.Senses);
        Require(offer.Destination == null && offer.Undecided && offer.Reason == PositionReasons.FireStandUndecided,
            $"an unclaimed stand must be neither admitted nor refused; destination={offer.Destination}, reason={offer.Reason}, undecided={offer.Undecided}");

        // Resolve still holds it: the hold is the plan's point, and only rock or a proven absence
        // refuses. What decides reads the undecided offer, not the hold.
        Vector2? chosen = positioner.Resolve(request, companion.Brain.Senses);
        Require(chosen == far, "Resolve must hold the planned point even where the flood has not claimed it");
    }

    private static void FireFromRefusesSolidGround()
    {
        var (companion, enemy) = PitScene();
        var positioner = companion.Brain.Positioner;
        // Settled first: on a fresh flood the rock tile is unclaimed and reads NotYet, which is the
        // row above's verdict. Only a finished flood knows rock from unclaimed ground.
        var settle = new PositionRequest(RequestKind.WithPlayer, Main.player[0].Bottom);
        for (int i = 0; i < 1200 && !positioner.ReachComplete; i++)
            positioner.Resolve(settle, companion.Brain.Senses);
        Require(positioner.ReachComplete, "the rock row needs a settled flood before it asks about rock");
        Vector2 rock = MovementQueries.HoverPoint(new Point(30, FloorY + 1));
        var request = new PositionRequest(RequestKind.FireFrom, rock, enemy);

        Vector2? chosen = positioner.Resolve(request, companion.Brain.Senses);
        Require(chosen == null, $"a stand inside rock must resolve to nothing; chose {chosen}");
        Require(positioner.ChoiceReason == "fire-stand-unreachable",
            $"a refused stand must say the stand is unreachable; reason={positioner.ChoiceReason}");

        var offer = positioner.PrepareOffer(request, companion.Brain.Senses);
        Require(offer.Destination != null && !offer.Undecided && offer.Reason == "fire-from-here",
            $"a rock stand must fall back to firing from here so combat keeps the body; destination={offer.Destination}, reason={offer.Reason}, undecided={offer.Undecided}");
        Require(Vector2.DistanceSquared(offer.Destination.Value, companion.NPC.Center) < 1f,
            $"the fallback is the body, not a substitute pixel; here={offer.Destination} body={companion.NPC.Center}");
    }

    private static void FireFromRefusesADeadTarget()
    {
        var (companion, _) = PitScene();
        var positioner = companion.Brain.Positioner;
        Vector2 stand = MovementQueries.HoverPoint(new Point(30, FloorY - 1));
        var request = new PositionRequest(RequestKind.FireFrom, stand, null);

        var offer = positioner.PrepareOffer(request, companion.Brain.Senses);
        Require(offer.Destination == null && !offer.Undecided && offer.Reason == "attack-target-not-attackable",
            $"a stand with nothing to aim at must be refused as settled; reason={offer.Reason}");
    }

    // ---- scene ----------------------------------------------------------------------------------------

    private static (CompanionNPC Companion, NPC Enemy) PitScene()
    {
        BuildPitWorld();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(30 * 16f, FloorY * 16f - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.position = new Vector2(30 * 16f, FloorY * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;

        var enemy = new NPC();
        enemy.SetDefaults(Terraria.ID.NPCID.Zombie);
        enemy.whoAmI = 30;
        enemy.active = true;
        enemy.velocity = Vector2.Zero;
        enemy.Bottom = new Vector2(51 * 16f + 8f, ShaftFloorY * 16f);
        Main.npc[30] = enemy;

        companion.Brain.Senses.Update(companion.NPC, player);
        return (companion, enemy);
    }

    private static void BuildPitWorld()
    {
        // Main's static constructor reads Program.SavePath, which is null until something sets it. The default
        // suite does this before any fixture runs; the standalone flag has to do it itself or touching Main throws.
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        Main.maxTilesX = Main.maxTilesY = 120;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)120, (ushort)120 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= ShaftFloorY + 2; y++)
                Solid(x, y);
        for (int x = ShaftLeft; x <= ShaftRight; x++)
            for (int y = FloorY; y < ShaftFloorY; y++)
                Open(x, y);
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = true;
        tile.TileType = 1;
    }

    private static void Open(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = false;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
