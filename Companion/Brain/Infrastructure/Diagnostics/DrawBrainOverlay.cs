#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Aiming;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.DiagnosticsConfiguration;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// Selectable evidence from the real brain. The menu and the drawings are two separate things:
/// <see cref="Enabled"/> is whether the chooser panel is on screen, <see cref="ShowWorld"/> is
/// whether the selected layers draw over the world. Closing the menu leaves the layers drawing,
/// because the menu exists to pick what to watch and watching it is the whole point — a panel
/// that has to stay open covers the thing it is describing. "Show world drawings" is the master
/// off switch, and the diagnostics config can force both off.
/// </summary>
public sealed class BrainOverlay : ModSystem
{
    public static ModKeybind? ToggleKey;
    public static bool Enabled, ShowWorld = true;
    public static bool ShowThreats = true, ShowPredictions = true, ShowRoutes = true, ShowCandidates = true;
    public static bool ShowProjectiles = true, ShowAiming = true, ShowMovement = true, ShowAttention = true, ShowRegion = true;
    public static bool ShowFollow = true, ShowSenses = true, ShowSafety = true, ShowCost = true, ShowClearance = true;
    public const int AllLayers = 1 | 2 | 4 | 8 | 16 | 32 | 64 | 128 | 256 | 512 | 1024 | 2048 | 4096 | 8192 | 16384;

    /// <summary>
    /// The layer switches as one integer, so the character save can carry which drawings the
    /// player had turned on. Which layers someone watches is a standing choice about how they work
    /// rather than a per-session decision, and re-picking them on every launch is the tax this
    /// removes. The chooser panel's own open/closed state is deliberately not in here: it covers
    /// the drawings it describes, so a session that reopened it every launch would start obscured.
    /// The bit order is the menu order and must not be reshuffled, because an older save's integer
    /// is read against it; a new layer is appended at the next bit, which is how the success-region
    /// layer took bit 9 and the four intent layers took bits 10 to 13, and an older save simply
    /// leaves it off. The clearance field took bit 14; the two dense layers still to come — the
    /// candidate grid by rejection reason and the light field — take bits 15 upward, so nothing
    /// below is reused.
    /// </summary>
    public static int Layers
    {
        get => (ShowWorld ? 1 : 0) | (ShowThreats ? 2 : 0) | (ShowPredictions ? 4 : 0)
            | (ShowProjectiles ? 8 : 0) | (ShowRoutes ? 16 : 0) | (ShowCandidates ? 32 : 0)
            | (ShowAiming ? 64 : 0) | (ShowMovement ? 128 : 0) | (ShowAttention ? 256 : 0) | (ShowRegion ? 512 : 0)
            | (ShowFollow ? 1024 : 0) | (ShowSenses ? 2048 : 0) | (ShowSafety ? 4096 : 0) | (ShowCost ? 8192 : 0)
            | (ShowClearance ? 16384 : 0);
        set
        {
            ShowWorld = (value & 1) != 0; ShowThreats = (value & 2) != 0; ShowPredictions = (value & 4) != 0;
            ShowProjectiles = (value & 8) != 0; ShowRoutes = (value & 16) != 0; ShowCandidates = (value & 32) != 0;
            ShowAiming = (value & 64) != 0; ShowMovement = (value & 128) != 0; ShowAttention = (value & 256) != 0;
            ShowRegion = (value & 512) != 0; ShowFollow = (value & 1024) != 0; ShowSenses = (value & 2048) != 0;
            ShowSafety = (value & 4096) != 0; ShowCost = (value & 8192) != 0; ShowClearance = (value & 16384) != 0;
        }
    }

    private static int scroll;
    private static ulong costTick = ulong.MaxValue;
    // Which tab the panel shows: 0 world layers, 1 decisions, 2 execution evidence. Input and drawing both
    // read TabBounds, so a click lands on the tab that is drawn there at every panel width.
    private static int page;
    private static ulong inputTick = ulong.MaxValue;
    private static readonly Color Panel = new(33, 43, 79), Edge = new(104, 130, 187);
    private static readonly Color Row = new(39, 51, 92), Highlight = new(66, 88, 151);
    // Capture follows the drawings, never the menu. Gating this on the menu being open meant a
    // layer stopped recording the moment the panel closed, so reopening it showed an empty layer
    // and the evidence for the ticks in between was never taken.
    public static bool MayCapture => CompanionDiagnosticsConfig.Current.EnableBrainInspector && ShowWorld;
    public override void Load()
    {
        ToggleKey = KeybindLoader.RegisterKeybind(Mod, "BrainOverlay", "OemOpenBrackets");
        TrajectoryAimer.CaptureRequested = () => MayCapture && ShowAiming;
        TrajectoryAimer.TraceEvaluated = BrainInspectorSamples.RecordTrace;
    }
    public override void Unload()
    {
        ToggleKey = null; Enabled = false;
        TrajectoryAimer.CaptureRequested = null; TrajectoryAimer.TraceEvaluated = null;
        BrainInspectorSamples.Reset();
    }
    public override void OnWorldLoad()
    {
        if (CompanionDiagnosticsConfig.Current.EnableBrainInspector)
            Layers = AllLayers;
    }
    public override void OnWorldUnload() { Close(); BrainInspectorSamples.Reset(); }
    /// <summary>
    /// One cost sample per game tick, taken here rather than while drawing because drawing runs per
    /// frame: below sixty frames a second a per-frame sample silently turns "one column per tick"
    /// into one column per frame, and the strip would then read as a slower brain on a slower machine.
    /// It is a read of three retained laps and re-times nothing. A tick the brain did not run on —
    /// downed, or a frame Terraria skipped its AI — records zero rather than repeating the last
    /// value, because a repeated value draws a plateau that never happened.
    /// </summary>
    public override void PostUpdateEverything()
    {
        if (!MayCapture || !ShowCost || costTick == Main.GameUpdateCount) return;
        costTick = Main.GameUpdateCount;
        Brain? brain = CompanionNPC.Instance?.Brain;
        BrainInspectorSamples.RecordCost(brain != null && brain.LastTick == Main.GameUpdateCount
            ? (float)(brain.DecideMs + brain.PositionMs + brain.NavigateMs)
            : 0f);
    }
    /// <summary>Dismiss the chooser panel. The selected layers keep drawing.</summary>
    public static void Close() { Enabled = false; }
    public static void ToggleMenu() { Enabled = !Enabled; }
    public static Rectangle PanelBounds(int width, int height)
        => new(12, 12, Math.Max(200, Math.Min(440, width - 24)), Math.Max(180, Math.Min(540, height - 24)));
    private static Rectangle Bounds => PanelBounds((int)(Main.screenWidth / Main.UIScale), (int)(Main.screenHeight / Main.UIScale));
    private static Point Mouse => new((int)(Main.mouseX / Main.UIScale), (int)(Main.mouseY / Main.UIScale));
    private static readonly string[] labels = { "Show world drawings", "Enemies and their velocity", "Predicted enemy movement", "Incoming projectiles", "Current route and destination", "Alternative destinations", "Aiming and rejected shots", "Movement and dodge choices", "Targets and attention", "Where the purpose succeeds", "Where following wants it", "What it senses", "Safety response", "Cost of thinking", "Clearance field" };
    private static readonly string[] hints = {
        "Hide all drawings without losing your selected layers.", "Red boxes are observed bodies; arrows show current velocity.",
        "Yellow paths contain only samples the brain calculated. Future enemy decisions remain unknown.",
        "Orange paths use the brain's linear projectile forecast; curved attacks may deviate.",
        "Green is walk, gold jump, blue drop. White marks the chosen destination.",
        "Cyan dots are evaluated positions, not guaranteed routes. Only retained alternatives are shown.",
        "Green arcs intercepted a target in simulation; red arcs were rejected. Evaluated does not mean fired. Samples expire after one second.",
        "Blue paths are evaluated controls; red paths were unsafe. The orange marker is a predicted passive-body collision. Samples expire after half a second.",
        "Lines connect the companion to its firing target, work target and player. Hands and feet may have different targets.",
        "Green boxes are the comfort regions a follow destination was admitted against, around the player's feet and the anchor then. The cyan box is where a tool stand's feet reach its tile, with the tile and the stand marked. Drawn from the positioner's retained region; nothing is recomputed.",
        "Green is where following would be content: the solid box is around your feet, the dashed box around the place it was asked to go, and the two faint rings are the near and far edges of the calm distance band. The white ring is the meeting place reunion priced, labelled with its reason and both bodies' ticks to it.",
        "Yellow is the continuation the brain predicts from your recent movement, labelled with its confidence and how many samples back it. A diamond is a drop: white it can reach, orange it has proven it cannot, hollow not yet flooded. The companion's own label carries its breath and the encounter pressure charged against optional work.",
        "Orange appears only while shared safety holds the body: the response's kind and phase join the companion's label, and the line runs to the air or landing tile it is escaping to. Nothing is drawn when no response is active.",
        "One column per tick of the last second: how long deciding, positioning and navigating took together. The hairline is eight milliseconds, half a frame, and the scale never moves, so a spike reads as a spike. White is under four milliseconds, orange at or above it.",
        "Every free tile near the companion tinted by how far it is from the nearest wall: dark red is a tile the body cannot fit in, and the tint fades to nothing at the field's cap. This is the field the route search prices, so the route runs where the tint is faintest.",
    };
    // Every index is named and the default is false rather than the last layer, because a default arm holding a real layer
    // silently maps the next bit anyone appends onto that layer's toggle instead of onto its own — which is exactly what the
    // arm did when it read `_ => ShowRegion` and four layers were appended after it.
    private static bool Value(int i) => i switch { 0 => ShowWorld, 1 => ShowThreats, 2 => ShowPredictions, 3 => ShowProjectiles, 4 => ShowRoutes, 5 => ShowCandidates, 6 => ShowAiming, 7 => ShowMovement, 8 => ShowAttention, 9 => ShowRegion, 10 => ShowFollow, 11 => ShowSenses, 12 => ShowSafety, 13 => ShowCost, 14 => ShowClearance, _ => false };
    private static void Flip(int i)
    {
        switch (i) { case 0: ShowWorld = !ShowWorld; break; case 1: ShowThreats = !ShowThreats; break; case 2: ShowPredictions = !ShowPredictions; break; case 3: ShowProjectiles = !ShowProjectiles; break; case 4: ShowRoutes = !ShowRoutes; break; case 5: ShowCandidates = !ShowCandidates; break; case 6: ShowAiming = !ShowAiming; break; case 7: ShowMovement = !ShowMovement; break; case 8: ShowAttention = !ShowAttention; break; case 9: ShowRegion = !ShowRegion; break; case 10: ShowFollow = !ShowFollow; break; case 11: ShowSenses = !ShowSenses; break; case 12: ShowSafety = !ShowSafety; break; case 13: ShowCost = !ShowCost; break; case 14: ShowClearance = !ShowClearance; break; }
    }
    public static void CaptureInput()
    {
        if (!Enabled || !CompanionDiagnosticsConfig.Current.EnableBrainInspector) return;
        if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape)) { Close(); return; }
        if (!Bounds.Contains(Mouse)) return;
        Main.LocalPlayer.mouseInterface = true;
        if (inputTick == Main.GameUpdateCount) return;
        inputTick = Main.GameUpdateCount;
        Rectangle panel = Bounds;
        int rows = page == 2 ? VisibleExecutionLines(panel) : VisibleRows(panel);
        int count = page switch
        {
            1 => CompanionNPC.Instance?.Brain.Chooser.LastScores.Count ?? 0,
            2 => CompanionNPC.Instance is { } shown ? DescribeExecutionEvidence.Of(shown.Brain).Count : 0,
            _ => labels.Length,
        };
        scroll = Math.Clamp(scroll - Math.Sign(Terraria.GameInput.PlayerInput.ScrollWheelDeltaForUI), 0, Math.Max(0, count - rows));
        if (!Main.mouseLeft || !Main.mouseLeftRelease) return;
        if (new Rectangle(panel.Right - 34, panel.Y + 8, 24, 24).Contains(Mouse)) { Close(); return; }
        for (int tab = 0; tab < 3; tab++)
            if (TabBounds(panel, tab).Contains(Mouse)) { page = tab; scroll = 0; return; }
        if (page != 0) return;
        for (int row = 0; row < rows && scroll + row < labels.Length; row++)
            if (RowBounds(panel, row).Contains(Mouse)) Flip(scroll + row);
    }
    public static int VisibleRows(Rectangle panel) => Math.Max(1, (panel.Height - 160) / 36);
    public static Rectangle RowBounds(Rectangle panel, int row) => new(panel.X + 12, panel.Y + 132 + row * 36, panel.Width - 24, 32);
    /// <summary>One of the three tabs across the strip under the heading; the last takes the division's remainder so the strip is covered exactly.</summary>
    public static Rectangle TabBounds(Rectangle panel, int tab)
    {
        int width = (panel.Width - 24) / 3;
        return new(panel.X + 12 + tab * width, panel.Y + 102, tab == 2 ? panel.Width - 24 - 2 * width : width, 24);
    }
    /// <summary>Millisecond ceiling the cost strip is drawn against, fixed so a spike reads as a spike: half of a sixty-a-second frame.</summary>
    public const float CostCeilingMs = 8f;
    /// <summary>Above this a column is drawn as a warning; a quarter of a frame is where the brain starts competing with the game.</summary>
    public const float CostWarnMs = 4f;
    private const int CostColumn = 3, CostHeight = 40, CostGap = 8, CostMargin = 12;
    /// <summary>Pixels between the plot's last column and the numeric reading, and between that reading and the strip's edge.</summary>
    private const int CostTextGap = 4;

    /// <summary>
    /// Where the cost strip sits, in the panel's own logical units: under the panel's bottom edge while the chooser is open,
    /// the screen's bottom-left once it closes, and the bottom-right when the panel is open but too tall to sit above the
    /// strip. The slots are tried in that order and the first one inside the screen and clear of the open panel wins.
    ///
    /// The ordering is not decoration. A plain bottom-left fallback puts the strip *inside* the panel on a short screen —
    /// at 800x600 with interface scale two the panel runs to five logical pixels past where the strip would start, and the
    /// panel is drawn afterwards, so the columns end up under a menu. A width of zero is the honest fourth answer, returned
    /// when no slot clears the panel at all, and the caller draws nothing: on a screen that small the panel being read is
    /// worth more than a strip nobody can see, and closing the panel brings it straight back.
    ///
    /// Pure arithmetic on the two screen extents, so every placement is checkable without a window.
    /// </summary>
    public static Rectangle StripBounds(int width, int height, Rectangle? openPanel)
    {
        int columns = BrainInspectorSamples.CostTicks;
        int w = Math.Min(columns * CostColumn, Math.Max(columns, width - 2 * CostMargin));
        int h = Math.Min(CostHeight, Math.Max(12, height - 2 * CostMargin));
        Span<Point> slots = stackalloc Point[3];
        int count = 0;
        if (openPanel is Rectangle open) slots[count++] = new Point(open.X, open.Bottom + CostGap);
        slots[count++] = new Point(CostMargin, height - h - CostMargin);
        slots[count++] = new Point(width - w - CostMargin, height - h - CostMargin);
        for (int i = 0; i < count; i++)
        {
            var candidate = new Rectangle(slots[i].X, slots[i].Y, w, h);
            if (candidate.X < 0 || candidate.Y < 0 || candidate.Right > width || candidate.Bottom > height) continue;
            if (openPanel is Rectangle panel && candidate.Intersects(panel)) continue;
            return candidate;
        }
        return Rectangle.Empty;
    }

    /// <summary>Execution evidence is one short line each, so it uses half-height slots in the same list area as the rows.</summary>
    public static int VisibleExecutionLines(Rectangle panel) => Math.Max(1, (panel.Height - 160) / 18);
    public static Rectangle ExecutionLineBounds(Rectangle panel, int line) => new(panel.X + 12, panel.Y + 132 + line * 18, panel.Width - 24, 16);
    public override void PostDrawInterface(SpriteBatch sb)
    {
        if (!CompanionDiagnosticsConfig.Current.EnableBrainInspector || Main.gameMenu) return;
        var companion = CompanionNPC.Instance;
        if (ShowWorld && companion != null && !companion.IsDowned)
        {
            DrawWorld(sb, companion);
            if (ShowCost) DrawCost(sb);
        }
        if (Enabled) DrawMenu(sb, companion);
    }
    private static void DrawMenu(SpriteBatch sb, CompanionNPC? companion)
    {
        Rectangle panel = Bounds;
        Fill(sb, panel, Panel * .94f); Border(sb, panel, Edge);
        Text(sb, "Companion's eye", panel.X + 14, panel.Y + 13, Color.Gold, 1f);
        Text(sb, "X", panel.Right - 28, panel.Y + 12, Color.White, .7f);
        Text(sb, companion?.Brain.ActivityStatus ?? "Waiting for a companion", panel.X + 14, panel.Y + 42, Color.White, .75f);
        Text(sb, "Choose layers. They keep drawing after you close this.", panel.X + 14, panel.Y + 64, Color.LightSteelBlue, .52f);
        Text(sb, "Observed: red   Predicted: yellow   Chosen: white", panel.X + 14, panel.Y + 83, Color.LightGray, .48f);
        string[] tabs = { "World layers", "Decisions", "Execution" };
        for (int tab = 0; tab < tabs.Length; tab++)
        {
            Rectangle bounds = TabBounds(panel, tab);
            Fill(sb, bounds, page == tab ? Highlight : Row);
            Text(sb, tabs[tab], bounds.X + 8, bounds.Y + 4, page == tab ? Color.Gold : Color.White, .55f);
        }
        int rows = VisibleRows(panel);
        if (page == 1)
        {
            DrawDecisions(sb, panel, companion, rows);
            return;
        }
        if (page == 2)
        {
            DrawExecution(sb, panel, companion);
            return;
        }
        scroll = Math.Clamp(scroll, 0, Math.Max(0, labels.Length - rows));
        int last = Math.Min(labels.Length, scroll + rows);
        for (int i = scroll; i < last; i++)
        {
            Rectangle row = RowBounds(panel, i - scroll);
            bool hover = row.Contains(Mouse);
            Fill(sb, row, hover ? Highlight : Row);
            Border(sb, new Rectangle(row.X + 8, row.Y + 8, 16, 16), Value(i) ? Color.Gold : Edge);
            if (Value(i)) Fill(sb, new Rectangle(row.X + 12, row.Y + 12, 8, 8), Color.Gold);
            Text(sb, labels[i], row.X + 34, row.Y + 6, Color.White, .75f);
            if (hover) Main.instance.MouseText(hints[i]);
        }
        Text(sb, last < labels.Length || scroll > 0 ? "Scroll for more layers" : "Hover a layer to learn what it means", panel.X + 14, panel.Bottom - 20, Color.LightSteelBlue, .48f);
    }
    private static void DrawDecisions(SpriteBatch sb, Rectangle panel, CompanionNPC? companion, int rows)
    {
        if (companion == null) return;
        var scores = companion.Brain.Chooser.LastScores;
        scroll = Math.Clamp(scroll, 0, Math.Max(0, scores.Count - rows));
        for (int i = scroll; i < Math.Min(scores.Count, scroll + rows); i++)
        {
            var score = scores[i];
            Rectangle row = RowBounds(panel, i - scroll);
            Fill(sb, row, Row);
            int bottom = row.Y + 4;
            Text(sb, score.Action.Name, panel.X + 15, bottom, Color.White, .7f);
            int width = Math.Max(20, panel.Width - 152), start = panel.X + 108;
            Border(sb, new Rectangle(start, bottom + 3, (int)(Math.Clamp(score.Raw / 1.5f, 0, 1) * width), 8), Color.LightSteelBlue);
            Fill(sb, new Rectangle(start, bottom + 4, (int)(Math.Clamp(score.Final / 1.5f, 0, 1) * width), 6), ReferenceEquals(score.Action, companion.Brain.LastAction) ? Color.Gold : Color.CornflowerBlue);
            Text(sb, score.Final.ToString("0.00"), panel.Right - 37, bottom, Color.White, .48f);
            if (row.Contains(Mouse)) Main.instance.MouseText($"{score.Action.Name}: {score.Raw:0.000} × protection {score.Protection:0.00} × commitment {score.Commitment:0.00} × safety horizon {score.Horizon:0.00} × useful work {score.UsefulWork:0.00} × reunion {score.Reunion:0.00} = {score.Final:0.000}"
                + $"\nOffer: {score.Eligibility} ({score.EligibilityReason})"
                + (score.Error.Length > 0 ? $"\nUnavailable: {score.Error}" : "")
                + (score.MethodEvidence.Length > 0 ? $"\nMethod: {score.MethodEvidence}" : ""));
        }
        Text(sb, scores.Count > rows ? "Scroll to inspect every behaviour" : "Raw score: outline   Final score: fill   Winner: gold", panel.X + 14, panel.Bottom - 20, Color.LightSteelBlue, .48f);
    }
    /// <summary>
    /// What the companion is doing about its choice, line by line from <see cref="DescribeExecutionEvidence"/>: each family's
    /// nominee beside its children's offers, the region the destination was admitted against, the control requested beside
    /// the control granted, and the open attempt beside how the last one ended. Every line is retained state.
    /// </summary>
    private static void DrawExecution(SpriteBatch sb, Rectangle panel, CompanionNPC? companion)
    {
        if (companion == null) return;
        var lines = DescribeExecutionEvidence.Of(companion.Brain);
        int visible = VisibleExecutionLines(panel);
        scroll = Math.Clamp(scroll, 0, Math.Max(0, lines.Count - visible));
        for (int i = scroll; i < Math.Min(lines.Count, scroll + visible); i++)
        {
            Rectangle bounds = ExecutionLineBounds(panel, i - scroll);
            var line = lines[i];
            if (line.Heading) Fill(sb, bounds, Row);
            Text(sb, Fit(line.Text, .5f, bounds.Width - 8), bounds.X + (line.Heading ? 4 : 12), bounds.Y + 1, line.Heading ? Color.Gold : Color.White, .5f);
        }
        Text(sb, lines.Count > visible ? "Scroll for the rest of the evidence" : "Retained evidence only; nothing here is recomputed", panel.X + 14, panel.Bottom - 20, Color.LightSteelBlue, .48f);
    }
    /// <summary><paramref name="text"/> shortened with an ellipsis until it fits <paramref name="width"/> at <paramref name="scale"/>.</summary>
    private static string Fit(string text, float scale, int width)
    {
        var font = FontAssets.MouseText.Value;
        if (font.MeasureString(text).X * scale <= width) return text;
        int length = text.Length;
        while (length > 1 && font.MeasureString(text[..length] + "…").X * scale > width) length--;
        return text[..length] + "…";
    }
    private static void DrawWorld(SpriteBatch sb, CompanionNPC c)
    {
        Brain brain = c.Brain;
        if (ShowThreats) foreach (var t in brain.Senses.Threats.Threats)
        {
            Border(sb, WorldRect(t.Npc.Hitbox), Color.OrangeRed);
            Line(sb, t.Npc.Center, t.Npc.Center + t.Npc.velocity * 12, Color.OrangeRed);
            var p = Screen(t.Npc.Top);
            Text(sb, $"you {t.Urgency:0.00} / companion {t.UrgencyToCompanion:0.00}", p.X, p.Y - 16, Color.Orange, .45f);
        }
        if (ShowPredictions) foreach (var t in brain.Senses.Threats.Threats) Path(sb, Infrastructure.Observation.PredictObservedMotion.ExistingForecast(t.Npc), Color.Yellow * .7f);
        if (ShowProjectiles) foreach (var p in brain.Senses.Projectiles.Threats)
        { Border(sb, WorldRect(p.Hitbox), Color.Orange); Line(sb, p.Hitbox.Center.ToVector2(), p.Predict(30).Center.ToVector2(), Color.Orange); }
        if (ShowClearance)
        {
            // The field the route search prices, drawn under the route so the route can be read
            // against it: a tile the body cannot fit in is red, and the tint fades to nothing at the
            // field's cap. Read from the shared field, never recomputed, so the drawing is the search's.
            const int Radius = 14;
            Point at = MovementQueries.Tile(c.NPC.Center);
            var world = MovementQueries.World;
            for (int dx = -Radius; dx <= Radius; dx++)
                for (int dy = -Radius; dy <= Radius; dy++)
                {
                    int x = at.X + dx, y = at.Y + dy;
                    if (!MovementQueries.IsFreeForOrb(x, y)) continue;
                    float clearance = Infrastructure.Movement.ClearanceField.Shared.At(world, x, y);
                    float share = 1f - Math.Clamp(clearance / Infrastructure.Movement.ClearanceField.MaxTiles, 0f, 1f);
                    if (share <= 0f) continue;
                    Fill(sb, WorldRect(new Rectangle(x * 16, y * 16, 16, 16)), Color.Red * (.5f * share));
                }
        }
        if (ShowRoutes && brain.Navigator.Path is { } route)
        {
            Vector2 previous = c.NPC.Center;
            for (int i = route.Index + 1; i < Math.Min(route.Count, route.Index + 128); i++)
            {
                Vector2 p = route.Points[i];
                Line(sb, previous, p, Color.LimeGreen * .65f); Dot(sb, p, Color.LimeGreen, 5); previous = p;
            }
            Dot(sb, brain.Navigator.Lookahead, Color.Gold, 6);
        }
        if (ShowRoutes && brain.Positioner.Chosen is Vector2 chosen) Dot(sb, chosen, Color.White, 9);
        if (ShowCandidates) foreach (string sample in brain.Positioner.CandidateEvidence.Split('|'))
        {
            string[] fields = sample.Split(':'); string[] xy = fields[0].Split(',');
            if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y)) Dot(sb, MovementQueries.HoverPoint(new Point(x, y)), Color.Cyan, 5);
        }
        if (ShowAiming) foreach (var trace in BrainInspectorSamples.AimTraces)
            if (Main.GameUpdateCount - trace.Tick <= 60 && trace.Points.Length > 0)
            {
                Path(sb, trace.Points, trace.Accepted ? Color.LimeGreen : Color.IndianRed * .6f);
                Dot(sb, trace.Points[^1], trace.Accepted ? Color.LimeGreen : Color.Red, 5);
                HoverEvidence(trace.Points[^1], trace.Reason);
            }
        if (ShowAiming && BrainInspectorSamples.LastAim is { } aim && Main.GameUpdateCount - aim.Tick <= 60)
        {
            Dot(sb, aim.Muzzle, Color.Gold, 7);
            HoverEvidence(aim.Muzzle, $"{aim.Weapon}: {aim.Outcome}. Evaluated launch: {aim.Launch?.ToString() ?? "none"}");
        }
        if (ShowMovement)
        {
            foreach (var trace in BrainInspectorSamples.MovementTraces) if (Main.GameUpdateCount - trace.Tick <= 30 && trace.Points.Length > 0)
            { Path(sb, trace.Points, trace.Accepted ? Color.Cyan : Color.Red * .65f); HoverEvidence(trace.Points[^1], trace.Reason); }
            if (BrainInspectorSamples.LastReflex is { } r && Main.GameUpdateCount - r.Tick <= 30) Dot(sb, r.Body.Centre, Color.OrangeRed, 12);
        }
        if (ShowAttention)
        {
            if (brain.EngageTarget is { } target) Line(sb, c.NPC.Center, target.Center, Color.OrangeRed);
            if (brain.LastAction?.ActivityTarget is Vector2 work) { Line(sb, c.NPC.Center, work, Color.Cyan); Dot(sb, work, Color.Cyan, 8); }
            if (brain.LastRequest.Kind is Infrastructure.Position.RequestKind.WithPlayer or Infrastructure.Position.RequestKind.Guard) Line(sb, c.NPC.Center, brain.Senses.Player.Bottom, Color.White * .4f);
        }
        if (ShowFollow) DrawFollow(sb, brain);
        if (ShowSenses) DrawSensed(sb, brain);
        if (ShowSafety && brain.Safety.Active && brain.Safety.Escape.AirTarget is Point refuge)
            Line(sb, c.NPC.Center, refuge.ToWorldCoordinates(), Color.Orange);
        // One label for the companion however many layers have something to say about it, because two
        // labels above one body overlap into an unreadable smear the moment both layers are on.
        DrawCompanionLabel(sb, c, brain);
        if (ShowRegion) DrawRegion(sb, brain.Positioner.Region);
    }

    /// <summary>Fixed screen sizes, so a drop or a meeting place forty tiles away is still findable rather than a pixel.</summary>
    private const int LootDiamond = 6, MeetingRing = 10;
    /// <summary>How far ahead the player-intent arrow is drawn. The sense's Intent is already confidence-weighted pace in px/tick,
    /// so the arrow's length carries the confidence without being scaled by it a second time, and the tip is literally the
    /// continuation the brain itself would predict this many ticks out.</summary>
    private const int IntentTicks = 45;

    /// <summary>
    /// Where following would be content: the comfort box around the player's feet and the grown box around the place the
    /// request actually named, built from the same two retained inputs the brain builds its objective from, plus the calm
    /// distance band and the meeting place reunion priced. The success-region layer draws the admitted box on top where one
    /// exists; this one draws what the current tick wants, which is not the same thing whenever a destination is being held.
    /// </summary>
    private static void DrawFollow(SpriteBatch sb, Brain brain)
    {
        var meeting = brain.Meeting;
        if (meeting.HasPlace)
        {
            // Both the ring and the label's offset are screen pixels, and they have to be the same kind of pixel or they
            // separate with the zoom: Label lifts its text a further 16 and the text is about 13 tall, so the label's foot
            // sits 3 px clear of the ring's top here and at every zoom. A world-radius ring put that foot inside the ring
            // the moment the view was zoomed in.
            ScreenRing(sb, Screen(meeting.Anchor), MeetingRing, Color.White);
            Label(sb, Screen(meeting.Anchor) - new Vector2(0, MeetingRing), $"{meeting.Reason}  you {Ticks(meeting.PlayerTicks)} / it {Ticks(meeting.CompanionTicks)}", Color.White);
        }
        if (brain.LastRequest.Kind != Infrastructure.Position.RequestKind.WithPlayer) return;
        // The heading box is the player's intent region now, so what is drawn is what every consumer
        // reads: the box where it actually sits (led, filtered, clamped and grown), the lead that put
        // it there as a line from his feet, the leading edge the walk aims at, and the pull as a tint
        // on the box, so a body drifting to an edge is visible before it is a complaint.
        Vector2 player = brain.Senses.Player.Bottom;
        var region = brain.Senses.Intent.Region;
        float pull = MathF.Min(1f, region.Pull(brain.Senses.Companion.Center));
        Color regionTint = Color.Lerp(Color.LightGreen, Color.Orange, pull);
        Box(sb, region.Centre, region.HalfSize, regionTint);
        if (region.Lead.LengthSquared() > 1f)
        {
            Line(sb, player, region.Centre, regionTint * .6f);
            ScreenRing(sb, Screen(region.LeadingEdge), MeetingRing * .5f, regionTint);
        }
        Label(sb, Screen(region.Centre) - new Vector2(0, region.HalfSize.Y),
            $"intent {(region.IsTravelling ? "travelling" : "still")} pull {pull:F2} settled {brain.Senses.Intent.RestingInsideTicks}", regionTint);
        DashedBox(sb, brain.LastRequest.Anchor, region.HalfSize * .25f, Color.LightGreen);
        Ring(sb, player, Infrastructure.Selection.Weights.CalmBandNear, Color.LightGreen * .3f);
        Ring(sb, player, Infrastructure.Selection.Weights.CalmBandFar, Color.LightGreen * .3f);
    }

    /// <summary>
    /// What the senses hold about the world the feet have to deal with: where the player is going, and which drops the body
    /// can actually get to. A drop's reach is asked of the positioner's last flood by the drop's own feet tile — a membership
    /// test on a retained set, never a new flood — so a drop resting on a slope can read unreachable while a stand beside it
    /// is reachable, and an unfinished flood leaves it hollow rather than calling it unreachable.
    /// </summary>
    private static void DrawSensed(SpriteBatch sb, Brain brain)
    {
        var player = brain.Senses.Player;
        Vector2 tip = player.Predict(IntentTicks);
        if (Vector2.DistanceSquared(tip, player.Bottom) > 4f)
        {
            Line(sb, player.Bottom, tip, Color.Yellow);
            Dot(sb, tip, Color.Yellow, 5);
            Label(sb, Screen(tip), $"intent {player.Activity.Confidence:0.00} from {player.Activity.Samples} samples", Color.Yellow);
        }
        foreach (var pickup in brain.Senses.Loot.Pickups)
        {
            bool reachable = brain.Positioner.Reaches(MovementQueries.Tile(pickup.Item.Bottom));
            // Known is the sense's own answer, not "the flood finished": beyond its known radius a
            // finished flood has proven nothing, and painting those diamonds as absent would show
            // the owner a refusal the brain never made.
            bool known = reachable || brain.Positioner.ProvenUnreachableTile(MovementQueries.Tile(pickup.Item.Bottom));
            Diamond(sb, pickup.Item.Center, LootDiamond, reachable ? Color.White : known ? Color.Orange : Color.LightSteelBlue, known);
        }
    }

    private static void DrawCompanionLabel(SpriteBatch sb, CompanionNPC c, Brain brain)
    {
        string text = "";
        if (ShowSenses)
            text = $"wet {brain.Senses.Self.LiquidContactTicks}  pressure {brain.Senses.Encounter.PressureTicks}";
        if (ShowSafety && brain.Safety.Active)
            text = (text.Length > 0 ? text + "  " : "")
                + $"{brain.Safety.Kind}: {(brain.Safety.Escape.EscapeActive ? brain.Safety.Escape.EscapeStage : brain.Safety.Reason)}";
        if (text.Length > 0) Label(sb, Screen(c.NPC.Top), text, Color.White);
    }

    /// <summary>
    /// What the last second of thinking cost, one column per tick, against a ceiling that never moves: a strip that rescaled
    /// itself would make every session look the same, and the whole question is whether this one spikes. The plot reserves a
    /// fifth of its height above the ceiling line so a tick that overruns half a frame is visibly over it rather than merely
    /// full, and a gutter on its right for the current reading so the newest columns are never drawn underneath it.
    /// <see cref="StripBounds"/> places it, and draws nothing at all where no placement is free.
    /// </summary>
    private static void DrawCost(SpriteBatch sb)
    {
        int samples = BrainInspectorSamples.CostSamples;
        if (samples == 0) return;
        Rectangle strip = StripBounds((int)(Main.screenWidth / Main.UIScale), (int)(Main.screenHeight / Main.UIScale), Enabled ? Bounds : null);
        if (strip.Width == 0) return;
        Fill(sb, strip, Panel * .88f);
        Border(sb, strip, Edge);
        // The reading is measured before the plot is laid out and the plot ends where the reading begins, because the newest
        // columns are the right-hand ones and they are the whole reason the strip is on screen: drawn under the text, a
        // spike in the last third of a second is the one sample nobody can see.
        string now = $"{BrainInspectorSamples.LastCost:0.00} ms";
        float text = FontAssets.MouseText.Value.MeasureString(now).X * .45f;
        // The reading never takes more than half the strip: on a narrow screen a plot squeezed to nothing is worse than a
        // reading that runs under its own last columns, and half of an already-small strip is still readable.
        int gutter = Math.Min((int)MathF.Ceiling(text) + CostTextGap, (strip.Width - 2) / 2);
        int plotWidth = strip.Width - 2 - gutter;
        int columns = BrainInspectorSamples.CostTicks, plot = Math.Max(1, strip.Height - 2);
        float width = plotWidth / (float)columns;
        for (int i = 0; i < samples; i++)
        {
            float ms = BrainInspectorSamples.CostAt(i);
            int height = (int)MathF.Round(Math.Clamp(ms / CostCeilingMs * .8f, 0f, 1f) * plot);
            if (height <= 0) continue;
            int x = strip.X + 1 + (int)((columns - samples + i) * width);
            int right = strip.X + 1 + plotWidth;
            Fill(sb, new Rectangle(x, strip.Bottom - 1 - height, Math.Max(1, Math.Min((int)width, right - x)), height),
                ms >= CostWarnMs ? Color.Orange : Color.White);
        }
        int ceiling = strip.Bottom - 1 - (int)MathF.Round(plot * .8f);
        ScreenLine(sb, new Vector2(strip.X + 1, ceiling), new Vector2(strip.X + 1 + plotWidth, ceiling), Color.LightSteelBlue * .7f);
        Text(sb, now, strip.Right - CostTextGap - text, strip.Y + 2, Color.LightSteelBlue, .45f);
    }

    private static string Ticks(float value) => float.IsNaN(value) ? "-" : value.ToString("0.0");

    /// <summary>One label centred over a point already in screen pixels, at the one text scale every world label here uses.</summary>
    private static void Label(SpriteBatch sb, Vector2 above, string text, Color c)
    {
        const float Scale = .45f;
        Text(sb, text, above.X - FontAssets.MouseText.Value.MeasureString(text).X * Scale / 2f, above.Y - 16, c, Scale);
    }
    /// <summary>The retained success region's boxes, its work tile and its anchor. Internal so the offscreen renderer can draw a
    /// seeded region and measure the painted pixels against the geometry <see cref="DescribeExecutionEvidence.RegionBoxes"/> returns.</summary>
    internal static void DrawRegion(SpriteBatch sb, in Infrastructure.Position.SuccessRegion region)
    {
        Color colour = region.Kind == Infrastructure.Position.SuccessRegionKind.ToolReach ? Color.Cyan : Color.LightGreen;
        foreach (var box in DescribeExecutionEvidence.RegionBoxes(region))
        {
            Vector2 a = Screen(box.Min), b = Screen(box.Max);
            Border(sb, new Rectangle((int)MathF.Round(a.X), (int)MathF.Round(a.Y), (int)MathF.Round(b.X - a.X), (int)MathF.Round(b.Y - a.Y)), colour);
        }
        if (region.WorkTile is Point tile) Dot(sb, tile.ToWorldCoordinates(), Color.Cyan, 7);
        if (region.Kind != Infrastructure.Position.SuccessRegionKind.None) Dot(sb, region.Anchor, Color.White, 5);
    }
    private static void HoverEvidence(Vector2 point, string explanation)
    {
        if (Vector2.DistanceSquared(Screen(point), Mouse.ToVector2()) <= 12 * 12
            && !(Enabled && Bounds.Contains(Mouse))) Main.instance.MouseText(explanation);
    }
    private static void Path(SpriteBatch sb, IReadOnlyList<Vector2> points, Color colour) { for (int i = 1; i < Math.Min(points.Count, 181); i++) Line(sb, points[i - 1], points[i], colour); }
    private static void Fill(SpriteBatch sb, Rectangle r, Color c) { if (r.Width > 0 && r.Height > 0) sb.Draw(TextureAssets.MagicPixel.Value, r, c); }
    private static void Border(SpriteBatch sb, Rectangle r, Color c) { Fill(sb, new(r.X, r.Y, r.Width, 1), c); Fill(sb, new(r.X, r.Bottom - 1, r.Width, 1), c); Fill(sb, new(r.X, r.Y, 1, r.Height), c); Fill(sb, new(r.Right - 1, r.Y, 1, r.Height), c); }
    private static void Text(SpriteBatch sb, string text, float x, float y, Color c, float scale) => Utils.DrawBorderStringFourWay(sb, FontAssets.MouseText.Value, text, x, y, c, Color.Black, Vector2.Zero, scale);
    private static Vector2 Screen(Vector2 p) => Vector2.Transform(p - Main.screenPosition, Main.GameViewMatrix.ZoomMatrix) / Main.UIScale;
    private static Rectangle WorldRect(Rectangle r) { Vector2 a = Screen(r.TopLeft()), b = Screen(r.BottomRight()); return new((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y)); }
    private static void Dot(SpriteBatch sb, Vector2 p, Color c, int size) { p = Screen(p); Fill(sb, new((int)p.X - size / 2, (int)p.Y - size / 2, size, size), c); }
    private static void Line(SpriteBatch sb, Vector2 from, Vector2 to, Color c) => ScreenLine(sb, Screen(from), Screen(to), c);
    /// <summary>The one line primitive, in the interface's own pixels. Every world line reaches it through <see cref="Line"/>,
    /// and the screen-space drawings (a diamond's edges, the cost strip's ceiling) use it directly rather than drawing their own.</summary>
    private static void ScreenLine(SpriteBatch sb, Vector2 a, Vector2 b, Color c)
    {
        Vector2 d = b - a;
        if (!float.IsFinite(a.X) || !float.IsFinite(a.Y) || !float.IsFinite(d.X) || !float.IsFinite(d.Y) || d.LengthSquared() < .1f) return;
        // MagicPixel is an atlas texture, not a 1x1 image. Scaling its whole source
        // multiplies both length and thickness by the atlas dimensions.
        sb.Draw(TextureAssets.MagicPixel.Value, a, new Rectangle(0, 0, 1, 1), c,
            d.ToRotation(), Vector2.Zero, new Vector2(d.Length(), 1.5f), SpriteEffects.None, 0);
    }
    /// <summary>A box in world pixels around <paramref name="centre"/> with half-extents <paramref name="half"/>, as <see cref="Border"/> draws it.</summary>
    private static void Box(SpriteBatch sb, Vector2 centre, Vector2 half, Color c) => Border(sb, WorldBox(centre, half), c);
    /// <summary>The same box with its edges broken, which is how a destination's box is told from the player's own at a glance.</summary>
    private static void DashedBox(SpriteBatch sb, Vector2 centre, Vector2 half, Color c)
    {
        const int On = 5, Off = 4;
        Rectangle r = WorldBox(centre, half);
        if (r.Width <= 0 || r.Height <= 0) return;
        for (int x = r.X; x < r.Right; x += On + Off)
        {
            int run = Math.Min(On, r.Right - x);
            Fill(sb, new Rectangle(x, r.Y, run, 1), c);
            Fill(sb, new Rectangle(x, r.Bottom - 1, run, 1), c);
        }
        for (int y = r.Y; y < r.Bottom; y += On + Off)
        {
            int run = Math.Min(On, r.Bottom - y);
            Fill(sb, new Rectangle(r.X, y, 1, run), c);
            Fill(sb, new Rectangle(r.Right - 1, y, 1, run), c);
        }
    }
    private static Rectangle WorldBox(Vector2 centre, Vector2 half)
    {
        Vector2 a = Screen(centre - half), b = Screen(centre + half);
        return new Rectangle((int)MathF.Round(a.X), (int)MathF.Round(a.Y), (int)MathF.Round(b.X - a.X), (int)MathF.Round(b.Y - a.Y));
    }
    private const int RingSegments = 48;
    /// <summary>A circle of world radius, drawn as chords through <see cref="Line"/> so it scales with the zoom like everything else.
    /// Use it only where the radius is a real world distance, as the calm band's edges are; a marker of fixed size wants
    /// <see cref="ScreenRing"/>, because a world radius drawn at zoom 2 is twice the pixels its own label was offset by.</summary>
    private static void Ring(SpriteBatch sb, Vector2 centre, float radius, Color c)
    {
        if (!(radius > 1f)) return;
        Vector2 previous = centre + new Vector2(radius, 0f);
        for (int i = 1; i <= RingSegments; i++)
        {
            float angle = MathHelper.TwoPi * i / RingSegments;
            Vector2 point = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            Line(sb, previous, point, c);
            previous = point;
        }
    }
    /// <summary>A circle of a fixed pixel radius around a point already in screen pixels, so a marker stays the same size at
    /// every zoom and a label offset by the same constant clears it at every zoom too.</summary>
    private static void ScreenRing(SpriteBatch sb, Vector2 centre, float radius, Color c)
    {
        if (!float.IsFinite(centre.X) || !float.IsFinite(centre.Y) || !(radius > 1f)) return;
        Vector2 previous = centre + new Vector2(radius, 0f);
        for (int i = 1; i <= RingSegments; i++)
        {
            float angle = MathHelper.TwoPi * i / RingSegments;
            Vector2 point = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            ScreenLine(sb, previous, point, c);
            previous = point;
        }
    }
    /// <summary>
    /// A drop, at a fixed screen size so a distant one stays findable: the outline always, and the interior filled only when
    /// the thing it marks is known one way or the other. Hollow is the third state and it is drawn as an absence rather than
    /// as a third colour, because an unflooded tile is not a fact about the drop.
    /// </summary>
    private static void Diamond(SpriteBatch sb, Vector2 world, int radius, Color c, bool filled)
    {
        Vector2 p = Screen(world);
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) return;
        Vector2 up = p - new Vector2(0, radius), down = p + new Vector2(0, radius);
        Vector2 left = p - new Vector2(radius, 0), right = p + new Vector2(radius, 0);
        ScreenLine(sb, up, right, c); ScreenLine(sb, right, down, c);
        ScreenLine(sb, down, left, c); ScreenLine(sb, left, up, c);
        if (!filled) return;
        for (int row = -radius + 1; row < radius; row++)
        {
            int run = radius - Math.Abs(row);
            Fill(sb, new Rectangle((int)p.X - run + 1, (int)p.Y + row, run * 2 - 1, 1), c);
        }
    }
}
