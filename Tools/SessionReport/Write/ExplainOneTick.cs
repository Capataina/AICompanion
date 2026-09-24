#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Everything the record holds about one tick, in the order a person diagnosing it asks: what the course
/// decided and why, what each census admitted, what the companion did, where the bodies were, what the
/// tick cost, and which findings the report raises on or near it.
///
/// <para><b>Every value is read by column name, and a column the capture does not carry is printed as
/// absent and listed again at the end with the schema.</b> A blank or a zero where the recorder never
/// wrote anything reads as an observation, which is the defect this whole reader is built against; a
/// 0.44.0 capture has no frame ledger, no section profile and no allocation columns, and saying so by
/// name is the only honest thing an explanation of one of its ticks can do about them.</para>
///
/// <para><b>Two of its sources are not per tick, and each says how old it is.</b> The <c>decision</c>
/// occurrence, which carries the census admissions, the leaders per domain and the refusal tally, is
/// periodic — 469 of 2,340 ticks on the 22 September 2026 capture, 46 on the world run of it — so the one
/// shown is the latest at or before the tick with its age beside it. The <c>course-decision</c> payloads are
/// joined to the tick through the decision identity the row carries, over that decision's whole span,
/// through <see cref="WriteCourseTimeline.PickFromSpan"/>, because a payload lands on the tick an outcome
/// is traced rather than on the tick being explained.</para>
/// </summary>
public static class ExplainOneTick
{
    /// <summary>How far either side of the tick a finding's span may lie and still be printed as near it: one second.</summary>
    public const int FindingMargin = 60;

    /// <summary>How many sections of the tick's own profile are printed.</summary>
    private const int SectionsShown = 8;

    public static string Of(Session session, long tick)
    {
        var text = new StringBuilder();
        if (session.Count == 0)
            return $"explain  {System.IO.Path.GetFileName(session.Path)} holds no rows, so there is no tick {tick:n0} to explain\n";
        int row = ReconstructTheSceneAtATick.RowOf(session, tick);
        if (row < 0)
            return $"explain  tick {tick:n0} is before the capture's first row (tick {session.Tick(0):n0})\n";

        var explain = new Explanation(session, row);
        string schema = session.Metadata.TryGetValue("schema", out string? s) ? s : "unrecorded";
        long at = session.Tick(row);
        text.Append(string.Create(CultureInfo.InvariantCulture,
            $"explain  tick {at:n0} of {System.IO.Path.GetFileName(session.Path)}  (schema {schema}; row {row + 1:n0} of {session.Count:n0}"));
        if (session.Find("wall_elapsed_ms") is { } wall && !float.IsNaN(wall.Number[row]))
            text.Append(string.Create(CultureInfo.InvariantCulture, $"; {wall.Number[row] / 1000.0:0.0} s into the recording"));
        text.Append(")\n");
        if (at != tick)
            text.Append($"         the capture holds no row at tick {tick:n0}; this is the last row before it\n");
        if (session.Metadata.TryGetValue("synthetic", out string? synthetic))
            text.Append($"         synthetic capture ({synthetic.Split(';')[0]}): nobody played this tick\n");

        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        if (!log.Present) text.Append("         no events sidecar beside the capture, so every occurrence-sourced line below is absent\n");

        Decision(text, explain, session, log, row, at);
        Census(text, explain, log, at);
        Did(text, explain, log, at);
        Where(text, explain, session, log, row);
        Cost(text, explain, session, log, row, at);
        Findings(text, session, at);

        text.Append('\n');
        text.Append(explain.Absent.Count == 0
            ? "absent   nothing: every column this explanation reads is in the capture\n"
            : $"absent   {explain.Absent.Count} column(s) this explanation reads are not in a schema {schema} capture: {string.Join(", ", explain.Absent)}\n");
        return text.ToString();
    }

    /// <summary>The columns whose change marks a tick worth explaining in a window: what was decided, what was asked
    /// of the body, who held it, what the hands held, and what the senses counted.</summary>
    internal static readonly string[] WatchedColumns =
        { "action", "choice_id", "request", "region_kind", "nav_status", "control_source", "held", "weapon", "threats", "loot" };

    /// <summary>How many changed ticks a window prints before it stops and says how many it left out.</summary>
    private const int WindowLines = 200;

    /// <summary>
    /// The ticks between <paramref name="from"/> and <paramref name="to"/> where a watched column changed or
    /// the brain went over its own cost fence, one line each naming what moved. A column the capture lacks is
    /// named once rather than read as never changing.
    /// </summary>
    public static string Window(Session session, long from, long to)
    {
        var text = new StringBuilder();
        string[] watched = WatchedColumns.Where(c => session.Has(c)).ToArray();
        string[] missing = WatchedColumns.Where(c => !session.Has(c)).ToArray();
        text.Append(string.Create(CultureInfo.InvariantCulture,
            $"explain  ticks {from:n0}–{to:n0} of {System.IO.Path.GetFileName(session.Path)}: the ticks where {string.Join(", ", watched)} changed, or the brain went over its cost fence\n"));
        if (missing.Length > 0) text.Append($"         absent from this capture, so never reported as changing: {string.Join(", ", missing)}\n");
        Column? fence = session.Find("cost_fence_ms"), brain = session.Find("brain_ms");
        int printed = 0, changed = 0, rows = 0, previous = -1;
        for (int i = 0; i < session.Count; i++)
        {
            long tick = session.Tick(i);
            if (tick < from) { previous = i; continue; }
            if (tick > to) break;
            rows++;
            var moved = new List<string>();
            foreach (string column in watched)
            {
                string now = session[column].Text[i];
                if (previous < 0) moved.Add($"{column} {now}");
                else if (session[column].Text[previous] is var was && was != now) moved.Add($"{column} {was} → {now}");
            }
            if (fence != null && brain != null && !float.IsNaN(fence.Number[i]) && brain.Number[i] > fence.Number[i])
                moved.Add(string.Create(CultureInfo.InvariantCulture, $"spike {brain.Number[i]:0.0} ms over a {fence.Number[i]:0.0} ms fence"));
            previous = i;
            if (moved.Count == 0) continue;
            changed++;
            if (printed++ < WindowLines) text.Append(string.Create(CultureInfo.InvariantCulture, $"  {tick,7:n0}  {string.Join(" · ", moved)}\n"));
        }
        if (printed > WindowLines) text.Append($"  and {printed - WindowLines} more changed tick(s); narrow the range\n");
        text.Append(rows == 0
            ? "  the capture holds no row inside that range\n"
            : string.Create(CultureInfo.InvariantCulture, $"  {changed:n0} of {rows:n0} row(s) changed something; `--explain <capture> <tick>` explains any one of them in full\n"));
        return text.ToString();
    }

    /// <summary>The row's cells by name, with every name the capture lacks remembered in the order asked.</summary>
    private sealed class Explanation
    {
        private readonly Session session;
        private readonly int row;
        public readonly List<string> Absent = new();

        public Explanation(Session session, int row) { this.session = session; this.row = row; }

        /// <summary>The cell, or null with the name recorded as absent.</summary>
        public string? Cell(string column)
        {
            if (session.Find(column) is { } c) return c.Text[row];
            if (!Absent.Contains(column)) Absent.Add(column);
            return null;
        }

        /// <summary><c>name value</c>, or <c>name (absent)</c>.</summary>
        public string Pair(string column) => Cell(column) is { } value ? $"{column} {value}" : $"{column} (absent)";

        public string Pairs(params string[] columns) => string.Join(" · ", columns.Select(Pair));
    }

    private static void Decision(StringBuilder text, Explanation explain, Session session, GodsEyeEventLog log, int row, long tick)
    {
        text.Append("\nwhat the course decided\n");
        text.Append($"  row       {explain.Pairs("action", "choice_id", "choice_tick", "choice_fresh")}\n");

        CourseDecisionLog course = ReadCourseDecisions.From(log);
        if (course.Decisions.Count == 0)
        {
            text.Append(log.Present
                ? "  course    no course-decision payload anywhere in the sidecar\n"
                : "  course    (absent: no sidecar)\n");
        }
        else
        {
            // The decision's own span: the rows carrying this row's decision identity, from the schema at which
            // choice_id became the course's identity; before it, the payloads on this tick alone.
            long from = tick, to = tick;
            bool spanned = session.Find("choice_id") is { } id && CompletedTransferClaimsWereReceived.SchemaAtLeast(session, WriteCourseTimeline.First);
            if (spanned)
            {
                Column ids = session["choice_id"];
                int first = row, last = row;
                while (first > 0 && ids.Text[first - 1] == ids.Text[row]) first--;
                while (last + 1 < session.Count && ids.Text[last + 1] == ids.Text[row]) last++;
                from = session.Tick(first); to = session.Tick(last);
            }
            CourseDecision[] ordered = course.Decisions.OrderBy(d => d.Tick).ToArray();
            int start = Array.FindIndex(ordered, d => d.Tick >= from);
            var inSpan = start < 0 ? Array.Empty<CourseDecision>() : ordered.Skip(start).TakeWhile(d => d.Tick <= to).ToArray();
            var (numbers, released) = start < 0 ? (null, null) : WriteCourseTimeline.PickFromSpan(ordered, start, to);
            text.Append(string.Create(CultureInfo.InvariantCulture,
                $"  span      ticks {from:n0}–{to:n0} ({(spanned ? "the rows carrying this decision identity" : "this tick alone: choice_id is not the course's identity below schema " + WriteCourseTimeline.First)}); {inSpan.Length} course-decision payload(s) traced inside it\n"));
            if (numbers is null)
                text.Append("  course    no payload traced inside the span; the producer traces an outcome rather than a tick, so a one-tick decision can land between two traces\n");
            else
            {
                text.Append(string.Create(CultureInfo.InvariantCulture,
                    $"  course    at tick {numbers.Tick:n0}: reason {numbers.Reason} · activity {Dash(numbers.Activity)} · {(numbers.Settled ? "settled" : "unsettled")} · bound step {Dash(numbers.Purpose)} · steps {numbers.Steps} · orders priced {numbers.OrdersPriced}, refused {numbers.OrdersRefused} · search {(numbers.SearchExhausted ? "exhausted" : "cut")} · facts {numbers.Facts:n0}\n"));
                text.Append(numbers.Refusals.Count == 0
                    ? "  refused   no refusal tally on that payload\n"
                    : "  refused   " + string.Join(" · ", numbers.Refusals.OrderByDescending(r => r.Value).Select(r => $"{r.Key} {r.Value}")) + "\n");
            }
            text.Append(released is null
                ? "  released  no release traced inside the span\n"
                : string.Create(CultureInfo.InvariantCulture, $"  released  {released.ReleaseReason} at tick {released.Tick:n0} (the release rides on an unsettled record, never the one holding the numbers)\n"));
        }

        // Leaders are written only by a decision that priced orders, so the latest occurrence is often a
        // still-deciding one naming none; the latest that names any is the one read, and both ages are said.
        GodsEyeEvent? board = LatestAtOrBefore(log, "decision", tick);
        GodsEyeEvent? priced = LatestAtOrBefore(log, "decision", tick, having: "course:");
        if (board is null)
            text.Append(log.Present ? "  leaders   no decision occurrence at or before this tick\n" : "  leaders   (absent: no sidecar)\n");
        else if (priced is null)
            text.Append(string.Create(CultureInfo.InvariantCulture, $"  leaders   no decision occurrence at or before this tick names a leader (the latest is at tick {board.tick:n0})\n"));
        else
        {
            var leaders = priced.detail.Split(';').Where(p => p.StartsWith("course:", StringComparison.Ordinal)).ToList();
            text.Append(string.Create(CultureInfo.InvariantCulture,
                $"  leaders   from the decision occurrence at tick {priced.tick:n0} ({tick - priced.tick:n0} tick(s) old; the occurrence is periodic and only a decision that priced orders names leaders"));
            text.Append(priced == board ? ")" : string.Create(CultureInfo.InvariantCulture, $"; the latest occurrence, at tick {board.tick:n0}, names none)"));
            {
                text.Append('\n');
                foreach (string leader in leaders)
                {
                    int equals = leader.IndexOf('=');
                    string domain = equals < 0 ? leader : leader["course:".Length..equals];
                    string terms = equals < 0 ? "" : leader[(equals + 1)..].Replace(",", " · ", StringComparison.Ordinal).Replace(":", " ", StringComparison.Ordinal);
                    text.Append($"            {domain,-14} {terms}\n");
                }
            }
        }
        text.Append($"  order     {explain.Pairs("task_order", "task_order_runner_up")}");
        if (!CompletedTransferClaimsWereReceived.SchemaAtLeast(session, WriteCourseTimeline.OrdersLive) && session.Has("task_order"))
            text.Append($"  (these read the retired family chooser below schema {WriteCourseTimeline.OrdersLive}, so a dash is a dead producer)");
        text.Append('\n');
    }

    private static void Census(StringBuilder text, Explanation explain, GodsEyeEventLog log, long tick)
    {
        text.Append("\nwhat each census admitted\n");
        // The latest occurrence carrying a census, which is the latest occurrence on every capture on disk; an
        // occurrence written without one is passed over rather than read as a census that admitted nothing.
        GodsEyeEvent? board = LatestAtOrBefore(log, "decision", tick, having: "course-admitted:") ?? LatestAtOrBefore(log, "decision", tick);
        var admitted = board?.detail.Split(';').Where(p => p.StartsWith("course-admitted:", StringComparison.Ordinal)).ToList() ?? new List<string>();
        var refused = board?.detail.Split(';').Where(p => p.StartsWith("course-refused:", StringComparison.Ordinal)).ToList() ?? new List<string>();
        if (board is null) text.Append(log.Present ? "  admitted  no decision occurrence at or before this tick\n" : "  admitted  (absent: no sidecar)\n");
        else if (admitted.Count == 0) text.Append(string.Create(CultureInfo.InvariantCulture, $"  admitted  the decision occurrence at tick {board.tick:n0} carries no course-admitted entry\n"));
        else
        {
            text.Append(string.Create(CultureInfo.InvariantCulture, $"  admitted  at tick {board.tick:n0} ({tick - board.tick:n0} tick(s) old)\n"));
            foreach (string entry in admitted)
            {
                int equals = entry.IndexOf('=');
                string domain = equals < 0 ? entry : entry["course-admitted:".Length..equals];
                string values = equals < 0 ? "" : entry[(equals + 1)..].Replace(",", " · ", StringComparison.Ordinal).Replace(":", " ", StringComparison.Ordinal);
                text.Append($"            {domain,-14} {values}\n");
            }
            if (refused.Count > 0)
                text.Append("  refusals  " + string.Join(" · ", refused.Select(r => r["course-refused:".Length..].Replace("=", " ", StringComparison.Ordinal))) + " (the same occurrence's tally)\n");
        }
        foreach (string funnel in new[] { "combat", "collect", "place-torches" })
        {
            GodsEyeEvent? latest = LatestAtOrBefore(log, "candidate-funnel", tick, funnel);
            if (latest is null) continue;
            text.Append(string.Create(CultureInfo.InvariantCulture,
                $"  funnel    {funnel} at tick {latest.tick:n0}: {Clip(latest.detail, 240)}\n"));
        }
        text.Append($"  row       {explain.Pairs("threats", "loot", "lighting_sites", "reach_complete", "target_evidence")}\n");
    }

    private static void Did(StringBuilder text, Explanation explain, GodsEyeEventLog log, long tick)
    {
        text.Append("\nwhat the companion did\n");
        text.Append($"  asked     {explain.Pairs("request", "spot", "position_reason", "region_kind")}\n");
        text.Append($"  navigator {explain.Pairs("nav_status", "route_points", "route_index", "route_remaining_px", "lookahead")}; route corners are not recorded at any schema (route_points is a count)\n");
        text.Append($"  controls  {explain.Pairs("control", "control_source", "desired_vel", "control_request_owner", "evade_reason")}\n");
        text.Append($"  hands     {explain.Pairs("held", "weapon", "fire", "shot", "torch", "hand_grant", "plan_id", "plan_reason")}\n");
        if (LatestAtOrBefore(log, "movement-state", tick) is { } movement)
            text.Append(string.Create(CultureInfo.InvariantCulture, $"  movement  at tick {movement.tick:n0}: {Clip(movement.detail, 220)}\n"));
        if (log.Events.FirstOrDefault(e => e.tick == tick && e.kind == "navigation-state") is { } navigation)
        {
            string[] keys = { "choice", "progress", "follow-objective", "candidates", "reachable-candidates", "position-alternatives", "plan-invalid" };
            text.Append("  position  " + string.Join(" · ", keys.Select(k => $"{k} {navigation.Field(k) ?? "(not written)"}")) + "\n");
        }
        var here = log.Events.Where(e => e.tick == tick && e.kind is not ("navigation-state" or "movement-state" or "decision" or "terrain-snapshot"
            or "candidate-funnel" or "course-course-decision" or "course-incidental-acceptance" or "combat-snapshot")).ToList();
        if (here.Count > 0)
        {
            text.Append($"  events    {here.Count} other occurrence(s) at this tick\n");
            foreach (GodsEyeEvent e in here.Take(12))
                text.Append($"            {e.kind} {Dash(e.label)} {Dash(e.channel)} {Clip(e.detail.Length > 0 ? e.detail : PayloadText(e), 160)}\n");
        }
    }

    private static void Where(StringBuilder text, Explanation explain, Session session, GodsEyeEventLog log, int row)
    {
        TickScene scene = ReconstructTheSceneAtATick.At(session, log, row);
        text.Append("\nwhere the bodies were\n");
        text.Append($"  companion {explain.Pairs("npc_px", "npc_vel", "life")} (npc_px is the orb's centre)\n");
        text.Append($"  player    {explain.Pairs("player_px", "player_vel", "player_state")} (player_px is his feet)\n");
        if (scene.Companion is { } c && scene.PlayerFeet is { } p)
        {
            float dx = c.X - p.X, dy = c.Y - (p.Y - 21);
            text.Append(string.Create(CultureInfo.InvariantCulture,
                $"  apart     {MathF.Sqrt(dx * dx + dy * dy):0} px ({MathF.Sqrt(dx * dx + dy * dy) / 16:0.0} tiles) from the orb to the middle of his body, {dx:+0;-0} px across and {dy:+0;-0} px down\n"));
        }
        if (scene.Region is { } r)
        {
            string inside = scene.Companion is { } body
                ? (MathF.Abs(body.X - r.CX) <= r.HX && MathF.Abs(body.Y - r.CY) <= r.HY ? "inside" : "outside") : "unknown";
            text.Append(string.Create(CultureInfo.InvariantCulture,
                $"  region    intent_region centre {r.CX:0},{r.CY:0}, half {r.HX:0}×{r.HY:0} px, so x {r.CX - r.HX:0}..{r.CX + r.HX:0} and y {r.CY - r.HY:0}..{r.CY + r.HY:0}; the orb is {inside}\n"));
        }
        else text.Append($"  region    {explain.Pair("intent_region")}\n");
        text.Append($"            {explain.Pairs("region_anchor_px", "region_comfort", "region_arrival")}\n");
        text.Append($"  hostiles  {scene.Hostiles.Count} placed from {scene.SightingSources}");
        if (scene.StaleHostiles > 0) text.Append($"; {scene.StaleHostiles} more last seen over {ReconstructTheSceneAtATick.SightingHorizon} ticks ago and not placed");
        text.Append('\n');
        foreach (Sighting h in scene.Hostiles)
            text.Append(string.Create(CultureInfo.InvariantCulture,
                $"            slot {h.Slot} {h.Name} at {h.X:0},{h.Y:0} ({h.Source}, {h.Age(scene.Tick):n0} tick(s) old{(h.Exact ? "" : ", tile centre")}){Distance(h, scene)}\n"));
        text.Append($"  other npcs {scene.OtherNpcs.Count} placed by a spawn or damage record and never held by the combat census, so not known to be hostile\n");
        foreach (Sighting n in scene.OtherNpcs)
            text.Append(string.Create(CultureInfo.InvariantCulture,
                $"            slot {n.Slot} {n.Name} at {n.X:0},{n.Y:0} ({n.Source}, {n.Age(scene.Tick):n0} tick(s) old){Distance(n, scene)}\n"));
        text.Append($"  drops     {scene.Drops.Count} placed\n");
        foreach (Sighting d in scene.Drops)
            text.Append(string.Create(CultureInfo.InvariantCulture,
                $"            {d.Name} at {d.X:0},{d.Y:0} ({d.Source}, {d.Age(scene.Tick):n0} tick(s) old){Distance(d, scene)}\n"));
    }

    private static string Distance(Sighting s, TickScene scene)
        => scene.PlayerFeet is { } p
            ? string.Create(CultureInfo.InvariantCulture, $", {MathF.Sqrt((s.X - p.X) * (s.X - p.X) + (s.Y - p.Y + 21) * (s.Y - p.Y + 21)) / 16:0.0} tiles from him")
            : "";

    private static void Cost(StringBuilder text, Explanation explain, Session session, GodsEyeEventLog log, int row, long tick)
    {
        text.Append("\nwhat the tick cost\n");
        text.Append($"  phases    {explain.Pairs("senses_ms", "reflex_ms", "decide_ms", "position_ms", "navigate_ms", "finalise_ms", "brain_ms")} (milliseconds)\n");
        text.Append($"  around    {explain.Pairs("record_ms", "frame_ms", "engine_ms", "draws")} (record_ms is the previous row's recorder)\n");
        string? sections = explain.Cell("sections");
        if (sections is null) text.Append("  sections  (absent)\n");
        else
        {
            var entries = MeasureWhereTheTimeGoes.Entries(sections).OrderByDescending(e => e.Self).Take(SectionsShown).ToList();
            text.Append(entries.Count == 0
                ? "  sections  none recorded on this row\n"
                : "  sections  " + string.Join(" · ", entries.Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Path} {e.Self:0.000}"))) + " ms self\n");
            text.Append($"            {explain.Pair("sections_other_ms")} outside the row's largest sections\n");
        }
        text.Append($"  memory    {explain.Pairs("tick_alloc_bytes", "brain_alloc_bytes", "alloc_sections", "gc0", "gc1", "gc2")}\n");
        string? fence = explain.Cell("cost_fence_ms");
        string? brain = explain.Cell("brain_ms");
        if (fence is null) text.Append("  spike     (absent: the capture carries no cost fence)\n");
        else
        {
            double fenceMs = Session.ParseNumber(fence), brainMs = brain is null ? double.NaN : Session.ParseNumber(brain);
            text.Append(double.IsNaN(fenceMs)
                ? "  spike     no fence yet: the recorder's window was still filling, which is not a fence of zero\n"
                : string.Create(CultureInfo.InvariantCulture,
                    $"  spike     brain {brainMs:0.000} ms against a fence of {fenceMs:0.000} ms: {(brainMs > fenceMs ? "a spike" : "not a spike")}\n"));
        }
        if (log.Events.FirstOrDefault(e => e.kind == "cost-spike" && e.Field("tick") == tick.ToString(CultureInfo.InvariantCulture)) is { } spike)
        {
            var largest = DescribeWhereTheTimeGoes.TreeEntries(spike.Field("tree") ?? "-").OrderByDescending(e => e.Self).Take(SectionsShown)
                .Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Path} {e.Self:0.00}"));
            text.Append($"            the cost-spike occurrence for this tick: {Clip(spike.detail.Split(";tree=")[0], 240)}; largest self: {string.Join(", ", largest)} ms\n");
        }
    }

    private static void Findings(StringBuilder text, Session session, long tick)
    {
        text.Append($"\nfindings on or within {FindingMargin} ticks of this tick\n");
        var (findings, _, _) = Program.Evaluate(session);
        var near = findings.Where(f => f.FirstTick - FindingMargin <= tick && tick <= f.LastTick + FindingMargin && !(f.FirstTick == 0 && f.LastTick == 0))
            .OrderBy(f => f.Severity).ThenBy(f => Math.Abs(f.FirstTick - tick)).ToList();
        int reader = findings.Count(f => f.FirstTick == 0 && f.LastTick == 0);
        if (near.Count == 0) text.Append("  none\n");
        foreach (Finding f in near.Take(20))
        {
            string where = f.FirstTick == f.LastTick ? $"tick {f.FirstTick:n0}" : $"ticks {f.FirstTick:n0}..{f.LastTick:n0}";
            string on = f.FirstTick <= tick && tick <= f.LastTick ? "on" : "near";
            text.Append($"  {f.Severity.ToString().ToLowerInvariant(),-10} {on,-4} {where}  {f.Title}  ({f.Check})\n");
        }
        if (near.Count > 20) text.Append($"  and {near.Count - 20} more\n");
        if (reader > 0) text.Append($"  {reader} finding(s) carry no tick (the whole-capture or reader-failure kind) and are left to the ordinary report\n");
    }

    /// <summary>The latest occurrence of a kind at or before a tick, optionally of one label.</summary>
    private static GodsEyeEvent? LatestAtOrBefore(GodsEyeEventLog log, string kind, long tick, string? label = null, string? having = null)
    {
        GodsEyeEvent? found = null;
        foreach (GodsEyeEvent e in log.Events)
        {
            if (e.tick > tick) continue;
            if (e.kind != kind || (label != null && e.label != label)) continue;
            if (having != null && !e.detail.Contains(";" + having, StringComparison.Ordinal)) continue;
            if (found is null || e.tick >= found.tick) found = e;
        }
        return found;
    }

    private static string Dash(string value) => value.Length == 0 ? "-" : value;

    /// <summary>A typed payload's fields as <c>name=text</c>, for the course occurrences whose detail is empty.</summary>
    private static string PayloadText(GodsEyeEvent e)
    {
        if (e.payload is not { ValueKind: System.Text.Json.JsonValueKind.Object } payload
            || !payload.TryGetProperty("Fields", out var fields) || fields.ValueKind != System.Text.Json.JsonValueKind.Object) return "";
        return string.Join(";", fields.EnumerateObject().Select(f =>
            $"{f.Name}={(f.Value.TryGetProperty("Text", out var t) ? t.ToString() : "")}"));
    }

    private static string Clip(string value, int width) => value.Length <= width ? value : value[..(width - 1)] + "…";
}
