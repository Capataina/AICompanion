#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// Where inside a brain tick the time went: a call-tree profiler of named sections, entered with a struct scope
/// (<c>using var _ = BrainSections.Enter(id);</c>) and rolled over once per brain tick into a readable snapshot of the
/// tick just finished.
///
/// <para><b>The phase columns said which phase and never which part of it.</b> When <c>decide_ms</c> reads 40 ms on
/// one tick, nothing in the record could say whether the light census, the order search, a travel query or the
/// recorder had spent it. A section is registered once by a short name and entered wherever that work runs; the tree
/// is built from the stack at entry, so the same section called from two places — a use simulated for planning and
/// the same simulator asked by the hand at fire time — is two nodes with two paths
/// (<c>decide.prepare.combat.reprice.aim.simulate</c>, <c>finalise.engage.aim.simulate</c>) rather than one number mixing both. A
/// node's path is its dotted name, and the parent is recoverable from it, which is what the recorder writes.</para>
///
/// <para><b>Entering and leaving allocate nothing and take no lock</b>, because the brain runs on the game's update
/// thread in a mod that has to share a frame with dozens of others. Every array is allocated here, once, at class
/// load: the section registry, the node table, the per-tick accumulators and the snapshot the tick is rolled into.
/// A node is created the first time its (parent, section) pair is entered by writing into a preallocated slot, and
/// its path string is built only when somebody reads it. A section entered while it is already open — recursion,
/// or a caller wrapping a helper that wraps itself — is inert rather than counted twice, which is what keeps every
/// inclusive time inside its parent's. <c>Tools/EngineReplay/Observation/VerifyBrainSectionProfiler.cs</c> proves
/// the allocation and nesting properties with the thread's allocation counter and a mutation of each.</para>
///
/// <para><b>Only one thread records.</b> <see cref="BeginTick"/> claims the thread that runs the brain; a scope
/// entered on any other thread — the recorder's writer worker, a harness running two cases at once — is inert,
/// which is how the no-lock rule stays safe rather than lucky.</para>
///
/// <para><b>What a tick's snapshot holds, and its one phase offset.</b> The accumulators roll over at
/// <see cref="EndTick"/>, which <c>Brain.Tick</c> calls in its own <c>finally</c>, so every brain section in the
/// snapshot belongs to the tick whose <c>brain_ms</c> the row carries. The recorder runs after the brain, so its
/// own <c>record</c> section lands in the <i>next</i> tick's snapshot — the same phase <c>record_ms</c> already has,
/// because a row cannot contain the time spent writing itself.</para>
///
/// <para>This file names no game type, because the movement core calls it and <c>Tools/NavReplay</c> and
/// <c>Tools/EngineReplay</c> compile the movement core without the game; both compile this file beside it, and in
/// EngineReplay that makes two copies of every static here, of which the live brain writes the <c>live::</c> one.</para>
/// </summary>
public static class BrainSections
{
    /// <summary>How many distinct section names can be registered. Registration is per call site, so this bounds
    /// the number of places instrumented rather than anything a tick does; one past it registers as inert.</summary>
    public const int SectionCapacity = 128;

    /// <summary>How many distinct (parent, section) nodes the tree can hold. A node is a call path, so the bound is
    /// on distinct paths, reached in the first ticks and then stable; a path past it is counted in
    /// <see cref="Overflowed"/> and its time stays in its parent's self time rather than being lost.</summary>
    public const int NodeCapacity = 512;

    /// <summary>How deep sections may nest. The brain's deepest instrumented path is six sections; a deeper one is
    /// counted in <see cref="Overflowed"/> and left to its parent.</summary>
    public const int DepthCapacity = 32;

    /// <summary>
    /// Whether sections are timed at all. On by default and only ever switched off to measure what the profiler
    /// itself costs — the profiling-off arm of <c>VerifyBrainSectionProfiler</c>'s overhead and invariance cases — so the switch exists for the instrument and not
    /// for play. Off, <see cref="Enter"/> is one branch and <see cref="EndTick"/> publishes nothing.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    private static readonly string[] sectionNames = new string[SectionCapacity];
    private static int sectionCount;

    // The node table. Node 0 is the root and is never entered; every other node is one section under one parent.
    private static readonly int[] nodeSection = new int[NodeCapacity];
    private static readonly int[] nodeParent = new int[NodeCapacity];
    private static readonly int[] nodeFirstChild = new int[NodeCapacity];
    private static readonly int[] nodeNextSibling = new int[NodeCapacity];
    private static readonly string?[] nodePath = new string?[NodeCapacity];
    private static int nodeCount = 1;

    // This tick's accumulation, rolled into the snapshot below by EndTick and cleared.
    private static readonly long[] inclusive = new long[NodeCapacity];
    private static readonly int[] calls = new int[NodeCapacity];
    private static readonly long[] allocated = new long[NodeCapacity];

    // The last finished tick.
    private static readonly long[] lastInclusive = new long[NodeCapacity];
    private static readonly long[] lastSelf = new long[NodeCapacity];
    private static readonly int[] lastCalls = new int[NodeCapacity];
    private static readonly long[] lastAllocated = new long[NodeCapacity];
    private static readonly long[] lastSelfAllocated = new long[NodeCapacity];
    private static int lastNodeCount = 1;

    private static readonly int[] stack = new int[DepthCapacity];
    private static int depth;
    private static readonly int[] openCount = new int[SectionCapacity];
    private static int ownerThread = -1;
    private static long tickAllocatedAtBegin;

    /// <summary>The tick the snapshot describes, or <see cref="ulong.MaxValue"/> before the first rollover. A reader
    /// compares it with the tick it is writing and writes nothing where they differ, because a stale tree printed
    /// as fresh is a hitch attributed to the wrong tick. A downed tick runs no brain but does roll over, carrying
    /// only its finalise section, so a reader that wants brain ticks also asks <c>brain_fresh</c>.</summary>
    public static ulong LastTick { get; private set; } = ulong.MaxValue;

    /// <summary>Bytes the brain thread allocated between <see cref="BeginTick"/> and <see cref="EndTick"/> of the
    /// snapshot's tick, everything the brain did included, sections or not.</summary>
    public static long LastBrainAllocatedBytes { get; private set; }

    /// <summary>Scopes refused because the tree or the stack was full, counted rather than silently lost.</summary>
    public static long Overflowed { get; private set; }

    /// <summary>Scopes that closed out of order — a scope disposed while a child it opened was still open. The
    /// using-statement form makes it impossible by construction; the count exists so a hand-written
    /// <c>Dispose</c> in the wrong order is a number rather than a corrupted tree.</summary>
    public static long Unbalanced { get; private set; }

    /// <summary>The number of nodes the snapshot covers; node indices below it are readable.</summary>
    public static int LastNodeCount => lastNodeCount;

    /// <summary>How many section names are registered.</summary>
    public static int SectionCount => sectionCount;

    /// <summary>
    /// The section called <paramref name="name"/>, registered on first ask and the same id for ever after. Called
    /// from a static field initialiser at each call site, so it runs once per site at class load and never on a
    /// tick. A name is one word of the dotted path — <c>light</c>, not <c>decide.discovery.light</c> — because the
    /// path is where it was entered from, which the call site does not know. Past <see cref="SectionCapacity"/> it
    /// returns -1, which <see cref="Enter"/> treats as inert. Some names come from data — a census registers its
    /// domain's name — so a dot is written as an underscore and an empty name as <c>unnamed</c> rather than thrown
    /// on: an instrument must never be the thing that stops a brain constructing.
    /// </summary>
    public static int Register(string name)
    {
        name = string.IsNullOrEmpty(name) ? "unnamed" : name.Replace('.', '_');
        for (int i = 0; i < sectionCount; i++)
            if (string.Equals(sectionNames[i], name, StringComparison.Ordinal)) return i;
        if (sectionCount >= SectionCapacity) return -1;
        sectionNames[sectionCount] = name;
        return sectionCount++;
    }

    /// <summary>The registered name of a section id.</summary>
    public static string SectionName(int section) => (uint)section < (uint)sectionCount ? sectionNames[section] : "?";

    /// <summary>
    /// A timed scope. The default value is inert, which is what every refusal returns, so a caller never branches.
    /// A readonly struct disposed by a using statement is called through a constrained call and never boxed.
    /// </summary>
    public readonly struct Scope : IDisposable
    {
        private readonly int node;
        private readonly long started;
        private readonly long allocatedAtEntry;

        internal Scope(int node, long started, long allocatedAtEntry)
        {
            this.node = node;
            this.started = started;
            this.allocatedAtEntry = allocatedAtEntry;
        }

        public void Dispose()
        {
            if (node != 0) Exit(node, started, allocatedAtEntry);
        }
    }

    /// <summary>Opens <paramref name="section"/> under whatever section is open now.</summary>
    public static Scope Enter(int section)
    {
        if (!Enabled || (uint)section >= (uint)sectionCount || Environment.CurrentManagedThreadId != ownerThread)
            return default;
        // Re-entry is inert: its time is already inside the open instance's, and counting it again would put a
        // child's inclusive time above its own ancestor's.
        if (openCount[section] > 0) return default;
        if (depth >= DepthCapacity - 1) { Overflowed++; return default; }
        int node = Child(stack[depth], section);
        if (node == 0) { Overflowed++; return default; }
        openCount[section]++;
        stack[++depth] = node;
        // Allocation first and the clock last on the way in, the clock first and allocation last on the way out,
        // so the reads of the other counter sit outside the timed interval rather than inside it.
        long allocatedNow = GC.GetAllocatedBytesForCurrentThread();
        return new Scope(node, Stopwatch.GetTimestamp(), allocatedNow);
    }

    private static void Exit(int node, long started, long allocatedAtEntry)
    {
        long now = Stopwatch.GetTimestamp();
        long allocatedNow = GC.GetAllocatedBytesForCurrentThread();
        if (depth == 0 || stack[depth] != node)
        {
            // A scope closing with a child still open, or a scope from before a reset. Unwind to it if it is on the
            // stack so the tree stays consistent; otherwise drop it.
            Unbalanced++;
            int at = depth;
            while (at > 0 && stack[at] != node) at--;
            if (at == 0) return;
            while (depth > at) { openCount[nodeSection[stack[depth]]]--; depth--; }
        }
        inclusive[node] += now - started;
        calls[node]++;
        allocated[node] += allocatedNow - allocatedAtEntry;
        openCount[nodeSection[node]]--;
        depth--;
    }

    /// <summary>The node for <paramref name="section"/> under <paramref name="parent"/>, created in a preallocated
    /// slot on first use; 0 when the table is full.</summary>
    private static int Child(int parent, int section)
    {
        for (int child = nodeFirstChild[parent]; child != 0; child = nodeNextSibling[child])
            if (nodeSection[child] == section) return child;
        if (nodeCount >= NodeCapacity) return 0;
        int created = nodeCount++;
        nodeSection[created] = section;
        nodeParent[created] = parent;
        nodeFirstChild[created] = 0;
        nodeNextSibling[created] = nodeFirstChild[parent];
        nodeFirstChild[parent] = created;
        return created;
    }

    /// <summary>Claims the calling thread as the one that records, and marks where this tick's allocation starts.
    /// <c>Brain.Tick</c> calls it first thing.</summary>
    public static void BeginTick()
    {
        if (!Enabled) return;
        ownerThread = Environment.CurrentManagedThreadId;
        tickAllocatedAtBegin = GC.GetAllocatedBytesForCurrentThread();
    }

    /// <summary>
    /// Rolls this tick's accumulation into the snapshot and clears it. <c>Brain.Tick</c> calls it in its own
    /// <c>finally</c>, after every phase's scope has closed. Copies and clears preallocated arrays; allocates nothing.
    /// </summary>
    public static void EndTick(ulong tick)
    {
        if (!Enabled || Environment.CurrentManagedThreadId != ownerThread) return;
        // A scope still open here was abandoned by a throw that skipped its using statement's own disposal, which a
        // using statement cannot do, or by a hand-held scope a throw walked past. It is closed without being
        // counted, so the next tick's sections do not nest under a node that is no longer running.
        if (depth > 0)
        {
            Unbalanced += depth;
            while (depth > 0) { openCount[nodeSection[stack[depth]]]--; depth--; }
        }
        int count = nodeCount;
        Array.Copy(inclusive, lastInclusive, count);
        Array.Copy(calls, lastCalls, count);
        Array.Copy(allocated, lastAllocated, count);
        // Self time is inclusive time less the children's inclusive time, taken in one pass over the nodes because a
        // child's index is always above its parent's. The root is never entered, so its inclusive time is the sum
        // of the top-level sections and its self time is zero.
        lastInclusive[0] = 0;
        for (int node = 1; node < count; node++)
            if (nodeParent[node] == 0) lastInclusive[0] += lastInclusive[node];
        Array.Copy(lastInclusive, lastSelf, count);
        for (int node = 1; node < count; node++) lastSelf[nodeParent[node]] -= lastInclusive[node];
        lastSelf[0] = 0;
        // Self allocation the same way: what a section allocated outside every section it opened.
        Array.Copy(lastAllocated, lastSelfAllocated, count);
        for (int node = 1; node < count; node++)
            if (nodeParent[node] != 0) lastSelfAllocated[nodeParent[node]] -= lastAllocated[node];
        lastNodeCount = count;
        LastTick = tick;
        LastBrainAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - tickAllocatedAtBegin;
        Array.Clear(inclusive, 0, count);
        Array.Clear(calls, 0, count);
        Array.Clear(allocated, 0, count);
    }

    /// <summary>The snapshot's parent of <paramref name="node"/>; 0 is the root.</summary>
    public static int Parent(int node) => nodeParent[node];

    /// <summary>The section a node is an instance of.</summary>
    public static int SectionOf(int node) => nodeSection[node];

    /// <summary>Milliseconds spent inside the node on the snapshot's tick, its children included.</summary>
    public static double InclusiveMilliseconds(int node) => lastInclusive[node] * MillisecondsPerTimestamp;

    /// <summary>Milliseconds spent inside the node and inside none of its children: where the time actually went.</summary>
    public static double SelfMilliseconds(int node) => lastSelf[node] * MillisecondsPerTimestamp;

    /// <summary>The raw clock ticks behind <see cref="InclusiveMilliseconds"/>, for a comparison that must be exact.</summary>
    public static long InclusiveTimestamps(int node) => lastInclusive[node];

    /// <summary>Times the node was entered on the snapshot's tick.</summary>
    public static int Calls(int node) => lastCalls[node];

    /// <summary>Bytes the thread allocated inside the node on the snapshot's tick, its children included.</summary>
    public static long AllocatedBytes(int node) => lastAllocated[node];

    /// <summary>Bytes the thread allocated inside the node and inside none of its children.</summary>
    public static long SelfAllocatedBytes(int node) => lastSelfAllocated[node];

    /// <summary>One clock tick in milliseconds. <see cref="Stopwatch.Elapsed"/> truncates to 100 ns; this does not.</summary>
    public static readonly double MillisecondsPerTimestamp = 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// The node's dotted path from the root, built the first time it is asked for and kept, so the one allocation a
    /// node ever costs is paid by a reader rather than by the tick that first entered it.
    /// </summary>
    public static string Path(int node)
    {
        if (node <= 0 || node >= nodeCount) return "";
        if (nodePath[node] is { } known) return known;
        int parent = nodeParent[node];
        string path = parent == 0 ? sectionNames[nodeSection[node]] : Path(parent) + "." + sectionNames[nodeSection[node]];
        nodePath[node] = path;
        return path;
    }

    /// <summary>
    /// The snapshot's <paramref name="count"/> nodes with the most self time, written into <paramref name="into"/>
    /// in descending order, and how many were written. Only nodes entered on the tick are eligible.
    /// </summary>
    public static int TopBySelf(Span<int> into, int count) => TopBy(lastSelf, into, count);

    /// <summary>The same ranking by self allocation: the sections that allocated the most outside their children.</summary>
    public static int TopBySelfAllocation(Span<int> into, int count) => TopBy(lastSelfAllocated, into, count);

    private static int TopBy(long[] key, Span<int> into, int count)
    {
        int written = 0;
        count = Math.Min(count, into.Length);
        for (int node = 1; node < lastNodeCount; node++)
        {
            if (lastCalls[node] == 0) continue;
            long self = key[node];
            int at = written;
            while (at > 0 && key[into[at - 1]] < self) at--;
            if (at >= count) continue;
            int last = Math.Min(written, count - 1);
            for (int i = last; i > at; i--) into[i] = into[i - 1];
            into[at] = node;
            if (written < count) written++;
        }
        return written;
    }

    /// <summary>
    /// The whole tree of the snapshot's tick, one entered node per entry, in node order (a parent always before its
    /// children):
    /// <c>path=inclusive/self/calls/allocated-bytes</c> joined by <c>|</c>. For the spike occurrence, which is
    /// written rarely enough that the recorder can afford to spell the whole tree out.
    /// </summary>
    public static void AppendTree(StringBuilder into)
    {
        bool first = true;
        for (int node = 1; node < lastNodeCount; node++)
        {
            if (lastCalls[node] == 0) continue;
            if (!first) into.Append('|');
            first = false;
            into.Append(Path(node)).Append('=')
                .Append(CultureInfo.InvariantCulture, $"{InclusiveMilliseconds(node):0.000}/{SelfMilliseconds(node):0.000}/{lastCalls[node]}/{lastAllocated[node]}");
        }
    }

    /// <summary>
    /// Puts every static back to a fresh process's state apart from the registry, which call sites filled at class
    /// load and cannot refill. The per-case reset calls it for both copies; a harness measuring the profiler's cost
    /// calls it between arms.
    /// </summary>
    public static void Reset()
    {
        Enabled = true;
        Array.Clear(nodeSection, 0, NodeCapacity);
        Array.Clear(nodeParent, 0, NodeCapacity);
        Array.Clear(nodeFirstChild, 0, NodeCapacity);
        Array.Clear(nodeNextSibling, 0, NodeCapacity);
        Array.Clear(nodePath, 0, NodeCapacity);
        nodeCount = 1;
        Array.Clear(inclusive, 0, NodeCapacity);
        Array.Clear(calls, 0, NodeCapacity);
        Array.Clear(allocated, 0, NodeCapacity);
        Array.Clear(lastInclusive, 0, NodeCapacity);
        Array.Clear(lastSelf, 0, NodeCapacity);
        Array.Clear(lastCalls, 0, NodeCapacity);
        Array.Clear(lastAllocated, 0, NodeCapacity);
        Array.Clear(lastSelfAllocated, 0, NodeCapacity);
        lastNodeCount = 1;
        Array.Clear(stack, 0, DepthCapacity);
        depth = 0;
        Array.Clear(openCount, 0, SectionCapacity);
        ownerThread = -1;
        tickAllocatedAtBegin = 0;
        LastTick = ulong.MaxValue;
        LastBrainAllocatedBytes = 0;
        Overflowed = 0;
        Unbalanced = 0;
    }
}
