extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;

using PositionRequest = live::AICompanion.Companion.Brain.PositionSelection.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.PositionSelection.RequestKind;
using T = live::AICompanion.Companion.Brain.WorldObservation.ThreatRecord;
using C = live::AICompanion.Companion.Brain.Behaviours.ActionContext;

/// <summary>
/// The firing-spot shortlist has to carry line-of-fire information before it is cut to the few
/// candidates that can afford a real trajectory solve. The cheap ranking pass used to pass a
/// literal 1 for every candidate's shot factor, so the eight that paid for a solve were chosen by
/// band, danger and openness alone — none of which knows whether a shot exists. The 2026-09-11
/// session recorded 810 hunt ticks standing on a spot the positioner had itself scored as having
/// no arc.
///
/// This builds the smallest geometry that separates the two rankings: an enemy in a pit, whose
/// rim is the only reachable place with a line to it, surrounded by open floor whose distant
/// spots win every cheap factor and cannot see into the pit at all.
/// </summary>
internal static class VerifyFiringPosition
{
    public static int Run()
    {
        VerifyShortlistPrefersSpotsThatCanActuallyShoot();
        Console.WriteLine("firing position: the solve shortlist carries line of sight and the chosen spot has an arc");
        return 0;
    }

    private const int FloorY = 80;
    // A narrow, deep shaft rather than a shallow bowl. A shallow one puts the clear/blocked
    // boundary in the middle of the distance ranking, so a clear spot reaches the shortlist by
    // accident and the check passes whatever the ranking knows. The shaft makes the separation
    // sharp: only the two lip tiles have a line down it, and they are the closest candidates of
    // all, which is exactly where a ranking that prefers standoff distance will never look.
    // The mouth is wide enough that a tile with solid floor under it can see down the shaft.
    // Terraria's CanHitLine tests three tile rows at each step, not one, so a mouth only as wide
    // as the shaft leaves every solid-floored lip blocked by the rock beside it and the fixture
    // would be asserting that an impossible shot should have been found.
    private const int ShaftLeft = 49, ShaftRight = 53, ShaftFloorY = 86;
    private const int EnemyTileX = 51;

    private static void VerifyShortlistPrefersSpotsThatCanActuallyShoot()
    {
        BuildPitWorld();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        // The player stands with the companion: this fixture is about where to shoot from, so the
        // follow band must not be the thing separating candidates.
        player.position = new Vector2(30 * 16f, FloorY * 16f - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.position = new Vector2(30 * 16f, FloorY * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;

        var enemy = new NPC();
        enemy.SetDefaults(Terraria.ID.NPCID.Zombie);
        enemy.whoAmI = 30;
        enemy.active = true;
        // Stationary, so its predicted path does not smear danger across the rim and decide the
        // comparison through a factor this check is not about.
        enemy.velocity = Vector2.Zero;
        enemy.Bottom = new Vector2(EnemyTileX * 16f + 8f, ShaftFloorY * 16f);
        Main.npc[30] = enemy;

        companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
        var threats = companion.Brain.Senses.Threats.Threats;
        threats.Clear();
        threats.Add(new T
        {
            Npc = enemy,
            DistanceToCompanion = Vector2.Distance(companion.NPC.Bottom, enemy.Bottom),
            DistanceToPlayer = Vector2.Distance(player.Bottom, enemy.Bottom),
        });

        var ctx = new C(companion, companion.Brain.Senses);
        var profile = companion.Arsenal.ProfileFor(ctx, enemy);
        Require(profile != null, "the firing-position fixture needs an equipped weapon profile to solve with");

        var request = new PositionRequest(RequestKind.LineOfFire, enemy.Center, enemy);
        // The reachable region is flooded incrementally across rescores rather than in one call, so
        // a single Resolve sees a region a few tiles wide and would exclude the lip on reachability
        // alone. Resolving repeatedly is what the brain does — the positioner rescores every twelfth
        // tick and advances the flood on its own cadence — and it is the only way this fixture asks
        // the ranking question rather than a question about flood budget.
        Vector2? chosen = null;
        for (int tick = 0; tick < 400; tick++)
            chosen = companion.Brain.Positioner.Resolve(request, companion.Brain.Senses, profile);

        string evidence = companion.Brain.Positioner.CandidateEvidence;
        // The geometry has to stay discriminating, or a green result would mean nothing: there must
        // be blind reachable spots for a sight-free ranking to prefer, and reachable spots with a
        // line for a sight-aware one to find. Both are asserted rather than assumed, because a
        // terrain edit that quietly removed either would leave this check passing for no reason.
        Require(companion.Brain.Positioner.ReachableCandidateCount > 8,
            $"the fixture needs more reachable candidates than the solve budget, or nothing is being shortlisted: reachable={companion.Brain.Positioner.ReachableCandidateCount}");
        Require(!Clear(46, enemy) && Clear(48, enemy),
            "the shaft must leave the near floor blind and its lip sighted, or the two rankings cannot disagree");
        Require(chosen != null,
            $"the pit fixture must produce a standing spot; evidence={evidence}");
        Require(evidence.Length > 0,
            "the positioner recorded no evaluated candidates, so the fixture proves nothing about the shortlist");

        // CandidateEvidence is "tileX,tileY:score:shot", strongest first, so the first entry is the
        // spot that won. A shortlist chosen without line of sight fills every slot with distant
        // floor spots that outscore the rim on standoff and cannot see the pit, and every one of
        // them reads no-arc.
        string best = evidence.Split('|')[0];
        Require(best.EndsWith(":clear-arc"),
            $"the chosen firing spot has no solved arc, so the shortlist was ranked without line of sight: best={best}; all={evidence}");

        bool anyClear = evidence.Contains(":clear-arc");
        Require(anyClear,
            $"no evaluated candidate could shoot the target, so the solve budget was spent entirely on blind spots: {evidence}");
    }

    /// <summary>
    /// A long open floor with a narrow shaft cut down into it. The enemy sits on the shaft floor,
    /// so a straight line from anywhere out on the main floor is stopped by the solid rock beside
    /// the shaft mouth, while the two tiles at the lip look straight down it. Both lips are on the
    /// main floor, so they are reachable by the same walk as every blind spot and cannot be removed
    /// by the reachability tier; the shaft's own interior is a one-way drop and is excluded by it.
    /// </summary>
    private static void BuildPitWorld()
    {
        Main.maxTilesX = Main.maxTilesY = 120;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)120, (ushort)120 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= ShaftFloorY + 2; y++)
                Solid(x, y);
        // Hollow the shaft, leaving its floor intact.
        for (int x = ShaftLeft; x <= ShaftRight; x++)
            for (int y = FloorY; y < ShaftFloorY; y++)
                Open(x, y);
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
    }

    /// <summary>Whether a body standing on the main floor at this tile column has a straight line to the target.</summary>
    private static bool Clear(int tileX, NPC target)
        => Collision.CanHitLine(new Vector2(tileX * 16f + 8f, FloorY * 16f - 30f), 1, 1,
            target.position, target.width, target.height);

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
