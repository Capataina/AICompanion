#nullable enable

using System;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using AICompanion.Brain.DecisionMatrix.Navigation;
using AICompanion.Companion;

namespace AICompanion.Brain.Debug;

/// <summary>
/// The record of what the brain did, one tab-separated line per companion tick, written
/// to a file under the mod's own source folder so a session is diagnosed from the record
/// instead of from memory. It writes what the overlay shows and more: every action's raw
/// and final score, the reflex, danger and horizon, the request and the spot, the path
/// and its next step, the body's position, velocity, ground, wet and collision state, what
/// is in the hand, the light, and where the player is and what they are doing. A jump is
/// readable as a tick where the body left the ground with upward velocity; a stuck
/// companion is a run of ticks with the same tile, a path and no progress.
///
/// One file per world session, named by the clock, opened on world load and closed on
/// world unload or mod unload; the folder is ignored by git and by the mod packager. At
/// sixty lines a second a session runs to a few megabytes, which is the price of never
/// having to guess again.
/// </summary>
public sealed class BrainTelemetry : ModSystem
{
    private const int FlushEveryTicks = 60;

    private static StreamWriter? writer;
    private static int sinceFlush;
    private static bool headerWritten;

    /// <summary>The folder the files land in: the mod's source folder, which is where the repository is.</summary>
    public static string Folder => Path.Combine(Main.SavePath, "ModSources", "AICompanion", "Telemetry");

    public override void OnWorldLoad()
    {
        Close();
        try
        {
            Directory.CreateDirectory(Folder);
            string stamp = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
            string path = Path.Combine(Folder, $"{stamp}.tsv");
            writer = new StreamWriter(path, false, Encoding.UTF8);
            plansPath = Path.Combine(Folder, $"{stamp}-plans.txt");
            lastDumpTick = -DumpEveryTicks;
            headerWritten = false;
            Mod.Logger.Info($"BrainTelemetry: writing {path}");
        }
        catch (Exception e)
        {
            writer = null;
            Mod.Logger.Error($"BrainTelemetry: could not open a file under {Folder}: {e.Message}");
        }
    }

    public override void OnWorldUnload() => Close();

    public override void Unload()
    {
        Close();
        Mod.Logger.Info("BrainTelemetry.Unload: done");
    }

    private static void Close()
    {
        try
        {
            writer?.Flush();
            writer?.Dispose();
        }
        catch (Exception e)
        {
            ModContent.GetInstance<AICompanion>().Logger.Warn($"BrainTelemetry: close failed: {e.Message}");
        }
        finally
        {
            writer = null;
            plansPath = null;
        }
    }

    private const int DumpEveryTicks = 300;
    // The pad is generous on purpose: the first dumps (13:48 run) cut the exit of a pocket
    // off at six tiles, and a window that hides the way out answers nothing.
    private const int DumpMaxWidth = 160, DumpMaxHeight = 200, DumpPad = 20;
    private static string? plansPath;
    private static long lastDumpTick;

    /// <summary>
    /// Write the tile window between a failed plan's start and its goal to a sidecar text
    /// file beside the session's telemetry, a few seconds apart at most, so the link the grid
    /// is missing can be read off the terrain after the run instead of guessed at. One
    /// character per tile: # solid, = platform or half block, ~ water or honey, L lava,
    /// o air the body can stand in, . other air; then S start, G goal, E where a partial
    /// path ends, N the companion's feet, P the player's feet on top.
    /// </summary>
    public static void DumpPlan(Point start, Point goal, Point? partialEnd, int expansions, string why)
    {
        if (plansPath == null || Main.GameUpdateCount - lastDumpTick < DumpEveryTicks)
            return;
        lastDumpTick = Main.GameUpdateCount;
        try
        {
            int x0 = Math.Min(start.X, goal.X) - DumpPad, x1 = Math.Max(start.X, goal.X) + DumpPad;
            int y0 = Math.Min(start.Y, goal.Y) - DumpPad, y1 = Math.Max(start.Y, goal.Y) + DumpPad;
            // A window too big to read is cut to the start's side, because the first missing link is near it.
            if (x1 - x0 >= DumpMaxWidth) { if (goal.X > start.X) x1 = x0 + DumpMaxWidth - 1; else x0 = x1 - DumpMaxWidth + 1; }
            if (y1 - y0 >= DumpMaxHeight) { if (goal.Y > start.Y) y1 = y0 + DumpMaxHeight - 1; else y0 = y1 - DumpMaxHeight + 1; }

            NPC? npc = CompanionNPC.Find();
            Point n = npc == null ? new Point(-1, -1) : NavGrid.FeetTile(npc.Bottom);
            Point p = NavGrid.FeetTile(Main.LocalPlayer.Bottom);

            var sb = new StringBuilder((x1 - x0 + 2) * (y1 - y0 + 1) + 200);
            sb.Append($"tick {Main.GameUpdateCount} {why}: start {start.X},{start.Y} goal {goal.X},{goal.Y}");
            if (partialEnd is Point e) sb.Append($" partial-end {e.X},{e.Y}");
            sb.Append($" expansions {expansions} npc {n.X},{n.Y} player {p.X},{p.Y} window x {x0}..{x1} y {y0}..{y1}\n");
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var t = new Point(x, y);
                    char c;
                    if (t == p) c = 'P';
                    else if (t == n) c = 'N';
                    else if (t == start) c = 'S';
                    else if (t == goal) c = 'G';
                    else if (partialEnd == t) c = 'E';
                    else if (NavGrid.IsSolid(x, y)) c = '#';
                    else if (NavGrid.IsSupport(x, y)) c = '=';
                    else if (NavGrid.IsLava(x, y)) c = 'L';
                    else if (NavGrid.IsLiquid(x, y)) c = '~';
                    else c = NavGrid.IsStandable(x, y) ? 'o' : '.';
                    sb.Append(c);
                }
                sb.Append('\n');
            }
            sb.Append('\n');
            File.AppendAllText(plansPath, sb.ToString());
        }
        catch (Exception e)
        {
            ModContent.GetInstance<AICompanion>().Logger.Warn($"BrainTelemetry.DumpPlan: {e.Message}");
        }
    }

    /// <summary>Write this tick's line; called once per tick by the NPC after its brain and body have run.</summary>
    public static void Record(CompanionNPC companion)
    {
        if (writer == null)
            return;
        Brain brain = companion.Brain;
        var senses = brain.Senses;
        NPC npc = companion.NPC;

        if (!headerWritten)
        {
            var h = new StringBuilder();
            h.Append("tick\tstate\taction\treflex");
            foreach (var a in brain.Chooser.Actions)
                h.Append('\t').Append(a.Name).Append("_raw\t").Append(a.Name).Append("_fin");
            h.Append("\tdanger\thorizon\tthreats\treachable\ttop_threat\ttarget\tloot");
            h.Append("\trequest\tanchor\tspot\tspot_score\tpath_steps\tpath_at\tnext_kind\tplan_failed\texpansions");
            h.Append("\tnpc_tile\tnpc_px\tnpc_vel\tground\twet\tcollide_x\tcollide_y\tdir\tlife\tbreath\tself_danger\theld\tweapon\tshot\ttorch\tambient");
            h.Append("\tplayer_tile\tplayer_intent\tplayer_dead\tplayer_attacking\tplayer_chopping\tplayer_mining");
            writer.WriteLine(h.ToString());
            headerWritten = true;
        }

        var sb = new StringBuilder(400);
        sb.Append(Main.GameUpdateCount);
        // Read the player's death from the player, not the sense: the brain (and so the sense)
        // stops ticking exactly when the player is dead, so the cached flag never turns.
        sb.Append('\t').Append(companion.IsDowned ? "downed" : Main.LocalPlayer.dead ? "player-dead" : "up");
        sb.Append('\t').Append(brain.LastAction?.Name ?? "-");
        sb.Append('\t').Append(brain.Reflexes.Active ?? "-");
        foreach (var a in brain.Chooser.Actions)
        {
            float raw = 0f, fin = 0f;
            foreach (var s in brain.Chooser.LastScores)
                if (ReferenceEquals(s.Action, a)) { raw = s.Raw; fin = s.Final; break; }
            sb.Append('\t').Append(raw.ToString("0.00")).Append('\t').Append(fin.ToString("0.00"));
        }

        int reachable = 0;
        DecisionMatrix.Senses.ThreatRecord? top = null;
        foreach (var t in senses.Threats.Threats)
        {
            if (t.Reachable) reachable++;
            if (top == null || t.Urgency > top.Urgency) top = t;
        }
        sb.Append('\t').Append(senses.Threats.PlayerDanger.ToString("0.00"));
        sb.Append('\t').Append(senses.Threats.Horizon == float.MaxValue ? "inf" : senses.Threats.Horizon.ToString("0"));
        sb.Append('\t').Append(senses.Threats.Threats.Count).Append('\t').Append(reachable);
        sb.Append('\t').Append(top == null ? "-" : $"{top.Npc.TypeName}:{top.Class.ToString()[0]}:u{top.Urgency:0.00}:t{(top.TicksToPlayer > 9999 ? 9999 : (int)top.TicksToPlayer)}:{(top.Reachable ? "r" : "x")}{(top.Shoots ? ":s" : "")}");
        sb.Append('\t').Append(brain.LastRequest.Target is NPC target && target.active ? target.TypeName : "-");
        sb.Append('\t').Append(senses.Loot.Pickups.Count);

        sb.Append('\t').Append(brain.LastRequest.Kind);
        sb.Append('\t').Append(Tile(brain.LastRequest.Anchor));
        sb.Append('\t').Append(brain.Positioner.Chosen is Vector2 c ? Tile(c) : "-");
        sb.Append('\t').Append(brain.Positioner.ChosenScore.ToString("0.00"));
        NavPath? path = brain.Navigator.Path;
        sb.Append('\t').Append(path?.Steps.Count ?? 0).Append('\t').Append(path?.Index ?? 0);
        sb.Append('\t').Append(path != null && !path.Finished ? path.Current.Kind.ToString() : "-");
        sb.Append('\t').Append(brain.Navigator.LastPlanFailed ? 1 : 0).Append('\t').Append(brain.Navigator.LastExpansions);

        sb.Append('\t').Append(Tile(npc.Bottom));
        sb.Append('\t').Append((int)npc.Bottom.X).Append(',').Append((int)npc.Bottom.Y);
        sb.Append('\t').Append(npc.velocity.X.ToString("0.00")).Append(',').Append(npc.velocity.Y.ToString("0.00"));
        sb.Append('\t').Append(companion.Motor.OnGround ? 1 : 0);
        sb.Append('\t').Append(npc.wet ? 1 : 0);
        sb.Append('\t').Append(npc.collideX ? 1 : 0).Append('\t').Append(npc.collideY ? 1 : 0);
        sb.Append('\t').Append(npc.direction);
        sb.Append('\t').Append(npc.life).Append('/').Append(npc.lifeMax);
        sb.Append('\t').Append(senses.Self.BreathFraction.ToString("0.00")).Append(senses.Self.HeadUnderwater ? "u" : "");
        sb.Append('\t').Append(senses.Self.SelfDanger.ToString("0.00")).Append(senses.Self.InLava ? "L" : senses.Self.OnFire ? "f" : "");
        sb.Append('\t').Append(companion.HeldItemType == 0 ? "-" : Lang.GetItemNameValue(companion.HeldItemType));
        sb.Append('\t').Append(companion.Arsenal.LastChosen?.Name ?? "-");
        sb.Append('\t').Append(companion.Arsenal.LastShotSolved ? 1 : 0);
        sb.Append('\t').Append(companion.Torch.Shown ? "shown" : companion.Torch.Lit ? "lit-busy" : "out");
        sb.Append('\t').Append(senses.Light.Ambient.ToString("0.00"));

        sb.Append('\t').Append(Tile(senses.Player.Bottom));
        sb.Append('\t').Append(senses.Player.Intent.X.ToString("0.0"));
        sb.Append('\t').Append(Main.LocalPlayer.dead ? 1 : 0);
        sb.Append('\t').Append(senses.Player.IsAttacking ? 1 : 0);
        sb.Append('\t').Append(senses.Player.IsChoppingTree ? 1 : 0);
        sb.Append('\t').Append(senses.Player.MinedOre != null ? 1 : 0);

        // A write that fails (disk full, a stream the OS closed) must not escape the NPC's AI
        // and take the companion with it; the record stops and the game goes on.
        try
        {
            writer.WriteLine(sb.ToString());
            if (++sinceFlush >= FlushEveryTicks)
            {
                sinceFlush = 0;
                writer.Flush();
            }
        }
        catch (Exception e)
        {
            ModContent.GetInstance<AICompanion>().Logger.Error($"BrainTelemetry: write failed, recording stops: {e.Message}");
            try { writer.Dispose(); } catch { /* the stream is already broken */ }
            writer = null;
        }
    }

    private static string Tile(Vector2 world) => $"{(int)(world.X / 16f)},{(int)(world.Y / 16f)}";
}
