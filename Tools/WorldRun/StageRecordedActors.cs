extern alias live;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;

/// <summary>
/// The recording's own hostiles and drops, put into the run at the ticks they appeared and taken
/// out at the ticks they left.
///
/// This is what makes a world run a scene rather than a walk. Before it the instrument replayed
/// every recorded journey through an empty world — the folder guide said so outright — so no combat
/// decision and no collection decision could be reproduced there at all, and the one fight the
/// suite could ask about was a frozen zombie the instrument staged for itself thirty steps ahead.
/// What is placed here is not staging: every actor comes from an occurrence the live session wrote,
/// at its own tick, slot, type and position, and nothing is invented to fill a gap.
///
/// Where it stands in the tick is a decision with a reason. The engine updates NPCs in slot order
/// and the companion is an early slot, so a hostile's own move happens after the companion's — the
/// brain therefore decides against where each hostile was at the end of the previous tick, which is
/// what it does in the game. Placements and retirements happen before the brain, because an actor
/// that appeared on tick N was in the world for the whole of the live tick N.
/// </summary>
internal sealed class StageRecordedActors
{
    /// <summary>
    /// How a placed hostile moves.
    ///
    /// <c>Native</c> drives the game's own <c>NPC.UpdateNPC</c>, which is the only motion that is a
    /// fact about the game rather than about this instrument; the row says so and nothing is
    /// claimed about damage, because nothing here runs the projectile update that would deal it.
    /// <c>Synthetic</c> walks the body at the player at a stated pace with no terrain in the way,
    /// and every row taken under it says the word, because a hostile that glides through rock is a
    /// scene this instrument invented.
    /// </summary>
    internal enum HostileMotion { Native, Synthetic }

    /// <summary>
    /// How fast a synthetic hostile closes on the player, in pixels a tick.
    ///
    /// A vanilla zombie walks at one pixel a tick and the rest of the surface roster is within a
    /// factor of two of it, so this is the roster's own pace rather than a number tuned here. It is
    /// only ever used when native AI could not be driven, and the row carries the word "synthetic"
    /// whenever it was.
    /// </summary>
    private const float SyntheticPacePixelsPerTick = 1f;

    private readonly ReadRecordedActors.Cast cast;
    private readonly List<int> staged = new();
    private readonly Dictionary<int, int> dropSlots = new();

    /// <summary>The tick this pass opened on, so the first call knows it is the first.</summary>
    private int? openedAt;

    public StageRecordedActors(ReadRecordedActors.Cast cast, HostileMotion motion)
    {
        this.cast = cast;
        Motion = motion;
    }

    public HostileMotion Motion { get; private set; }

    /// <summary>The first tick a native hostile update threw, with what it threw, or null when none did.</summary>
    public string? NativeMotionFailure { get; private set; }

    /// <summary>How many placed actors were taken out of the world by a throw from their own native update.</summary>
    public int RetiredByAThrow { get; private set; }

    /// <summary>How many of the cast this run actually put into the world, against how many the recording holds.</summary>
    public int PlacedHostiles { get; private set; }
    public int PlacedDrops { get; private set; }

    /// <summary>How many actors were placed at the window's first tick because they were already alive when it opened.</summary>
    public int PlacedAlreadyAlive { get; private set; }

    public int HostilesAlive { get; private set; }
    public int DropsPresent { get; private set; }

    /// <summary>
    /// What every row built on this run must say about how its actors behaved.
    ///
    /// It reports placed against recorded rather than recorded alone, and that is a correction
    /// rather than a flourish: this sentence used to read "18 recorded NPCs and 6 recorded drops
    /// placed", which is true only of a run from the capture's first tick. A windowed run stages
    /// whatever falls inside its window plus whatever was already alive when it opened, and a row
    /// that claims the whole cast while three of eighteen actors stand in the world is a row nobody
    /// can weigh.
    /// </summary>
    /// <summary>
    /// How many placed drops left the world before the recording's own pickup took them, which in this
    /// host can only be the run's companion collecting them: no item update runs here, so nothing
    /// despawns a drop and no player code picks one up. A partial stack left lying is not counted.
    /// </summary>
    public int DropsTakenByTheRun { get; private set; }

    /// <summary>
    /// How far a placed hostile may stray from the player before it is put back beside him, in pixels, or
    /// null to leave it wherever its own AI took it — which is every recorded scene.
    ///
    /// It exists for the load ladder, whose question is what the brain costs with N hostiles near the
    /// player. A zombie walks a pixel a tick and the bot three, so without it a rung staged at forty would
    /// deliver forty hostiles strung out behind a walking player and the rung would measure a smaller
    /// fight than it names. A re-placement is staging and is counted in <see cref="LeashReplacements"/>.
    /// </summary>
    public float? LeashPixels { get; set; }

    /// <summary>How many times the leash put a hostile back beside the player.</summary>
    public int LeashReplacements { get; private set; }

    /// <summary>Where a leashed hostile is put back, from a fixed seed so two rungs of one ladder re-place alike.</summary>
    private Random leashOffsets = new(LeashSeed);
    private const int LeashSeed = 0x1EA5;
    private readonly List<int> takenThisTick = new();

    public string Describe()
        => $"{PlacedHostiles} of {cast.Hostiles.Count} recorded NPCs and {PlacedDrops} of {cast.Drops.Count} recorded drops placed from the events sidecar at their own ticks, slots and positions"
        + (PlacedAlreadyAlive > 0
            ? $", {PlacedAlreadyAlive} of them at the window's first tick because the recording already had them alive — those stand where they spawned rather than where they had walked to, which the recording does not hold; "
            : "; ")
        + $"hostile motion {(Motion == HostileMotion.Native ? "is the game's own NPC.UpdateNPC" : $"is synthetic — a straight walk at the player at {SyntheticPacePixelsPerTick:0.##} px/tick with terrain ignored")}"
        + (RetiredByAThrow > 0 ? $", with {RetiredByAThrow} actor(s) retired by a throw from their own native update, the first being {NativeMotionFailure}" : "")
        + "; a placed actor is held at its recorded life and for the recording's own lifetime against the engine's despawn and its kill path, because the saved world's clock is not carried and a surface zombie in this host's daylight sends itself home in ten ticks; "
        + "no projectile update runs, so nothing here deals damage and no hostile dies of being shot — a hostile leaves when the recording had it die; "
        + "the mod's hostile-targeting hook is not dispatched in this host, so hostiles aim at the replayed player rather than at the companion; "
        + cast.Note;

    /// <summary>
    /// Clears every actor this stage placed, which every pass does before its first tick.
    ///
    /// The engine's slots are process-wide and a second pass that inherited the first pass's dead
    /// zombie would grade a different scene while the determinism row said the two runs agreed.
    /// </summary>
    public void Reset()
    {
        foreach (int slot in staged)
            if (slot >= 0 && slot < Main.npc.Length && Main.npc[slot] != null)
                Main.npc[slot].active = false;
        staged.Clear();
        foreach (int slot in dropSlots.Values)
            if (slot >= 0 && slot < Main.item.Length && Main.item[slot] != null)
                Main.item[slot].active = false;
        dropSlots.Clear();
        HostilesAlive = 0;
        DropsPresent = 0;
        RetiredByAThrow = 0;
        NativeMotionFailure = null;
        PlacedHostiles = 0;
        PlacedDrops = 0;
        PlacedAlreadyAlive = 0;
        DropsTakenByTheRun = 0;
        LeashReplacements = 0;
        leashOffsets = new Random(LeashSeed);
        openedAt = null;
    }

    /// <summary>
    /// Places what the recording had appear by this tick and retires what it had leave, before the
    /// brain observes anything.
    ///
    /// The window's first tick is the case that used to be missing and it is not an edge: placing
    /// only on `tick == SpawnTick` means every actor the recording already had alive when a
    /// `--from-tick` window opens is never placed at all. Measured on the capture of 22 September,
    /// a window from tick 1,700 staged three of eighteen actors and its census then admitted usable
    /// work on *zero* of three hundred ticks, so both verdicts passed with an empty denominator
    /// while the run's own sentence still claimed eighteen placed. An actor placed this way stands
    /// where it *spawned*, because the recording holds no position for it between its spawn and its
    /// death, and the sentence every row carries says so.
    /// </summary>
    public void BeforeTheBrain(int tick)
    {
        bool opening = openedAt is null;
        openedAt ??= tick;

        foreach (ReadRecordedActors.Hostile hostile in cast.Hostiles)
        {
            if (hostile.Slot <= 0 || hostile.Slot >= Main.npc.Length) continue;
            NPC npc = Main.npc[hostile.Slot];
            bool alreadyAlive = opening && hostile.SpawnTick < tick && tick < hostile.DeathTick;
            if (tick == hostile.SpawnTick || alreadyAlive)
            {
                PlacedHostiles++;
                if (alreadyAlive) PlacedAlreadyAlive++;
                // The recorded slot rather than a fresh one from NPC.NewNPC, because the brain and
                // the recorder both key an actor on its slot and a reader comparing this run
                // against the capture that produced it compares slot to slot. A slot the recording
                // used twice is re-used here the same way, because the first occupant's death tick
                // has already passed by the time the second spawns.
                npc.SetDefaults(hostile.Type);
                npc.Center = hostile.Centre;
                npc.velocity = Vector2.Zero;
                npc.life = hostile.Life;
                npc.active = true;
                npc.whoAmI = hostile.Slot;
                npc.target = 0;
                if (!staged.Contains(hostile.Slot)) staged.Add(hostile.Slot);
            }
            else if (tick == hostile.DeathTick && npc.active && npc.type == hostile.Type)
            {
                // Set directly rather than through StrikeNPC, whose death path writes dust and gore
                // through cosmetic slots no decision reads.
                npc.life = 0;
                npc.active = false;
            }
        }

        // A placed drop gone before the recording took it was taken by the run's companion, and it is
        // forgotten here so the recording's own retirement later cannot reach into its item slot — which
        // by then may hold a different drop placed into the freed slot.
        takenThisTick.Clear();
        foreach ((int recordedSlot, int itemSlot) in dropSlots)
            if (!Main.item[itemSlot].active) takenThisTick.Add(recordedSlot);
        foreach (int recordedSlot in takenThisTick)
        {
            dropSlots.Remove(recordedSlot);
            DropsTakenByTheRun++;
        }

        foreach (ReadRecordedActors.Drop drop in cast.Drops)
        {
            bool stillLying = opening && drop.SightingTick < tick && tick < drop.TakenTick;
            if ((tick == drop.SightingTick || stillLying) && drop.TakenTick > drop.SightingTick)
            {
                int slot = FreeItemSlot();
                if (slot < 0) continue;
                PlacedDrops++;
                if (stillLying) PlacedAlreadyAlive++;
                Item item = Main.item[slot];
                item.SetDefaults(drop.Type);
                item.stack = Math.Max(1, drop.Stack);
                item.Center = drop.Centre;
                item.velocity = Vector2.Zero;
                item.active = true;
                item.whoAmI = slot;
                item.noGrabDelay = 0;
                dropSlots[drop.Slot] = slot;
            }
            else if (tick == drop.TakenTick && dropSlots.TryGetValue(drop.Slot, out int placed))
            {
                // The recording picked it up on this tick. A drop the run's own companion already
                // collected is inactive by now and this is a no-op; a drop it left is taken away,
                // because leaving it would make the scene richer than the play was.
                Main.item[placed].active = false;
                dropSlots.Remove(drop.Slot);
            }
        }

        HostilesAlive = staged.Count(slot => Main.npc[slot].active);
        DropsPresent = dropSlots.Values.Count(slot => Main.item[slot].active);
    }

    /// <summary>
    /// Moves every placed hostile, after the companion's own tick.
    ///
    /// A native update that throws retires that one actor rather than the whole run's motion, and
    /// the retirement is counted and named in every row. The choice between the two was measured
    /// rather than guessed: on the whole-capture replay of 22 September 2026 exactly one actor
    /// threw — a firefly at tick 1,575, which sets its own life to minus one in daylight and then
    /// reaches <c>NPC.NPCLoot_DropItems</c>, which reads the item drop database this host does not
    /// build — and switching the whole run to synthetic motion because of it replaced the game's own
    /// AI for six other hostiles with a straight walk, which is a worse scene than the one it was
    /// protecting. The count is what keeps it from being a silence: a run that retired half its
    /// hostiles this way says so on every row.
    /// </summary>
    public void AfterTheBrain(int tick, Player player)
    {
        foreach (int slot in staged)
        {
            NPC npc = Main.npc[slot];
            if (!npc.active) continue;
            HoldItAsLongAsTheRecordingDid(npc, slot, tick);
            if (Motion == HostileMotion.Native)
            {
                try
                {
                    npc.UpdateNPC(slot);
                }
                catch (Exception failure)
                {
                    // The top frames travel with the message, because a retirement that names only
                    // the exception type leaves the next reader to reproduce a throw that happened
                    // once, fifteen hundred ticks into a run, on whichever actor the run's own
                    // divergence put there.
                    string where = string.Join(" <- ", (failure.StackTrace ?? "").Split('\n')
                        .Take(3).Select(line => line.Trim()));
                    NativeMotionFailure ??= string.Create(CultureInfo.InvariantCulture,
                        $"tick {tick}, type {npc.type} in slot {slot}: {failure.GetType().Name}: {failure.Message} :: {where}");
                    RetiredByAThrow++;
                    npc.active = false;
                }
                continue;
            }
            Vector2 toward = player.Center - npc.Center;
            if (toward.LengthSquared() > 1f)
                npc.position += Vector2.Normalize(toward) * SyntheticPacePixelsPerTick;
        }
        if (LeashPixels is { } leash) KeepThemNear(player, leash);
    }

    /// <summary>Puts every placed hostile further than the leash back beside the player, six to twenty
    /// tiles to one side at his feet's height, which is where the soak's cast places a zombie too.</summary>
    private void KeepThemNear(Player player, float leash)
    {
        foreach (int slot in staged)
        {
            NPC npc = Main.npc[slot];
            if (!npc.active || Vector2.DistanceSquared(npc.Center, player.Center) <= leash * leash) continue;
            float side = leashOffsets.Next(2) == 0 ? -1f : 1f;
            npc.Center = player.Bottom + new Vector2(side * (6 + leashOffsets.Next(15)) * 16f, -24f);
            npc.velocity = Vector2.Zero;
            LeashReplacements++;
        }
    }

    /// <summary>
    /// Keeps a placed actor in the world for as long as the recording kept it, against the engine's
    /// own wish to send it home.
    ///
    /// This is the one place the instrument overrules native AI, and it closes a gap that made the
    /// whole staging useless. A surface zombie in daylight sets its own <c>timeLeft</c> to ten and
    /// leaves — measured here on 22 September 2026, every placed zombie gone exactly ten ticks after
    /// it was placed — because the saved world's clock is not one of the fields
    /// <c>LoadTheSavedWorld</c> carries, so this host runs at whatever time of day <c>Main</c>'s own
    /// static initialiser left. The recording is the authority on which actors were present and
    /// when, and the engine is the authority only on how they moved; letting the engine decide
    /// presence would replay a scene the session never had, and it would do it silently, because a
    /// despawned hostile and a hostile the brain never noticed look identical in every row.
    ///
    /// It only ever extends. The recording's own death tick still retires the actor, on the tick it
    /// retired in the session, so nothing here keeps an actor alive past its recorded life.
    /// </summary>
    private void HoldItAsLongAsTheRecordingDid(NPC npc, int slot, int tick)
    {
        foreach (ReadRecordedActors.Hostile hostile in cast.Hostiles)
        {
            if (hostile.Slot != slot || hostile.Type != npc.type || tick < hostile.SpawnTick || tick > hostile.DeathTick) continue;
            int recordedRemaining = hostile.DeathTick == int.MaxValue ? int.MaxValue : hostile.DeathTick - tick + 1;
            if (npc.timeLeft < recordedRemaining) npc.timeLeft = Math.Min(recordedRemaining, int.MaxValue);
            // And its recorded life, restored before the engine's own update rather than after, so
            // the engine never enters its kill path for a staged actor. Two things ride on that.
            // Nothing here deals damage — no projectile update runs — so a staged actor losing life
            // is the engine hurting it for a reason the session did not have; and the kill path ends
            // in `NPC.NPCLoot_DropItems`, which reads the item drop database this host deliberately
            // does not build, so it throws. Measured 22 September 2026: a firefly died at tick 1,575
            // of the whole-capture replay and took native motion down with it for the rest of the run.
            if (npc.life < hostile.Life) npc.life = hostile.Life;
            if (npc.lifeMax < hostile.Life) npc.lifeMax = hostile.Life;
            return;
        }
    }

    /// <summary>
    /// A slot no drop is in. Slot zero is skipped because the engine treats it as the "no item"
    /// answer in several of its own walks, and the high slots are the mouse item and its kin.
    /// </summary>
    private static int FreeItemSlot()
    {
        for (int slot = 1; slot < Main.item.Length - 2; slot++)
            if (Main.item[slot] != null && !Main.item[slot].active)
                return slot;
        return -1;
    }
}
