#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Light;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// Every light that is in the world only while something is carrying it, read from the engine's own list
/// rather than enumerated by us. The engine already draws this line and it draws it exactly where we need
/// it: world light — placed torches, lava, glowing tiles, sunlight — comes from the tile scanner, and
/// everything else arrives through <see cref="Lighting.AddLight"/>, which the colour engine collects in a
/// private per-frame list before merging it into the map. So the rule is "read the list", not "know every
/// source": a held torch, a light pet, a glowstick in flight, a mining helmet and a lantern from a mod
/// nobody here has heard of are all in it by construction, and so is the companion's own torch, which
/// stops being a special case.
///
/// <para>The list is what makes the companion light a mine as it follows someone through it. Without it,
/// the passage beside a player carrying a torch reads as lit, nothing is placed, and the passage is dark
/// again the moment he walks on — the opposite of lighting the mine as you go.</para>
///
/// <para><b>Which batch is the right one to read, and why there is no lag to apologise for.</b>
/// <c>LightingEngine.ProcessArea</c> is a four-state machine (minimap, metrics, scan, blur) that advances
/// one state per call, so the list accumulates across up to four frames and is consumed only on the blur
/// state: <c>ProcessBlur</c> calls <c>ApplyPerFrameLights</c>, which merges each entry and then clears the
/// list, then <c>Present</c> swaps the working map into the active one we read. The batch cleared by the
/// last blur is therefore not an approximation of the transients in the map we are reading — it is
/// exactly them. We observe once per brain tick, which is once per frame, so we see the list grow and then
/// drop to nothing; the snapshot taken immediately before a drop is that batch, and the drop is how the
/// clear is detected without reaching into a private method. Until a drop has ever been seen, the current
/// list stands in, which is what the first frames of a world and every headless fixture get.</para>
///
/// <para>A MonoMod hook on <c>Lighting.AddLight</c> was the alternative and lost on two counts: the field
/// is plainly readable at our own tick so the hook buys no access, and a hook would record calls made
/// after the blur as though they were in the map, which is the very error the count-drop rule avoids. It
/// remains the fallback if a future version clears the list somewhere we cannot observe.</para>
///
/// <para>Colour mode only. Legacy lighting does not fill this list, so there the sense discounts nothing
/// and a transient reads as room light — the same limitation the coverage bounds already carry, and the
/// reason the mode is checked rather than assumed.</para>
/// </summary>
public static class TransientLights
{
    /// <summary>One transient light, reduced to what the falloff model needs: where it is and how bright it
    /// is in the same units <see cref="Lighting.Brightness"/> reports, so the two are directly comparable.</summary>
    public readonly record struct Source(Point Tile, float Strength);

    /// <summary>The furthest any light can carry. The engine stops propagating below 0.0185 and multiplies
    /// by 0.91 per tile, so even a light at full strength is gone by this many tiles and a source outside
    /// the window by more than this cannot touch a sample inside it. Derived rather than chosen, because a
    /// guessed radius is either a wrong answer or a wasted scan.</summary>
    public static readonly int MaxReachTiles =
        (int)Math.Ceiling(Math.Log(PropagationCutoff) / Math.Log(DecayThroughAirPerTile));

    private const float DecayThroughAirPerTile = 0.91f;
    private const float PropagationCutoff = 0.0185f;

    private static readonly List<Source> complete = new();
    private static readonly Dictionary<Point, float> pending = new();
    private static int lastRawCount = -1;
    private static bool seenAClear;

    /// <summary>How many distinct transient sources the last complete batch held, for the telemetry and for
    /// a fixture that needs to know the scene actually produced one.</summary>
    public static int Count => complete.Count;

    /// <summary>The sources of the last complete batch, in no particular order.</summary>
    public static IReadOnlyList<Source> Sources => complete;

    /// <summary>Forgets every batch. For a fixture moving between scenes, and for a world unload: a source
    /// from the last world is not a source in this one, and nothing else would ever retire it.</summary>
    public static void Forget()
    {
        complete.Clear();
        pending.Clear();
        lastRawCount = -1;
        seenAClear = false;
    }

    /// <summary>
    /// Takes this frame's view of the engine's list, keeping only what could matter inside
    /// <paramref name="window"/>, and promotes the previous view to the complete batch when the engine has
    /// cleared the list since.
    /// </summary>
    public static void Snapshot(Rectangle window)
    {
        if (Lighting.Mode != LightMode.Color) { complete.Clear(); return; }
        var raw = Handles.List;
        if (raw == null) { complete.Clear(); return; }

        int count = raw.Count;
        // The list shrank, so the blur ran and cleared it: what we had gathered before that is the batch
        // the blur merged, which is the batch baked into the map being read now.
        if (count < lastRawCount)
        {
            complete.Clear();
            foreach (var entry in pending) complete.Add(new Source(entry.Key, entry.Value));
            pending.Clear();
            seenAClear = true;
        }
        lastRawCount = count;

        // Read the window generously: a source outside it can still light a tile inside it, up to the
        // distance anything can carry at all.
        Rectangle reach = window;
        reach.Inflate(MaxReachTiles, MaxReachTiles);
        for (int i = 0; i < count; i++)
        {
            if (!Handles.Read(raw, i, out Point tile, out float strength)) continue;
            // A light too weak to reach the dark threshold at its own tile cannot make anything read lit,
            // so it is dropped here rather than carried through the per-sample loop. This is what keeps a
            // scene full of dust and gore lights from costing anything.
            if (strength < Weights.LightDarkBelow) continue;
            if (!reach.Contains(tile)) continue;
            // Merged by maximum, keyed on the tile, because that is how the engine merges them and because
            // a source that is re-added every frame would otherwise accumulate one entry per frame: the
            // list is only cleared every fourth frame, and headless it is never cleared at all.
            pending[tile] = pending.TryGetValue(tile, out float had) ? Math.Max(had, strength) : strength;
        }

        // Before the engine has ever cleared the list in front of us — the opening frames of a world, and
        // every headless fixture, where nothing drives ProcessArea — the gathered view is the best there
        // is, and it is correct rather than merely available: with no blur having run, no batch has been
        // consumed, so everything gathered is still pending in the map's next blur.
        if (!seenAClear)
        {
            complete.Clear();
            foreach (var entry in pending) complete.Add(new Source(entry.Key, entry.Value));
        }
    }

    /// <summary>
    /// The brightest any transient could have made this tile. Maximum rather than sum, because the engine's
    /// blur is a running maximum per channel — <c>LightMap.BlurLine</c> takes the larger of the running
    /// value and the tile's own and propagates that — so two lights reaching one tile leave the brighter of
    /// the two there, never their total.
    /// </summary>
    public static float BrightestAt(int x, int y)
    {
        float best = 0f;
        for (int i = 0; i < complete.Count; i++)
        {
            Source source = complete[i];
            int steps = Math.Abs(x - source.Tile.X) + Math.Abs(y - source.Tile.Y);
            if (steps > MaxReachTiles) continue;
            float reached = source.Strength * MathF.Pow(DecayThroughAirPerTile, steps);
            if (reached >= PropagationCutoff && reached > best) best = reached;
        }
        return best;
    }

    /// <summary>The reflection this needs, resolved once. A missing name is a game version that has moved
    /// the list, and it fails by name here rather than quietly discounting nothing for ever after.</summary>
    private static class Handles
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly object? Engine =
            typeof(Lighting).GetField("NewEngine", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        private static readonly FieldInfo? ListField =
            Engine?.GetType().GetField("_perFrameLights", Instance);
        private static readonly FieldInfo? PositionField =
            ListField?.FieldType.GetGenericArguments()[0].GetField("Position");
        private static readonly FieldInfo? ColorField =
            ListField?.FieldType.GetGenericArguments()[0].GetField("Color");

        public static System.Collections.IList? List =>
            Engine == null || ListField == null ? null : (System.Collections.IList?)ListField.GetValue(Engine);

        public static bool Read(System.Collections.IList raw, int index, out Point tile, out float strength)
        {
            tile = default;
            strength = 0f;
            if (PositionField == null || ColorField == null) return false;
            object? entry = raw[index];
            if (entry == null) return false;
            tile = (Point)PositionField.GetValue(entry)!;
            var colour = (Vector3)ColorField.GetValue(entry)!;
            // The same reduction Lighting.Brightness applies, so a modelled source and an observed sample
            // are in one scale: the mean of the three channels times the global brightness.
            strength = Math.Clamp(Lighting.GlobalBrightness * (colour.X + colour.Y + colour.Z) / 3f, 0f, 1f);
            return true;
        }
    }
}
