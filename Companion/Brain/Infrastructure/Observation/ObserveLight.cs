#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics;
using Terraria.Graphics.Light;
using Terraria.ID;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// How dark it is, as a field rather than a number. Every open-air tile on a coarse lattice through a
/// screen-sized window around the companion carries the brightness the engine computed there, with every
/// sample the companion's own carried torch could account for dropped, so the answer to "is it dark here"
/// does not depend on whether the companion is currently holding the thing that makes it look light.
///
/// <para>A field rather than a mean because a mean cannot answer the question anyone actually asks. The
/// mean over a window holding a lit chamber and three dark wings is somewhere in the middle, and it is
/// wrong about every part of the window: the chamber reads dark enough to light and the wings read light
/// enough to leave. Both consumers want a local answer — the torch wants "is there dark air near me or
/// on my heading", the lighting activity wants "where is the nearest dark region I can reach" — and both
/// are queries over a field.</para>
///
/// <para>The window follows the companion, not the camera: a companion sent into a cave while the player
/// stands in daylight must read the cave. The engine only computes light for the visible screen and
/// answers zero outside it, so a zero alone is not darkness — spending a torch on it spends one on a
/// place nobody measured. Every sample is therefore taken against the active engine's own computed
/// bounds, and an uncomputed tile is unmeasured rather than dark.</para>
/// </summary>
public sealed class LightSense
{
    private const int SampleStrideTiles = 4;
    private const int RefreshTicks = 10;

    /// <summary>Brightness at the player's tile, 0..1.</summary>
    public float AtPlayer { get; private set; } = 1f;

    /// <summary>Brightness at the companion's tile, 0..1; includes its own torch, shown for the overlay only.</summary>
    public float AtCompanion { get; private set; } = 1f;

    /// <summary>Tick of the retained reads; not the lighting engine's calculation time.</summary>
    public ulong? ReadTick { get; private set; }

    /// <summary>How many open-air samples the engine had computed at the last refresh. Zero means the field
    /// answers nothing, which is a different state from answering "dark" and is never treated as one.</summary>
    public int MeasuredSamples { get; private set; }

    // The field, as a lattice over the window: one entry per sampled cell, in row-major order, with
    // brightness already less the companion's own torch. A cell that is not open air, or that the engine
    // had not computed, is absent rather than dark.
    private readonly List<Sample> samples = new();
    private int sinceRefresh = RefreshTicks;
    private Rectangle lastFrame = Rectangle.Empty;
    // The torch state the field was built under, so a point query answers by the same rule as the lattice.
    private bool torchWasOut;
    private Point torchHand;

    private readonly record struct Sample(Point Tile, float Brightness);

    public void Update(NPC companion, Player player)
    {
        // The cadence is a cost saving, not a statement that the field is still true. A lighting frame that
        // has only just started being presented, or that has changed size, is a different instrument reading
        // from the one the field was built against, and answering from the old one is answering about a
        // world nobody measured. Scrolling moves the presented area without resizing it, so this costs
        // nothing while walking and fires exactly when the engine begins, ends or resizes its frame.
        Rectangle frame = Coverage.Current().Area;
        bool newFrame = frame.Width != lastFrame.Width || frame.Height != lastFrame.Height;
        lastFrame = frame;
        if (++sinceRefresh < RefreshTicks && !newFrame)
            return;
        sinceRefresh = 0;

        Point c = companion.Center.ToTileCoordinates();
        Point p = player.Center.ToTileCoordinates();
        AtCompanion = RawBrightness(c.X, c.Y);
        AtPlayer = RawBrightness(p.X, p.Y);

        // The companion's own torch, as the engine's own falloff would have spread it from the hand. It is
        // subtracted rather than cut out as a disc, because a disc throws away every real reading inside it
        // — including the dark tiles a torch has not actually reached — while a subtraction leaves them.
        // Read from the same body the bearer lights: it is out when the hand was free, so a companion
        // swinging a pickaxe contributes nothing and none is taken off.
        var self = companion.ModNPC as global::AICompanion.Companion.CharacterBody.CompanionNPC;
        bool torchOut = self?.Torch.Shown ?? false;
        Point hand = torchOut ? (companion.Center + new Vector2(companion.direction * 10f, -6f)).ToTileCoordinates() : default;
        torchWasOut = torchOut;
        torchHand = hand;

        // The window is screen-sized and centred on the companion, and it is deliberately not clipped to
        // where the screen happens to be. Clipping was the old approximation of "where has the engine
        // computed light", and the coverage test below is that question asked exactly: a tile inside the
        // screen rectangle the engine has not processed is still unmeasured, and a tile the engine has
        // processed outside it is a real reading. Asking the precise question and then also applying the
        // approximation only throws away readings the engine actually has.
        // ...and it is at least a neighbourhood around the body, because the screen is not always a real
        // rectangle: headless there is no screen at all, and a window derived from a zero width collapses to
        // the single column the companion stands in, which reads as a world with no light measured anywhere.
        // The lighting search carried this union before the field existed and guarded on the screen being
        // wide enough to mean anything; both consumers now inherit it from one place.
        Coverage coverage = Coverage.Current();
        int halfWidth = Math.Max(Main.screenWidth / 32, Weights.LightWindowMinimumHalfWidthTiles);
        int halfHeight = Math.Max(Main.screenHeight / 32, Weights.LightWindowMinimumHalfHeightTiles);
        int left = c.X - halfWidth, top = c.Y - halfHeight;
        int right = c.X + halfWidth, bottom = c.Y + halfHeight;

        samples.Clear();
        for (int x = left; x <= right; x += SampleStrideTiles)
            for (int y = top; y <= bottom; y += SampleStrideTiles)
            {
                if (!IsOpenAir(x, y) || !coverage.Contains(x, y))
                    continue;
                float lit = RawBrightness(x, y);
                if (torchOut)
                {
                    float mine = CarriedTorchAt(hand, x, y);
                    // The engine merges a light into the map by max and propagates it by max, so the torch
                    // never added to this tile: it either lost to what was already there, or it replaced it.
                    // A reading brighter than the torch could produce is therefore the world's own light and
                    // is kept whole. A reading the torch could account for tells us only that the ambient is
                    // no brighter than the torch — which answers the question when the torch itself is below
                    // the dark level (the place is dark and reads dark), and destroys it when the torch is
                    // above it, because every ambient from pitch black up to the torch looks identical. That
                    // last case is not dark and not lit: it is unmeasured, and it is dropped rather than
                    // stored, so no query counts it either way.
                    if (lit <= mine + TorchModelTolerance && mine >= Weights.LightDarkBelow)
                        continue;
                }
                samples.Add(new Sample(new Point(x, y), Math.Clamp(lit, 0f, 1f)));
            }
        MeasuredSamples = samples.Count;
        ReadTick = Main.GameUpdateCount;
    }

    /// <summary>
    /// Empty space a torch could light. Packed dirt, platforms and furniture are not darkness: averaging
    /// them in made a 5x5 of stone read as a dark room and hid real air pockets. This is the one place
    /// that ruling is enforced, so every consumer of the field inherits it.
    /// </summary>
    public static bool IsOpenAir(int x, int y)
    {
        if (!WorldGen.InWorld(x, y, 1)) return false;
        Tile tile = Main.tile[x, y];
        return !tile.HasUnactuatedTile;
    }

    /// <summary>How much of the open air measured within <paramref name="radiusTiles"/> of a tile is dark.
    /// <c>Measured</c> of zero means nobody has read this neighbourhood, which holds a decision rather
    /// than making one.</summary>
    public readonly record struct DarkReading(int Measured, int Dark, float Total)
    {
        public float DarkFraction => Measured == 0 ? 0f : Dark / (float)Measured;
        /// <summary>Mean brightness of the measured air. The share above answers "is there dark air here",
        /// which is the torch-holding question; this answers "is this area dark", which is the
        /// torch-placing one, and a half-lit neighbourhood fails it while passing the share.</summary>
        public float MeanBrightness => Measured == 0 ? 0f : Total / Measured;
        public bool Unmeasured => Measured == 0;
    }

    public DarkReading DarkAirNear(Point centre, int radiusTiles)
    {
        int measured = 0, dark = 0;
        float total = 0f;
        long radius2 = (long)radiusTiles * radiusTiles;
        for (int i = 0; i < samples.Count; i++)
        {
            long dx = samples[i].Tile.X - centre.X, dy = samples[i].Tile.Y - centre.Y;
            if (dx * dx + dy * dy > radius2) continue;
            measured++;
            total += samples[i].Brightness;
            if (samples[i].Brightness < Weights.LightDarkBelow) dark++;
        }
        return new DarkReading(measured, dark, total);
    }

    /// <summary>
    /// A connected run of dark samples. <paramref name="Centre"/> is the member nearest the run's average
    /// position — a member rather than the average itself, so it is always somewhere actually dark — and
    /// <paramref name="Tiles"/> is every sample in it, because a consumer looking for somewhere to put a
    /// torch needs the whole area: the only wall a region touches can be at its far end, and a box around
    /// one point would never see it.
    /// </summary>
    public readonly record struct DarkRegion(Point Centre, int DarkSamples, IReadOnlyList<Point> Tiles);

    /// <summary>
    /// The nearest region of dark air to <paramref name="from"/>, clustered so that a scattering of dark
    /// samples around one unlit chamber is one answer rather than a dozen. Nearest by the region's own
    /// centre, because walking to the near edge of a large dark area and lighting there leaves the rest of
    /// it dark and the next nomination standing where the companion already is.
    /// </summary>
    public DarkRegion? NearestDarkRegion(Point from, int maxTiles)
    {
        var all = DarkRegionsNearest(from, maxTiles);
        return all.Count == 0 ? null : all[0];
    }

    /// <summary>
    /// Every dark region in range, nearest first. A caller wants the list rather than only the nearest
    /// because "nearest" and "usable" are different questions: a torch needs a wall or a floor to attach
    /// to, and the nearest dark air can be an open shaft with nothing in it to hold one. Giving up at the
    /// first region would leave a lit band across a cave splitting the dark into a near half with no
    /// surface and a far half full of them, and nothing would ever be lit.
    /// </summary>
    public List<DarkRegion> DarkRegionsNearest(Point from, int maxTiles)
    {
        var dark = new List<Point>();
        for (int i = 0; i < samples.Count; i++)
            if (samples[i].Brightness < Weights.LightDarkBelow
                && Math.Abs(samples[i].Tile.X - from.X) <= maxTiles && Math.Abs(samples[i].Tile.Y - from.Y) <= maxTiles)
                dark.Add(samples[i].Tile);
        var found = new List<(long Distance, DarkRegion Region)>();
        if (dark.Count == 0) return new List<DarkRegion>();

        // Adjacency is one lattice step in any of the eight directions: two dark samples a stride apart are
        // the same unlit space, because the tiles between them were never sampled rather than found lit.
        var remaining = new HashSet<Point>(dark);
        var queue = new Queue<Point>();
        var members = new List<Point>();
        foreach (Point seed in dark)
        {
            if (!remaining.Remove(seed)) continue;
            queue.Clear();
            queue.Enqueue(seed);
            members.Clear();
            long sumX = 0, sumY = 0;
            int count = 0;
            while (queue.Count > 0)
            {
                Point t = queue.Dequeue();
                members.Add(t);
                sumX += t.X; sumY += t.Y; count++;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        Point neighbour = new(t.X + dx * SampleStrideTiles, t.Y + dy * SampleStrideTiles);
                        if (remaining.Remove(neighbour)) queue.Enqueue(neighbour);
                    }
            }
            // The representative point is the member nearest the centroid, never the centroid itself. A
            // centroid is not in its own region whenever the region is not convex, and the commonest dark
            // region in this game is exactly that shape: a ring of unlit air around a lit pocket, whose
            // average position is the middle of the lit hole. Nominating that sends every placement
            // candidate into the one part of the area that already has light.
            Point centroid = new((int)(sumX / count), (int)(sumY / count));
            Point centre = members[0];
            long nearestToCentroid = long.MaxValue;
            for (int i = 0; i < members.Count; i++)
            {
                long mx = members[i].X - centroid.X, my = members[i].Y - centroid.Y;
                long d = mx * mx + my * my;
                if (d >= nearestToCentroid) continue;
                nearestToCentroid = d;
                centre = members[i];
            }
            // Ranked by the nearest dark air in the region, not by the region's representative point. A
            // region is an area rather than a place, so "the nearest one" is the one whose darkness the
            // body reaches first: ranking by the representative point puts a compact region whose middle is
            // close ahead of a sprawling one that starts at arm's length, which is not what nearest means
            // to anyone walking there.
            long nearest = long.MaxValue;
            for (int i = 0; i < members.Count; i++)
            {
                long mx = members[i].X - from.X, my = members[i].Y - from.Y;
                nearest = Math.Min(nearest, mx * mx + my * my);
            }
            found.Add((nearest, new DarkRegion(centre, count, members.ToArray())));
        }
        found.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        return found.ConvertAll(static f => f.Region);
    }

    /// <summary>The field's own reading at a tile, for the overlay; absent where nothing was sampled.</summary>
    public float? BrightnessAt(Point tile)
    {
        for (int i = 0; i < samples.Count; i++)
            if (samples[i].Tile == tile) return samples[i].Brightness;
        return null;
    }

    /// <summary>
    /// What the engine says about one exact tile, under the same two rules the field is built from: open
    /// air the engine has computed, and not a reading the companion's own torch could account for. Absent
    /// means unmeasured, never bright.
    ///
    /// <para>This exists beside the lattice because a lattice cannot answer a question narrower than its
    /// own stride. Asking whether one candidate site is dark by counting samples within a few tiles of it
    /// reads at most a handful of lattice points, and a lit patch smaller than the stride can sit entirely
    /// between them. The lattice is for regional questions — how much dark air is around here, where is the
    /// nearest dark region — and this is for the one question that is genuinely about a single tile.</para>
    /// </summary>
    public float? MeasuredBrightnessAt(Point tile)
    {
        if (!IsOpenAir(tile.X, tile.Y) || !Coverage.Current().Contains(tile.X, tile.Y)) return null;
        float lit = RawBrightness(tile.X, tile.Y);
        if (!torchWasOut) return lit;
        float mine = CarriedTorchAt(torchHand, tile.X, tile.Y);
        return lit <= mine + TorchModelTolerance && mine >= Weights.LightDarkBelow ? null : lit;
    }

    /// <summary>
    /// The measured open air in a disc around one tile, sampled from the engine at its own stride rather
    /// than read off the lattice, and its mean brightness. This is what decides whether a particular spot
    /// wants a torch, and it is a neighbourhood rather than a point because a torch lights an area: a tile
    /// that is itself dark but sits against a lit room needs no torch, and asking only about the tile says
    /// it does. It cannot come from the lattice, whose stride is wider than the lit patches that have to be
    /// resolved here — a lit disc narrower than the stride can sit entirely between two samples.
    /// </summary>
    public DarkReading MeasuredAround(Point centre, int radiusTiles, int strideTiles)
    {
        Coverage coverage = Coverage.Current();
        int stride = Math.Max(1, strideTiles);
        int measured = 0, dark = 0;
        float total = 0f;
        for (int dx = -radiusTiles; dx <= radiusTiles; dx += stride)
            for (int dy = -radiusTiles; dy <= radiusTiles; dy += stride)
            {
                if (dx * dx + dy * dy > radiusTiles * radiusTiles) continue;
                int x = centre.X + dx, y = centre.Y + dy;
                if (!IsOpenAir(x, y) || !coverage.Contains(x, y)) continue;
                float lit = RawBrightness(x, y);
                if (torchWasOut)
                {
                    float mine = CarriedTorchAt(torchHand, x, y);
                    if (lit <= mine + TorchModelTolerance && mine >= Weights.LightDarkBelow) continue;
                }
                measured++;
                total += lit;
                if (lit < Weights.LightDarkBelow) dark++;
            }
        return new DarkReading(measured, dark, total);
    }

    private static float RawBrightness(int x, int y)
        => WorldGen.InWorld(x, y, 1) ? MathHelper.Clamp(Lighting.Brightness(x, y), 0f, 1f) : 0f;

    /// <summary>
    /// The brightest the companion's own torch could make a tile, modelled the way the engine spreads it.
    /// <see cref="LightMap.LightDecayThroughAir"/> is 0.91 and <c>LegacyLighting._negLight</c> is the same
    /// 0.91 in its colour mode, applied as a multiply per tile; the blur runs a horizontal pass and a
    /// vertical pass in turn, so a light reaching a tile offset by (dx, dy) has been multiplied |dx| + |dy|
    /// times, which makes the falloff Manhattan rather than round. Propagation stops below 0.0185 in the
    /// same loop, so a torch reaches exactly nothing past that. <see cref="Lighting.Brightness"/> is the
    /// mean of the three channels times <see cref="Lighting.GlobalBrightness"/>, so the torch is reduced
    /// the same way before it is compared. If any of those four numbers drifts from the engine, the model
    /// stops bounding what the engine actually wrote and the companion reads its own torchlight as the
    /// room's.
    ///
    /// <para>This is an upper bound rather than an estimate, and deliberately so: it decays only through
    /// air, while the engine decays through solid at 0.56, so near a wall the real torch is dimmer than
    /// this says. Bounding high means hiding a few samples the torch did not actually reach, which costs a
    /// little coverage; bounding low would mean keeping samples the torch did brighten and calling the
    /// companion's own light the room's, which is the failure this whole field exists to prevent.</para>
    /// </summary>
    private const float DecayThroughAirPerTile = 0.91f;
    private const float PropagationCutoff = 0.0185f;
    /// <summary>Slack on the comparison against the modelled torch, for the difference between the engine's
    /// per-channel blur and this mean-of-channels model of it. Without it a tile the torch lit to exactly
    /// its modelled value reads as the world's own light through floating-point noise.</summary>
    private const float TorchModelTolerance = 0.02f;

    private static float CarriedTorchAt(Point hand, int x, int y)
    {
        TorchID.TorchColor(TorchID.Torch, out float r, out float g, out float b);
        float source = Lighting.GlobalBrightness * (r + g + b) / 3f;
        int steps = Math.Abs(x - hand.X) + Math.Abs(y - hand.Y);
        float reached = source * MathF.Pow(DecayThroughAirPerTile, steps);
        return reached < PropagationCutoff ? 0f : reached;
    }

    /// <summary>
    /// Reads light only where the game's lighting engine has computed it. `Lighting.Brightness` answers zero
    /// for a dark tile and also for a tile outside the engine's buffers, so a zero alone is not darkness.
    /// Colour mode presents a processed area and its GetColor answers zero outside it; the legacy modes index
    /// a camera-sized state buffer from the requested rectangle widened by the off-screen margin and answer
    /// zero beyond it. This asks the active engine that same bounds question, through fields the engine keeps
    /// private, and fails loudly at first use if they move rather than silently treating the whole world as
    /// measured.
    /// </summary>
    public readonly record struct Coverage(bool Legacy, Rectangle Area, float LegacyColumns, float LegacyRows)
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

        public bool Contains(int x, int y)
        {
            if (Area.Width <= 0) return false;
            if (!Legacy) return Area.Height > 0 && Area.Contains(x, y);
            // LegacyLighting.GetColor's own bounds: the buffer index runs from the requested rectangle's
            // corner less the off-screen margin, and a camera-sized buffer ends the valid range.
            int column = x - Area.X + Lighting.OffScreenTiles, row = y - Area.Y + Lighting.OffScreenTiles;
            return column >= 0 && row >= 0 && column < LegacyColumns && row < LegacyRows;
        }

        /// <summary>The active engine's computed bounds, snapshotted once so a sweep reads one consistent frame.</summary>
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
    }
}
