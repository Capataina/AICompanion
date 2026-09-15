#nullable enable
using System;
using System.Collections.Generic;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// The card's logical sizes, in UI units, as one table every page lays itself out from. They come from the
/// agreed mock (<c>InterfaceExperiments/companion-card.html</c>) and the owner's rulings of 15 September 2026;
/// the render fixture holds the drawn card to its own copy of those numbers rather than to this table, so a
/// change here that drifts from the mock fails there.
///
/// <para>The mock's frame has a 2px border and 10px of padding, so its content sits 12px inside the frame's
/// outer edge. A native <c>UIPanel</c> draws its border inside its own bounds, so the native frame uses 12px of
/// padding to put every region at the mock's outer position; the content area is two pixels shorter than the
/// frame's inner height for the same reason, which is why a page's content is 508 tall in a 580 frame.</para>
/// </summary>
public static class CardRegions
{
    public const float Width = 720;
    public const float Padding = 12;
    public const float InnerWidth = Width - 2 * Padding;
    public const float TitleHeight = 34;
    /// <summary>The one vertical gap between every stacked region.</summary>
    public const float Rhythm = 12;
    public const float IdentityHeight = 100;
    /// <summary>A tile is a fill bar, a count and a name, and nothing under the name, so it is short.</summary>
    public const float TileHeight = 56;
    public const float TileGap = 10;
    public const float BodyTop = TitleHeight + Rhythm;
    public const float TilesTop = BodyTop + IdentityHeight + Rhythm;
    public const float OverviewHeight = Padding + TilesTop + TileHeight + 2 + Padding;
    /// <summary>A page's content at the mock's full height; on a screen too short for it the card gives the content less, never more.</summary>
    public const float ContentHeight = 508;
    /// <summary>What a page spends around its content, whatever the content's height: padding, title bar, rhythm and the border allowance.</summary>
    public const float PageOverhead = Padding + BodyTop + 2 + Padding;
    public const float PageHeight = PageOverhead + ContentHeight;

    public const float RoundButton = 30;
    /// <summary>Space between two title-bar actions, and between the last action and the close button.</summary>
    public const float ActionGap = 6, ActionEndGap = 8;

    /// <summary>Terraria's item slot at the card's size, never stretched; a grid's column count follows from the width.</summary>
    public const float Slot = 48, SlotPitch = 52;

    /// <summary>Where a card that has never been dragged opens: below a docked notch, with a gap.</summary>
    public const float BelowNotch = 24;
    /// <summary>How close to a docked notch the card's title bar may ever come.</summary>
    public const float NotchClearance = 12;
}

/// <summary>A button a page puts in the card's title bar: round for a one-character symbol, a pill otherwise.</summary>
public sealed record CardAction(string Label, Action Click, Func<bool>? Selected = null)
{
    public bool Round => Label.Length == 1;
}

/// <summary>A full page of the card: its name for the title bar and the actions that sit right-aligned in it.</summary>
public interface ICardPage
{
    string Title { get; }
    IReadOnlyList<CardAction> Actions { get; }
}
