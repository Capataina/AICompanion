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
# "Build succeeded" in two seconds over a DLL that never changed. It records nothing: no
# AIC_LEDGER_RUN is opened, so the ledger store gains no file and the run cannot be mistaken later
# for a scored one. And it refuses to be silent about a fragment that matched nothing, which is the
# failure this suite is most careful about everywhere else: an unmatched filter prints no rows,
# exits 0, and looks exactly like a case that passed.
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
# five report through a self-test and EngineReplay is the suite itself.
case "$instrument" in
  engine-replay)  project="Tools/EngineReplay";  arguments="" ;;
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
# AIC_LEDGER_RUN is cleared rather than merely left unset. The emitter writes a row only when that
# variable names a file, and empty reads as null, so this records nothing — but a caller who already
# exported one, or a shell left over from a verify run, would otherwise file one-case probe rows
# into a real run, and a run file from a probe is a clean, unfiltered-looking record that the next
# scoreboard would happily resolve as a baseline. Not inheriting is the property; unset by default
# is only the usual case.
DYLD_LIBRARY_PATH="$native" AIC_LEDGER_RUN= AIC_LEDGER_CASE="$name" \
  dotnet run --project "$project" --no-build $arguments >"$out" 2>&1
run_status=$?

# The leading-whitespace tolerance is not cosmetic: CombatAudit indents every row it prints by two
# spaces, and an anchored pattern read its whole self-test as nothing having run.
rows=$(grep -cE "^[[:space:]]*(GREEN|RED|PASS|FAIL|SKIP|row |measure )|failed:" "$out")
failures=$(grep -cE "^[[:space:]]*(RED|FAIL)|failed:" "$out")

# A clean run is compressed to its rows, because everything else an instrument prints on the way is
# scene detail and the point of running one case is not to read it. A run that failed is not
# compressed at all, because the reason is the whole reason to have run it — and a fixture is under
# no obligation to put that reason on a line beginning with RED. The ledger's own forced red prints
# "AIC_LEDGER_FORCE_RED=always asked this case to fail, and it did", which every row pattern here
# misses, so filtering a failure would report exit 1 with nothing said about why.
if [ "$full" -eq 1 ] || [ "$failures" -gt 0 ] || [ "$run_status" -ne 0 ]; then
  cat "$out"
else
  grep -E "^[[:space:]]*(GREEN|RED|PASS|FAIL|SKIP|row |measure )|failed:|Exception" "$out"
fi
rm -f "$out"

# Silence is the answer this script exists to refuse. A fragment nobody has in their tree selects no
# case, prints nothing and exits 0, which on a terminal is indistinguishable from a case that ran
# and passed — so it is exit 2, the code this repository keeps for a question that was never asked.
if [ "$rows" -eq 0 ] && [ "$run_status" -eq 0 ]; then
  echo "run-case: no case in $instrument matched \"$name\", so nothing was checked" >&2
  exit 2
fi

if [ "$failures" -gt 0 ] || [ "$run_status" -ne 0 ]; then
  if [ "$rows" -eq 0 ]; then
    # The instrument failed and filed nothing a reader can point at, which is the shape the ledger's
    # own `error` command exists for. Saying "0 rows" plainly is the whole of the report here: the
    # output above is all there is, and pretending to a count would be worse than admitting to none.
    echo "run-case: \"$name\" through $instrument — exit $run_status, no row printed; the output above is everything it said"
  else
    echo "run-case: \"$name\" through $instrument — $rows row(s), $failures reporting a failure, exit $run_status"
  fi
  exit 1
fi

echo "run-case: \"$name\" through $instrument — $rows row(s), nothing red"
exit 0
