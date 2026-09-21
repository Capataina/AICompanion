extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Light;
using Terraria.ID;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using BrainTelemetry = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry;
using Scored = live::AICompanion.Companion.Brain.Infrastructure.Selection.Chooser.Scored;
using CandidateFunnel = live::AICompanion.Companion.Brain.Activities.CandidateFunnel;
using CourseValue = live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.CourseValue;
using CollectNearbyItems = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.CollectNearbyItems;
using LightSense = live::AICompanion.Companion.Brain.Infrastructure.Observation.LightSense;
using LightUsefulArea = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.LightUsefulArea;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using Offer = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using RecommendTorchPlacement = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.RecommendTorchPlacement;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;

/// <summary>
/// What a preparation did with each candidate is a record rather than a reconstruction: lighting, collection and hunting
/// each name the stage that refused a candidate and the numbers it read, lighting's cheap attachment filter never refuses
/// a tile the game's own torch step would accept, and the player's own smart cursor is recorded as the reference lighting
/// is judged against. The rows read surfaces this lane added, so each is proven by a mutation rather than against the code
/// before it; the behavioural rows that could be red against that code are in <c>VerifyTorchPlacementRule</c>.
/// </summary>
internal static class VerifyCandidateFunnel
{
    private const int StandRow = 59;

    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        Preferences saved = Preferences.Current;
        LightMode mode = Lighting.Mode;
        float brightness = Lighting.GlobalBrightness;
        Item[] items = Enumerable.Range(0, 16).Select(i => Main.item[i]).ToArray();
        int red = 0;
        void Each(string name, Action fixture)
        {
            LimitPlanningWork.Unbounded = true;
            Preferences.Current = new Preferences { TorchPlacement = true, PotBreaking = false };
            try { fixture(); Console.WriteLine($"GREEN {name}"); }
            catch (Exception e) { red++; Console.WriteLine($"RED {name}: {e.Message}"); }
            finally
            {
                LimitPlanningWork.End();
                LimitPlanningWork.Unbounded = false;
                Preferences.Current = saved;
                VerifyUsefulAssistance.ClearMeasuredLight();
                Lighting.Mode = mode;
                Lighting.GlobalBrightness = brightness;
                for (int i = 0; i < items.Length; i++) Main.item[i] = items[i];
            }
        }
        Each("funnel: lighting names the stage that refused a tile, and the tile it offered is the one that got furthest",
            LightingNamesTheStageThatRefusedATile);
        Each("funnel: a sealed dark pocket's tiles are refused at their stand, and that is the furthest any tile got",
            ASealedPocketIsRefusedAtItsStand);
        Each("placer: the cheap attachment filter never refuses a tile the game's own torch step accepts",
            TheAttachmentFilterNeverRefusesWhatTheStepAccepts);
        Each("reference: the tile his own smart cursor would offer is recorded with its light and lighting's stage for it",
            HisCursorsTileIsTheReference);
        Each("funnel: collection names a drop with no contact pose as refused there",
            CollectionNamesADropWithNoContactPose);
        Each("value: a priced course's published terms account for its published total",
            TheTimeFactorIsRecordedAndMultipliesToTheFinal);
        if (red == 0) Console.WriteLine("candidate funnel: lighting, collection and the player's reference name every refusal");
        return red;
    }

    /// <summary>
    /// A dark floor with a torch already standing a few tiles from the body. Every floor tile within the game's spacing of
    /// that torch is dark and could take a torch by attachment, and the game's own torch step refuses it; the nearest tile
    /// past the spacing is offered. The funnel's nearest entries are those refused tiles, each named at the placer and
    /// carrying the light it read, and the candidate that got furthest is the offered one.
    /// </summary>
    private static void LightingNamesTheStageThatRefusedATile()
    {
        var ctx = VerifyTorchPlacementRule.Scene();
        VerifyTorchPlacementRule.GiveTorches(ctx, held: false);
        Point standing = new(24, StandRow);
        VerifyOreWork.Place(standing, TileID.Torches);
        VerifyTorchPlacementRule.Settle(ctx);
        VerifyTorchPlacementRule.PresentEngineLight((_, _) => new Vector3(.02f), VerifyTorchPlacementRule.GameGlobalBrightness, placed: null);
        VerifyTorchPlacementRule.ForceRefresh(ctx);

        var action = new LightUsefulArea();
        VerifyPreparedActivities.PrepareAndScore(action, ctx);
        CandidateFunnel funnel = action.Funnel;
        string record = $"offer={action.Eligibility}/{action.EligibilityReason} target={action.ActivityTarget} counts={funnel.Summary()} entries={funnel.Describe()}";
        Require(action.Eligibility == Offer.Usable && action.ActivityTarget is Vector2,
            $"premise: a dark floor with room past the spacing is a lighting job; {record}");
        Point beside = new(21, StandRow);
        CandidateFunnel.Entry? entry = funnel.Entries.Cast<CandidateFunnel.Entry?>().FirstOrDefault(e => e!.Value.Tile == beside);
        Require(entry is { } refused && refused.RefusedAt == "placer-refused" && refused.Passed == "lit" && refused.Readings.StartsWith("light=", StringComparison.Ordinal),
            $"a dark tile beside the standing torch is refused by the game's torch step, by that name, with the light it read; entry={entry} {record}");
        Require(funnel.Best is { RefusedAt: "" } best && best.Tile == action.ActivityTarget!.Value.ToTileCoordinates()
            && best.Readings.Contains("trip=", StringComparison.Ordinal) && funnel.BestStage == CandidateFunnel.Offered,
            $"the candidate that got furthest is the offered tile, with its trip and value; best={funnel.Best} {record}");
        Require(funnel.Counts.TryGetValue("placer-refused", out int placerRefused) && placerRefused >= 8,
            $"every refused tile is counted, not only the kept sample; {record}");
    }

    /// <summary>
    /// A lit floor and a dark pocket sealed in rock under it. The floor tiles are refused for their light at the gathering
    /// scan, the pocket tiles are dark and put to the reach sense, whose finished flood refuses every one of their stands.
    /// Nothing is offered, and the funnel's furthest candidate is a pocket tile refused at its stand, carrying the verdict.
    /// </summary>
    private static void ASealedPocketIsRefusedAtItsStand()
    {
        var ctx = VerifyTorchPlacementRule.Scene();
        for (int x = 22; x <= 38; x++)
            for (int y = 61; y <= 72; y++)
                VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
        for (int x = 26; x <= 34; x++)
            for (int y = 64; y <= 68; y++)
                Main.tile[x, y].ClearEverything();
        VerifyTorchPlacementRule.GiveTorches(ctx, held: false);
        VerifyTorchPlacementRule.Settle(ctx);
        VerifyTorchPlacementRule.PresentEngineLight((_, y) => new Vector3(y <= 60 ? 1f : .02f), VerifyTorchPlacementRule.GameGlobalBrightness, placed: null);
        VerifyTorchPlacementRule.ForceRefresh(ctx);

        var action = new LightUsefulArea();
        VerifyPreparedActivities.PrepareAndScore(action, ctx);
        CandidateFunnel funnel = action.Funnel;
        string record = $"offer={action.Eligibility}/{action.EligibilityReason} counts={funnel.Summary()} entries={funnel.Describe()}";
        Require(action.Eligibility != Offer.Usable, $"premise: nothing dark is reachable; {record}");
        Require(funnel.Counts.TryGetValue("lit", out int lit) && lit > 0, $"the lit floor is refused for its light; {record}");
        Require(funnel.Best is { RefusedAt: "stand-unreachable" } best && best.Tile.Y >= 64 && best.Readings.Contains("stand=No", StringComparison.Ordinal),
            $"the furthest candidate is a pocket tile whose stand the flood refused, with that verdict; best={funnel.Best} {record}");
        Require(funnel.BestStage == "stand-unreachable", $"the compact column names that stage; {record}");
    }

    /// <summary>
    /// Every attachment the torch step distinguishes, each in its own cell far from the others: a wall behind, a solid tile
    /// on either side, a solid tile below, half a brick below, a slope below and to each side, and open air with nothing.
    /// For every empty tile in the region, a tile the step accepts is a tile the filter passes; and the filter is not the
    /// step, because a half brick below passes the filter and is refused by the step.
    /// </summary>
    private static void TheAttachmentFilterNeverRefusesWhatTheStepAccepts()
    {
        var ctx = VerifyTorchPlacementRule.Scene();
        Item torch = VerifyTorchPlacementRule.GiveTorches(ctx, held: false);
        Player player = ctx.Companion.StandIn.Player;
        for (int x = 12; x <= 88; x++)
            for (int y = 20; y <= 50; y++)
                Main.tile[x, y].ClearEverything();
        var cells = new (string Name, Action<int, int> Build)[]
        {
            ("wall behind", (x, y) => { Tile tile = Main.tile[x, y]; tile.WallType = WallID.Stone; }),
            ("solid left", (x, y) => VerifyOreWork.Place(new Point(x - 1, y), TileID.Dirt)),
            ("solid right", (x, y) => VerifyOreWork.Place(new Point(x + 1, y), TileID.Dirt)),
            ("solid below", (x, y) => VerifyOreWork.Place(new Point(x, y + 1), TileID.Dirt)),
            ("half brick below", (x, y) => { VerifyOreWork.Place(new Point(x, y + 1), TileID.Dirt); Tile tile = Main.tile[x, y + 1]; tile.IsHalfBlock = true; }),
            ("slope below", (x, y) => { VerifyOreWork.Place(new Point(x, y + 1), TileID.Dirt); Tile tile = Main.tile[x, y + 1]; tile.Slope = SlopeType.SlopeDownLeft; }),
            ("slope left", (x, y) => { VerifyOreWork.Place(new Point(x - 1, y), TileID.Dirt); Tile tile = Main.tile[x - 1, y]; tile.Slope = SlopeType.SlopeDownRight; }),
            ("slope right", (x, y) => { VerifyOreWork.Place(new Point(x + 1, y), TileID.Dirt); Tile tile = Main.tile[x + 1, y]; tile.Slope = SlopeType.SlopeDownLeft; }),
            ("nothing", (_, _) => { }),
        };
        for (int i = 0; i < cells.Length; i++) cells[i].Build(16 + i * 8, 30);
        int accepted = 0, filteredOnly = 0;
        var refusedByFilter = new List<string>();
        for (int x = 12; x <= 88; x++)
            for (int y = 26; y <= 34; y++)
            {
                Point tile = new(x, y);
                if (Main.tile[x, y].HasTile) continue;
                bool step = RecommendTorchPlacement.Accepts(tile, torch, player);
                bool filter = RecommendTorchPlacement.MayAccept(tile);
                if (step) accepted++;
                if (step && !filter) refusedByFilter.Add(tile.ToString());
                if (filter && !step) filteredOnly++;
            }
        Require(accepted >= 5, $"premise: the cells must give the step several tiles to accept; accepted={accepted}");
        Require(refusedByFilter.Count == 0,
            $"the filter refused tiles the game's torch step accepts, so the search would never ask about them: {string.Join(" ", refusedByFilter)}");
        Require(filteredOnly > 0, $"premise: the filter must pass something the step refuses, or it is the step and this proves nothing; accepted={accepted}");
    }

    /// <summary>
    /// The player stands in a dark room holding a torch. The reference is the tile his own cursor would offer, worked out
    /// here from his reach box independently of the production code, read dark, and lighting's own stage for it is that it
    /// is offered or would pass every stage. The same room in daylight records the same tile read lit and refused as lit,
    /// and with no torch anywhere the reference is still that tile, because the companion's torches are its own.
    /// </summary>
    private static void HisCursorsTileIsTheReference()
    {
        var ctx = VerifyTorchPlacementRule.Scene();
        VerifyTorchPlacementRule.BuildSealedRoom();
        Item torch = VerifyTorchPlacementRule.GiveTorches(ctx, held: true);
        VerifyTorchPlacementRule.Settle(ctx);
        VerifyTorchPlacementRule.PresentEngineLight((_, _) => new Vector3(.02f), VerifyTorchPlacementRule.GameGlobalBrightness, placed: null,
            (ctx.Player.Center.ToTileCoordinates(), VerifyTorchPlacementRule.TorchColour()));
        VerifyTorchPlacementRule.ForceRefresh(ctx);
        Point? his = VerifyTorchPlacementRule.HisCursorTile(ctx.Player, torch);
        Require(his is Point, "premise: his cursor must offer a tile in the room");

        var dark = new LightUsefulArea();
        VerifyPreparedActivities.PrepareAndScore(dark, ctx);
        string darkRecord = $"reference={dark.PlayerReferenceTile} reading={dark.PlayerReferenceReading} stage={dark.PlayerReferenceStage} offer={dark.EligibilityReason} target={dark.ActivityTarget}";
        Require(dark.PlayerReferenceTile == his,
            $"the reference is the tile his own cursor would offer from his reach with the cursor at his centre; expected={his} {darkRecord}");
        Require(dark.PlayerReferenceReading is { IsDark: true },
            $"that tile in the dark room reads dark to the placement question; {darkRecord}");
        Require(dark.PlayerReferenceStage is "offered" or "passed-every-stage",
            $"lighting's stage for a dark tile it can reach is that it is offered or would pass every stage; {darkRecord}");

        VerifyTorchPlacementRule.PresentEngineLight((_, _) => new Vector3(1f), VerifyTorchPlacementRule.GameGlobalBrightness, placed: null);
        VerifyTorchPlacementRule.ForceRefresh(ctx);
        var lit = new LightUsefulArea();
        VerifyPreparedActivities.PrepareAndScore(lit, ctx);
        string litRecord = $"reference={lit.PlayerReferenceTile} reading={lit.PlayerReferenceReading} stage={lit.PlayerReferenceStage}";
        Require(lit.PlayerReferenceTile == his && lit.PlayerReferenceReading is { Light: LightSense.PlacementLight.Lit } && lit.PlayerReferenceStage == "lit",
            $"the same tile in daylight is recorded lit and refused as lit; {litRecord}");

        ctx.Player.inventory[0] = new Item();
        var none = new LightUsefulArea();
        VerifyPreparedActivities.PrepareAndScore(none, ctx);
        Require(none.PlayerReferenceTile == his && none.PlayerReferenceStage == "lit",
            $"with no torch anywhere the reference is still the tile his cursor would offer for an ordinary torch, refused as lit in daylight; expected={his} reference={none.PlayerReferenceTile} stage={none.PlayerReferenceStage}");
    }

    /// <summary>
    /// A drop resting in a hole one tile wide, sealed in rock, far from the body. No cell the orb can hover in touches it,
    /// so collection refuses it for want of a contact pose; the funnel names that stage for that drop and carries the
    /// landing the pose was searched around.
    /// </summary>
    private static void CollectionNamesADropWithNoContactPose()
    {
        var ctx = VerifyCollectionContracts.SetUpFloor();
        Preferences.Current = new Preferences { TorchPlacement = false, PotBreaking = false };
        for (int x = 36; x <= 44; x++)
            for (int y = 53; y <= 59; y++)
                VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
        Main.tile[40, 56].ClearEverything();
        TerrainChanges.Reset();
        VerifyTorchPlacementRule.Settle(ctx);
        Item drop = VerifyCollectionContracts.Drop(ItemID.Gel, 3, new Vector2(40 * 16 + 8, 57 * 16), slot: 9);
        ctx.Senses.Loot.Pickups.Clear();
        ctx.Senses.Loot.Pickups.Add(new(drop, 1f, Vector2.Distance(ctx.Npc.Center, drop.Center)));
        var collect = new CollectNearbyItems();
        collect.Prepare(ctx);
        CandidateFunnel funnel = collect.Funnel;
        string record = $"offer={collect.Eligibility}/{collect.EligibilityReason} counts={funnel.Summary()} entries={funnel.Describe()}";
        Require(collect.EligibilityReason == "drop-has-no-contact-pose", $"premise: the sealed drop has no contact pose; {record}");
        Require(funnel.Best is { RefusedAt: "no-contact-pose" } best && best.Identity.StartsWith("item9:", StringComparison.Ordinal)
            && best.Readings.Contains("landing=", StringComparison.Ordinal),
            $"collection names that drop as refused for want of a contact pose, with the landing it searched around; best={funnel.Best} {record}");
    }

    /// <summary>
    /// The dark room with lighting and keeping company the only activities, driven through the real chooser. The lighting
    /// job is a task somewhere, so its final carries the time-per-job factor, and the factors the chooser records for it —
    /// every multiplier the evaluation and the task ordering applied — multiply back to the final it recorded. The same
    /// holds for the factor list the decision occurrence writes, read back by name, which is the form a capture carries.
    /// </summary>
    private static void TheTimeFactorIsRecordedAndMultipliesToTheFinal()
    {
        var ctx = VerifyTorchPlacementRule.Scene();
        VerifyTorchPlacementRule.BuildSealedRoom();
        VerifyTorchPlacementRule.GiveTorches(ctx, held: true);
        VerifyTorchPlacementRule.Settle(ctx);
        VerifyTorchPlacementRule.PresentEngineLight((_, _) => new Vector3(.02f), VerifyTorchPlacementRule.GameGlobalBrightness, placed: null,
            (ctx.Player.Center.ToTileCoordinates(), VerifyTorchPlacementRule.TorchColour()));
        VerifyTorchPlacementRule.ForceRefresh(ctx);
        var brain = ctx.Companion.Brain;
        brain.Chooser.Actions.RemoveAll(a => a.Name != "place-torches" && a.Name != "keep-company");
        // The course's own value for the lighting domain, taken from the leaders the search publishes.
        // This row used to read `Chooser.LastScores` and multiply nine named factors into a final; the
        // course has no factors, so the subject had to be translated rather than renamed. What survives
        // is the property the old row existed for — **the numbers a reader is shown account for the
        // decision that was made** — against the terms the course actually has.
        string domain = "light-target";
        CourseValue? priced = null;
        for (int tick = 0; tick < 120 && priced == null; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.Course.LastLeaders.TryGetValue(domain, out var leader) && leader.Total.Nominal > 0)
                priced = leader;
        }
        Require(priced != null,
            $"premise: lighting must be priced with a positive value in the dark room, or there is no "
            + $"composition to check; leaders {string.Join(" ", brain.Course.LastLeaders.Select(e => $"{e.Key}={e.Value.Total.Nominal:0.0000}"))}");
        CourseValue value = priced!;
        // `Evaluate` composes the total as useful − harm − gap + the nominal tail, and a non-zero tail
        // announces itself as `tail-has-no-bounds`, so requiring that unknown's absence is what makes
        // the three published terms the whole of the sum rather than most of it. Without it a tail could
        // absorb any discrepancy and this row would pass on arithmetic nobody checked.
        Require(!value.Unknowns.Contains("tail-has-no-bounds"),
            $"premise: the priced course must carry no nominal tail, or the three published terms are not "
            + $"the whole total; unknowns {string.Join(",", value.Unknowns)}");
        double composed = value.UsefulEffects - value.Harm - value.Companionship;
        Require(Math.Abs(composed - value.Total.Nominal) <= 1e-6 * Math.Max(1, Math.Abs(value.Total.Nominal)),
            $"the published terms must account for the published total, or a reader of a decision is "
            + $"shown numbers that are not the ones it was made on; useful {value.UsefulEffects:0.000000} "
            + $"− harm {value.Harm:0.000000} − gap {value.Companionship:0.000000} = {composed:0.000000}, "
            + $"against total {value.Total.Nominal:0.000000}");
        // The recorder's own line is built from this same `LastLeaders` object, so parsing it back would
        // assert one set of numbers against itself. What is genuinely unchecked is whether a reader can
        // recover them from a capture, which is SessionReport's half and belongs with AIC-420.
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
