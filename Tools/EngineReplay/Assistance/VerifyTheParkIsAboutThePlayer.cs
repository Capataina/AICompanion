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

    /// <summary>
    /// The band is a live dial and this is the scene that reads it. In an ordinary room the tied ring of
    /// saturated corners lies entirely above the band point, so every value of the band picks the ring's
    /// lowest corner and the number itself is invisible: raising it from three tiles to thirty moved the
    /// park by one tile and reddened nothing (measured by a sentinel on 22 September 2026). A dial no row
    /// can move is a dial nobody can change safely.
    ///
    /// <para>So this scene is a chimney of constant width standing on the floor he is on: every height in
    /// it is exactly as clear as every other, the tie therefore runs from below the band point to the top
    /// of the region, and the band alone says where in that column the companion waits. A scene of two
    /// discrete shelves was tried first and could not be built honestly — the band point is three tiles
    /// over his head, and any chamber tall enough to hold a well-cleared corner that close to him is a
    /// chamber he is standing inside.</para>
    ///
    /// <para>What is asserted is the outcome rather than the rule: the companion waits two to five tiles
    /// over his head, which is README's "band of air a few tiles above your head" in numbers. Every value
    /// of the constant maps to a different height in this column, so the row reads the dial rather than
    /// the sign of it — at thirty tiles the park climbs to the top of the region and the row goes red with
    /// the height it chose.</para>
    /// </summary>
    public static int BandDecidesWhereInAColumnHeWaits()
    {
        var companion = AChimneyWhereEveryHeightTies(out Player player);
        Vector2 park = Park(companion, player, new Vector2(PlayerColumn * 16f, 58 * 16f), out string reason);

        float head = player.Center.Y - (player.Bottom.Y - player.Center.Y);
        var region = companion.Brain.Senses.Intent.Region;
        // The tied column, measured rather than assumed: its lowest and highest corner are what say the
        // choice of height was open in both directions.
        float best = float.MinValue, lowest = float.MinValue, highest = float.MaxValue;
        Point from = CornerGraph.NearestCorner(region.Centre - region.HalfSize);
        Point to = CornerGraph.NearestCorner(region.Centre + region.HalfSize);
        var tied = new List<float>();
        for (int x = from.X; x <= to.X; x++)
            for (int y = from.Y; y <= to.Y; y++)
            {
                var corner = new Point(x, y);
                if (!CornerGraph.Usable(MovementQueries.World, corner)) continue;
                float clearance = ClearanceField.Shared.AtCorner(MovementQueries.World, corner);
                if (clearance > best + 0.05f) { best = clearance; tied.Clear(); }
                if (clearance >= best - 0.05f) tied.Add(CornerGraph.ToWorld(corner).Y);
            }
        foreach (float y in tied) { lowest = MathF.Max(lowest, y); highest = MathF.Min(highest, y); }

        // The premise, and it is the whole reason this scene exists: the column has to offer the companion a
        // tied height *outside* the band this row asserts as well as one inside it. Without that the
        // assertion passes on geometry and the constant is invisible again, which is exactly the state the
        // ordinary room was in — there every tied corner was one tile from every other, so a band of three
        // tiles and a band of thirty chose places a tile apart and nothing could tell them apart.
        float lowTiles = (head - lowest) / 16f, highTiles = (head - highest) / 16f;
        Require(tied.Count > 0 && lowTiles >= 2f && lowTiles <= 5f && highTiles > 5f,
            $"the premise: the tied column must offer a height inside the two-to-five-tile band and one above it; the best clearance "
            + $"was {best:0.00} tiles over {tied.Count} corner(s) spanning {lowTiles:0.0} to {highTiles:0.0} tiles over his head "
            + $"(y {highest:0.0}..{lowest:0.0}), region y {region.Centre.Y - region.HalfSize.Y:0.0}..{region.Centre.Y + region.HalfSize.Y:0.0}");

        float tilesOverHead = (head - park.Y) / 16f;
        Require(tilesOverHead >= 2f && tilesOverHead <= 5f,
            $"in a column where every height is equally clear, the companion waits a few tiles over his head rather than at either end of it: "
            + $"it chose y={park.Y:0.0} ({reason}), {tilesOverHead:0.0} tiles over his head at {head:0.0}, in a tied column running "
            + $"{(head - lowest) / 16f:0.0} to {(head - highest) / 16f:0.0} tiles over it at {best:0.00} tiles of clearance. "
            + "This row is where the band constant is read: a band nobody can move is a band nobody can change safely.");

        EmitLedgerRows.Detail($"park band: a tied column of {tied.Count} corners at {best:0.00} tiles of clearance running "
            + $"{(head - lowest) / 16f:0.0} to {(head - highest) / 16f:0.0} tiles over his head; the park took {tilesOverHead:0.0} tiles over it at {park}, reason {reason}");
        return 0;
    }

    /// <summary>A chimney of constant width standing on the floor the player stands on, so every height inside it
    /// carries the same clearance and the tie the band breaks runs from under his head to the top of the region.
    /// Wide enough that the walls never decide, and the only solid thing inside the region is the floor.</summary>
    private static CompanionNPC AChimneyWhereEveryHeightTies(out Player player)
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.tileSolid[TileID.Dirt] = true;
        for (int x = 28; x <= 72; x++)
            for (int y = 24; y <= 64; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.HasTile = true;
                tile.TileType = TileID.Dirt;
            }
        for (int x = PlayerColumn - 6; x <= PlayerColumn + 6; x++)
            for (int y = 26; y < FloorRow; y++)
            {
                Tile carved = Main.tile[x, y];
                carved.HasTile = false;
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
