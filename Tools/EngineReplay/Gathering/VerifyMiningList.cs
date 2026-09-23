extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using MiningList = live::AICompanion.Companion.PlayerIntegration.CompanionMiningList;
using ListMode = live::AICompanion.Companion.PlayerIntegration.MiningListMode;
using WorkPolicies = live::AICompanion.Companion.Brain.Activities.WorkPolicies;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using OfferEligibility = live::AICompanion.Companion.Brain.Activities.OfferEligibility;

/// <summary>
/// The mining list's three layers against the live code: the found set a character keeps, the marks and the
/// mode it saves, and the mining activity refusing the ore the list leaves. Every refusal row carries its own
/// control in the same scene, the same ore offered once the mark or mode says so, because a row that only
/// shows "nothing was offered" passes against a scene with no reachable ore at all.
/// </summary>
internal static class VerifyMiningList
{
    public static int Run()
    {
        Preferences saved = Preferences.Current;
        try
        {
            int failed = 0;
            failed += RunOneRow.Case("a fresh list mines every ore", AFreshListMinesEveryOre, "mining list");
            failed += RunOneRow.Case("a marked ore under Skip marked is not offered, and is the moment its mark is cleared", AMarkedOreUnderSkipIsNotOffered, "mining list");
            failed += RunOneRow.Case("an unmarked ore under Only marked is not offered, and is the moment it is marked", AnUnmarkedOreUnderOnlyIsNotOffered, "mining list");
            failed += RunOneRow.Case("a job under way ends on the preparation after its ore is marked", AJobUnderWayEndsWhenItsOreIsMarked, "mining list");
            failed += RunOneRow.Case("mimic does not help with an ore the list leaves", MimicDoesNotHelpWithALeftOre, "mining list");
            failed += RunOneRow.Case("an allowed ore beside a left one is still work", AnAllowedOreBesideALeftOneIsWork, "mining list");
            failed += RunOneRow.Case("an ore entering the inventory is known, and stays known when it leaves", AnOreEnteringTheInventoryStaysKnown, "mining list");
            failed += RunOneRow.Case("the list survives save and load and keeps an unloaded mod's ore", TheListSurvivesSaveAndLoad, "mining list");
            failed += RunOneRow.Case("an absent or malformed list mines every ore", AnAbsentOrMalformedListMinesEverything, "mining list");
            return failed;
        }
        finally
        {
            Preferences.Current = saved;
            WorkPolicies.Mining = WorkPolicy.Opportunistic;
        }
    }

    private static MiningList Fresh()
    {
        Preferences.Current = new Preferences();
        return Preferences.Current.MiningList;
    }

    private static void AFreshListMinesEveryOre()
    {
        MiningList list = Fresh();
        Require(list.Mode == ListMode.SkipMarked && list.Known.Count == 0 && list.Allows(TileID.Copper) && list.Allows(TileID.Iron),
            $"a new list must skip nothing and know nothing; mode={list.Mode} known={list.Known.Count}");
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(25, 59));
        Require(Bound(ctx) == new Point(25, 59),
            $"exposed copper under a fresh list must be bound work; {DriveGatheringThroughTheCourse.Account(ctx)}");
    }

    // The list is honoured where work is found, which is the census now: a left ore is published refused by
    // `mining-list` and never bound, and a changed mark or mode reopens the census, so the control half of each
    // row is answered by the very next decision.
    private static Point? Bound(live::AICompanion.Companion.Brain.Activities.ActionContext ctx)
        => DriveGatheringThroughTheCourse.Decide(ctx, "mine") is { } step ? DriveGatheringThroughTheCourse.Tile(step) : null;

    private static string CensusReason(live::AICompanion.Companion.Brain.Activities.ActionContext ctx, Point ore)
        => DriveGatheringThroughTheCourse.Site(ctx, "mine-target", ore) is { } site ? $"{site.Admission}/{site.Reason}" : "absent";

    private static void AMarkedOreUnderSkipIsNotOffered()
    {
        MiningList list = Fresh();
        list.SetMarked(TileID.Copper, true);
        Point ore = new(25, 59);
        var (action, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        Require(Bound(ctx) == null && action.JobId == 0,
            $"marked copper under Skip marked must not become a job; {DriveGatheringThroughTheCourse.Account(ctx)} job={action.JobId}");
        Require(CensusReason(ctx, ore) == "unusable/mining-list",
            $"the refusal must name the list rather than absent ore; got {CensusReason(ctx, ore)}");
        // The control, on the next decision rather than after any cadence: clearing the mark is new evidence about
        // which ores are work, exactly as a change of pick is.
        list.SetMarked(TileID.Copper, false);
        Require(Bound(ctx) == ore,
            $"the same copper must be bound on the decision after its mark is cleared; {DriveGatheringThroughTheCourse.Account(ctx)}");
    }

    private static void AnUnmarkedOreUnderOnlyIsNotOffered()
    {
        MiningList list = Fresh();
        list.Mode = ListMode.OnlyMarked;
        Point ore = new(25, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        Require(Bound(ctx) == null && CensusReason(ctx, ore) == "unusable/mining-list",
            $"unmarked copper under Only marked must not be bound; census {CensusReason(ctx, ore)}; {DriveGatheringThroughTheCourse.Account(ctx)}");
        list.SetMarked(TileID.Copper, true);
        Require(Bound(ctx) == ore,
            $"marking the copper under Only marked must make it bound on the next decision; {DriveGatheringThroughTheCourse.Account(ctx)}");
    }

    private static void AJobUnderWayEndsWhenItsOreIsMarked()
    {
        MiningList list = Fresh();
        var (action, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(25, 59), new Point(26, 59));
        VerifyOreWork.AcceptBoundOre(ctx, action, "premise: a two-tile copper vein must start a job");
        Require(action.RemainingTiles == 2, $"premise: the job must hold both tiles; remaining={action.RemainingTiles}");
        list.SetMarked(TileID.Copper, true);
        action.Prepare(ctx);
        Require(action.RemainingTiles == 0 && action.JobId == 0,
            $"marking the job's ore must end the job on the next preparation; job={action.JobId} remaining={action.RemainingTiles}");
        Require(action.LastConclusion is { } end && end.Reason == "ore left by mining list",
            $"the ended job must name the list as its reason; got '{action.LastConclusion?.Reason}'");
        Require(Bound(ctx) == null, $"a marked vein must not be bound again; {DriveGatheringThroughTheCourse.Account(ctx)}");
    }

    private static void MimicDoesNotHelpWithALeftOre()
    {
        MiningList list = Fresh();
        list.SetMarked(TileID.Copper, true);
        Point ore = new(25, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Mimic, TileID.Copper, ore, playerHit: ore);
        Require(Bound(ctx) == null,
            $"the player mining a marked ore must not start mimic help; {DriveGatheringThroughTheCourse.Account(ctx)}");
        list.SetMarked(TileID.Copper, false);
        Require(Bound(ctx) == ore,
            $"with the mark cleared the same mimic scene must help; {DriveGatheringThroughTheCourse.Account(ctx)}");
    }

    private static void AnAllowedOreBesideALeftOneIsWork()
    {
        // The premise first: in this scene the nearer copper is the one the course binds with nothing marked, so
        // the row below cannot pass by the iron simply being the one it would bind anyway.
        Fresh();
        var (_, plainCtx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(25, 59));
        PlaceOre(TileID.Iron, new Point(29, 59));
        VerifyOreWork.ResettleReach(plainCtx);
        Require(Bound(plainCtx) == new Point(25, 59),
            $"premise: with nothing marked the nearer copper must be bound first; {DriveGatheringThroughTheCourse.Account(plainCtx)}");

        MiningList list = Fresh();
        list.SetMarked(TileID.Copper, true);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(25, 59));
        PlaceOre(TileID.Iron, new Point(29, 59));
        VerifyOreWork.ResettleReach(ctx);
        Require(Bound(ctx) == new Point(29, 59),
            $"marking copper must leave the iron beside it as work; {DriveGatheringThroughTheCourse.Account(ctx)}");
    }

    private static void PlaceOre(ushort type, Point at)
    {
        Main.tileSolid[type] = true;
        Tile tile = Main.tile[at.X, at.Y];
        tile.ClearEverything();
        tile.HasTile = true;
        tile.TileType = type;
    }

    private static void AnOreEnteringTheInventoryStaysKnown()
    {
        Player player = Main.LocalPlayer;
        var owner = player.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>();
        MiningList previous = owner.Preferences.MiningList;
        Item[] savedInventory = player.inventory.Select(item => item.Clone()).ToArray();
        Item savedCursor = Main.mouseItem.Clone();
        try
        {
            var list = new MiningList();
            owner.Preferences.MiningList = list;
            foreach (Item item in player.inventory) item.TurnToAir();
            Main.mouseItem.TurnToAir();
            player.inventory[12].SetDefaults(ItemID.DirtBlock);
            owner.PostUpdate();
            Require(list.Known.Count == 0, $"a dirt block places no ore and must not be known; known={string.Join(",", list.Known)}");
            player.inventory[13].SetDefaults(ItemID.CopperOre);
            owner.PostUpdate();
            Main.mouseItem.SetDefaults(ItemID.IronOre);
            owner.PostUpdate();
            owner.PostUpdate();
            Require(list.Known.SequenceEqual(new[] { (int)TileID.Copper, (int)TileID.Iron }),
                $"copper then iron must be known once each, in the order they were held; known={string.Join(",", list.Known)}");
            player.inventory[13].TurnToAir();
            Main.mouseItem.TurnToAir();
            owner.PostUpdate();
            Require(list.Known.SequenceEqual(new[] { (int)TileID.Copper, (int)TileID.Iron }),
                $"selling or dropping the last ore must not forget it; known={string.Join(",", list.Known)}");
        }
        finally
        {
            owner.Preferences.MiningList = previous;
            for (int i = 0; i < savedInventory.Length; i++) player.inventory[i] = savedInventory[i];
            Main.mouseItem = savedCursor;
        }
    }

    private static void TheListSurvivesSaveAndLoad()
    {
        var prefs = new Preferences();
        MiningList list = prefs.MiningList;
        Player player = Main.LocalPlayer;
        Item[] savedInventory = player.inventory.Select(item => item.Clone()).ToArray();
        try
        {
            foreach (Item item in player.inventory) item.TurnToAir();
            player.inventory[0].SetDefaults(ItemID.CopperOre);
            player.inventory[1].SetDefaults(ItemID.IronOre);
            list.RecordHeld(player);
        }
        finally { for (int i = 0; i < savedInventory.Length; i++) player.inventory[i] = savedInventory[i]; }
        list.SetMarked(TileID.Iron, true);
        list.Mode = ListMode.OnlyMarked;

        var tag = new TagCompound();
        prefs.Save(tag);
        // An ore from a mod that is not loaded is written the way the list writes a loaded mod's ore, by its
        // mod-qualified name, and must come back out of the list untouched.
        TagCompound inner = tag.GetCompound("miningList");
        inner["known"] = inner.GetList<string>("known").Append("GoneMod/GoneOre").ToList();
        inner["marked"] = inner.GetList<string>("marked").Append("GoneMod/GoneOre").ToList();
        using var stream = new MemoryStream();
        TagIO.ToStream(tag, stream);
        stream.Position = 0;
        MiningList copy = Preferences.Load(TagIO.FromStream(stream)).MiningList;
        Require(copy.Known.SequenceEqual(new[] { (int)TileID.Copper, (int)TileID.Iron }),
            $"known ores must round-trip in order; known={string.Join(",", copy.Known)}");
        Require(copy.IsMarked(TileID.Iron) && !copy.IsMarked(TileID.Copper) && copy.Mode == ListMode.OnlyMarked,
            $"marks and mode must round-trip; iron={copy.IsMarked(TileID.Iron)} copper={copy.IsMarked(TileID.Copper)} mode={copy.Mode}");
        Require(copy.Allows(TileID.Iron) && !copy.Allows(TileID.Copper), "the loaded list must allow only the marked iron under Only marked");
        TagCompound again = copy.Save();
        Require(again.GetList<string>("known").Contains("GoneMod/GoneOre") && again.GetList<string>("marked").Contains("GoneMod/GoneOre"),
            "an ore from an unloaded mod must be written back rather than forgotten");
    }

    private static void AnAbsentOrMalformedListMinesEverything()
    {
        MiningList absent = Preferences.Load(new TagCompound()).MiningList;
        Require(absent.Mode == ListMode.SkipMarked && absent.Known.Count == 0 && absent.Allows(TileID.Copper),
            "a save written before the list existed must load a list that mines everything");
        var malformed = new TagCompound { ["miningList"] = new TagCompound { ["mode"] = 7, ["known"] = new List<string> { "Copper" } } };
        MiningList safe = Preferences.Load(malformed).MiningList;
        Require(safe.Mode == ListMode.SkipMarked && safe.Allows(TileID.Copper),
            $"an unknown mode must fall back to Skip marked; mode={safe.Mode}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
