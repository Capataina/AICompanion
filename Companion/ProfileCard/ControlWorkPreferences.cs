#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// The three work controls on the right of the identity strip: mining and chopping at Off, Mimic and Auto, and
/// torches at Off and On, each an item icon beside a joined segment control. These are the only preferences the
/// card offers. Hunting and pot breaking are not toggles in the first version and following distance never
/// changes, by the owner's ruling, so the card draws none of them; their saved values are still honoured by the
/// brain, which is recorded in this folder's guide.
/// </summary>
public sealed class ControlWorkPreferences : UIElement
{
    public const float RowHeight = 26, RowStep = 33, IconSize = 26, IconGap = 10, SegmentHeight = 22;

    /// <summary>What a row controls, its icon item and its choices; the order is the drawn order. There is no hover hint, by the owner's ruling that nothing on the card shows hover text.</summary>
    public static readonly (string Name, int Icon, string[] Choices)[] Rows =
    {
        ("Mining", ItemID.CopperPickaxe, new[] { "Off", "Mimic", "Auto" }),
        ("Chopping", ItemID.CopperAxe, new[] { "Off", "Mimic", "Auto" }),
        ("Torches", ItemID.Torch, new[] { "Off", "On" }),
    };

    private readonly List<UIElement> rows = new();

    public ControlWorkPreferences()
    {
        for (int i = 0; i < Rows.Length; i++)
        {
            var (_, icon, choices) = Rows[i];
            var row = new UIElement();
            row.Top.Set(4 + i * RowStep, 0); row.Width.Set(0, 1f); row.Height.Set(RowHeight, 0);
            var picture = new ItemIcon(icon);
            picture.Width.Set(IconSize, 0); picture.Height.Set(IconSize, 0);
            row.Append(picture);
            JoinedSegments control = i switch
            {
                0 => new JoinedSegments(choices, c => (int)WorkPolicies.Mining == c, c => WorkPolicies.Mining = (WorkPolicy)c),
                1 => new JoinedSegments(choices, c => (int)WorkPolicies.Chopping == c, c => WorkPolicies.Chopping = (WorkPolicy)c),
                _ => new JoinedSegments(choices, c => CompanionPreferences.Current.TorchPlacement == (c == 1), c => CompanionPreferences.Current.TorchPlacement = c == 1),
            };
            control.Left.Set(IconSize + IconGap, 0); control.Top.Set((RowHeight - SegmentHeight) / 2, 0);
            control.Width.Set(-(IconSize + IconGap), 1f); control.Height.Set(SegmentHeight, 0);
            row.Append(control);
            rows.Add(row);
            Append(row);
        }
    }

    /// <summary>The joined control of a row, for the fixture to click.</summary>
    public JoinedSegments Control(int row) => (JoinedSegments)rows[row].Children.Last();

    /// <summary>An item's own icon, fitted into its square without stretching.</summary>
    private sealed class ItemIcon(int type) : UIElement
    {
        protected override void DrawSelf(SpriteBatch sb) => DrawItemFitted(sb, type, GetDimensions().ToRectangle());
    }

    public static void DrawItemFitted(SpriteBatch sb, int type, Rectangle area)
    {
        Main.instance?.LoadItem(type);
        if (TextureAssets.Item[type]?.IsLoaded != true) return;
        Texture2D texture = TextureAssets.Item[type].Value;
        Rectangle source = Main.itemAnimations[type]?.GetFrame(texture) ?? texture.Frame();
        float scale = Math.Min(1f, Math.Min((float)area.Width / source.Width, (float)area.Height / source.Height));
        sb.Draw(texture, area.Center.ToVector2(), source, Color.White, 0, source.Size() / 2, scale, SpriteEffects.None, 0);
    }
}
