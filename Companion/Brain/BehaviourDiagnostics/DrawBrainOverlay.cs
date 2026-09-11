#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.SharedMovementSystem;
using AICompanion.Companion.Brain.ProjectileAiming;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.DiagnosticsConfiguration;

namespace AICompanion.Companion.Brain.BehaviourDiagnostics;

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
    public static bool ShowThreats, ShowPredictions, ShowRoutes = true, ShowCandidates;
    public static bool ShowProjectiles, ShowAiming, ShowMovement, ShowAttention;
    private static int scroll;
    private static bool decisionsPage;
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
        PlanLocalMovement.CaptureRequested = () => MayCapture && ShowMovement;
        PlanLocalMovement.CandidateEvaluated = BrainInspectorSamples.RecordMovement;
        TrajectoryAimer.CaptureRequested = () => MayCapture && ShowAiming;
        TrajectoryAimer.TraceEvaluated = BrainInspectorSamples.RecordTrace;
    }
    public override void Unload()
    {
        ToggleKey = null; Enabled = ShowWorld = false;
        PlanLocalMovement.CaptureRequested = null; PlanLocalMovement.CandidateEvaluated = null;
        TrajectoryAimer.CaptureRequested = null; TrajectoryAimer.TraceEvaluated = null;
        BrainInspectorSamples.Reset();
    }
    public override void OnWorldUnload() { Close(); BrainInspectorSamples.Reset(); }
    /// <summary>Dismiss the chooser panel. The selected layers keep drawing.</summary>
    public static void Close() { Enabled = false; }
    public static void ToggleMenu() { Enabled = !Enabled; }
    public static Rectangle PanelBounds(int width, int height)
        => new(12, 12, Math.Max(200, Math.Min(440, width - 24)), Math.Max(180, Math.Min(540, height - 24)));
    private static Rectangle Bounds => PanelBounds((int)(Main.screenWidth / Main.UIScale), (int)(Main.screenHeight / Main.UIScale));
    private static Point Mouse => new((int)(Main.mouseX / Main.UIScale), (int)(Main.mouseY / Main.UIScale));
    private static readonly string[] labels = { "Show world drawings", "Enemies and their velocity", "Predicted enemy movement", "Incoming projectiles", "Current route and destination", "Alternative destinations", "Aiming and rejected shots", "Movement and dodge choices", "Targets and attention" };
    private static readonly string[] hints = {
        "Hide all drawings without losing your selected layers.", "Red boxes are observed bodies; arrows show current velocity.",
        "Yellow paths contain only samples the brain calculated. Future enemy decisions remain unknown.",
        "Orange paths use the brain's linear projectile forecast; curved attacks may deviate.",
        "Green is walk, gold jump, blue drop. White marks the chosen destination.",
        "Cyan dots are evaluated positions, not guaranteed routes. Only retained alternatives are shown.",
        "Green arcs intercepted a target in simulation; red arcs were rejected. Evaluated does not mean fired. Samples expire after one second.",
        "Blue paths are evaluated controls; red paths were unsafe. The orange marker is a predicted passive-body collision. Samples expire after half a second.",
        "Lines connect the companion to its firing target, work target and player. Hands and feet may have different targets."
    };
    private static bool Value(int i) => i switch { 0 => ShowWorld, 1 => ShowThreats, 2 => ShowPredictions, 3 => ShowProjectiles, 4 => ShowRoutes, 5 => ShowCandidates, 6 => ShowAiming, 7 => ShowMovement, _ => ShowAttention };
    private static void Flip(int i)
    {
        switch (i) { case 0: ShowWorld = !ShowWorld; break; case 1: ShowThreats = !ShowThreats; break; case 2: ShowPredictions = !ShowPredictions; break; case 3: ShowProjectiles = !ShowProjectiles; break; case 4: ShowRoutes = !ShowRoutes; break; case 5: ShowCandidates = !ShowCandidates; break; case 6: ShowAiming = !ShowAiming; break; case 7: ShowMovement = !ShowMovement; break; case 8: ShowAttention = !ShowAttention; break; }
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
        int rows = VisibleRows(panel);
        int count = decisionsPage ? CompanionNPC.Instance?.Brain.Chooser.LastScores.Count ?? 0 : labels.Length;
        scroll = Math.Clamp(scroll - Math.Sign(Terraria.GameInput.PlayerInput.ScrollWheelDeltaForUI), 0, Math.Max(0, count - rows));
        if (!Main.mouseLeft || !Main.mouseLeftRelease) return;
        if (new Rectangle(panel.Right - 34, panel.Y + 8, 24, 24).Contains(Mouse)) { Close(); return; }
        if (new Rectangle(panel.X + 12, panel.Y + 102, panel.Width - 24, 24).Contains(Mouse))
        { decisionsPage = Mouse.X >= panel.Center.X; scroll = 0; return; }
        if (decisionsPage) return;
        for (int row = 0; row < rows && scroll + row < labels.Length; row++)
            if (RowBounds(panel, row).Contains(Mouse)) Flip(scroll + row);
    }
    public static int VisibleRows(Rectangle panel) => Math.Max(1, (panel.Height - 160) / 36);
    public static Rectangle RowBounds(Rectangle panel, int row) => new(panel.X + 12, panel.Y + 132 + row * 36, panel.Width - 24, 32);
    public override void PostDrawInterface(SpriteBatch sb)
    {
        if (!CompanionDiagnosticsConfig.Current.EnableBrainInspector || Main.gameMenu) return;
        var companion = CompanionNPC.Instance;
        if (ShowWorld && companion != null && !companion.IsDowned) DrawWorld(sb, companion);
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
        Fill(sb, new(panel.X + 12, panel.Y + 102, (panel.Width - 24) / 2, 24), decisionsPage ? Row : Highlight);
        Fill(sb, new(panel.Center.X, panel.Y + 102, (panel.Width - 24) / 2, 24), decisionsPage ? Highlight : Row);
        Text(sb, "World layers", panel.X + 24, panel.Y + 106, decisionsPage ? Color.White : Color.Gold, .6f);
        Text(sb, "Decisions", panel.Center.X + 12, panel.Y + 106, decisionsPage ? Color.Gold : Color.White, .6f);
        int rows = VisibleRows(panel);
        if (decisionsPage)
        {
            DrawDecisions(sb, panel, companion, rows);
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
            if (row.Contains(Mouse)) Main.instance.MouseText($"{score.Action.Name}: {score.Raw:0.000} × protection {score.Protection:0.00} × commitment {score.Commitment:0.00} × safety horizon {score.Horizon:0.00} × useful work {score.UsefulWork:0.00} = {score.Final:0.000}");
        }
        Text(sb, scores.Count > rows ? "Scroll to inspect every behaviour" : "Raw score: outline   Final score: fill   Winner: gold", panel.X + 14, panel.Bottom - 20, Color.LightSteelBlue, .48f);
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
        if (ShowPredictions) foreach (var t in brain.Senses.Threats.Threats) Path(sb, WorldObservation.PredictObservedMotion.ExistingForecast(t.Npc), Color.Yellow * .7f);
        if (ShowProjectiles) foreach (var p in brain.Senses.Projectiles.Threats)
        { Border(sb, WorldRect(p.Hitbox), Color.Orange); Line(sb, p.Hitbox.Center.ToVector2(), p.Predict(30).Center.ToVector2(), Color.Orange); }
        if (ShowRoutes && brain.Navigator.Path is { } route)
        {
            Vector2 previous = c.NPC.Bottom;
            for (int i = route.Index; i < Math.Min(route.Steps.Count, route.Index + 128); i++)
            {
                var step = route.Steps[i]; Vector2 p = NavGrid.FeetWorld(step.Tile);
                Color colour = step.Kind switch { MoveKind.Jump => Color.Gold, MoveKind.Drop => Color.SkyBlue, MoveKind.FallThrough => Color.Violet, _ => Color.LimeGreen };
                Line(sb, previous, p, colour * .65f); Dot(sb, p, colour, 5); previous = p;
            }
        }
        if (ShowRoutes && brain.Positioner.Chosen is Vector2 chosen) Dot(sb, chosen, Color.White, 9);
        if (ShowCandidates) foreach (string sample in brain.Positioner.CandidateEvidence.Split('|'))
        {
            string[] fields = sample.Split(':'); string[] xy = fields[0].Split(',');
            if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y)) Dot(sb, NavGrid.FeetWorld(new Point(x, y)), Color.Cyan, 5);
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
            if (BrainInspectorSamples.LastReflex is { } r && Main.GameUpdateCount - r.Tick <= 30) Dot(sb, r.Body.Feet, Color.OrangeRed, 12);
        }
        if (ShowAttention)
        {
            if (brain.EngageTarget is { } target) Line(sb, c.NPC.Center, target.Center, Color.OrangeRed);
            if (brain.LastAction?.ActivityTarget is Vector2 work) { Line(sb, c.NPC.Center, work, Color.Cyan); Dot(sb, work, Color.Cyan, 8); }
            if (brain.LastRequest.Kind is PositionSelection.RequestKind.WithPlayer or PositionSelection.RequestKind.Guard) Line(sb, c.NPC.Center, brain.Senses.Player.Bottom, Color.White * .4f);
        }
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
    private static void Line(SpriteBatch sb, Vector2 from, Vector2 to, Color c)
    {
        Vector2 a = Screen(from), d = Screen(to) - a;
        if (!float.IsFinite(a.X) || !float.IsFinite(a.Y) || !float.IsFinite(d.X) || !float.IsFinite(d.Y) || d.LengthSquared() < .1f) return;
        // MagicPixel is an atlas texture, not a 1x1 image. Scaling its whole source
        // multiplies both length and thickness by the atlas dimensions.
        sb.Draw(TextureAssets.MagicPixel.Value, a, new Rectangle(0, 0, 1, 1), c,
            d.ToRotation(), Vector2.Zero, new Vector2(d.Length(), 1.5f), SpriteEffects.None, 0);
    }
}
