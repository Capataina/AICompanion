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
using AICompanion.Companion.Brain.Activities.Gathering;
using AICompanion.Companion.Brain.Activities;
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
            "survive" => "escaping danger",
            "collect" => "nearby drops or worthwhile pot contents",
            "place-torches" => "lighting a dark route with supplied torches",
            "keep-company" => "accompanying you and moving nearby when there is time",
            _ => "no activity currently selected"
        };
        string activity = brain.ActivityStatus;
        if (action is MineOre mine && mine.TargetTile is Point tile && WorldGen.InWorld(tile.X, tile.Y))
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
        // The portrait is the game's Destroyer probe, the texture the orb itself is drawn with
        // until its own art exists, so the card shows the thing that is actually in the world.
        // One frame of the sheet, never the sheet.
        Main.instance?.LoadNPC(NPCID.Probe);
        if (TextureAssets.Npc[NPCID.Probe]?.IsLoaded == true)
        {
            Texture2D texture = TextureAssets.Npc[NPCID.Probe].Value;
            var source = new Rectangle(0, 0, texture.Width, texture.Height / Math.Max(1, Main.npcFrameCount[NPCID.Probe]));
            float scale = Math.Min((portrait.Width - 16f) / source.Width, (portrait.Height - 16f) / source.Height);
            sb.Draw(texture, portrait.Center.ToVector2(), source, Color.White, 0, source.Size() / 2, scale, SpriteEffects.None, 0);
        }
        int x = r.X + 88;
        string name = companion == null || string.IsNullOrWhiteSpace(companion.NPC.GivenName) ? "Companion" : companion.NPC.GivenName;
        var save = Main.LocalPlayer.GetModPlayer<CompanionPlayer>();
        string level = companion == null ? "" : $"Level {save.Experience.Level}";
        Vector2 levelSize = FontAssets.MouseText.Value.MeasureString(level) * .8f;
        DrawCardPrimitives.WrappedText(sb, name, new Rectangle(x, r.Y + 3, r.Right - (int)levelSize.X - x - 20, 26), Color.White, 1f);
        DrawCardPrimitives.Text(sb, level, new Vector2(r.Right - levelSize.X - 6, r.Y + 6), Gold, .8f);

        // Health, mana and experience stacked in the notch's order, each a bar with its reading
        // beside it in the bar's own colour, so the strip and the notch teach one column.
        int readingW = 112, barY = r.Y + 27, rowStep = 12;
        Rectangle bar = new(x, barY, r.Right - x - readingW - 6, 7);
        (float Fraction, string Reading, Color Fill)[] rows = companion == null
            ? new[] { (0f, "Unavailable", HealthFill) }
            : new[]
            {
                (Math.Clamp((float)companion.NPC.life / Math.Max(1, companion.NPC.lifeMax), 0, 1), $"{companion.NPC.life} / {companion.NPC.lifeMax} HP", HealthFill),
                (companion.Mana.Fraction, $"{(int)MathF.Round(companion.Mana.Current)} / {companion.Mana.Max} MP", ManaFill),
                (save.Experience.Fraction, $"{save.Experience.IntoLevel} / {save.Experience.NeededNow} XP", ExperienceFill),
            };
        foreach (var (fraction, reading, fill) in rows)
        {
            DrawCardPrimitives.Fill(sb, bar, new Color(22, 24, 69));
            DrawCardPrimitives.Fill(sb, bar with { Width = (int)(bar.Width * fraction) }, fill);
            DrawCardPrimitives.Text(sb, reading, new Vector2(bar.Right + 8, bar.Y - 3), fill, .66f);
            bar.Y += rowStep;
        }
        DrawCardPrimitives.WrappedText(sb, companion == null ? "No companion is present." : Describe(companion), new Rectangle(x, r.Y + 64, r.Right - x - 8, r.Height - 64), Color.White, .75f);
        DrawCardPrimitives.Fill(sb, new Rectangle(r.X, r.Bottom, r.Width, 1), DrawCardPrimitives.Edge * .6f);
    }

    private static readonly Color Gold = new(255, 224, 102);
    private static readonly Color HealthFill = new(102, 221, 116);
    /// <summary>The game's own mana-star blue and the mock's experience gold; the notch draws the same two.</summary>
    private static readonly Color ManaFill = new(106, 168, 255);
    private static readonly Color ExperienceFill = new(255, 210, 74);
}
