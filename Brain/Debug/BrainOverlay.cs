#nullable enable

using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using AICompanion.Brain.DecisionMatrix.Navigation;
using AICompanion.Companion;

namespace AICompanion.Brain.Debug;

/// <summary>
/// Draws what the brain is thinking above the companion: every action's score, the
/// winner, danger and horizon, threats with reachability, the position request, the
/// chosen spot and the path. Toggled with a keybind. The brain is tuned by watching
/// it be wrong, and this is the window.
/// </summary>
public sealed class BrainOverlay : ModSystem
{
    public static ModKeybind? ToggleKey;
    public static bool Enabled;

    public override void Load()
    {
        // The name is the localisation key segment (Keybinds.BrainOverlay.DisplayName), so no space.
        // Default is F6. The key left of 1 was tried and can never work on a Mac ISO keyboard:
        // FNA logs "KEY/SCANCODE MISSING FROM SDL2->XNA DICTIONARY: SDL_SCANCODE_GRAVE" and drops
        // the press before it becomes a key, so neither a keybind nor a raw-key fallback sees it.
        // F6 needs Fn on a MacBook unless the F-keys are set to standard; rebindable under Controls.
        ToggleKey = KeybindLoader.RegisterKeybind(Mod, "BrainOverlay", "F6");
        Mod.Logger.Info("BrainOverlay.Load: keybind registered, default F6");
    }

    public override void Unload()
    {
        ToggleKey = null;
        Enabled = false;
        Mod.Logger.Info("BrainOverlay.Unload: done");
    }

    public override void PostDrawInterface(SpriteBatch spriteBatch)
    {
        if (!Enabled || CompanionNPC.Instance is not CompanionNPC companion)
            return;
        Brain brain = companion.Brain;
        var senses = brain.Senses;

        var sb = new StringBuilder();
        sb.AppendLine($"action  {brain.LastAction?.Name ?? "-"}   reflex {brain.Reflexes.Active ?? "-"}");
        sb.AppendLine($"danger  {senses.Threats.PlayerDanger:0.00}   horizon {(senses.Threats.Horizon == float.MaxValue ? "inf" : senses.Threats.Horizon.ToString("0"))}   intent {senses.Player.Intent.X:0.0}");
        foreach (var s in brain.Chooser.LastScores)
            sb.AppendLine($"  {s.Action.Name,-10} {s.Raw:0.00} -> {s.Final:0.00}");
        int reachable = 0;
        foreach (var t in senses.Threats.Threats) if (t.Reachable) reachable++;
        sb.AppendLine($"threats {senses.Threats.Threats.Count} ({reachable} reachable)   loot {senses.Loot.Pickups.Count}");
        sb.AppendLine($"request {brain.LastRequest.Kind}   spot {(brain.Positioner.Chosen is Vector2 c ? $"{(int)(c.X / 16)},{(int)(c.Y / 16)}" : "-")} ({brain.Positioner.ChosenScore:0.00})");
        sb.AppendLine($"path {(brain.Navigator.Path == null ? "none" : $"{brain.Navigator.Path.Steps.Count} steps, at {brain.Navigator.Path.Index}")}   planned {brain.Navigator.LastExpansions} exp{(brain.Navigator.LastPlanFailed ? "  FAILED" : "")}");
        sb.AppendLine($"tick {brain.TotalMs:0.0} ms  (senses {brain.SensesMs:0.0} reflex {brain.ReflexMs:0.0} decide {brain.DecideMs:0.0} position {brain.PositionMs:0.0} navigate {brain.NavigateMs:0.0})   last plan {brain.Navigator.LastPlanMs:0.0} flood {brain.Positioner.LastFloodMs:0.0}   edges cached {AStar.CachedTiles}");
        sb.AppendLine($"weapon {companion.Arsenal.LastChosen?.Name ?? "-"}   shot {(companion.Arsenal.LastShotSolved ? "solved" : "none")}");
        sb.AppendLine($"light ambient {senses.Light.Ambient:0.00} player {senses.Light.AtPlayer:0.00} here {senses.Light.AtCompanion:0.00}   torch {(companion.Torch.Shown ? "shown" : companion.Torch.Lit ? "lit, hand busy" : "out")}");
        sb.AppendLine($"self danger {senses.Self.SelfDanger:0.00}   breath {senses.Self.BreathFraction:0.00}{(senses.Self.HeadUnderwater ? " under" : "")}{(senses.Self.InLava ? "  LAVA" : senses.Self.OnFire ? "  on fire" : "")}   lava paths {(AStar.AllowLava ? "on" : "off")}");

        Vector2 head = ToScreen(companion.NPC.Top + new Vector2(0f, -8f));
        Vector2 size = FontAssets.MouseText.Value.MeasureString(sb.ToString()) * 0.7f;
        Vector2 at = new(head.X - size.X / 2f, head.Y - size.Y);
        Utils.DrawBorderString(spriteBatch, sb.ToString(), at, Color.White, 0.7f);

        Texture2D pixel = TextureAssets.MagicPixel.Value;
        if (brain.Navigator.Path is NavPath path)
        {
            for (int i = path.Index; i < path.Steps.Count; i++)
            {
                Vector2 p = ToScreen(NavGrid.FeetWorld(path.Steps[i].Tile) + new Vector2(0f, -8f));
                Color colour = path.Steps[i].Kind switch { MoveKind.Jump => Color.Orange, MoveKind.Drop => Color.SkyBlue, MoveKind.FallThrough => Color.Violet, _ => Color.LimeGreen };
                spriteBatch.Draw(pixel, new Rectangle((int)p.X - 3, (int)p.Y - 3, 6, 6), colour);
            }
        }
        if (brain.Positioner.Chosen is Vector2 chosen)
        {
            Vector2 p = ToScreen(chosen + new Vector2(0f, -8f));
            spriteBatch.Draw(pixel, new Rectangle((int)p.X - 5, (int)p.Y - 5, 10, 10), Color.Magenta);
        }
        foreach (var t in senses.Threats.Threats)
        {
            Vector2 p = ToScreen(t.Npc.Top + new Vector2(0f, -12f));
            string label = $"{(t.Reachable ? "" : "x ")}{t.Class.ToString()[0]} u{t.Urgency:0.0} t{(t.TicksToPlayer > 999 ? 999 : (int)t.TicksToPlayer)}{(t.Shoots ? " s" : "")}";
            Utils.DrawBorderString(spriteBatch, label, p - new Vector2(20f, 0f), t.Reachable ? Color.OrangeRed : Color.Gray, 0.6f);
        }
    }

    /// <summary>World to interface coordinates: the zoom transform, then divided by the UI scale, the way Main does for its own world-anchored text.</summary>
    private static Vector2 ToScreen(Vector2 world)
        => Vector2.Transform(world - Main.screenPosition, Main.GameViewMatrix.ZoomMatrix) / Main.UIScale;
}
