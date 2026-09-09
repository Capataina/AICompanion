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
            ScenarioCapture.Reset();
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
    // The window is wider than the screen on purpose, by Caner's ruling on 2026-09-08: the
    // replay treats the window's edge as a wall, so a route that leaves the box reads as "no
    // path", and the first dumps (six-tile pad) and the fifth run's (twenty) both had to be
    // widened from the saved world before they said anything. A screen is about 120 by 70
    // tiles at normal zoom; the pad alone is more than half of that on every side, so an
    // alternate route the companion never took is in the picture too. A block costs about
    // a byte per tile, so a run of sixty dumps is a few megabytes of git-ignored text.
    private const int DumpMaxWidth = 480, DumpMaxHeight = 400, DumpPad = 80;
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
        WriteWindow(start, goal, partialEnd, expansions, why);
    }

    /// <summary>
    /// The same window, written by a scenario detector (<see cref="ScenarioCapture"/>) under its
    /// own reason and its own cooldown, so a follow failure or a stuck run becomes a replayable
    /// block whether or not a plan failed in the same second.
    /// </summary>
    public static void DumpScenario(Point start, Point goal, string why)
    {
        if (plansPath == null)
            return;
        WriteWindow(start, goal, null, 0, why);
    }

    private static void WriteWindow(Point start, Point goal, Point? partialEnd, int expansions, string why)
    {
        // Read once and check here rather than trusting the caller's check: both callers do check,
        // but the field is a mutable static that world unload clears, so the check has to be in the
        // scope that uses it for the write to be sound as well as for the compiler to agree.
        string? path = plansPath;
        if (path == null)
            return;
        try
        {
            NPC? npc = CompanionNPC.Find();
            Point n = npc == null ? new Point(-1, -1) : NavGrid.FeetTile(npc.Bottom);
            Point p = NavGrid.FeetTile(Main.LocalPlayer.Bottom);

            // The window holds the player's tile as well as the start and the goal, because "could
            // it have reached the player" is the question the replay tool answers, and a goal the
            // positioner picked above a pit says nothing about the player six rows below the window.
            int x0 = Math.Min(Math.Min(start.X, goal.X), p.X) - DumpPad, x1 = Math.Max(Math.Max(start.X, goal.X), p.X) + DumpPad;
            int y0 = Math.Min(Math.Min(start.Y, goal.Y), p.Y) - DumpPad, y1 = Math.Max(Math.Max(start.Y, goal.Y), p.Y) + DumpPad;
            // A window too big to read is cut to the start's side, because the first missing link is near it.
            if (x1 - x0 >= DumpMaxWidth) { if (goal.X > start.X) x1 = x0 + DumpMaxWidth - 1; else x0 = x1 - DumpMaxWidth + 1; }
            if (y1 - y0 >= DumpMaxHeight) { if (goal.Y > start.Y) y1 = y0 + DumpMaxHeight - 1; else y0 = y1 - DumpMaxHeight + 1; }

            var sb = new StringBuilder((x1 - x0 + 2) * (y1 - y0 + 1) + 200);
            sb.Append($"tick {Main.GameUpdateCount} {why}: start {start.X},{start.Y} goal {goal.X},{goal.Y}");
            if (partialEnd is Point e) sb.Append($" partial-end {e.X},{e.Y}");
            sb.Append($" expansions {expansions} npc {n.X},{n.Y} player {p.X},{p.Y} window x {x0}..{x1} y {y0}..{y1}\n");
            sb.Append($"markers S {start.X},{start.Y} G {goal.X},{goal.Y} N {n.X},{n.Y} P {p.X},{p.Y}");
            if (partialEnd is Point pe) sb.Append($" E {pe.X},{pe.Y}");
            sb.Append('\n');
            // The player's trail is the design's own pass line ("if I can get through, it can"),
            // written as one line the replay tool reads back and checks tile by tile.
            if (npc is NPC body && body.ModNPC is CompanionNPC companion && companion.Brain.Senses.Player.Trail.Count > 0)
            {
                sb.Append("trail");
                foreach (Point t in companion.Brain.Senses.Player.Trail)
                    sb.Append(' ').Append(t.X).Append(',').Append(t.Y);
                sb.Append('\n');
            }
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var t = new Point(x, y);
                    // The replay tool reads this alphabet back through the same function, so a
                    // slope or half block dumped here is the shape the offline planner sees. A
                    // marker is drawn over air only: on a half block or a floor slope the feet
                    // tile is the supporting tile itself, and a marker written there erased the
                    // support from the replay. The markers line above carries every position.
                    char c = TextTileWorld.Glyph(NavGrid.World.Shape(x, y), NavGrid.IsLiquid(x, y), NavGrid.IsLava(x, y));
                    if (c == '.' && NavGrid.IsStandable(x, y)) c = 'o';
                    if (c is '.' or 'o')
                    {
                        if (t == p) c = 'P';
                        else if (t == n) c = 'N';
                        else if (t == start) c = 'S';
                        else if (t == goal) c = 'G';
                        else if (partialEnd == t) c = 'E';
                    }
                    sb.Append(c);
                }
                sb.Append('\n');
            }
            sb.Append('\n');
            File.AppendAllText(path, sb.ToString());
        }
        catch (Exception e)
        {
            ModContent.GetInstance<AICompanion>().Logger.Warn($"BrainTelemetry.WriteWindow ({why}): {e.Message}");
        }
    }

    /// <summary>Write this tick's line; called once per tick by the NPC after its brain and body have run.</summary>
    public static void Record(CompanionNPC companion)
    {
        if (writer == null)
            return;
        ScenarioCapture.Watch(companion);
        Brain brain = companion.Brain;
        var senses = brain.Senses;
        NPC npc = companion.NPC;

        if (!headerWritten)
        {
            var h = new StringBuilder();
            h.Append("tick\tstate\taction\treflex");
            foreach (var a in brain.Chooser.Actions)
                h.Append('\t').Append(a.Name).Append("_raw\t").Append(a.Name).Append("_fin");
            h.Append("\tdanger\tself_threat\thorizon\tthreats\treachable\ttop_threat\ttarget\tloot");
            h.Append("\trequest\tanchor\tspot\tspot_score\tpath_steps\tpath_at\tnext_kind\tplan_failed\texpansions");
            h.Append("\tnpc_tile\tnpc_px\tnpc_vel\tground\twet\tcollide_x\tcollide_y\tpress\tdir\tlife\tbreath\tself_danger\theld\tweapon\tshot\tfire\texp_bow\texp_knife\texp_target\tengage\ttorch\tambient");
            h.Append("\tplayer_tile\tplayer_intent\tplayer_dead\tplayer_attacking\tplayer_chopping\tplayer_mining");
            h.Append("\tplan_ms\tflood_ms\tsenses_ms\treflex_ms\tdecide_ms\tposition_ms\tnavigate_ms\tbrain_ms\tedge_cache\tstranded");
            // The reachability tier, which is where the companion decides whether to enter somewhere
            // it cannot leave and the one decision no offline pass can watch: how many tiles it can
            // reach, how many of those it can come home from, whether the spot it picked is one of
            // them, and whether the refusing flood was discarded because the player was outside it.
            h.Append("\treach_n\treturnable_n\tspot_home\tplayer_one_way");
            h.Append("\tedge_n\tedge_kind\tedge_from\tedge_to\tedge_proven\tedge_took\tedge_outcome");
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
        // Danger to the companion beside danger to the player, because the two came apart badly:
        // it died 84 tiles out with the player's danger reading 0.00 on every hit it took.
        sb.Append('\t').Append(senses.Threats.CompanionDanger.ToString("0.00"));
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
        // Whether the body asked to pass its platform this tick. It is written here because the
        // game reads and clears the flag in UpdateCollision, which runs after this line, and
        // because two attempts at the fall-through defect were spent inferring this column's value
        // from velocity and ground: a press that is not held shows as a three-tick fall and a
        // catch, and with the column present that reads directly instead of being reconstructed.
        sb.Append('\t').Append(companion.Motor.WantsFallThrough ? 1 : 0);
        sb.Append('\t').Append(npc.direction);
        sb.Append('\t').Append(npc.life).Append('/').Append(npc.lifeMax);
        sb.Append('\t').Append(senses.Self.BreathFraction.ToString("0.00")).Append(senses.Self.HeadUnderwater ? "u" : "");
        sb.Append('\t').Append(senses.Self.SelfDanger.ToString("0.00")).Append(senses.Self.InLava ? "L" : senses.Self.OnFire ? "f" : "");
        sb.Append('\t').Append(companion.HeldItemType == 0 ? "-" : Lang.GetItemNameValue(companion.HeldItemType));
        sb.Append('\t').Append(companion.Arsenal.LastChosen?.Name ?? "-");
        sb.Append('\t').Append(companion.Arsenal.LastShotSolved ? 1 : 0);
        // Why no projectile left the hands, which the shot flag alone cannot say: a reload and a
        // target with no reachable arc both read as a zero there, and they want opposite fixes.
        sb.Append('\t').Append(companion.Arsenal.LastFireOutcome);
        // Both weapons' expected damage, the rejected one included, so the choice can be read back
        // instead of re-derived: a row where the loser scored higher is a defect with no other tell.
        sb.Append('\t').Append(companion.Arsenal.LastPrimaryExpected.ToString("0.0"));
        sb.Append('\t').Append(companion.Arsenal.LastSecondaryExpected.ToString("0.0"));
        sb.Append('\t').Append(companion.Arsenal.LastTargetExpected.ToString("0.0"));
        // What the hands are shooting at, which is now independent of what the feet were told, so
        // "it was following me and not attacking" is a row where engage reads "-" beside threats.
        sb.Append('\t').Append(brain.EngageTarget is NPC eng && eng.active ? eng.TypeName : "-");
        sb.Append('\t').Append(companion.Torch.Shown ? "shown" : companion.Torch.Lit ? "lit-busy" : "out");
        sb.Append('\t').Append(senses.Light.Ambient.ToString("0.00"));

        sb.Append('\t').Append(Tile(senses.Player.Bottom));
        sb.Append('\t').Append(senses.Player.Intent.X.ToString("0.0"));
        sb.Append('\t').Append(Main.LocalPlayer.dead ? 1 : 0);
        sb.Append('\t').Append(senses.Player.IsAttacking ? 1 : 0);
        sb.Append('\t').Append(senses.Player.IsChoppingTree ? 1 : 0);
        sb.Append('\t').Append(senses.Player.MinedOre != null ? 1 : 0);
        // The cost of the pose grid in the game, per tick: the last plan's and the last reach flood's wall-clock.
        sb.Append('\t').Append(brain.Navigator.LastPlanMs.ToString("0.00")).Append('\t').Append(brain.Positioner.LastFloodMs.ToString("0.00"));
        // The two above are sticky (the last search's cost, repeated until the next); these six
        // are this tick's, so a sum over a stretch of rows is the brain's real share of the wall.
        sb.Append('\t').Append(brain.SensesMs.ToString("0.00")).Append('\t').Append(brain.ReflexMs.ToString("0.00"))
          .Append('\t').Append(brain.DecideMs.ToString("0.00")).Append('\t').Append(brain.PositionMs.ToString("0.00"))
          .Append('\t').Append(brain.NavigateMs.ToString("0.00")).Append('\t').Append(brain.TotalMs.ToString("0.00"))
          .Append('\t').Append(DecisionMatrix.Navigation.AStar.CachedTiles)
          // Ticks sealed off from the player; a roam is a wander row while this stays above zero.
          .Append('\t').Append(brain.StrandedTicks)
          .Append('\t').Append(brain.Positioner.ReachCount)
          .Append('\t').Append(brain.Positioner.ReturnableCount)
          .Append('\t').Append(brain.Positioner.ChosenReturnable ? 1 : 0)
          .Append('\t').Append(brain.Positioner.PlayerOnlyOneWay ? 1 : 0);
        // The last step the follower finished or faulted, sticky until the next: the move, the
        // ticks it was proven to take against the ticks it took, and how it ended. Read against
        // the replay's --follow on the same block, this is where the body model and the game's
        // engine disagree per kind of move; the count column says when a new one has landed.
        if (brain.Navigator.LastEdge is DecisionMatrix.Navigation.EdgeReport edge)
            sb.Append('\t').Append(brain.Navigator.EdgeCount).Append('\t').Append(edge.Kind)
              .Append('\t').Append(edge.From.X).Append(',').Append(edge.From.Y)
              .Append('\t').Append(edge.Tile.X).Append(',').Append(edge.Tile.Y)
              .Append('\t').Append(edge.Expected).Append('\t').Append(edge.Actual).Append('\t').Append(edge.Outcome);
        else
            sb.Append("\t0\t-\t-\t-\t0\t0\t-");

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
