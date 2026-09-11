#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Map;
using Terraria.UI;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.Brain.Behaviours.Work;
using AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Companion.ProfileCard;

/// <summary>Reads retained action evidence; displaying a reason never chooses an action.</summary>
public sealed class DrawCompanionStatus : UIElement
{
    public override void Update(GameTime time)
    {
        base.Update(time);
        if (IsMouseHovering && CompanionNPC.Instance is { } companion)
            Main.instance?.MouseText(Describe(companion));
    }

    public static string Describe(CompanionNPC companion)
    {
        var brain = companion.Brain;
        if (companion.IsDowned) return "Downed · stay nearby to revive";
        if (brain.FollowRecovery.Active) return "Catching up · returning to your side";
        if (brain.MovementStalled) return "Stuck · no movement progress; looking for another route";
        var action = brain.LastAction;
        string target = action?.ActivityTarget is Vector2 point ? Direction(companion.NPC.Center, point) : "";
        string reason = action?.Name switch
        {
            "mine" => WorkPolicies.Mining == WorkPolicy.Mimic ? "mimicking your mining" : "opportunistic",
            "chop" => WorkPolicies.Chopping == WorkPolicy.Mimic ? "mimicking your chopping" : "opportunistic",
            "guard" => "protecting you from a nearby threat",
            "hunt" => "voluntary hunting is on",
            "kite" => "making room to fight",
            "survive" => "escaping danger",
            "loot" => "nearby drops to collect",
            "place-torches" => "lighting a dark route with supplied torches",
            "break-pots" => "nearby pot within reach",
            "walk-with" => "keeping within your chosen distance",
            "wander" => "staying nearby while there is time",
            _ => "no activity currently selected"
        };
        string activity = brain.ActivityStatus;
        if (action is MineAction mine && mine.TargetTile is Point tile && WorldGen.InWorld(tile.X, tile.Y))
        {
            string ore = Lang.GetMapObjectName(MapHelper.TileToLookup(Main.tile[tile.X, tile.Y].TileType, 0));
            activity = "Mining · " + (string.IsNullOrWhiteSpace(ore) ? "ore vein" : ore + " vein");
        }
        string reflex = brain.Reflexes.Active is { Length: > 0 } active ? " · " + active : "";
        return activity + (target.Length > 0 ? " · " + target : "") + " · " + reason + reflex;
    }

    private static string Direction(Vector2 from, Vector2 to)
    {
        Vector2 tiles = (to - from) / 16;
        int distance = (int)MathF.Round(Math.Max(Math.Abs(tiles.X), Math.Abs(tiles.Y)));
        if (distance <= 1) return "beside me";
        string direction = Math.Abs(tiles.Y) > Math.Abs(tiles.X) ? (tiles.Y > 0 ? "below" : "above") : (tiles.X > 0 ? "right" : "left");
        return $"{distance} tiles {direction}";
    }

    protected override void DrawSelf(SpriteBatch sb)
    {
        Rectangle r = GetDimensions().ToRectangle();
        var companion = CompanionNPC.Instance;
        Rectangle portrait = new(r.X + 2, r.Y + 3, 72, r.Height - 8);
        DrawCardPrimitives.Fill(sb, portrait, new Color(37, 41, 122) * .75f);
        if (companion != null && companion.Body.UsesPlayerRenderer && Main.MapPlayerRenderer != null)
            Main.MapPlayerRenderer.DrawPlayerHead(Main.Camera, companion.Body.Player, portrait.Center.ToVector2(), 1f, 1.8f, Color.White);
        else if (TextureAssets.Npc[NPCID.Guide]?.IsLoaded == true)
        {
            // The NPC already uses the Guide as its fallback. Draw one frame, never the sheet.
            Texture2D texture = TextureAssets.Npc[NPCID.Guide].Value;
            var source = new Rectangle(0, 0, texture.Width, texture.Height / 25);
            float scale = Math.Min((portrait.Width - 12f) / source.Width, (portrait.Height - 8f) / source.Height);
            sb.Draw(texture, portrait.Center.ToVector2(), source, Color.White, 0, source.Size() / 2, scale, SpriteEffects.None, 0);
        }
        int x = r.X + 88;
        string name = companion == null || string.IsNullOrWhiteSpace(companion.NPC.GivenName) ? "Companion" : companion.NPC.GivenName;
        string hp = companion == null ? "Unavailable" : $"{companion.NPC.life} / {companion.NPC.lifeMax} HP";
        Vector2 hpSize = FontAssets.MouseText.Value.MeasureString(hp) * .75f;
        DrawCardPrimitives.WrappedText(sb, name, new Rectangle(x, r.Y + 3, r.Right - (int)hpSize.X - x - 20, 26), Color.White, 1f);
        DrawCardPrimitives.Text(sb, hp, new Vector2(r.Right - hpSize.X - 6, r.Y + 7), Color.LightGreen, .75f);
        Rectangle bar = new(x, r.Y + 31, r.Right - x - 6, 8);
        DrawCardPrimitives.Fill(sb, bar, new Color(22, 24, 69));
        bar.Width = companion == null ? 0 : (int)(bar.Width * Math.Clamp((float)companion.NPC.life / Math.Max(1, companion.NPC.lifeMax), 0, 1));
        DrawCardPrimitives.Fill(sb, bar, new Color(102, 221, 116));
        DrawCardPrimitives.WrappedText(sb, companion == null ? "No companion is present." : Describe(companion), new Rectangle(x, r.Y + 48, r.Right - x - 8, r.Height - 48), Color.White, .75f);
        DrawCardPrimitives.Fill(sb, new Rectangle(r.X, r.Bottom, r.Width, 1), DrawCardPrimitives.Edge * .6f);
    }
}
