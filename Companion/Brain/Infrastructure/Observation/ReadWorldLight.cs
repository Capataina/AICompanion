#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Graphics.Light;
using Terraria.ModLoader;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// The world's own light — placed torches, lamps, lava, glowing tiles and the sky — with no light anybody is carrying in it,
/// taken from the colour engine at the one moment its map holds exactly that.
///
/// <para><b>Where that moment is.</b> <c>LightingEngine.ProcessArea</c> is a four-state machine (minimap, metrics, scan, blur)
/// advancing one state per call. The scan state sizes the working map, which zeroes nothing it will not overwrite, and fills
/// every cell from <c>TileLightScanner.ExportTo</c>: each tile's own light and its mask, and nothing else. Everything a body
/// carries — a held torch, a pet, a helmet, a glowstick, a modded lantern, the companion's own torch — reaches the engine
/// through <c>Lighting.AddLight</c> into a per-frame list that only the next call's <c>ProcessBlur</c> merges, by maximum,
/// before it blurs and <c>Present</c> swaps the map in. So while the state reads Blur, the working map is the world's light
/// unblurred, and copying it then and blurring the copy with the engine's own <c>LightMap.Blur</c> gives the light the world
/// would show with nobody carrying anything, by the engine's own arithmetic rather than by a model of it.</para>
///
/// <para><b>Why this replaced a model.</b> The sense used to read the presented map and discount every carried light by
/// modelling its falloff, and under a carried light the world's own light was then unknowable, which was guessed at three
/// times and wrong each time: read as lit, a player holding a torch left a dark cave unlit; read as dark, every lit room he
/// crossed took torches; remembered from before the light arrived, a pocket he walked into already holding a torch, or opened
/// by mining, was never lit. Reading the quantity those guesses approximated removed the case they were each a patch for.</para>
///
/// <para><b>Why it is observed from the draw as well as the brain.</b> A brain tick samples the engine at one phase of its
/// cycle, and that phase can stay off Blur for ever: with frame skip off the game draws on every frame and updates only once
/// a sixtieth of a second has passed, so a 120 Hz display draws twice per tick. <see cref="CaptureWorldLight"/> observes after
/// every draw's lighting, which sees every state; the brain's observation adds the lightning frames, whose two extra lighting
/// calls run inside the update. <see cref="Observe"/> takes each scan once, however many times it is seen.</para>
///
/// <para>Colour mode only. The legacy engines keep no such map, so there the sense reads the presented light, carried light
/// included.</para>
/// </summary>
public static class WorldLight
{
    private static readonly LightMap Map = new();
    // Brightness() blurs Map on demand.  Keep the pre-blur source separately so a
    // counterfactual never starts from a previous reader's already-propagated map.
    private static readonly LightMap RawMap = new();
    private static Rectangle area = Rectangle.Empty;
    private static bool blurred;
    private static bool armed = true;

    /// <summary>The tiles the last taken scan covers; empty before the first scan, and outside colour mode.</summary>
    public static Rectangle Area => area;

    /// <summary>How many scans have been taken since the last <see cref="Forget"/>, for a fixture proving a scan was taken once.</summary>
    public static int Captures { get; private set; }

    /// <summary>What blurring the last taken scan cost, in milliseconds; zero until one is blurred.</summary>
    public static double LastBlurMs { get; private set; }

    /// <summary>Forgets the scan, for a world unload and for a fixture moving between scenes: a map of the last world is not
    /// a reading of this one.</summary>
    public static void Forget()
    {
        area = Rectangle.Empty;
        blurred = false;
        armed = true;
        Captures = 0;
    }

    /// <summary>
    /// Takes the working map if the engine is between its scan and its blur and this scan has not been taken yet. A scan
    /// counts as new once the engine has been seen in any other state since the last one was taken; the engine cannot run a
    /// whole cycle between two draws, so no scan is taken twice and none is skipped outside the lightning frames.
    /// </summary>
    public static void Observe()
    {
        if (Lighting.Mode != LightMode.Color)
        {
            area = Rectangle.Empty;
            armed = true;
            return;
        }
        if (!Handles.BetweenScanAndBlur())
        {
            armed = true;
            return;
        }
        if (!armed) return;
        armed = false;

        LightMap working = Handles.WorkingMap();
        Rectangle scanned = Handles.WorkingArea();
        int width = working.Width, height = working.Height;
        if (scanned.Width <= 0 || scanned.Height <= 0 || width <= 0 || height <= 0)
        {
            area = Rectangle.Empty;
            return;
        }
        // The map is column-major (index x * Height + y), so its first Width * Height cells are every cell the engine reads.
        Map.SetSize(width, height);
        RawMap.SetSize(width, height);
        Array.Copy(Handles.Colours(working), Handles.Colours(Map), width * height);
        Array.Copy(Handles.Masks(working), Handles.Masks(Map), width * height);
        Array.Copy(Handles.Colours(working), Handles.Colours(RawMap), width * height);
        Array.Copy(Handles.Masks(working), Handles.Masks(RawMap), width * height);
        Map.NonVisiblePadding = working.NonVisiblePadding;
        RawMap.NonVisiblePadding = working.NonVisiblePadding;
        // The decay rates are set on the working map inside the blur we are ahead of, from the player's vision and the water
        // style; the active map carries the same rule applied one cycle earlier, which is the nearest reading there is.
        LightMap active = Handles.ActiveMap();
        Map.LightDecayThroughAir = active.LightDecayThroughAir;
        Map.LightDecayThroughSolid = active.LightDecayThroughSolid;
        Map.LightDecayThroughWater = active.LightDecayThroughWater;
        Map.LightDecayThroughHoney = active.LightDecayThroughHoney;
        CopyDecay(Map, RawMap);
        area = scanned;
        blurred = false;
        Captures++;
    }

    /// <summary>
    /// The world's own brightness at a tile, in the units <see cref="Lighting.Brightness"/> reports — the mean colour times the
    /// global brightness, unclamped — or null where no scan covers the tile. The copy is blurred on the first question asked of
    /// it, so a scan nobody asks about costs a copy and nothing more.
    /// </summary>
    public static float? Brightness(int x, int y)
    {
        if (area.Width <= 0 || !area.Contains(x, y)) return null;
        if (!blurred)
        {
            long start = Stopwatch.GetTimestamp();
            Map.Blur();
            LastBlurMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            blurred = true;
        }
        Vector3 colour = Map[x - area.X, y - area.Y];
        return Math.Max(0f, Lighting.GlobalBrightness * (colour.X + colour.Y + colour.Z) / 3f);
    }

    /// <summary>A native torch emission prepared for a hypothetical placement.  The style is the
    /// placed tile's frame style: TileLightScanner reads ordinary torches with frameY / 22.</summary>
    public readonly record struct PlannedTorch(Point Tile, short Style);

    /// <summary>Runs the colour engine's own mask-aware blur over a private clone of the captured
    /// pre-blur world light after seeding every planned torch by maximum.  All torches are composed
    /// before the one blur; separately blurred maps cannot represent their overlap.</summary>
    public static ProjectedLight ProjectTorches(IEnumerable<PlannedTorch> planned)
        => CaptureProjectionSnapshot().ProjectTorches(planned);

    /// <summary>Freezes the native pre-blur input once for every branch of one decision census.</summary>
    public static ProjectionSnapshot CaptureProjectionSnapshot()
    {
        if (area.Width <= 0 || Lighting.Mode != LightMode.Color) return new(Rectangle.Empty, null, 0);
        return new(area, CloneMap(RawMap), Lighting.GlobalBrightness);
    }

    private static LightMap CloneMap(LightMap source)
    {
        var copy = new LightMap();
        copy.SetSize(source.Width, source.Height);
        Array.Copy(Handles.Colours(source), Handles.Colours(copy), source.Width * source.Height);
        Array.Copy(Handles.Masks(source), Handles.Masks(copy), source.Width * source.Height);
        copy.NonVisiblePadding = source.NonVisiblePadding;
        CopyDecay(source, copy);
        return copy;
    }

    public sealed class ProjectionSnapshot
    {
        private readonly LightMap? raw;
        private readonly float globalBrightness;
        private readonly Vector3[] torchColours;
        internal ProjectionSnapshot(Rectangle area, LightMap? raw, float globalBrightness)
        {
            Area = area; this.raw = raw; this.globalBrightness = globalBrightness;
            torchColours = new Vector3[TorchID.Count];
            for (short style = 0; style < torchColours.Length; style++)
            {
                TorchID.TorchColor(style, out float r, out float g, out float b);
                torchColours[style] = new Vector3(r, g, b);
            }
        }
        public Rectangle Area { get; }

        public ProjectedLight ProjectTorches(IEnumerable<PlannedTorch> planned)
        {
            if (raw == null) return ProjectedLight.Unavailable;
            LightMap copy = CloneMap(raw);
            bool complete = true;
            foreach (PlannedTorch torch in planned)
            {
                if (!Area.Contains(torch.Tile) || torch.Style < 0 || torch.Style >= TorchID.Count)
                { complete = false; continue; }
                int x = torch.Tile.X - Area.X, y = torch.Tile.Y - Area.Y;
                copy[x, y] = Vector3.Max(copy[x, y], torchColours[torch.Style]);
            }
            long started = Stopwatch.GetTimestamp();
            copy.Blur();
            double milliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            return new(Area, copy, globalBrightness, milliseconds, complete);
        }
    }

    private static void CopyDecay(LightMap from, LightMap to)
    {
        to.LightDecayThroughAir = from.LightDecayThroughAir;
        to.LightDecayThroughSolid = from.LightDecayThroughSolid;
        to.LightDecayThroughWater = from.LightDecayThroughWater;
        to.LightDecayThroughHoney = from.LightDecayThroughHoney;
    }

    public sealed class ProjectedLight
    {
        internal static readonly ProjectedLight Unavailable = new(Rectangle.Empty, null, 0, 0, false);
        private readonly LightMap? map;
        private readonly float globalBrightness;
        internal ProjectedLight(Rectangle area, LightMap? map, float globalBrightness, double blurMilliseconds, bool complete)
        { Area = area; this.map = map; this.globalBrightness = globalBrightness; BlurMilliseconds = blurMilliseconds; Complete = complete; }
        public Rectangle Area { get; }
        public double BlurMilliseconds { get; }
        /// <summary>False when the native pre-blur snapshot did not cover a requested torch.</summary>
        public bool Complete { get; }
        public float? Brightness(int x, int y)
        {
            if (map == null || !Area.Contains(x, y)) return null;
            Vector3 colour = map[x - Area.X, y - Area.Y];
            return Math.Max(0f, globalBrightness * (colour.X + colour.Y + colour.Z) / 3f);
        }
    }

    /// <summary>The engine's private state, resolved once by name. A missing name is a game version that has moved it, and
    /// it fails by name at first use rather than reading nothing for ever after.</summary>
    private static class Handles
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly LightingEngine Engine =
            typeof(Lighting).GetField("NewEngine", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as LightingEngine
            ?? throw new MissingFieldException(typeof(Lighting).FullName, "NewEngine");
        private static readonly FieldInfo State = Field(typeof(LightingEngine), "_state");
        private static readonly object Blur = Enum.Parse(State.FieldType, "Blur");
        private static readonly FieldInfo Working = Field(typeof(LightingEngine), "_workingLightMap");
        private static readonly FieldInfo Active = Field(typeof(LightingEngine), "_activeLightMap");
        private static readonly FieldInfo WorkingProcessedArea = Field(typeof(LightingEngine), "_workingProcessedArea");
        private static readonly FieldInfo ColourArray = Field(typeof(LightMap), "_colors");
        private static readonly FieldInfo MaskArray = Field(typeof(LightMap), "_mask");

        private static FieldInfo Field(Type type, string name)
            => type.GetField(name, Instance) ?? throw new MissingFieldException(type.FullName, name);

        public static bool BetweenScanAndBlur() => Blur.Equals(State.GetValue(Engine));
        public static LightMap WorkingMap() => (LightMap)Working.GetValue(Engine)!;
        public static LightMap ActiveMap() => (LightMap)Active.GetValue(Engine)!;
        public static Rectangle WorkingArea() => (Rectangle)WorkingProcessedArea.GetValue(Engine)!;
        public static Array Colours(LightMap map) => (Array)ColourArray.GetValue(map)!;
        public static Array Masks(LightMap map) => (Array)MaskArray.GetValue(map)!;
    }
}

/// <summary>Observes the colour engine after every draw's lighting, so the world's own light is taken on every scan whatever the
/// ratio of draws to updates, and forgets it when the world closes.</summary>
public sealed class CaptureWorldLight : ModSystem
{
    public override void PostDrawTiles() => WorldLight.Observe();

    public override void OnWorldUnload() => WorldLight.Forget();
}
