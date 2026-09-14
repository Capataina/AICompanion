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
        var header = RunHeader.Now(commit, dirty, Git.Head(root), Option("--note") ?? "", Option("--filter") ?? "");
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
    case "--self-test":
    case "self-test":
        return SelfTestTheStore.Run() == 0 ? 0 : 1;
    case "error":
    {
        // An instrument that exits non-zero having written no red row is the one failure the rest
        // of this ledger cannot see: the scoreboard grades rows, and a process that crashed before
        // its first row, or failed in a path that files none, contributes silence that reads
        // exactly like a clean instrument. The caller is the shell, which knows the exit status and
        // nothing else, so the decision of whether that status is already accounted for is made
        // here against the rows rather than there against a grep.
        if (args.Length < 4) { Usage(); return 2; }
        Run? into = RunStore.Read(args[1]);
        if (into == null) { Console.Error.WriteLine($"ledger: {args[1]} is not a readable run file"); return 2; }
        string instrument = args[2];
        if (into.Rows.Any(r => string.Equals(r.Instrument, instrument, StringComparison.OrdinalIgnoreCase) && r.Verdict is "fail" or "error"))
        {
            // Its own rows already say what went wrong, and in more detail than an exit code can.
            return 0;
        }
        Environment.SetEnvironmentVariable(EmitLedgerRows.RunPathVariable, args[1]);
        EmitLedgerRows.Error(instrument, "Instrument", $"{instrument} ran to a non-zero exit",
            string.Join(' ', args[3..]));
        return 0;
    }
    case "reds":
    {
        // The red cases, one per line, for a rerun to iterate. It lives here rather than as a grep
        // in the shell because the run file is JSON and a shell that parses JSON with grep is one
        // clearer message away from selecting nothing and reporting that as no reds.
        //
        // The instrument is printed beside the case because a rerun has to know which project to
        // run: dispatching every red to one instrument reruns a case that instrument does not own,
        // which selects nothing there and so grades a red as unreproducible when it was never asked.
        if (args.Length < 2) { Usage(); return 2; }
        Run? run = RunStore.Read(args[1]);
        if (run == null) { Console.Error.WriteLine($"ledger: {args[1]} is not a readable run file"); return 2; }
        foreach ((string instrument, string @case) in run.Rows.Where(r => r.Verdict is "fail" or "error")
                     .Select(r => (r.Instrument, r.Case)).Distinct())
            Console.WriteLine($"{instrument}\t{@case}");
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
    // The run being scored is never its own baseline, and the exclusion goes into the walk rather
    // than onto its answer: nulling a self-match afterwards abandons the search at the first
    // ancestor instead of continuing past it.
    Run? baseline = RunStore.Baseline(root, from, excluding: after.Path, scoring: after);
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
      begin [--commit <hash>] [--dirty] [--note <text>]
            [--filter <case>]                             open a run file; prints its path
      scoreboard <run.jsonl> [--baseline <commit|file>]   score a run against its baseline
      compare <A> <B>                                     two commits or two run files
      baseline [<commit>]                                 nearest ancestor with a clean run
      reds <run.jsonl>                                    the red cases as instrument<TAB>case
      error <run.jsonl> <instrument> <message>            record a non-zero exit that wrote no row
      --self-test                                         the store's own rules, as ledger rows
      list [<commit>]                                     every run in the store
    """);
