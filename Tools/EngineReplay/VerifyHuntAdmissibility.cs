extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;

using H = live::AICompanion.Companion.Brain.Behaviours.Combat.HuntAction;
using T = live::AICompanion.Companion.Brain.WorldObservation.ThreatRecord;
using C = live::AICompanion.Companion.Brain.Behaviours.ActionContext;
using PositionRequest = live::AICompanion.Companion.Brain.PositionSelection.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.PositionSelection.RequestKind;

/// <summary>
/// A hunt target is admissible only if a standing position exists that the walker can reach and
/// from which a weapon has a line to it. The owner's rule: can I hit this enemy, if not can I move
/// to hit it, and if neither then it should not be hunted at all.
///
/// Both halves are checked, and the second is the one that matters. Applying the from-here shot
/// test to every target is the obvious change and rejects every enemy the companion would simply
/// have had to walk toward — worse than the behaviour it replaces. So a target with no shot from
/// where the companion stands, but a reachable tile that can see it, must stay admissible.
/// </summary>
internal static class VerifyHuntAdmissibility
{
    public static int Run()
    {
        VerifyWalkableFiringPositionKeepsTheTarget();
        VerifySealedTargetIsRefused();
        VerifyTheCheckIsAffordableOnAHopelessCrowd();
        Console.WriteLine("hunt admissibility: a repositionable target is kept, an unshootable target is refused, and the check stays affordable");
        return 0;
    }

    /// <summary>
    /// Establishing a firing opportunity samples terrain, and it runs inside the per-tick score of
    /// every hunt, so it is exactly the kind of addition that has made this brain unplayable before.
    /// The worst case is a crowd where nothing is shootable and the companion is walking: every
    /// target is refused, the retry limit is reached each tick, and a body in motion keeps moving
    /// out from under the verdict cache.
    ///
    /// The bound is loose on purpose. It is here to catch an order-of-magnitude regression — a cache
    /// key that stops holding, a sample stride that collapses to one — rather than to police a few
    /// microseconds, because a tight timing assertion on a shared machine fails for reasons that
    /// have nothing to do with this code.
    /// </summary>
    private static void VerifyTheCheckIsAffordableOnAHopelessCrowd()
    {
        BuildFloor();
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= FloorY + 30; y++)
                Solid(x, y);
        for (int x = 59; x <= 61; x++)
            for (int y = FloorY + 11; y <= FloorY + 12; y++)
                Open(x, y);
        Rebuild();

        var companion = Place(companionTileX: 58, enemyTileX: 60, enemyTileY: FloorY + 13, out NPC first, out C ctx);
        var threats = companion.Brain.Senses.Threats.Threats;
        for (int slot = 26; slot < 34; slot++)
        {
            var extra = new NPC();
            extra.SetDefaults(Terraria.ID.NPCID.Zombie);
            extra.whoAmI = slot;
            extra.active = true;
            extra.velocity = Vector2.Zero;
            extra.Bottom = first.Bottom + new Vector2((slot - 30) * 8f, 0f);
            Main.npc[slot] = extra;
            threats.Add(new T { Npc = extra, DistanceToCompanion = 210, DistanceToPlayer = 210 });
        }
        SettleReach(companion, first);

        var hunt = new H();
        hunt.Score(ctx); // first call warms the terrain caches this is not trying to measure
        var clock = System.Diagnostics.Stopwatch.StartNew();
        const int Ticks = 240;
        for (int tick = 0; tick < Ticks; tick++)
        {
            // Walking, so the verdict cache cannot simply hold a single answer for the whole run.
            companion.NPC.position.X += 2f;
            hunt.Score(ctx);
        }
        double perTick = clock.Elapsed.TotalMilliseconds / Ticks;
        Console.WriteLine($"hunt admissibility cost: {perTick:0.000} ms per score over {Ticks} ticks with {threats.Count} unshootable targets");
        Require(perTick < 2.0d,
            $"establishing firing opportunity costs {perTick:0.000} ms per tick on a hopeless crowd, which is a whole frame budget spent deciding not to fight");
    }

    private const int FloorY = 80;

    /// <summary>
    /// The trap case. A wall between the companion and the enemy blocks the shot from where it
    /// stands, while the open floor beyond the wall's end has a clear line. Walking there is the
    /// whole point of hunting, so the target must survive selection.
    /// </summary>
    private static void VerifyWalkableFiringPositionKeepsTheTarget()
    {
        BuildFloor();
        // A short pillar beside the companion. It blocks the flat line from the companion's own
        // position without sealing anything: the floor continues past it in both directions.
        for (int y = FloorY - 3; y < FloorY; y++) Solid(40, y);
        Rebuild();

        var companion = Place(companionTileX: 38, enemyTileX: 60, enemyTileY: FloorY, out NPC enemy, out C ctx);
        SettleReach(companion, enemy);

        // Without this the case could pass trivially by the companion already having a shot, which
        // is the one situation the trap is not about.
        Require(!companion.Arsenal.CanEngage(ctx, enemy),
            "the trap case needs the pillar to actually block the shot from where the companion stands");

        var hunt = new H();
        float score = hunt.Score(ctx);
        Require(score > 0f,
            "an enemy with no shot from here but a reachable sighted floor beyond a pillar was refused: " +
            $"applying the from-here test to every target rejects exactly the enemies hunting exists to walk toward. rejection={hunt.LastRejection}");
        Require(hunt.Target != null && hunt.Target.Npc == enemy,
            $"the repositionable enemy was not the selected target; rejection={hunt.LastRejection}");
    }

    /// <summary>
    /// The playtest case. The enemy sits in a sealed chamber with solid rock all around its own
    /// level, so no standable tile within weapon reach has a line to it. The 2026-09-11 session
    /// stood in hunt mode at exactly this shape rather than refusing it.
    /// </summary>
    private static void VerifySealedTargetIsRefused()
    {
        BuildFloor();
        // A sealed chamber below the main floor: two tiles of air inside thick rock, with no opening
        // at all, so nothing standable outside it can see in. The companion stands almost directly
        // above it, because an enemy placed far away is refused on activity radius long before the
        // shot question is asked and the check would pass without testing anything it claims to.
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= FloorY + 30; y++)
                Solid(x, y);
        for (int x = 59; x <= 61; x++)
            for (int y = FloorY + 11; y <= FloorY + 12; y++)
                Open(x, y);
        Rebuild();

        var companion = Place(companionTileX: 58, enemyTileX: 60, enemyTileY: FloorY + 13, out NPC enemy, out C ctx);
        SettleReach(companion, enemy);

        var hunt = new H();
        float score = hunt.Score(ctx);
        // The refusal must be earned against a settled region, not against a flood that never grew:
        // an unfinished region is deliberately Unknown rather than a refusal, so a green result
        // here with an unsettled region would be measuring nothing.
        Require(companion.Brain.Positioner.ReachComplete,
            "the sealed case must settle its reachable region before a refusal means anything");
        Require(!companion.Arsenal.CanEngage(ctx, enemy),
            "the sealed case needs the chamber to actually block the shot from where the companion stands");
        Require(score == 0f,
            $"a sealed enemy no reachable position can shoot was still hunted: score={score}; target={hunt.Target?.Npc.whoAmI}; rejection={hunt.LastRejection}");
        Require(hunt.LastRejection == "no-reachable-firing-position",
            $"the refusal must name its reason so a session can be read for it; got {hunt.LastRejection}");
    }

    /// <summary>
    /// Grows the positioner's reachable region, which is flooded incrementally across rescores. The
    /// admissibility rule reads that region, and an unfinished one is deliberately Unknown rather
    /// than a refusal, so a fixture that did not settle it would prove nothing about the refusal.
    /// </summary>
    private static void SettleReach(live::AICompanion.Companion.CharacterBody.CompanionNPC companion, NPC enemy)
    {
        var request = new PositionRequest(RequestKind.LineOfFire, enemy.Center, enemy);
        var profile = companion.Arsenal.ProfileFor(new C(companion, companion.Brain.Senses), enemy);
        for (int tick = 0; tick < 400; tick++)
            companion.Brain.Positioner.Resolve(request, companion.Brain.Senses, profile);
    }

    private static live::AICompanion.Companion.CharacterBody.CompanionNPC Place(
        int companionTileX, int enemyTileX, int enemyTileY, out NPC enemy, out C ctx)
    {
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(companionTileX * 16f, FloorY * 16f - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.position = new Vector2(companionTileX * 16f, FloorY * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;

        enemy = new NPC();
        enemy.SetDefaults(Terraria.ID.NPCID.Zombie);
        enemy.whoAmI = 25;
        enemy.active = true;
        enemy.velocity = Vector2.Zero;
        enemy.Bottom = new Vector2(enemyTileX * 16f + 8f, enemyTileY * 16f);
        Main.npc[25] = enemy;

        companion.Brain.Senses.Update(companion.NPC, player, companion.Breath);
        var threats = companion.Brain.Senses.Threats.Threats;
        threats.Clear();
        threats.Add(new T
        {
            Npc = enemy,
            DistanceToCompanion = Vector2.Distance(companion.NPC.Bottom, enemy.Bottom),
            DistanceToPlayer = Vector2.Distance(player.Bottom, enemy.Bottom),
        });
        ctx = new C(companion, companion.Brain.Senses);
        return companion;
    }

    private static void BuildFloor()
    {
        Main.maxTilesX = Main.maxTilesY = 140;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)140, (ushort)140 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= FloorY + 2; y++)
                Solid(x, y);
    }

    private static void Rebuild()
    {
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
    }

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = true;
        tile.TileType = 1;
    }

    private static void Open(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = false;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
