#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics;
using Terraria.Graphics.Light;

namespace AICompanion.Companion.Brain.WorldObservation;

/// <summary>
/// Reads light only where the game's lighting engine has computed it. `Lighting.Brightness` answers zero
/// for a dark tile and also for a tile outside the engine's buffers, so a zero alone is not darkness, and
/// spending a torch on it spends one on a place nobody measured. Colour mode presents a processed area and
/// its GetColor answers zero outside it; the legacy modes index a camera-sized state buffer from the
/// requested rectangle widened by the off-screen margin and answer zero beyond it. This reader asks the
/// active engine that same bounds question, through fields the engine keeps private, and fails loudly at
/// first use if they move rather than silently treating the whole world as measured.
/// </summary>
public static class MeasureLightCoverage
{
    private const BindingFlags InstanceField = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly object ColourEngine = typeof(Lighting).GetField("NewEngine", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
        ?? throw new MissingFieldException(typeof(Lighting).FullName, "NewEngine");
    private static readonly FieldInfo ProcessedArea = ColourEngine.GetType().GetField("_activeProcessedArea", InstanceField)
        ?? throw new MissingFieldException(ColourEngine.GetType().FullName, "_activeProcessedArea");
    private static readonly FieldInfo RequestedLeft = LegacyField("_requestedRectLeft");
    private static readonly FieldInfo RequestedRight = LegacyField("_requestedRectRight");
    private static readonly FieldInfo RequestedTop = LegacyField("_requestedRectTop");
    private static readonly FieldInfo LegacyCamera = LegacyField("_camera");

    private static FieldInfo LegacyField(string name) => typeof(LegacyLighting).GetField(name, InstanceField)
        ?? throw new MissingFieldException(typeof(LegacyLighting).FullName, name);

    /// <summary>The active engine's computed bounds, snapshotted once so a sweep reads one consistent frame.</summary>
    public readonly record struct Coverage(bool Legacy, Rectangle Area, float LegacyColumns, float LegacyRows)
    {
        public bool Contains(int x, int y)
        {
            if (Area.Width <= 0) return false;
            if (!Legacy) return Area.Height > 0 && Area.Contains(x, y);
            // LegacyLighting.GetColor's own bounds: the buffer index runs from the requested rectangle's
            // corner less the off-screen margin, and a camera-sized buffer ends the valid range.
            int column = x - Area.X + Lighting.OffScreenTiles, row = y - Area.Y + Lighting.OffScreenTiles;
            return column >= 0 && row >= 0 && column < LegacyColumns && row < LegacyRows;
        }
    }

    public static Coverage Current()
    {
        if (Lighting.Mode == LightMode.Color)
            return new Coverage(false, (Rectangle)ProcessedArea.GetValue(ColourEngine)!, 0f, 0f);
        LegacyLighting engine = Lighting.LegacyEngine;
        if (LegacyCamera.GetValue(engine) is not Camera camera) return new Coverage(true, Rectangle.Empty, 0f, 0f);
        int left = (int)RequestedLeft.GetValue(engine)!, right = (int)RequestedRight.GetValue(engine)!, top = (int)RequestedTop.GetValue(engine)!;
        Vector2 size = camera.UnscaledSize;
        // A requested rectangle with no width means the engine has not processed an area yet.
        return new Coverage(true, new Rectangle(left, top, Math.Max(0, right - left), 1),
            size.X / 16f + Lighting.OffScreenTiles * 2 + 10f, size.Y / 16f + Lighting.OffScreenTiles * 2);
    }

    /// <summary>Samples on a coarse lattice around a tile: how many the engine computed, how many it did not, and
    /// the mean brightness of the computed ones. A sample inside a carried light is neither: it is lit only while
    /// that light is carried there.</summary>
    public readonly record struct Darkness(int Measured, int Unmeasured, float MeanBrightness)
    {
        public int Samples => Measured + Unmeasured;
        public float MeasuredFraction => Samples == 0 ? 0f : Measured / (float)Samples;
    }

    public static Darkness Around(Point centre, int radiusTiles, int strideTiles, IReadOnlyList<(Vector2 Centre, float Radius)> carried)
    {
        Coverage coverage = Current();
        int measured = 0, unmeasured = 0;
        float sum = 0f;
        int stride = Math.Max(1, strideTiles);
        for (int dx = -radiusTiles; dx <= radiusTiles; dx += stride)
            for (int dy = -radiusTiles; dy <= radiusTiles; dy += stride)
            {
                if (dx * dx + dy * dy > radiusTiles * radiusTiles) continue;
                int x = centre.X + dx, y = centre.Y + dy;
                if (!WorldGen.InWorld(x, y, 1) || InsideCarriedLight(x, y, carried)) continue;
                if (!coverage.Contains(x, y)) { unmeasured++; continue; }
                measured++;
                sum += MathHelper.Clamp(Lighting.Brightness(x, y), 0f, 1f);
            }
        return new Darkness(measured, unmeasured, measured > 0 ? sum / measured : 0f);
    }

    private static bool InsideCarriedLight(int x, int y, IReadOnlyList<(Vector2 Centre, float Radius)> carried)
    {
        // A tile is sixteen pixels; its centre is what a carried light's radius is measured to.
        Vector2 tile = new(x * 16f + 8f, y * 16f + 8f);
        for (int i = 0; i < carried.Count; i++)
            if (Vector2.DistanceSquared(tile, carried[i].Centre) <= carried[i].Radius * carried[i].Radius) return true;
        return false;
    }
}
