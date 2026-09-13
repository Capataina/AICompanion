using AICompanion.Companion.Brain.WorldObservation;
using AICompanion.Companion.Brain.SharedMovementSystem;
using Microsoft.Xna.Framework;
using Terraria;

internal static class VerifyPersonalDanger
{
    public static int Run()
    {
        // Two sealed chambers share a floor. An enemy can reach one occupant but not the other;
        // proximity alone is insufficient because both occupants are within the threat horizon.
        for (int x = 8; x <= 52; x++)
        for (int y = 52; y <= 62; y++)
        {
            Tile tile = Main.tile[x, y];
            tile.ClearEverything();
            tile.HasTile = y <= 54 || y >= 60 || x <= 10 || x >= 50 || x == 30;
            tile.TileType = 1;
        }
        NavGrid.World = new GameTileWorld();
        AStar.InvalidateEdges();
        AStar.MsBudget = 0;
        Main.npc = Enumerable.Range(0, Main.maxNPCs + 1).Select(_ => new NPC()).ToArray();
        Main.projectile = Enumerable.Range(0, Main.maxProjectiles + 1).Select(_ => new Projectile()).ToArray();
        var enemy = Main.npc[0];
        enemy.SetDefaults(Terraria.ID.NPCID.BlueSlime);
        enemy.active = true;
        enemy.whoAmI = 0;
        enemy.position = new Vector2(21 * 16, 960 - enemy.height);
        enemy.velocity = new Vector2(1, 0);
        var player = new Player { width = 20, height = 42, position = new Vector2(40 * 16, 918), statLifeMax2 = 100 };
        var companion = new NPC { width = 20, height = 42, position = new Vector2(20 * 16, 918), lifeMax = 100, life = 100 };
        var sense = new ThreatSense();
        for (int tick = 0; tick < 65; tick++) sense.Update(player, companion);
        if (sense.Threats.Count != 1 || sense.PlayerDanger != 0 || sense.CompanionDanger <= 0)
            throw new InvalidOperationException($"Personal danger coupled to owner: player={sense.PlayerDanger}, companion={sense.CompanionDanger}, threats={sense.Threats.Count}");
        (player.position, companion.position) = (companion.position, player.position);
        for (int tick = 0; tick < 65; tick++) sense.Update(player, companion);
        if (sense.PlayerDanger <= 0 || sense.CompanionDanger != 0)
            throw new InvalidOperationException($"Reverse chamber danger coupled: player={sense.PlayerDanger}, companion={sense.CompanionDanger}");
        companion.position = new Vector2(20 * 16, 918);
        sense.Update(player, companion);
        if (sense.CompanionDanger <= 0)
            throw new InvalidOperationException("Entering the enemy's chamber retained a stale safe verdict");
        player.position = new Vector2(40 * 16, 918);
        enemy.noGravity = true;
        for (int tick = 0; tick < 65; tick++) sense.Update(player, companion);
        if (sense.PlayerDanger != 0 || sense.CompanionDanger <= 0)
            throw new InvalidOperationException("A tile-colliding flyer's danger was not independent");
        enemy.noTileCollide = true;
        sense.Update(player, companion);
        if (sense.PlayerDanger <= 0 || sense.CompanionDanger <= 0)
            throw new InvalidOperationException("A phaser failed to threaten both occupants");
        Console.WriteLine("personal danger: independent walker/flyer chambers, moved-endpoint invalidation and phaser pass");
        return 0;
    }
}
