#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AICompanion.Tools.Ledger;

// The ledger's own surface. Everything here is either opening a run, reading runs back, or saying
// which past run a new one should be read against; nothing here runs an instrument, and nothing
// here parses an instrument's output, because instruments write rows and this reads rows.
//
//   dotnet run --project Tools/Ledger -- begin [--commit <hash>] [--note <text>]
//   dotnet run --project Tools/Ledger -- scoreboard <run.jsonl> [--baseline <commit|run.jsonl>]
//   dotnet run --project Tools/Ledger -- compare <A> <B>          commits or run files
//   dotnet run --project Tools/Ledger -- baseline [<commit>]
//   dotnet run --project Tools/Ledger -- list [<commit>]
//
// Exit codes: 0 when the scoreboard's verdict is clean, 1 when it is not, 2 when the question could
// not be asked at all — which follows verify.sh's own convention, where an unasked question is
// neither a pass nor a failure.

string root = Git.Root(Directory.GetCurrentDirectory());
if (args.Length == 0) { Usage(); return 2; }

switch (args[0])
{
    case "begin":
    {
        string commit = Option("--commit") ?? Git.Head(root);
        if (commit.Length == 0) { Console.Error.WriteLine("ledger begin: git could not name HEAD, and a run with no commit cannot be compared against anything"); return 2; }
        // A run's dirty flag is the tree's, except when the caller names the commit — a backfill
        // over a recorded capture describes the build that wrote it, and the checkout it happens to
        // run in says nothing about that build's tree.
        bool dirty = Option("--commit") == null ? Git.Dirty(root) : Flag("--dirty");
        var header = RunHeader.Now(commit, dirty, Git.Head(root), Option("--note") ?? "");
        Console.WriteLine(RunStore.Begin(root, header));
        return 0;
    }
    case "scoreboard":
    {
        if (args.Length < 2) { Usage(); return 2; }
        Run? after = RunStore.Read(args[1]);
        if (after == null) { Console.Error.WriteLine($"ledger: {args[1]} is not a readable run file"); return 2; }
        (Run? before, IReadOnlyList<Run> repeats) = ResolveBaseline(after);
        (string text, int exit) = Scoreboard.Render(after, before, repeats);
        Console.Write(text);
        return exit;
    }
    case "compare":
    {
        if (args.Length < 3) { Usage(); return 2; }
        Run? a = Resolve(args[1]), b = Resolve(args[2]);
        if (a == null) { Console.Error.WriteLine($"ledger: no run for {args[1]}"); return 2; }
        if (b == null) { Console.Error.WriteLine($"ledger: no run for {args[2]}"); return 2; }
        (string text, int exit) = Scoreboard.Render(b, a, RunStore.At(root, a.Header.Commit));
        Console.Write(text);
        return exit;
    }
    case "baseline":
    {
        string commit = args.Length > 1 ? args[1] : Git.Head(root);
        Run? baseline = RunStore.Baseline(root, commit);
        if (baseline == null)
        {
            Console.WriteLine($"no ancestor of {commit} has a clean run in the store");
            return 2;
        }
        Console.WriteLine($"{baseline.Header.Commit}  {baseline.Path}");
        return 0;
    }
    case "list":
    {
        var runs = args.Length > 1 ? RunStore.At(root, args[1]) : RunStore.All(root);
        foreach (Run run in runs)
            Console.WriteLine($"{run.Header.Commit}  {(run.Clean ? "clean" : "red  ")}  {run.Header.Timestamp}  {run.Rows.Count,5} rows  load {run.Header.Load,5:0.##}  {run.Name}");
        Console.WriteLine($"{runs.Count} run(s)");
        return 0;
    }
    default:
        Usage();
        return 2;
}

(Run? Baseline, IReadOnlyList<Run> Repeats) ResolveBaseline(Run after)
{
    if (Option("--baseline") is { } named)
    {
        Run? explicitBaseline = Resolve(named);
        return (explicitBaseline, explicitBaseline == null ? Array.Empty<Run>() : RunStore.At(root, explicitBaseline.Header.Commit));
    }
    // A dirty run has no commit of its own that means anything, so it is read against the last
    // clean run at the commit it was taken on rather than against an ancestor of a tree nobody
    // can reconstruct.
    string from = after.Header.Dirty && after.Header.RanAt.Length > 0 ? after.Header.RanAt : after.Header.Commit;
    Run? baseline = RunStore.Baseline(root, from);
    // The run being scored is never its own baseline.
    if (baseline != null && baseline.Path == after.Path) baseline = null;
    return (baseline, baseline == null ? Array.Empty<Run>() : RunStore.At(root, baseline.Header.Commit));
}

Run? Resolve(string reference)
{
    if (File.Exists(reference)) return RunStore.Read(reference);
    // A commit resolves to its newest run, which is what "compare these two commits" means when a
    // commit has been run more than once; the repeats stay available to the noise band separately.
    return RunStore.At(root, reference).FirstOrDefault();
}

string? Option(string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

bool Flag(string name) => Array.IndexOf(args, name) >= 0;

void Usage() => Console.Error.WriteLine(
    """
    usage: dotnet run --project Tools/Ledger -- <command>
      begin [--commit <hash>] [--dirty] [--note <text>]   open a run file; prints its path
      scoreboard <run.jsonl> [--baseline <commit|file>]   score a run against its baseline
      compare <A> <B>                                     two commits or two run files
      baseline [<commit>]                                 nearest ancestor with a clean run
      list [<commit>]                                     every run in the store
    """);
