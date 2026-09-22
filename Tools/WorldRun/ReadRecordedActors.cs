using System.Globalization;
using System.Text.Json;
using Microsoft.Xna.Framework;

/// <summary>
/// The other two things a recorded session held besides the player: the hostiles that stood in it
/// and the drops that lay on the floor.
///
/// The route reader next door takes the TSV, which has one row per companion tick and a column for
/// how many threats and how much loot the senses counted — a count and nothing else. Where each of
/// them actually was is in the events sidecar, and until this file existed the world run replayed
/// every recorded journey through an empty world: no hostile, no drop, and therefore not one
/// combat or collection decision reproduced, whatever the recording held. That is the gap this
/// closes, and it is the reason a play in which the companion stood beside five hostiles and three
/// drops doing nothing could not be reproduced headlessly at all.
///
/// Two things it refuses to do, for the reason the route reader refuses to guess a kit. It never
/// invents an actor the recording does not name: a hostile is placed because an <c>npc-spawn</c>
/// occurrence says one appeared, at the tick, slot, type and centre that occurrence carries, and a
/// drop is placed because a collection funnel or a pickup saw one. And it never infers a drop from
/// a death, however reliably a zombie drops loot in the real game, because that is a heuristic
/// running underneath the thing being measured.
/// </summary>
internal static class ReadRecordedActors
{
    /// <summary>
    /// One recorded hostile — or critter: the recorder writes every NPC that appears, and which of
    /// them is a threat is the threat sense's judgement rather than this reader's.
    ///
    /// <paramref name="Slot"/> is the engine slot the live session had it in, kept rather than
    /// reassigned: the brain and the recorder both key an actor on its slot, and a reader comparing
    /// this run against the capture that produced it is comparing slot to slot.
    /// </summary>
    internal readonly record struct Hostile(
        int SpawnTick,
        int Slot,
        int Generation,
        int Type,
        string Name,
        Vector2 Centre,
        int Life,
        /// <summary>The tick the recording had it die, or <c>int.MaxValue</c> for one that outlived the capture.</summary>
        int DeathTick);

    /// <summary>
    /// One recorded drop, at the first tick anything in the recording saw it.
    ///
    /// The position is only as precise as its source. A collection funnel names the drop's tile
    /// rather than its pixel, so a drop placed from one sits at that tile's centre and is within
    /// eight pixels of where it lay; a pickup carries the item's own centre and is exact. Both are
    /// stated in the row rather than smoothed over, because eight pixels is a third of a body.
    /// </summary>
    internal readonly record struct Drop(
        int SightingTick,
        int Slot,
        int Type,
        int Stack,
        Vector2 Centre,
        /// <summary>Whether the centre came from an item's own position or from the tile a funnel named.</summary>
        bool CentreIsExact,
        /// <summary>The tick the recorded session picked it up, or <c>int.MaxValue</c> for one still lying there at the end.</summary>
        int TakenTick);

    /// <summary>
    /// What the sidecar held, with the honest account of what it could not hold.
    ///
    /// <paramref name="Shortfall"/> is the difference between the drops this reader can name and the
    /// most the capture's own <c>loot</c> column ever counted at once. It is not a defect in the
    /// reader: the sidecar names a drop only when a collection funnel changed or a pickup happened,
    /// so a drop that lay on the floor all session without either is in the count and nowhere else.
    /// Every row built on this cast carries the shortfall, because a run staged with fewer drops
    /// than the play had is a weaker scene than the play and must not read as an equal one.
    /// </summary>
    internal sealed record Cast(
        string EventsPath,
        IReadOnlyList<Hostile> Hostiles,
        IReadOnlyList<Drop> Drops,
        int MostDropsCountedAtOnce,
        int Shortfall,
        /// <summary>
        /// The tick the recording last credited the companion with a kill, or -1 when it never did.
        ///
        /// It is the opening of the window the silence row asks about: in the capture this was built
        /// against the companion killed its last hostile at tick 1,816, never fired again over the
        /// remaining 524 ticks, and five hostiles stood beside it throughout. A run in which the
        /// companion fires after that tick is a run in which the silence has gone.
        /// </summary>
        int LastCompanionKillTick,
        /// <summary>
        /// The companion's live preference set, from the recording's own <c>configuration</c>
        /// occurrence, or empty when the schema wrote none.
        ///
        /// It is read from the occurrence rather than from the capture's <c># config=</c> header
        /// because the two disagree and the header is the one that is wrong: the capture this was
        /// built against declares <c>chopping=Opportunistic</c> in its header and
        /// <c>chopping=Mimic</c> in the occurrence its first tick wrote, and every census line of the
        /// session reads <c>mimic-awaiting-player-tree-contact</c>, which is Mimic. A replay that
        /// took the header would give the companion a whole job the play never had.
        /// </summary>
        string Configuration,
        string Note);

    /// <summary>The companion's own NPC type, which appears in the spawn occurrences and is not a hostile.</summary>
    private const string CompanionLabel = "Companion";

    /// <summary>The sidecar beside a capture, named the way the recorder names it.</summary>
    public static string EventsPathFor(string capturePath)
        => Path.Combine(Path.GetDirectoryName(capturePath) ?? ".",
            Path.GetFileNameWithoutExtension(capturePath) + "-events.jsonl");

    public static Cast Read(string capturePath, int mostDropsCountedAtOnce)
    {
        string events = EventsPathFor(capturePath);
        if (!File.Exists(events))
            return new Cast(events, Array.Empty<Hostile>(), Array.Empty<Drop>(), mostDropsCountedAtOnce, mostDropsCountedAtOnce, -1, "",
                $"no events sidecar at {Path.GetFileName(events)}, so no hostile and no drop can be placed; "
                + "this capture's schema predates the sidecar, or it was not kept beside the TSV");

        var spawns = new List<Hostile>();
        var deaths = new List<(int Subject, int Tick)>();
        var sightings = new List<Drop>();
        var pickups = new List<(int Slot, int Type, int Tick)>();
        int lastCompanionKill = -1;
        string configuration = "";

        foreach (string line in File.ReadLines(events))
        {
            if (line.Length == 0 || line[0] != '{') continue;
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement e = document.RootElement;
            string kind = Text(e, "kind");
            int tick = Integer(e, "tick");
            switch (kind)
            {
                case "npc-spawn":
                {
                    string label = Text(e, "label");
                    if (label == CompanionLabel) continue;
                    int subject = Integer(e, "subject");
                    spawns.Add(new Hostile(tick, subject / 1_000_000, subject % 1_000_000,
                        int.Parse(Text(e, "related"), CultureInfo.InvariantCulture), label,
                        new Vector2(Real(e, "pos_x"), Real(e, "pos_y")), Integer(e, "amount"), int.MaxValue));
                    break;
                }
                case "npc-death":
                    deaths.Add((Integer(e, "subject"), tick));
                    break;
                case "configuration":
                    // The first one is what the session ran under; a later one is a change the
                    // player made mid-session and is not applied, because this instrument places
                    // the scene once and a preference that moved halfway through is a second scene.
                    if (configuration.Length == 0) configuration = Text(e, "detail");
                    break;
                case "experience-credit":
                    // `related` names who earned it. A kill credited to the player says nothing
                    // about whether the companion's hands were working.
                    if (Text(e, "related") == "companion" && Text(e, "label") == "enemy-kill")
                        lastCompanionKill = Math.Max(lastCompanionKill, tick);
                    break;
                case "candidate-funnel":
                    if (Text(e, "label") == "collect")
                        sightings.AddRange(DropsInAFunnel(Text(e, "detail"), tick));
                    break;
                case "pickup":
                {
                    // `related` is slot * 1,000,000 + generation and `label` is the item type. The
                    // pickup is both a retirement of a drop this reader may have placed and a
                    // sighting in its own right, because it carries the item's exact centre and
                    // some drops were picked up without a funnel ever naming them.
                    int slot = Integer(e, "related") / 1_000_000;
                    int type = int.Parse(Text(e, "label"), CultureInfo.InvariantCulture);
                    pickups.Add((slot, type, tick));
                    sightings.Add(new Drop(tick, slot, type, Integer(e, "amount"),
                        new Vector2(Real(e, "pos_x"), Real(e, "pos_y")), CentreIsExact: true, TakenTick: tick));
                    break;
                }
            }
        }

        var hostiles = new List<Hostile>(spawns.Count);
        foreach (Hostile spawn in spawns)
        {
            int identity = spawn.Slot * 1_000_000 + spawn.Generation;
            int died = deaths.Where(d => d.Subject == identity && d.Tick >= spawn.SpawnTick)
                .Select(d => d.Tick).DefaultIfEmpty(int.MaxValue).Min();
            hostiles.Add(spawn with { DeathTick = died });
        }

        // One drop per occupancy of a slot by a type, rather than one per slot and type: a slot is
        // reused, so the sightings of one slot-and-type have to be cut into episodes at the pickups
        // between them. Generation cannot do the cutting, which is the thing worth knowing here —
        // `RecordItemSpawn` is what advances an item's generation and it did not run for every drop
        // in the capture this was built against, so slot 1 carried generation 1 through two
        // different items. Grouping without the cut merges them, and the merge loses exactly the
        // drops that matter: in that capture the drop still lying on the floor at the end shared
        // slot 1 with one picked up three hundred ticks earlier, so the tail's only nameable drop
        // vanished into a drop that had already been collected.
        var drops = new List<Drop>();
        foreach (var group in sightings.GroupBy(d => (d.Slot, d.Type)))
        {
            var taken = pickups.Where(p => p.Slot == group.Key.Slot && p.Type == group.Key.Type)
                .Select(p => p.Tick).OrderBy(t => t).ToList();
            Drop? open = null;
            int nextPickup = 0;
            foreach (Drop sighting in group.OrderBy(d => d.SightingTick).ThenByDescending(d => d.CentreIsExact))
            {
                while (nextPickup < taken.Count && taken[nextPickup] < sighting.SightingTick)
                {
                    if (open is { } closing) drops.Add(closing with { TakenTick = taken[nextPickup] });
                    open = null;
                    nextPickup++;
                }
                // The first sighting of an episode owns it. A later sighting of the same item before
                // any pickup is the same drop seen again, and the earliest one is the tick it was
                // first there.
                open ??= sighting;
            }
            if (open is { } last)
                drops.Add(last with
                {
                    TakenTick = nextPickup < taken.Count ? taken[nextPickup] : int.MaxValue
                });
        }
        drops.Sort((a, b) => a.SightingTick.CompareTo(b.SightingTick));

        int placeable = drops.Count(d => d.TakenTick == int.MaxValue);
        int shortfall = Math.Max(0, mostDropsCountedAtOnce - placeable);
        string note = $"{hostiles.Count} recorded NPCs and {drops.Count} recorded drops from {Path.GetFileName(events)}; "
            + $"{placeable} of those drops were never picked up, against a loot column that reached {mostDropsCountedAtOnce}, "
            + (shortfall == 0
                ? "so every drop the senses counted is named"
                : $"so {shortfall} drop(s) the senses counted are named nowhere in this schema and cannot be staged");

        return new Cast(events, hostiles, drops, mostDropsCountedAtOnce, shortfall, lastCompanionKill, configuration, note);
    }

    /// <summary>
    /// The drops a collection funnel named, out of its detail string.
    ///
    /// The shape is <c>counts=…;entries=item&lt;slot&gt;:&lt;type&gt;@&lt;tileX&gt;,&lt;tileY&gt;:&lt;from&gt;&gt;&lt;to&gt;[readings]</c>,
    /// with entries separated by a pipe and the readings carrying the stack. The readings hold
    /// semicolons of their own, so the entries are cut at the first <c>;entries=</c> rather than by
    /// splitting the whole detail on semicolons.
    /// </summary>
    private static IEnumerable<Drop> DropsInAFunnel(string detail, int tick)
    {
        const string marker = ";entries=";
        int at = detail.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) yield break;
        foreach (string entry in detail[(at + marker.Length)..].Split('|'))
        {
            if (!entry.StartsWith("item", StringComparison.Ordinal)) continue;
            int colon = entry.IndexOf(':');
            int atSign = entry.IndexOf('@');
            if (colon < 0 || atSign < 0 || atSign < colon) continue;
            if (!int.TryParse(entry[4..colon], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)) continue;
            if (!int.TryParse(entry[(colon + 1)..atSign], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)) continue;
            int endOfPlace = entry.IndexOf(':', atSign);
            if (endOfPlace < 0) endOfPlace = entry.Length;
            string[] place = entry[(atSign + 1)..endOfPlace].Split(',');
            if (place.Length != 2
                || !int.TryParse(place[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tileX)
                || !int.TryParse(place[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tileY)) continue;
            // The funnel writes `item.Center.ToTileCoordinates()`, which floors, so the drop lay
            // somewhere inside that tile and the tile's own centre is the best point available.
            yield return new Drop(tick, slot, type, StackIn(entry),
                new Vector2(tileX * 16 + 8, tileY * 16 + 8), CentreIsExact: false, TakenTick: int.MaxValue);
        }
    }

    private static int StackIn(string entry)
    {
        const string marker = "stack=";
        int at = entry.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return 1;
        int end = at + marker.Length;
        while (end < entry.Length && char.IsDigit(entry[end])) end++;
        return int.TryParse(entry[(at + marker.Length)..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stack) ? stack : 1;
    }

    private static string Text(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static int Integer(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    private static float Real(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? (float)value.GetDouble() : 0f;
}
