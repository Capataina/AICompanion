#!/bin/sh
# Run one named case on its own, outside the whole harness, and say what it reported.
#
# This is the command behind every "does it still do that when nothing else is running" question,
# and it is the procedure that produced this repository's harness finding: `combat admissibility
# cost` measures 2.692 ms per score standalone against 6.143 ms inside the whole suite, which is a
# measurement rather than a suspicion only because the same case can be run both ways. Reaching it
# meant retyping, about thirty times, a line carrying three things a person does not remember:
# DYLD_LIBRARY_PATH into the Steam install, without which the native fixtures cannot load
# tModLoader's own libraries; -p:UseAppHost=false, for the SDK fault Directory.Build.rsp describes;
# and AIC_LEDGER_CASE, which is how --case reaches every instrument.
#
# Three things it does that the hand-typed line did not. It builds the instrument first and stops on
# the compiler's errors rather than running a stale assembly — the repository's oldest trap, a
# "Build succeeded" in two seconds over a DLL that never changed. It leaves the ledger store
# untouched: rows go to a temp file outside `Tools/Ledger/runs/`, which is the only directory
# `CompareRunsAndScore` scans for baselines, so nothing this script writes can be resolved as one
# however clean its header looks. And it refuses to be silent about a fragment that matched nothing,
# which is the failure this suite is most careful about everywhere else: an unmatched filter prints
# no rows, exits 0, and looks exactly like a case that passed.
#
# Those first two used to be in tension and the first version of this script lost to it. Opening no
# run file at all kept the store cleaner still, and removed the only channel a passing row travels
# on, because `EmitLedgerRows.Row` writes to the run file and returns without printing. Rows read
# off stdout therefore reported ledger, nav-replay and session-report cases that ran and passed as
# never having been asked. Reading the rows where the emitter already writes them gives an exact
# count per verdict and keeps the isolation, because isolation was always about *where* the file is
# rather than about whether one exists.
#
# What stays with the caller is which case to run and what its output means. Nothing here grades a
# number, and a red case is reported rather than interpreted.
#
# Usage, from anywhere:
#   sh Tools/run-case.sh "<case name fragment>"
#   sh Tools/run-case.sh "ore work"
#   sh Tools/run-case.sh "the hold audit reproduces" combat-audit
#   sh Tools/run-case.sh --no-build "combat is admitted"     skip the build, for a timing comparison
#   sh Tools/run-case.sh --full "ore work"                   every line the instrument printed
#   sh Tools/run-case.sh --help
#
# Matching is substring and case-insensitive, because the case names are whole sentences and nobody
# retypes one exactly. A fragment matching several cases runs all of them.
#
# One instrument does not honour the filter at all: `combat-audit` runs and prints all eight of its
# rows for any fragment, including one that matches nothing (measured 2026-09-21 with the fragment
# "zzz-nothing-matches-this", eight PASS lines, exit 0). The mechanism is in its own source rather
# than inferred from that: `Tools/CombatAudit/SelfTest.cs` files each row with
# `EmitLedgerRows.Pass` and `.Fail` directly, and only `EmitLedgerRows.Case` consults `Selected`, so
# there is nothing on that path for a filter to reach. So a combat-audit request here is the whole
# self-test, and the
# summary line below says how many rows actually reported rather than assuming the fragment cut
# anything. `verify.sh --case` and `--rerun-red` reach that instrument through the same variable and
# have the same behaviour; this is a property of the instrument and not of this script.
#
# Exit codes: 0 when nothing the instrument reported was a failure, 1 when something was, 2 when the
# case could not be asked at all — a fragment that matches nothing, an unknown instrument, or an
# instrument that would not compile.
#
# For the same case many times at one commit, with the interval that says what the result bounds,
# use Tools/measure-flake.sh instead; this script runs it once.

cd "$(dirname "$0")/.." || exit 2

# 2026-09-16, environment workaround, delete when the SDK is fixed: see Tools/verify.sh.
dotnet() {
  case "$1" in
    run|build)
      sub="$1"; shift
      command dotnet "$sub" -p:UseAppHost=false "$@"
      ;;
    *) command dotnet "$@" ;;
  esac
}

usage() {
  echo "usage: sh Tools/run-case.sh [--full] [--no-build] \"<case name fragment>\" [instrument]"
  echo
  echo "  Runs the cases whose names contain the fragment, in their own process, with the"
  echo "  environment the native fixtures need. Matching is substring, case-insensitive."
  echo "  Nothing is written to the ledger store."
  echo
  echo "  --full       print every line the instrument printed, not only its rows"
  echo "               (a failing run prints everything regardless: the reason is why you ran it)"
  echo "  --no-build   run the assembly already on disk; for a timing comparison, or after a build"
  echo
  echo "  instruments: engine-replay (default) · ledger · nav-replay · session-report · combat-audit"
  echo "  combat-audit ignores the fragment and runs its whole self-test; the others honour it."
  echo "  world-run is not one: its inputs are paths, and Tools/WorldRun/CLAUDE.md carries its command."
  echo
  echo "  exit 0 nothing failed · 1 something did · 2 the case could not be asked"
}

full=0
build=1
name=""
instrument="engine-replay"
positional=0
while [ $# -gt 0 ]; do
  case "$1" in
    --full) full=1; shift ;;
    --no-build) build=0; shift ;;
    -h|--help) usage; exit 0 ;;
    -*) echo "run-case: unknown option $1" >&2; usage >&2; exit 2 ;;
    *)
      positional=$((positional + 1))
      if [ $positional -eq 1 ]; then name="$1"; else instrument="$1"; fi
      shift ;;
  esac
done

if [ -z "$name" ]; then
  usage >&2
  exit 2
fi

# The instrument names are the ledger's own, so a red row read off a scoreboard is rerun by pasting
# the name beside the case without translating anything. The arguments differ because four of the
# five report through a self-test and EngineReplay is the suite itself. EngineReplay gets --perf
# because a case asked for by name is run whatever its tier; without it a perf-tier case would file a
# skip saying the tier was not due, which is the answer to a question nobody asked here.
case "$instrument" in
  engine-replay)  project="Tools/EngineReplay";  arguments="--perf" ;;
  ledger)         project="Tools/Ledger";        arguments="--self-test" ;;
  nav-replay)     project="Tools/NavReplay";     arguments="--self-test" ;;
  session-report) project="Tools/SessionReport"; arguments="--self-test" ;;
  combat-audit)   project="Tools/CombatAudit";   arguments="--self-test" ;;
  world-run)
    echo "run-case: world-run takes a route or a scenario rather than a case name" >&2
    echo "run-case: its command is in Tools/WorldRun/CLAUDE.md" >&2
    exit 2 ;;
  *)
    echo "run-case: no instrument named '$instrument'" >&2
    usage >&2
    exit 2 ;;
esac

if [ "$build" -eq 1 ]; then
  build_log=$(mktemp)
  if ! sh Tools/build.sh "$(basename "$project")" >"$build_log" 2>&1; then
    echo "run-case: $instrument does not compile, so the case was not asked" >&2
    cat "$build_log" >&2
    rm -f "$build_log"
    exit 2
  fi
  rm -f "$build_log"
fi

# The native fixtures load tModLoader's own dynamic libraries, and without this the ones that touch
# the engine die on a load failure rather than on anything about the case. The location is this
# machine's usual one and is overridable, the same way the world run names its inputs by
# environment: a checkout on another machine sets AIC_TMODLOADER_NATIVE rather than editing this.
native="${AIC_TMODLOADER_NATIVE:-$HOME/Library/Application Support/Steam/steamapps/common/tModLoader/Libraries/Native/OSX}"

out=$(mktemp)
ledger=$(mktemp)
# The run file is a temp path rather than an absent one, and that is a correction to how this script
# first shipped. Clearing AIC_LEDGER_RUN did keep the store clean, but it also removed the only
# channel a *passing* row can travel on: `EmitLedgerRows.Row` appends to the run file and returns
# without printing, and Pass, Fail, Skipped and Measure all route through it. Only Detail, the
# exception path and EngineReplay's own RunOneRow reach stdout at all, so on ledger, nav-replay and
# session-report a case that ran and passed printed no row-shaped line, and a wrapper counting rows
# off the terminal reported it as never having been asked. That is this repository's own
# silence-reads-wrong defect — the one check-navigation-boundary.sh shipped once — firing in the
# mirror direction, and a second pattern in the grep would not have closed it, because a row that is
# never printed cannot be matched by any pattern.
#
# Writing rows where the emitter already writes them keeps both properties at once. Isolation is
# preserved because CompareRunsAndScore resolves a baseline by scanning Tools/Ledger/runs/, so a
# path under the system temp directory is unreachable as one however clean its header looks, and the
# file is deleted below in any case. A caller who exported a real AIC_LEDGER_RUN is still refused,
# because this assignment replaces theirs rather than merely hoping none is set.
DYLD_LIBRARY_PATH="$native" AIC_LEDGER_RUN="$ledger" AIC_LEDGER_CASE="$name" \
  dotnet run --project "$project" --no-build $arguments >"$out" 2>&1
run_status=$?

# Counted from the rows themselves. A skipped row is a case the instrument declined by name, so the
# rows that *ran* are the total less those — which is what separates "your fragment matched nothing"
# from "everything matched and passed", a distinction the stdout stream could not carry.
rows=$(grep -c '"kind":"row"' "$ledger" || :)
skipped=$(grep -c '"verdict":"skipped"' "$ledger" || :)
failures=$(grep -c '"verdict":"fail"' "$ledger" || :)
ran=$((rows - skipped))

# A clean run is compressed to its rows, because everything else an instrument prints on the way is
# scene detail and the point of running one case is not to read it. A run that failed is not
# compressed at all, because the reason is the whole reason to have run it — and a fixture is under
# no obligation to put that reason on a line beginning with RED. The ledger's own forced red prints
# "AIC_LEDGER_FORCE_RED=always asked this case to fail, and it did", which every row pattern here
# misses, so filtering a failure would report exit 1 with nothing said about why.
if [ "$full" -eq 1 ] || [ "$failures" -gt 0 ] || [ "$run_status" -ne 0 ]; then
  cat "$out"
else
  # The stdout filter first, because an instrument that does narrate says it better than a row does.
  # Where it yields nothing the rows themselves are printed, so an instrument that files without
  # printing still shows its work instead of reporting a bare count.
  narrated=$(grep -E "^[[:space:]]*(GREEN|RED|PASS|FAIL|SKIP|row |measure )|failed:|Exception" "$out" || :)
  if [ -n "$narrated" ]; then
    echo "$narrated"
  else
    sed -n 's/.*"case":"\([^"]*\)".*"verdict":"\([^"]*\)".*/  \2	\1/p' "$ledger" | grep -v '^  skipped	' || :
  fi
fi
rm -f "$out" "$ledger"

# combat-audit files its rows through EmitLedgerRows.Pass and .Fail directly, where only
# EmitLedgerRows.Case consults the selection, so AIC_LEDGER_CASE reaches it and does nothing. Every
# row runs whatever fragment is passed. Saying so on every run is the honest handling available from
# a shell: the count below is its whole self-test rather than an answer about the named case, and a
# reader who is not told that reads a green line as being about the thing they asked for.
if [ "$instrument" = "combat-audit" ]; then
  echo "run-case: combat-audit ignores the case fragment, so the rows below are its whole self-test" >&2
fi

# Silence is the answer this script exists to refuse, in both directions. A fragment nobody has in
# their tree runs no case, which is exit 2 — the code this repository keeps for a question that was
# never asked — and it is distinguished from a clean pass by the rows the instrument filed rather
# than by what it happened to print.
if [ "$ran" -eq 0 ] && [ "$run_status" -eq 0 ]; then
  if [ "$instrument" = "combat-audit" ]; then
    echo "run-case: combat-audit filed no row at all, which means the instrument is broken rather than that combat is fine" >&2
  else
    echo "run-case: no case in $instrument matched \"$name\", so nothing was checked ($skipped declined by name)" >&2
  fi
  exit 2
fi

if [ "$failures" -gt 0 ] || [ "$run_status" -ne 0 ]; then
  if [ "$ran" -eq 0 ]; then
    # The instrument failed and filed nothing a reader can point at, which is the shape the ledger's
    # own `error` command exists for. Saying "0 rows" plainly is the whole of the report here: the
    # output above is all there is, and pretending to a count would be worse than admitting to none.
    echo "run-case: \"$name\" through $instrument — exit $run_status, no row filed; the output above is everything it said"
  else
    echo "run-case: \"$name\" through $instrument — $ran row(s), $failures reporting a failure, exit $run_status"
  fi
  exit 1
fi

echo "run-case: \"$name\" through $instrument — $ran row(s), nothing red"
exit 0
