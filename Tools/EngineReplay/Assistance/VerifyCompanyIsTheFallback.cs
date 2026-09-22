extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using CompanionGear = live::AICompanion.Companion.Inventory.CompanionGear;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using GearSlot = live::AICompanion.Companion.Inventory.GearSlot;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using LineOfSight = live::AICompanion.Companion.Brain.Infrastructure.Observation.LineOfSight;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// Keeping company is the fallback for when nothing is worth doing, and never a job that out-competes one. The owner ruled
/// it on 15 September 2026 from the third orb play, where a slime ten to fifteen tiles above an idle player was left alone
/// while the companion hovered beside him, and where keeping company was chosen on 60.3% of the session.
///
/// <para>Four rows, each with its pass line declared before the first run, and each read only through the chooser's own
/// score ledger and the whole brain, so the same file runs against the value rules this lane replaced and a mutation that
/// plants one of them back reddens a named row:</para>
/// <list type="bullet">
/// <item>an idle player, a bow in the companion's first slot and a slime thirteen tiles above his feet: hunting is chosen
/// within 120 ticks;</item>
/// <item>the same slime starting just past the new-job radius at the screen's edge and moving away: hunting is chosen on no
/// tick;</item>
/// <item>keeping company's raw value is at most the far cap at every sampled distance short of the hard leash, and takes
/// everything past it;</item>
/// <item>inside the region with a wall between the orb and the player, keeping company's raw value is exactly the wander
/// floor: losing sight is not distance.</item>
/// </list>
///
/// <para>The screen is declared for the hunting rows, because hunting's on-screen rule is a rectangle built from the screen's
/// position and size, both zero headless, and a slime judged against a four-hundred-pixel box at the world's origin would be
/// "off screen" in every scene. Both are put back afterwards, since the light field and the next fixture read them.</para>
/// </summary>
internal static class VerifyCompanyIsTheFallback
{
    private const int FloorY = 100, PlayerTileX = 40, WorldTiles = 200;

    public static int Run()
    {
        int red = 0;
        void Each(string name, Action fixture)
        {
            LimitPlanningWork.Unbounded = true;
            Vector2 heldPosition = Main.screenPosition;
            int heldWidth = Main.screenWidth, heldHeight = Main.screenHeight;
            try { fixture(); Console.WriteLine($"GREEN {name}"); }
            // A failed assertion is its own message; anything else is a scene that broke before it asserted, and its stack
            // is the only thing that says where.
            catch (InvalidOperationException e) { red++; Console.WriteLine($"RED {name}: {e.Message}"); }
            catch (Exception e) { red++; Console.WriteLine($"RED {name}: {e}"); }
            finally
            {
                LimitPlanningWork.Unbounded = false;
                Main.screenPosition = heldPosition;
                Main.screenWidth = heldWidth;
                Main.screenHeight = heldHeight;
                Main.npc[25] = new NPC();
            }
        }
        Each("a slime above an idle player is hunted rather than hovered beside", ASlimeAboveAnIdlePlayerIsHunted);
        Each("a slime past the new-job radius moving away is left to keeping company", AFarRecedingSlimeIsLeft);
        Each("rejoining is worth at most the far cap until the hard leash", RejoiningIsCappedShortOfTheLeash);
        Each("inside the region with no sight of the player, keeping company is worth only the floor", NoSightInsideTheRegionIsNotDistance);
        if (red == 0) Console.WriteLine("company is the fallback: a slime worth hunting is hunted, a far one is not, rejoining is capped and sight is not distance");
        return red;
    }

    /// <summary>
    /// A slime thirteen tiles above an idle player's feet and two to his right, in open air, with a wooden bow in the companion's
    /// first slot. Enemies do not run their own AI headless, so the slime stays where it is put; the orb can fly to it and shoot
    /// it, and nothing else on the board offers anything.
    /// </summary>
    private static void ASlimeAboveAnIdlePlayerIsHunted()
    {
        var (companion, player, ctx) = Scene();
        NPC slime = Slime(player.Bottom + new Vector2(32f, -13 * 16f));
        int chosenAt = -1;
        string ledger = "";
        for (int tick = 0; tick < 240 && chosenAt < 0; tick++)
        {
            FollowTheScreen(player);
            VerifyOreWork.AdvanceBrain(ctx);
            if (companion.Brain.LastAction?.Name == "combat") chosenAt = tick;
            if (tick % 20 == 0 || chosenAt >= 0) ledger = Board(companion, $"tick {tick}");
        }
        Console.WriteLine($"slime above an idle player: hunting chosen at tick {chosenAt}; {ledger}");
        Require(slime.active, "the scene premise: the slime must still be there");
        Require(chosenAt >= 0 && chosenAt <= 120, $"a reachable slime above an idle player must be hunted within 120 ticks; chosen at {chosenAt}; {ledger}");
    }

    /// <summary>
    /// The same slime on the floor, starting just past the new-job radius from the player — at the edge of a 1920-wide screen
    /// with hunting's margin — and moving away at a slime's hopping pace for 150 ticks. It is never a job to take: hunting must
    /// not be chosen on any tick.
    /// </summary>
    private static void AFarRecedingSlimeIsLeft()
    {
        var (companion, player, ctx) = Scene();
        float start = Preferences.Current.NewActivityRadius + 16f;
        NPC slime = Slime(new Vector2(player.Center.X + start, player.Bottom.Y));
        int hunting = 0;
        string ledger = "";
        for (int tick = 0; tick < 150; tick++)
        {
            slime.position.X += 2f;
            FollowTheScreen(player);
            VerifyOreWork.AdvanceBrain(ctx);
            if (companion.Brain.LastAction?.Name == "combat") { hunting++; ledger = Board(companion, $"tick {tick}"); }
            if (tick == 0) Require(Main.screenPosition.X + Main.screenWidth + 200 > slime.Center.X,
                $"the scene premise: the slime must start inside hunting's on-screen rectangle, at the screen's edge; slime {slime.Center} screen right {Main.screenPosition.X + Main.screenWidth}");
        }
        if (ledger.Length == 0) ledger = Board(companion, "end");
        Console.WriteLine($"far receding slime: hunting chosen on {hunting} of 150 ticks; {ledger}");
        Require(hunting == 0, $"a slime past the new-job radius moving away must be left to keeping company; hunting chosen on {hunting} ticks; {ledger}");
    }

    /// <summary>
    /// The orb placed at distances to the right of and above the player, out to just short of the hard leash and once past it,
    /// with nothing on the board but keeping company. Its raw value — the larger of rejoining and hovering — must stay at or
    /// under the far cap everywhere short of the leash, and be the whole of it past the leash.
    /// </summary>
    private static void RejoiningIsCappedShortOfTheLeash()
    {
        var (companion, player, ctx) = Scene();
        var brain = companion.Brain;
        foreach (var action in brain.Actions.Where(a => a.Name != "keep-company").ToList()) brain.Actions.Remove(action);
        float cap = Weights.KeepCompanyFarCap, leash = Weights.LeashHard;
        float highest = 0f, highestAt = 0f;
        var samples = new List<string>();
        foreach (Vector2 direction in new[] { new Vector2(1, 0), new Vector2(0, -1) })
        {
            for (float distance = 64f; distance < leash - 8f; distance += 96f)
            {
                Vector2 at = player.Center + direction * distance;
                if (!InWorld(at)) break;
                float raw = CompanyRawAt(companion, player, ctx, at);
                if (raw > highest) { highest = raw; highestAt = distance; }
                if (samples.Count < 40) samples.Add($"{direction}:{distance:0}={raw:0.000}");
            }
        }
        Vector2 past = player.Center + new Vector2(leash + 96f, 0f);
        float beyond = CompanyRawAt(companion, player, ctx, past);
        string ledger = $"highest {highest:0.000} at {highestAt:0} px against a cap of {cap:0.000}; past the leash {beyond:0.000}; {string.Join(" ", samples)}";
        Console.WriteLine($"rejoining capped short of the leash: {ledger}");
        Require(highest <= cap + 1e-4f, $"rejoining must be worth at most the far cap anywhere short of the hard leash; {ledger}");
        // The line above reads the cap it guards, so a cap raised back to the whole of attention would pass it unchanged,
        // and a mutation planted that way did. The ruling underneath the tunable is that rejoining alone never takes
        // everything the way the hard leash does, so it is asserted against the leash's own value and not the constant.
        Require(highest < beyond - .01f, $"short of the hard leash rejoining must stay below what the leash takes, or it is a second leash; {ledger}");
        Require(beyond > .99f, $"past the hard leash rejoining takes everything; {ledger}");
    }

    /// <summary>
    /// The orb inside the player's region with a stone column between it and him, so it has no line of sight. Nothing on the
    /// board but keeping company, which must offer exactly the wander floor: a body inside the region is with the player
    /// whether or not it can see him.
    /// </summary>
    private static void NoSightInsideTheRegionIsNotDistance()
    {
        var (companion, player, ctx) = Scene();
        var brain = companion.Brain;
        foreach (var action in brain.Actions.Where(a => a.Name != "keep-company").ToList()) brain.Actions.Remove(action);
        int column = PlayerTileX + 3;
        for (int y = FloorY - 12; y < FloorY; y++) Solid(column, y);
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        Vector2 at = new((column + 3) * 16f + 8f, player.Center.Y - 40f);
        float raw = CompanyRawAt(companion, player, ctx, at);
        var region = brain.Senses.Intent.Region;
        bool seen = LineOfSight.Between(companion.NPC, player);
        string ledger = $"orb {companion.NPC.Center} player {player.Center} region centre {region.Centre} half {region.HalfSize} inside {region.Contains(companion.NPC.Center)} sight {seen} keep-company raw {raw:0.0000} floor {Weights.WanderFloor:0.0000}";
        Console.WriteLine($"no sight inside the region: {ledger}");
        Require(region.Contains(companion.NPC.Center) && !seen, $"the scene premise: the orb must be inside the region and unable to see the player; {ledger}");
        Require(MathF.Abs(raw - Weights.WanderFloor) < 1e-5f, $"inside the region with no sight of the player keeping company is worth only the floor; {ledger}");
    }

    private static float CompanyRawAt(CompanionNPC companion, Player player, ActionContext ctx, Vector2 at)
    {
        companion.NPC.Center = at;
        companion.NPC.velocity = Vector2.Zero;
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        companion.Brain.Senses.Update(companion.NPC, player);
        // Keeping company's own offer, read from the activity rather than from the retired scorer's
        // ledger. It is the one activity with no course domain to read instead, and that is deliberate
        // rather than an omission: an empty course *is* companionship, so the course mints no opportunity
        // for it and there is nothing for `ReadCourseWorthPerActivity` to find. What this row is about —
        // how the rejoin value grows with distance and where it caps — has always been the activity's own
        // arithmetic, and `Score()` after a preparation is that number at its source.
        var company = companion.Brain.Actions
            .OfType<live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany>().Single();
        company.Prepare(ctx);
        return company.Score();
    }

    private static string Board(CompanionNPC companion, string when)
    {
        // Each activity's own published offer, plus what the course made of its domain. The retired
        // scorer's ledger is empty in anything the course decides, so a board read from it printed a row
        // of dashes in exactly the failure messages a reader needs it for.
        var worths = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics
            .ReadCourseWorthPerActivity.Of(companion.Brain);
        string Of(string name) => worths.FirstOrDefault(w => w.Action.Name == name) is { Action: not null } w
            ? $"{name} {w.Raw:0.000}/{w.Final:0.000} course:{w.Offer}:{w.OfferReason} own:{w.Action.Eligibility}:{w.Action.EligibilityReason}" : $"{name} -";
        return $"{when}: chosen {companion.Brain.LastAction?.Name ?? "none"}; {Of("combat")}; {Of("keep-company")}";
    }

    private static (CompanionNPC Companion, Player Player, ActionContext Ctx) Scene()
    {
        Main.maxTilesX = WorldTiles;
        Main.maxTilesY = 140;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)WorldTiles, (ushort)140 }, null)!;
        Main.tileSolid[TileID.Stone] = true;
        for (int x = 5; x < WorldTiles - 5; x++)
            for (int y = FloorY; y <= FloorY + 2; y++)
                Solid(x, y);
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        Main.SceneMetrics ??= new SceneMetrics();
        // Creating the companion fills every projectile, NPC and item slot with an inactive instance, which is the reset this
        // scene needs; touching those slots before it reads a null in a fresh process, where only the NPC and player tables
        // have been filled.
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.velocity = Vector2.Zero;
        player.Bottom = new Vector2(PlayerTileX * 16f + 8f, FloorY * 16f);
        companion.NPC.Center = player.Center + new Vector2(0f, -80f);
        companion.NPC.velocity = Vector2.Zero;
        CompanionGear slots = player.GetModPlayer<CompanionPlayer>().Gear;
        for (int i = 0; i < CompanionGear.SlotCount; i++) slots.Slots[i] = new Item();
        slots.Slots[(int)GearSlot.FirstWeapon].SetDefaults(ItemID.WoodenBow);
        Main.screenWidth = 1920;
        Main.screenHeight = 1080;
        FollowTheScreen(player);
        var ctx = new ActionContext(companion, companion.Brain.Senses);
        companion.Brain.Senses.Update(companion.NPC, player);
        return (companion, player, ctx);
    }

    private static NPC Slime(Vector2 feet)
    {
        var slime = new NPC();
        slime.SetDefaults(NPCID.BlueSlime);
        slime.whoAmI = 25;
        slime.active = true;
        slime.velocity = Vector2.Zero;
        slime.Bottom = feet;
        Main.npc[25] = slime;
        return slime;
    }

    private static void FollowTheScreen(Player player)
        => Main.screenPosition = player.Center - new Vector2(Main.screenWidth / 2f, Main.screenHeight / 2f);

    private static bool InWorld(Vector2 point)
        => point.X > 8 * 16f && point.X < (WorldTiles - 8) * 16f && point.Y > 8 * 16f && point.Y < FloorY * 16f - 16f;

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.ClearEverything();
        tile.HasTile = true;
        tile.TileType = TileID.Stone;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
