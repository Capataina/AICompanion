extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Light;
using Terraria.ID;
using LightUsefulArea = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.LightUsefulArea;
using Offer = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using Policy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using TorchBearer = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.TorchBearer;
using CaptureWorldLight = live::AICompanion.Companion.Brain.Infrastructure.Observation.CaptureWorldLight;
using WorldLight = live::AICompanion.Companion.Brain.Infrastructure.Observation.WorldLight;

/// <summary>
/// Lighting reads only light the engine computed. Scenes leave the colour engine between a scan of the world's own light
/// and its blur, with the presented map showing that light and anything carried, headless, and prepare the production
/// lighting activity against them. Unmeasured light is no opportunity however deep the companion stands; a measured dark
/// area is; a measured lit area is not. Carried light — the companion's shown torch, or a torch the player holds — does not
/// make an area look lit, so the site offered sits inside the carried light's disc, while the same light standing in the
/// world does, so the site sits outside it. A site whose neighbourhood is already lit loses to one that is not. The
/// companion's torches are its own, so a dark area is worth a trip whether or not anyone carries one.
/// </summary>
internal static class VerifyUsefulAssistance
{
    private const BindingFlags InstanceField = BindingFlags.NonPublic | BindingFlags.Instance;

    public static int Run()
    {
        Preferences saved = Preferences.Current;
        LightMode mode = Lighting.Mode;
        float brightness = Lighting.GlobalBrightness;
        try
        {
            Preferences.Current = new Preferences { TorchPlacement = true, PotBreaking = false };
            // Both rows run and file a sub-row; the fixture fails afterwards if either did.
            int failed = RunOneRow.Case(LightingReadsOnlyMeasuredDarkness, "useful assistance")
                + RunOneRow.Case(DistantDarkAirIsOfferedWhenFeetAreInLight, "useful assistance");
            if (failed == 0)
                Console.WriteLine("useful assistance: unmeasured light, measured dark and lit areas, carried torches, lit neighbourhoods, distant dark air and a dark area with no torch anywhere pass");
            return failed;
        }
        finally
        {
            Preferences.Current = saved;
            ClearMeasuredLight();
            Lighting.Mode = mode;
            Lighting.GlobalBrightness = brightness;
        }
    }

    private readonly record struct Offered(float Score, Offer Eligibility, string Reason, Vector2? Target)
    {
        public override string ToString() => $"score={Score:0.000} {Eligibility}:{Reason} target={Target}";
    }

    private static void LightingReadsOnlyMeasuredDarkness()
    {
        // The ore world puts the companion's and the player's centres on tile (20, 58).
        static float Dark(int x, int y) => .05f;
        static float DiscAt(int cx, int cy, int radius, int x, int y)
            => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius ? 1f : .05f;
        var unmeasured = PrepareLighting(null, torch: true);
        var dark = PrepareLighting(Dark, torch: true);
        var lit = PrepareLighting((_, _) => .8f, torch: true);
        var ownTorchShown = PrepareLighting(Dark, torch: true, companionTorchShown: true, carried: (x, y) => DiscAt(20, 58, 9, x, y));
        var sameLightUncarried = PrepareLighting((x, y) => DiscAt(20, 58, 9, x, y), torch: true);
        var playerHoldsTorch = PrepareLighting(Dark, torch: false, playerHoldsTorch: true, carried: (x, y) => DiscAt(20, 58, 9, x, y));
        bool InsideTheDisc(Vector2? site) => site is Vector2 s && Vector2.Distance(s, new Vector2(20 * 16f + 8f, 58 * 16f + 8f)) <= 9 * 16f;
        var exhaustedDark = PrepareLighting(Dark, torch: false);
        var exhaustedUnmeasured = PrepareLighting(null, torch: false);
        string ledger = $"unmeasured {unmeasured}; dark {dark}; lit {lit}; own torch shown {ownTorchShown}; same light uncarried {sameLightUncarried}; "
            + $"player holds torch {playerHoldsTorch}; no torch, dark {exhaustedDark}; no torch, unmeasured {exhaustedUnmeasured}";

        Require(unmeasured.Score == 0 && unmeasured.Target == null,
            $"light the engine never computed is not darkness; {ledger}");
        Require(dark.Score > 0 && dark.Eligibility == Offer.Usable && dark.Target != null,
            $"a measured dark area with a torch to place is a lighting opportunity; {ledger}");
        Require(lit.Score == 0 && lit.Target == null,
            $"a measured lit area needs no torch; {ledger}");
        Require(ownTorchShown.Score > 0 && ownTorchShown.Eligibility == Offer.Usable && InsideTheDisc(ownTorchShown.Target),
            $"the companion's own shown torch must not make a dark area look lit, so the nearest site is still one it lights; {ledger}");
        Require(sameLightUncarried.Score > 0 && sameLightUncarried.Target is Vector2 uncarriedSite
            && Vector2.Distance(uncarriedSite, new Vector2(20 * 16f + 8f, 58 * 16f + 8f)) > 9 * 16f,
            $"world light in a disc must not hide dark air outside it; {ledger}");
        Require(playerHoldsTorch.Score > 0 && playerHoldsTorch.Eligibility == Offer.Usable && InsideTheDisc(playerHoldsTorch.Target),
            $"a torch the player holds lights the area only while it is carried there, so a site under it is still offered; {ledger}");
        Require(exhaustedDark.Score > 0 && exhaustedDark.Eligibility == Offer.Usable && exhaustedDark.Target != null,
            $"the companion's torches are its own, so a measured dark area with no torch anywhere is still a lighting opportunity; {ledger}");
        Require(exhaustedUnmeasured.Score == 0 && exhaustedUnmeasured.Target == null,
            $"with no torch and nothing measured there is no phantom lighting trip; {ledger}");

        Vector2 firstSite = dark.Target!.Value;
        Point site = firstSite.ToTileCoordinates();
        var litNeighbourhood = PrepareLighting((x, y) => DiscAt(site.X, site.Y, 4, x, y), torch: true);
        Require(litNeighbourhood.Eligibility == Offer.Usable && litNeighbourhood.Target is Vector2 moved && Vector2.Distance(moved, firstSite) > 4 * 16f,
            $"a site whose neighbourhood is already lit must lose to a dark one; first site {site}; lit neighbourhood {litNeighbourhood}; {ledger}");
    }

    /// <summary>
    /// The playtest case: feet sit in a lit bubble while dark air is on the same screen. Lighting
    /// must walk to that air rather than report the local disc as already lit.
    /// </summary>
    private static void DistantDarkAirIsOfferedWhenFeetAreInLight()
    {
        static float LitFeetDarkPocket(int x, int y)
        {
            int dx = x - 20, dy = y - 58;
            return dx * dx + dy * dy <= 18 * 18 ? 0.8f : 0.05f;
        }
        var offered = PrepareLighting(LitFeetDarkPocket, torch: true);
        Require(offered.Score > 0 && offered.Eligibility == Offer.Usable && offered.Target != null,
            $"dark air outside a lit disc around the feet must be a lighting job; {offered}");
        Point tile = offered.Target!.Value.ToTileCoordinates();
        int dist2 = (tile.X - 20) * (tile.X - 20) + (tile.Y - 58) * (tile.Y - 58);
        Require(dist2 > 10 * 10,
            $"the offered site {tile} is still in the lit disc around the feet; {offered}");
    }

    private static Offered PrepareLighting(Func<int, int, float>? light, bool torch, bool companionTorchShown = false, bool playerHoldsTorch = false,
        Func<int, int, float>? carried = null)
    {
        // The ore sits far from the companion so its tile cannot compete with torch sites.
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(70, 59));
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        for (int i = 0; i < ctx.Companion.Bag.Items.Length; i++) ctx.Companion.Bag.Items[i] = new Item();
        ctx.Player.selectedItem = 1;
        if (torch || playerHoldsTorch)
        {
            Item supply = new();
            supply.SetDefaults(ItemID.Torch);
            supply.stack = 5;
            ctx.Player.inventory[0] = supply;
            if (playerHoldsTorch) ctx.Player.selectedItem = 0;
        }
        if (light == null) ClearMeasuredLight();
        else WriteMeasuredLight(new Rectangle(0, 0, 100, 100), light, carried);
        typeof(TorchBearer).GetProperty("Shown")!.GetSetMethod(true)!.Invoke(ctx.Companion.Torch, new object[] { companionTorchShown });
        // Lighting reads two senses now rather than measuring per candidate, so the scene has to be observed
        // before it is prepared: the light field must hold the frame written above, and the reach region must
        // have settled, because work that reads reachability refuses an unfinished flood instead of walking
        // at it — an unprimed region would make every row below read "not yet known" rather than its verdict.
        var brain = ctx.Companion.Brain;
        brain.Senses.Update(ctx.Npc, ctx.Player);
        var home = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer, ctx.Player.Bottom);
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
            brain.Positioner.Resolve(home, brain.Senses);
        Require(brain.Positioner.ReachComplete, "the lighting scenes need a settled reach region before preparing");
        var action = new LightUsefulArea();
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        return new Offered(score, action.Eligibility, action.EligibilityReason, action.ActivityTarget);
    }

    /// <summary>
    /// Presents light over <paramref name="area"/> as the colour engine holds it between a scan and its blur: <paramref name="world"/>
    /// is the world's own light, in the working map the scan fills, and the presented map shows it with <paramref name="carried"/>
    /// merged in by maximum, the way the blur merges what a body carries. The values are the final light rather than sources,
    /// so the presented map carries no decay, which the scan's blur takes from it and so leaves every value as written; then the
    /// scan is taken through the draw's own observation.
    /// </summary>
    internal static void WriteMeasuredLight(Rectangle area, Func<int, int, float> world, Func<int, int, float>? carried = null)
    {
        Lighting.Mode = LightMode.Color;
        Lighting.GlobalBrightness = 1f;
        object engine = ColourEngine();
        LightMap scan = EngineMap(engine, "_workingLightMap"), presented = EngineMap(engine, "_activeLightMap");
        scan.SetSize(area.Width, area.Height);
        presented.SetSize(area.Width, area.Height);
        scan.NonVisiblePadding = presented.NonVisiblePadding = 0;
        for (int x = 0; x < area.Width; x++)
            for (int y = 0; y < area.Height; y++)
            {
                Vector3 own = new(world(area.X + x, area.Y + y));
                scan[x, y] = own;
                scan.SetMaskAt(x, y, LightMaskMode.None);
                presented[x, y] = carried == null ? own : Vector3.Max(own, new Vector3(carried(area.X + x, area.Y + y)));
                presented.SetMaskAt(x, y, LightMaskMode.None);
            }
        presented.LightDecayThroughAir = presented.LightDecayThroughSolid = 0f;
        presented.LightDecayThroughWater = presented.LightDecayThroughHoney = Vector3.Zero;
        TakeTheScan(engine, area);
    }

    /// <summary>Leaves the colour engine as a process that has drawn nothing, with the colour engine selected.</summary>
    internal static void ClearMeasuredLight()
    {
        Lighting.Mode = LightMode.Color;
        ForgetEngineLight();
    }

    /// <summary>No area presented or scanned, no scan taken, the presented map's decay back to the engine's own, and the engine
    /// at the start of its cycle, so the next observation takes nothing a previous scene left.</summary>
    internal static void ForgetEngineLight()
    {
        object engine = ColourEngine();
        engine.GetType().GetField("_activeProcessedArea", InstanceField)!.SetValue(engine, Rectangle.Empty);
        engine.GetType().GetField("_workingProcessedArea", InstanceField)!.SetValue(engine, Rectangle.Empty);
        RestoreEngineDecay(EngineMap(engine, "_activeLightMap"));
        SetEngineState(engine, "MinimapUpdate");
        WorldLight.Forget();
    }

    /// <summary>
    /// Leaves the engine between a scan of <paramref name="area"/> and its blur, with the presented map covering the same area,
    /// and makes the observation the draw makes after lighting: once in another state, so the scan counts as new, then once
    /// on it. Whatever the scene painted into the working map is then the world's light the sense reads.
    /// </summary>
    internal static void TakeTheScan(object engine, Rectangle area)
    {
        engine.GetType().GetField("_workingProcessedArea", InstanceField)!.SetValue(engine, area);
        engine.GetType().GetField("_activeProcessedArea", InstanceField)!.SetValue(engine, area);
        var draw = new CaptureWorldLight();
        SetEngineState(engine, "MinimapUpdate");
        draw.PostDrawTiles();
        SetEngineState(engine, "Blur");
        draw.PostDrawTiles();
    }

    /// <summary>The decay rates a <c>LightMap</c> is constructed with, which are the engine's own before any vision effect.</summary>
    internal static void RestoreEngineDecay(LightMap map)
    {
        map.LightDecayThroughAir = .91f;
        map.LightDecayThroughSolid = .56f;
        map.LightDecayThroughWater = new Vector3(.88f, .96f, 1.015f) * .91f;
        map.LightDecayThroughHoney = new Vector3(.75f, .7f, .6f) * .91f;
    }

    internal static void SetEngineState(object engine, string state)
    {
        FieldInfo field = engine.GetType().GetField("_state", InstanceField)!;
        field.SetValue(engine, Enum.Parse(field.FieldType, state));
    }

    internal static LightMap EngineMap(object engine, string name) => (LightMap)engine.GetType().GetField(name, InstanceField)!.GetValue(engine)!;

    internal static object ColourEngine() => typeof(Lighting).GetField("NewEngine", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
