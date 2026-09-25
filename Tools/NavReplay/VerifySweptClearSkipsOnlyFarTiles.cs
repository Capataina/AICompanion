#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Tools.Ledger;

/// <summary>
/// The swept test skips a tile without reading it only when that tile could never be within the body's radius
/// of the segment, so skipping changes which tiles are read and never which answer comes back.
///
/// <para>`CircleContact.SweptClear` scans the segment's bounding box, and for the smoother's long diagonal
/// chords most of that box is far from the line. `MayReachSegment` is the cheap necessary condition that lets it
/// pass those tiles by — the tile's centre within the radius plus its half-diagonal — and the exact
/// segment-to-rectangle distance still decides every tile the condition admits. The claim that makes this safe is
/// geometric, and this row checks it where it is most likely to be wrong: segments of every length and slant,
/// including degenerate ones, against every tile of their boxes grown by two, with the tiles whose exact distance
/// sits right at the radius counted so the row cannot pass on a sample that never came near the edge.</para>
/// </summary>
internal static class VerifySweptClearSkipsOnlyFarTiles
{
    public static int Run()
    {
        var random = new Random(20260925);
        long checkedTiles = 0, skipped = 0, nearTheEdge = 0, wronglySkipped = 0;
        string? firstWrong = null;
        float radiusSquared = CircleContact.Radius * CircleContact.Radius;
        for (int trial = 0; trial < 4000; trial++)
        {
            Vector2 a = new((float)(random.NextDouble() * 800), (float)(random.NextDouble() * 800));
            // A quarter of the segments are short or zero-length, where the projection clamps and a sloppy
            // bound is most likely to cut a tile beside an endpoint.
            float reach = trial % 4 == 0 ? (float)(random.NextDouble() * 8) : (float)(random.NextDouble() * 48 * 16);
            double angle = random.NextDouble() * Math.PI * 2;
            Vector2 b = a + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * reach;
            int x0 = (int)MathF.Floor((MathF.Min(a.X, b.X) - CircleContact.Radius) / 16f) - 2, x1 = (int)MathF.Floor((MathF.Max(a.X, b.X) + CircleContact.Radius) / 16f) + 2;
            int y0 = (int)MathF.Floor((MathF.Min(a.Y, b.Y) - CircleContact.Radius) / 16f) - 2, y1 = (int)MathF.Floor((MathF.Max(a.Y, b.Y) + CircleContact.Radius) / 16f) + 2;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    checkedTiles++;
                    float exact = CircleContact.SegmentDistanceSquaredToTile(x, y, a, b);
                    if (MathF.Abs(MathF.Sqrt(exact) - CircleContact.Radius) < 1f) nearTheEdge++;
                    if (CircleContact.MayReachSegment(x, y, a, b)) continue;
                    skipped++;
                    if (exact < radiusSquared)
                    {
                        wronglySkipped++;
                        firstWrong ??= $"tile {x},{y} against {a} -> {b} sits {MathF.Sqrt(exact):0.###} px from the segment and was skipped";
                    }
                }
        }
        EmitLedgerRows.Detail($"{checkedTiles} tiles against 4000 segments: {skipped} skipped, {nearTheEdge} within a pixel of the radius, {wronglySkipped} skipped while inside it");
        if (skipped == 0) throw new InvalidOperationException("premise: no tile was ever skipped, so the row says nothing about the prefilter");
        if (nearTheEdge < 1000) throw new InvalidOperationException($"premise: only {nearTheEdge} tiles sat near the radius, too few to test the bound's edge");
        if (wronglySkipped > 0) throw new InvalidOperationException($"the swept test skipped {wronglySkipped} tiles the body's radius reaches; first: {firstWrong}");
        return 0;
    }
}
