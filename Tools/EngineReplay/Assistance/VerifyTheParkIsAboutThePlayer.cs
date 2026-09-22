extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Tools.Ledger;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using ClearanceField = live::AICompanion.Companion.Brain.Infrastructure.Movement.ClearanceField;
using CornerGraph = live::AICompanion.Companion.Brain.Infrastructure.Movement.CornerGraph;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using PositionRequest = live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;

/// <summary>
/// The `WithPlayer` park is a fact about the player, not about the body — and it points upward.
///
/// <para>Clearance saturates at <c>ClearanceField.MaxTiles</c>, so in any room with room in it a whole
/// ring of corners ties on the term that is supposed to decide, and whatever breaks that tie is the
/// real chooser. Until 22 September 2026 the tiebreak was distance to the body, which made the park
/// *follow the body*: every rescore re-picked the nearest tied corner to wherever the body had drifted
/// to, so the journey to the last one was cancelled and re-asked, and the 10:05 capture's census read
/// `WithPlayer: asked 27, reached 1, abandoned 26`. It also meant a body that arrived low had nothing
/// in the comparison that knew which way was up.</para>
///
/// <para>So this row asks the positioner the *same question twice with the body somewhere else*, which
/// is the shape the defect cannot survive: one scene, one player, one terrain, the body low on the
/// first pass and high on the second, and the park must be the same point. Then it requires that point
/// to sit above the top of the player's head and at the band-ward end of the tied ring rather than
/// wherever the body was — README's "it lives in the band of air a few tiles above your head".</para>
///
/// <para>A third check is the clause the brief asked to make impossible by construction, reported as
/// already impossible: a park with under a tile of terrain clearance. A corner is admitted only when
/// the four tiles around it are free (`CornerGraph.Usable`), and a free tile's clearance is its
/// distance to the nearest wall tile, which is at least one whenever the tile itself is free. So no
/// usable corner can carry less than a tile, and the row measures that over the scene rather than
/// adding a guard for a case the graph cannot produce. Combined clearance can still fall below a tile
/// through an enemy body, which is deliberate and is not terrain.</para>
/// </summary>
internal static class VerifyTheParkIsAboutThePlayer
{
    private const int FloorRow = 60, PlayerColumn = 50;

    public static int Run()
    {
        var companion = Scene(out Player player);

        // Two passes, identical but for where the body is: three tiles off the floor, then ten. The
        // column is the same on both so the reach flood is rooted in the same neighbourhood and the
        // admissible set is the question's constant rather than one of its variables.
        Vector2 low = Park(companion, player, new Vector2(PlayerColumn * 16f, FloorRow * 16f - 3 * 16f), out string lowReason);
        Vector2 high = Park(companion, player, new Vector2(PlayerColumn * 16f, FloorRow * 16f - 10 * 16f), out string highReason);

        Require(Vector2.Distance(low, high) < 0.01f,
            $"the park must not move with the body: from three tiles up it chose {low} ({lowReason}) and from ten tiles up {high} ({highReason}). "
            + "A park that follows the body is re-picked at every rescore and the journey to the last one is abandoned.");

        float head = player.Center.Y - (player.Bottom.Y - player.Center.Y);
        Require(low.Y <= head,
            $"the park must sit above the top of his head; it chose y={low.Y:0.0} against his head at {head:0.0}");

        // Horizontally the park is where he is heading, not wherever the candidate scan starts. A
        // tiebreak that names only the vertical axis leaves this one to the loop's order, which puts
        // the park on the region's leftmost admissible column — behind a player walking right.
        float heading = companion.Brain.Senses.Intent.Region.Heading.X;
        Require(MathF.Abs(low.X - heading) <= 16f,
            $"the park must sit over where he is heading, within a tile; it chose x={low.X:0.0} against a heading of {heading:0.0}");

        // The band-ward end of the tied ring. Clearance still outranks the band, so the park is the
        // *lowest* corner that is as clear as the best — coming down toward him rather than climbing to
        // the region's ceiling. Measured directly off the field so the row does not restate the rule.
        float best = 0f, lowestTied = float.MinValue;
        var region = companion.Brain.Senses.Intent.Region;
        Point from = CornerGraph.NearestCorner(region.Centre - region.HalfSize);
        Point to = CornerGraph.NearestCorner(region.Centre + region.HalfSize);
        for (int x = from.X; x <= to.X; x++)
            for (int y = from.Y; y <= to.Y; y++)
            {
                var corner = new Point(x, y);
                if (!CornerGraph.Usable(MovementQueries.World, corner)) continue;
                float clearance = ClearanceField.Shared.AtCorner(MovementQueries.World, corner);
                Require(clearance >= 1f,
                    $"a usable corner cannot carry under a tile of terrain clearance, because its four tiles are free; corner {corner} read {clearance:0.00} tiles");
                if (clearance > best) { best = clearance; lowestTied = float.MinValue; }
                if (clearance >= best - 0.05f) lowestTied = MathF.Max(lowestTied, CornerGraph.ToWorld(corner).Y);
            }
        Require(low.Y <= lowestTied + 0.01f,
            $"the park must be at the band-ward end of the tied ring, not above it: it chose y={low.Y:0.0} where the lowest corner "
            + $"as clear as the best ({best:0.00} tiles) sits at y={lowestTied:0.0}");

        EmitLedgerRows.Detail($"park: chose {low} on both passes, {(head - low.Y) / 16f:0.0} tiles above his head, "
            + $"best clearance {best:0.00} tiles, lowest equally clear corner y={lowestTied:0.0}, reason {lowReason}");
        return 0;
    }

    /// <summary>Put the body somewhere and resolve `WithPlayer` until the rescore cadence has settled on an answer.</summary>
    private static Vector2 Park(CompanionNPC companion, Player player, Vector2 body, out string reason)
    {
        companion.NPC.Center = body;
        companion.NPC.velocity = Vector2.Zero;
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        VerifyOreWork.SettleReach(companion, player);
        var request = new PositionRequest(RequestKind.WithPlayer, player.Center);
        Vector2? chosen = null;
        for (int i = 0; i < 24; i++)
            chosen = companion.Brain.Positioner.Resolve(request, companion.Brain.Senses);
        var positioner = companion.Brain.Positioner;
        reason = positioner.ChoiceReason;
        Require(chosen is Vector2,
            $"the scene must produce a park; reason={reason} candidates={positioner.CandidateCount} reached={positioner.ReachableCandidateCount} "
            + $"rejected={positioner.RejectedCandidateCount} complete={positioner.ReachComplete}");
        return chosen!.Value;
    }

    /// <summary>One floor, open air above it, no hazards: the scene where clearance saturates and the tiebreak decides.</summary>
    private static CompanionNPC Scene(out Player player)
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, FloorRow];
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
        }
        player = Main.player[0];
        player.dead = false;
        player.active = true;
        player.statLife = player.statLifeMax2 = 100;
        player.Bottom = new Vector2(PlayerColumn * 16f, FloorRow * 16f);
        player.velocity = Vector2.Zero;
        companion.NPC.active = true;
        MovementQueries.Hazards = Array.Empty<Rectangle>();
        return companion;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
