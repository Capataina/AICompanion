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

/// <summary>
/// Lighting reads only light the engine computed. Scenes write the colour engine's own presented light map
/// and processed area, headless, and prepare the production lighting activity against them. Unmeasured light
/// is no opportunity however deep the companion stands; a measured dark area is; a measured lit area is not.
/// Carried light — the companion's shown torch, or a torch the player holds — does not make an area look lit,
/// while the same light with nobody carrying it does. A site whose neighbourhood is already lit loses to one that
/// is not. With no torch left, a dark area offers nothing to walk to.
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
            LightingReadsOnlyMeasuredDarkness();
            DistantDarkAirIsOfferedWhenFeetAreInLight();
            Console.WriteLine("useful assistance: unmeasured light, measured dark and lit areas, carried torches, lit neighbourhoods, distant dark air and an exhausted supply pass");
            return 0;
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
        var ownTorchShown = PrepareLighting((x, y) => DiscAt(20, 58, 9, x, y), torch: true, companionTorchShown: true);
        var sameLightUncarried = PrepareLighting((x, y) => DiscAt(20, 58, 9, x, y), torch: true);
        var playerHoldsTorch = PrepareLighting((x, y) => DiscAt(20, 58, 9, x, y), torch: false, playerHoldsTorch: true);
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
        Require(ownTorchShown.Score > 0 && ownTorchShown.Eligibility == Offer.Usable,
            $"the companion's own shown torch must not make a dark area look lit; {ledger}");
        Require(sameLightUncarried.Score > 0 && sameLightUncarried.Target is Vector2 uncarriedSite
            && Vector2.Distance(uncarriedSite, new Vector2(20 * 16f + 8f, 58 * 16f + 8f)) > 9 * 16f,
            $"world light in a disc must not hide dark air outside it; {ledger}");
        Require(playerHoldsTorch.Score > 0 && playerHoldsTorch.Eligibility == Offer.Usable,
            $"a torch the player holds lights the area only while it is carried there; {ledger}");
        Require(exhaustedDark.Score == 0 && exhaustedDark.Target == null && exhaustedDark.Eligibility == Offer.KnownUnusable && exhaustedDark.Reason == "no-torch-supply",
            $"with the last torch gone a dark area offers no trip; {ledger}");
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

    private static Offered PrepareLighting(Func<int, int, float>? light, bool torch, bool companionTorchShown = false, bool playerHoldsTorch = false)
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
        else WriteMeasuredLight(new Rectangle(0, 0, 100, 100), light);
        typeof(TorchBearer).GetProperty("Shown")!.GetSetMethod(true)!.Invoke(ctx.Companion.Torch, new object[] { companionTorchShown });
        // Lighting reads two senses now rather than measuring per candidate, so the scene has to be observed
        // before it is prepared: the light field must hold the frame written above, and the reach region must
        // have settled, because work that reads reachability refuses an unfinished flood instead of walking
        // at it — an unprimed region would make every row below read "not yet known" rather than its verdict.
        var brain = ctx.Companion.Brain;
        brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Motor);
        var home = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer, ctx.Player.Bottom);
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
            brain.Positioner.Resolve(home, brain.Senses, null);
        Require(brain.Positioner.ReachComplete, "the lighting scenes need a settled reach region before preparing");
        var action = new LightUsefulArea();
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        return new Offered(score, action.Eligibility, action.EligibilityReason, action.ActivityTarget);
    }

    /// <summary>Presents a computed light map over <paramref name="area"/> in the colour engine, the way its own
    /// blur step presents one: the light values and the processed area swap in together.</summary>
    internal static void WriteMeasuredLight(Rectangle area, Func<int, int, float> brightness)
    {
        Lighting.Mode = LightMode.Color;
        Lighting.GlobalBrightness = 1f;
        object engine = ColourEngine();
        var map = (LightMap)engine.GetType().GetField("_activeLightMap", InstanceField)!.GetValue(engine)!;
        map.SetSize(area.Width, area.Height);
        for (int x = 0; x < area.Width; x++)
            for (int y = 0; y < area.Height; y++)
                map[x, y] = new Vector3(brightness(area.X + x, area.Y + y));
        engine.GetType().GetField("_activeProcessedArea", InstanceField)!.SetValue(engine, area);
    }

    /// <summary>Leaves the colour engine with nothing presented, as before its first completed frame.</summary>
    internal static void ClearMeasuredLight()
    {
        Lighting.Mode = LightMode.Color;
        object engine = ColourEngine();
        engine.GetType().GetField("_activeProcessedArea", InstanceField)!.SetValue(engine, Rectangle.Empty);
    }

    private static object ColourEngine() => typeof(Lighting).GetField("NewEngine", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
