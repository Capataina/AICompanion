#!/bin/sh
# The whole check: build the mod without packaging it, prove the build produced a fresh DLL rather
# than a cached "succeeded", then run every instrument to the end and score the result.
#
# "To the end" is the change that matters here and it is worth stating plainly, because the old
# shape looked identical from outside. This script used to exit on the first instrument that
# failed, and EngineReplay used to sum thirty-eight fixtures whose assertions throw — so the first
# fixture to fail took the rest of the chain with it, and the run reported one exit code that could
# not tell twenty-two unrun fixtures from twenty-two passing ones. Now every instrument runs, every
# case reports its own row, and the ledger's scoreboard is the verdict.
#
# Usage, from the repository root:
#   sh Tools/verify.sh                          everything, scored against the baseline
#   sh Tools/verify.sh --case <name>            only cases whose name contains <name>
#   sh Tools/verify.sh --rerun-red 5            rerun each red case 5 times and grade it
#
# Exit codes: 0 when nothing is red, 1 when something is, 2 when a check could not be asked at all
# — which is neither a pass nor a failure, and is what an absent ripgrep produces.

cd "$(dirname "$0")/.." || exit 2

case_filter=""
rerun=0
while [ $# -gt 0 ]; do
  case "$1" in
    --case) case_filter="$2"; shift 2 ;;
    --rerun-red) rerun="$2"; shift 2 ;;
    *) echo "verify: unknown option $1" >&2; exit 2 ;;
  esac
done

dll="bin/Debug/net8.0/AICompanion.dll"

log=$(mktemp)
dotnet build -nologo -v q -p:BuildMod=false >"$log" 2>&1
build_status=$?

if [ $build_status -ne 0 ]; then
  echo "verify: build failed"
  grep -E "error|Build FAILED" "$log"
  rm -f "$log"
  exit 1
fi
rm -f "$log"

# BuildMod=false skips packaging on purpose (works with the game open or closed, never rewrites a
# .tmod underneath a playtest), so "Build succeeded" alone is not proof the DLL reflects the source
# on disk — the root CLAUDE.md's own trap: a no-op build reports success in under two seconds
# without writing anything. The mod project compiles every .cs file in the tree except Tools/ (see
# AICompanion.csproj's own Compile Remove) and any hidden folder, which the .NET SDK leaves out by
# default; agent worktrees under .claude/worktrees/ are full copies of the source, so a check that
# read them refused a fresh build whenever a seat edited a file there. The invariant checked is the
# one that matters: the DLL must be no older than the newest source file that feeds it.
#
# The build stays a hard precondition rather than a row, because every instrument below loads this
# assembly: a stale DLL does not make the suite red, it makes every verdict in it describe code
# that is not on disk.
if [ ! -f "$dll" ]; then
  echo "verify: build reported success but $dll does not exist"
  exit 1
fi
newest_source=$(find . -path ./bin -prune -o -path ./obj -prune -o -path ./Tools -prune -o -type d -path '*/.*' -prune -o -name "*.cs" -newer "$dll" -print 2>/dev/null | head -1)
if [ -n "$newest_source" ]; then
  echo "verify: build reported success but $dll is stale — $newest_source is newer than the DLL"
  exit 1
fi

run=$(dotnet run --project Tools/Ledger -- begin)
if [ -z "$run" ]; then
  echo "verify: could not open a ledger run, so nothing below would be recorded" >&2
  exit 2
fi
export AIC_LEDGER_RUN="$run"
[ -n "$case_filter" ] && export AIC_LEDGER_CASE="$case_filter"
echo "verify: recording to $run"

boundary_log=$(mktemp)
sh Tools/check-navigation-boundary.sh >"$boundary_log" 2>&1
boundary_status=$?
# Exit 2 from the boundary check means the question was not asked — its search tool is absent —
# which is neither a pass nor a violation. Naming it "broken" would send a reader hunting for
# movement code that names Terraria types when nothing searched for any, so it is recorded as a
# skip carrying its reason and the run carries on: one absent tool must not suppress the checks
# that can still run.
boundary_unchecked=0
if [ $boundary_status -eq 2 ]; then
  echo "verify: navigation boundary could not be checked"
  cat "$boundary_log"
  boundary_unchecked=1
elif [ $boundary_status -ne 0 ]; then
  echo "verify: navigation boundary broken"
  cat "$boundary_log"
fi
rm -f "$boundary_log"

# Instruments, each run to completion whatever the one before it did. The exit codes are collected
# and not acted on: the ledger's rows are the verdict, and an instrument's own exit code is now a
# summary of rows it already wrote.
for project in Tools/NavReplay Tools/SessionReport; do
  test_log=$(mktemp)
  dotnet run --project "$project" -- --self-test >"$test_log" 2>&1
  status=$?
  [ $status -ne 0 ] && cat "$test_log"
  tail -n 1 "$test_log"
  rm -f "$test_log"
done

engine_log=$(mktemp)
dotnet run --project Tools/EngineReplay >"$engine_log" 2>&1
cat "$engine_log"
rm -f "$engine_log"

# Rerunning a red is how one observation becomes a claim about a rate. A case that fails once and
# passes once at the same commit is flaky by observation rather than by suspicion, which is the
# only definition a ledger can supply — and the arithmetic for how many runs a claim needs is in
# the scoreboard's interval, not in a number chosen here.
if [ "$rerun" -gt 0 ]; then
  reds=$(dotnet run --project Tools/Ledger -- reds "$run")
  if [ -z "$reds" ]; then
    echo "verify: --rerun-red $rerun asked for, and nothing was red"
  else
    echo "$reds" | while IFS= read -r red; do
      [ -z "$red" ] && continue
      echo "verify: rerunning '$red' $rerun time(s)"
      i=1
      while [ "$i" -le "$rerun" ]; do
        # Each rerun is its own process. That is the isolation: a fixture that leaves a
        # process-wide static changed cannot reach the next attempt, so a case that passes alone
        # and fails in the suite is telling you about order rather than about itself.
        AIC_LEDGER_CASE="$red" dotnet run --project Tools/EngineReplay >/dev/null 2>&1
        i=$((i + 1))
      done
    done
  fi
fi

dotnet run --project Tools/Ledger -- scoreboard "$run"
verdict=$?

if [ $boundary_unchecked -eq 1 ]; then
  echo "verify: instruments ran and were scored — but the navigation boundary was NOT checked (install ripgrep: brew install ripgrep)"
  exit 2
fi
if [ $boundary_status -ne 0 ]; then
  echo "verify: the navigation boundary is broken, which no row above can express"
  exit 1
fi
exit $verdict
