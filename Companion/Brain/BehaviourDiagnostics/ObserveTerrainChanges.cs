#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.SharedMovementSystem;
namespace AICompanion.Companion.Brain.BehaviourDiagnostics;
/// <summary>Tile callbacks announce dirtiness; snapshots run after the engine applies the edit.</summary>
public sealed class ObserveTerrainChanges : GlobalTile
{
    public override void HitWire(int i, int j, int type) => RecordTerrainChunks.Dirty(i, j);
    public override void PlaceInWorld(int i, int j, int type, Item item) => RecordTerrainChunks.Dirty(i, j);
    public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
    { if (!fail && !effectOnly) RecordTerrainChunks.Dirty(i, j); }
}

/// <summary>Rolling local world evidence, bounded per frame rather than ending after an event quota.</summary>
public sealed class RecordTerrainChunks : ModSystem
{
    private const int Side = 16, Radius = 3, CapturesPerTick = 2, MaximumRemembered = 8192;
    private static readonly Queue<Point> pending = new();
    private static readonly HashSet<Point> queued = new();
    private static readonly Dictionary<Point, string> prior = new();
    private static readonly Queue<Point> age = new();
    private static Point companion, player;
    private static bool actorsKnown;
    private static int cursor;

    internal static void Reset()
    { pending.Clear(); queued.Clear(); prior.Clear(); age.Clear(); actorsKnown = false; cursor = 0; }

    public static void ObserveActors(NPC npc, Player owner)
    {
        companion = new Point((int)npc.Center.X / (16 * Side), (int)npc.Center.Y / (16 * Side));
        player = new Point((int)owner.Center.X / (16 * Side), (int)owner.Center.Y / (16 * Side));
        actorsKnown = true;
    }

    public static void Dirty(int x, int y)
    {
        if (!GodsEyeEvents.Active) return;
        Point key = new(x / Side, y / Side);
        if (!actorsKnown || (!Near(key, companion) && !Near(key, player))) return;
        Enqueue(key);
    }

    private static bool Near(Point a, Point b) => Math.Abs(a.X - b.X) <= Radius && Math.Abs(a.Y - b.Y) <= Radius;
    private static void Enqueue(Point key) { if (queued.Add(key)) pending.Enqueue(key); }

    public override void PostUpdateEverything()
    {
        if (!GodsEyeEvents.Active || !actorsKnown) return;
        int diameter = Radius * 2 + 1;
        int cell = cursor++ % (diameter * diameter);
        int dx = cell % diameter - Radius, dy = cell / diameter - Radius;
        Enqueue(new Point(companion.X + dx, companion.Y + dy));
        Enqueue(new Point(player.X + dx, player.Y + dy));
        for (int count = 0; count < CapturesPerTick && pending.TryDequeue(out Point key); count++)
        {
            queued.Remove(key);
            if (!Near(key, companion) && !Near(key, player)) continue;
            int x = key.X * Side, y = key.Y * Side;
            var glyphs = new StringBuilder(Side * Side);
            byte[] liquids = new byte[Side * Side * 2], materials = new byte[Side * Side * 4];
            byte[] states = new byte[Side * Side], frames = new byte[Side * Side * 4];
            int index = 0, clipped = 0;
            for (int row = 0; row < Side; row++)
            for (int col = 0; col < Side; col++, index++)
            {
                int tx = x + col, ty = y + row;
                if (!NavGrid.World.InWorld(tx, ty)) { glyphs.Append('?'); clipped++; continue; }
                Tile tile = Main.tile[tx, ty];
                glyphs.Append(TextTileWorld.Glyph(NavGrid.World.Shape(tx, ty), NavGrid.World.Water(tx, ty), NavGrid.World.Lava(tx, ty), NavGrid.World.PassThrough(tx, ty)));
                liquids[index * 2] = tile.LiquidAmount;
                liquids[index * 2 + 1] = (byte)tile.LiquidType;
                materials[index * 4] = (byte)tile.TileType;
                materials[index * 4 + 1] = (byte)(tile.TileType >> 8);
                materials[index * 4 + 2] = (byte)tile.WallType;
                materials[index * 4 + 3] = (byte)(tile.WallType >> 8);
                states[index] = (byte)((tile.HasTile ? 1 : 0) | (tile.IsActuated ? 2 : 0)
                    | (Main.tileSolid[tile.TileType] ? 4 : 0) | (Main.tileSolidTop[tile.TileType] ? 8 : 0)
                    | ((int)tile.Slope << 4) | (tile.IsHalfBlock ? 128 : 0));
                frames[index * 4] = (byte)tile.TileFrameX; frames[index * 4 + 1] = (byte)(tile.TileFrameX >> 8);
                frames[index * 4 + 2] = (byte)tile.TileFrameY; frames[index * 4 + 3] = (byte)(tile.TileFrameY >> 8);
            }
            string data = $"width={Side};height={Side};clipped={clipped};tiles={glyphs};liquid-amount-type={Convert.ToBase64String(liquids)};tile-wall-u16le={Convert.ToBase64String(materials)};tile-state-u8={Convert.ToBase64String(states)};tile-frame-i16le={Convert.ToBase64String(frames)}";
            if (prior.TryGetValue(key, out string? old) && old == data) continue;
            if (!prior.ContainsKey(key)) age.Enqueue(key);
            prior[key] = data;
            while (prior.Count > MaximumRemembered && age.TryDequeue(out Point oldest)) prior.Remove(oldest);
            GodsEyeEvents.RecordTerrainSnapshot(x, y, data);
        }
    }
}
