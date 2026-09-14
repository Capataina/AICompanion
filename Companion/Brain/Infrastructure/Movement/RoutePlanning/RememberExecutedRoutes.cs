#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>Bounded, directed experience from completed real traversals. Memory proposes
/// connections; the ordinary execution proof still decides whether today's body can use them.</summary>
public sealed class RememberExecutedRoutes
{
    public static RememberExecutedRoutes World { get; } = new();
    private const int Capacity = 1024;
    // Format 3 is where a jump's start speed stopped meaning the profile's nominal and started
    // meaning the take-off the arc was proven from, and where the run-up's mark and launch point
    // joined it. A format-2 archive carries 3.5 in a slot that now promises a speed the floor
    // behind the take-off delivers, so it is refused rather than read: the two numbers are the
    // same shape and only the version separates them.
    private const int Format = 3;
    private readonly List<Entry> entries = new();
    public int Count => entries.Count;
    public int Revision { get; private set; }
    public sealed record Entry(int FromX, int FromY, int ToX, int ToY, int Kind,
        float Jump, float StartSpeed, float Steer, int Ticks, bool Rest,
        int Left, int Top, int Width, int Height, ulong Terrain,
        float RunUpBack = 0f, float LaunchAlong = 0f);
    private sealed record Archive(int Version, Entry[] Edges);
    private static NavStep Step(Entry e) => new(new Point(e.ToX, e.ToY), (MoveKind)e.Kind,
        new Point(e.FromX, e.FromY), e.Jump, e.StartSpeed, e.Steer, e.Ticks, e.Rest,
        RunUpBack: e.RunUpBack, LaunchAlong: e.LaunchAlong);
    private static Rectangle Bounds(Entry e) => new(e.Left, e.Top, e.Width, e.Height);

    public void Clear() { entries.Clear(); Revision++; }
    public void Record(ITileWorld world, NavStep step, BodyState entry, BodyState end, Rectangle swept)
    {
        // Representative nodes currently model the basic kit. Do not silently teach those
        // nodes a transition earned with future consumable abilities or a different body state.
        if (entry.Capabilities != MovementCapabilities.Basic || entry.Mobility != default
            || end.Mobility != default || !end.Covers(step.Tile) || step.From == step.Tile) return;
        int left = (int)MathF.Floor(swept.Left / 16f) - 1, top = (int)MathF.Floor(swept.Top / 16f) - 1;
        var area = new Rectangle(left, top, swept.Right / 16 - left + 2, swept.Bottom / 16 - top + 2);
        if ((long)area.Width * area.Height > 4096) return;
        Forget(step);
        if (entries.Count == Capacity) entries.RemoveAt(0);
        entries.Add(new Entry(step.From.X, step.From.Y, step.Tile.X, step.Tile.Y, (int)step.Kind,
            step.JumpScale, step.LaunchVx, step.SteerX, Math.Max(1, step.Ticks), step.FromRest,
            area.X, area.Y, area.Width, area.Height, Fingerprint(world, area),
            step.RunUpBack, step.LaunchAlong));
        Revision++;
    }

    public void Forget(NavStep step)
    {
        if (entries.RemoveAll(e => e.FromX == step.From.X && e.FromY == step.From.Y
            && e.ToX == step.Tile.X && e.ToY == step.Tile.Y && e.Kind == (int)step.Kind) > 0) Revision++;
    }
    public void TileChanged(int x, int y)
    {
        if (entries.RemoveAll(e => Bounds(e).Contains(x, y)) > 0) Revision++;
    }

    public string Save() => JsonSerializer.Serialize(new Archive(Format, entries.ToArray()));
    public bool Load(string data)
    {
        Clear();
        if (data.Length > 1024 * 1024) return false;
        Archive? archive;
        try { archive = JsonSerializer.Deserialize<Archive>(data); }
        catch (JsonException) { return false; }
        if (archive?.Version != Format || archive.Edges == null || archive.Edges.Length > Capacity) return false;
        foreach (Entry e in archive.Edges)
        {
            if (e == null || e.Kind < 0 || e.Kind > (int)MoveKind.FallThrough || e.Ticks <= 0 || e.Ticks > 1200
                || e.Width <= 0 || e.Height <= 0 || (long)e.Width * e.Height > 4096
                || (long)e.Left + e.Width > int.MaxValue || (long)e.Top + e.Height > int.MaxValue
                || e.FromX < 0 || e.FromY < 0 || e.ToX < 0 || e.ToY < 0
                || !Bounds(e).Contains(e.FromX, e.FromY) || !Bounds(e).Contains(e.ToX, e.ToY)
                || !float.IsFinite(e.Jump) || !float.IsFinite(e.StartSpeed) || !float.IsFinite(e.Steer))
            { Clear(); return false; }
            entries.Add(e);
        }
        return true;
    }

    /// <summary>Known suffixes ending at the requested goal. The graph is directed: climbing
    /// from A to B does not grant the reverse connection. No route is learned from a plan alone.</summary>
    public Dictionary<Point, List<NavStep>> Suffixes(ITileWorld world, Point goal, bool allowLava = true,
        Func<NavStep, float>? price = null, Func<NavStep, bool>? permitted = null)
    {
        var result = new Dictionary<Point, List<NavStep>> { [goal] = new() };
        var cost = new Dictionary<Point, float> { [goal] = 0 };
        var open = new PriorityQueue<Point, float>(); open.Enqueue(goal, 0);
        var incoming = entries.GroupBy(e => new Point(e.ToX, e.ToY)).ToDictionary(g => g.Key, g => g.ToArray());
        var invalid = new HashSet<Entry>();
        int work = 0;
        while (open.TryDequeue(out Point to, out float queued) && work++ < Capacity)
        {
            if (LimitPlanningWork.Expired) break;
            if (queued > cost[to] || !incoming.TryGetValue(to, out Entry[]? links)) continue;
            foreach (Entry e in links)
            {
                if (LimitPlanningWork.Expired) break;
                if (Fingerprint(world, Bounds(e)) != e.Terrain) { invalid.Add(e); continue; }
                if (!allowLava && ContainsLava(world, Bounds(e))) continue;
                Point from = new(e.FromX, e.FromY);
                NavStep step = Step(e);
                if (permitted != null && !permitted(step)) continue;
                // Pruning is part of route selection: apply the caller's complete current
                // price here, before an alternative suffix can be discarded.
                float next = cost[to] + (price?.Invoke(step) ?? Traversal.MovementCost(step)
                    * (world.Water(e.FromX, e.FromY) || world.Lava(e.FromX, e.FromY) ? 2f : 1f));
                if (cost.TryGetValue(from, out float old) && old <= next) continue;
                var suffix = new List<NavStep> { Step(e) }; suffix.AddRange(result[to]);
                if (suffix.Count > 128) continue;
                result[from] = suffix; cost[from] = next; open.Enqueue(from, next);
            }
        }
        if (entries.RemoveAll(invalid.Contains) > 0) Revision++;
        result.Remove(goal);
        return result;
    }

    private static bool ContainsLava(ITileWorld world, Rectangle area)
    {
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
                if (world.Lava(x, y)) return true;
        return false;
    }

    private static ulong Fingerprint(ITileWorld world, Rectangle area)
    {
        ulong hash = 14695981039346656037UL;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                uint tile = (uint)world.Shape(x, y) | (world.PassThrough(x, y) ? 16u : 0u)
                    | ((uint)world.LiquidKind(x, y) << 5) | ((uint)world.LiquidAmount(x, y) << 8);
                hash = unchecked((hash ^ tile) * 1099511628211UL);
            }
        return hash;
    }
}
