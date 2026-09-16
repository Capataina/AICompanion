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

# 2026-09-16, environment workaround, delete when the SDK is fixed: the .NET
# 10.0.401 SDK's apphost creation fails on this machine for every project (MSB4018,
# OverflowException in FileStatus.IsMemberOfGroup), so plain `dotnet run` dies after
# compiling. -p:UseAppHost=false makes run launch the dll via exec instead.
# Directory.Build.rsp already carries it for `dotnet build`, but run's launch
# decision only honours CLI-passed properties, hence this wrapper.
dotnet() {
  case "$1" in
    run|build)
      sub="$1"; shift
      command dotnet "$sub" -p:UseAppHost=false "$@"
      ;;
    *) command dotnet "$@" ;;
  esac
}

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

if [ -n "$case_filter" ]; then
  run=$(dotnet run --project Tools/Ledger -- begin --filter "$case_filter")
else
  run=$(dotnet run --project Tools/Ledger -- begin)
fi
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

# Instruments, each run to completion whatever the one before it did.
#
# An instrument's exit code is normally a summary of rows it already wrote, and the ledger's rows
# are the verdict. The exception is the one this loop has to handle: an instrument that fails
# *without* writing a red row — a crash before its first case, a build error inside its own project,
# a failing path that files nothing — contributes silence, and silence is what a clean instrument
# contributes too. So a non-zero exit is handed to the ledger, which files an error row only if that
# instrument's own rows do not already account for it. Reconciling the two is the ledger's decision
# rather than this script's, because the shell knows the status and cannot read the rows.
record_exit() {
  if [ "$2" -ne 0 ]; then
    dotnet run --project Tools/Ledger -- error "$run" "$1" "exited $2 without filing a red row of its own; see the printed output above"
  fi
}

for project in Tools/Ledger Tools/NavReplay Tools/SessionReport; do
  test_log=$(mktemp)
  dotnet run --project "$project" -- --self-test >"$test_log" 2>&1
  status=$?
  [ $status -ne 0 ] && cat "$test_log"
  tail -n 1 "$test_log"
  rm -f "$test_log"
  case "$project" in
    Tools/Ledger) record_exit "ledger" "$status" ;;
    Tools/NavReplay) record_exit "nav-replay" "$status" ;;
    Tools/SessionReport) record_exit "session-report" "$status" ;;
  esac
done

engine_log=$(mktemp)
dotnet run --project Tools/EngineReplay >"$engine_log" 2>&1
engine_status=$?
cat "$engine_log"
rm -f "$engine_log"
record_exit "engine-replay" "$engine_status"

# The corpus mirror is a row of NavReplay's self-test now: the reflection's exactness over the
# committed scenarios is checked there, and the walker's replay of the corpus — the thing the
# mirror relation used to run both ways — went with the walker. A scenario is played against the
# orb by the world run below, in the real world it was cut from.

# world-run — the whole brain and the native body in a real saved world, behind the player track a
# recording holds. Both of its inputs live outside the repository on purpose: Telemetry/ is
# gitignored and a .wld is never committed, so neither can be discovered from a clone. They are
# named by environment with this machine's usual locations as the default, and an absent one makes
# the instrument file a skip carrying its reason rather than fail — a fresh checkout has neither,
# and an error row there would disqualify every run in the store as a baseline.
#
# It plays a slice rather than a whole capture. A full 22,473-tick recording is about five minutes
# once the determinism row has run it twice, which is not a cost this script can carry; the whole
# capture is a command run on purpose, and Tools/WorldRun/CLAUDE.md carries it.
#
# The suite name carries the capture and the tick it starts from, because the route is discovered
# rather than fixed: the newest recording is a different route every playtest, and a fixed suite
# name would make the same case name mean a different journey from one run to the next, so every
# measure would read as "changed" when what changed was the input. Naming the input means a new
# capture opens new rows and "unchanged" keeps meaning what it says.
world_run_route="${AIC_WORLD_RUN_ROUTE:-$(ls -1t Telemetry/*.tsv 2>/dev/null | head -1)}"
world_run_world="${AIC_WORLD_RUN_WORLD:-$(ls -1t "$HOME/Library/Application Support/Terraria/tModLoader/Worlds"/*.wld 2>/dev/null | head -1)}"
world_run_from="${AIC_WORLD_RUN_FROM:-1}"
world_run_log=$(mktemp)
dotnet run --project Tools/WorldRun -- \
  --route="$world_run_route" --world="$world_run_world" \
  --from-tick="$world_run_from" --ticks="${AIC_WORLD_RUN_TICKS:-600}" \
  --suite="recorded route $(basename "$world_run_route" .tsv)@$world_run_from" >"$world_run_log" 2>&1
world_run_status=$?
cat "$world_run_log"
rm -f "$world_run_log"
record_exit "world-run" "$world_run_status"

# The committed scenario checkpoints: the two windows from the last walker play, the statue ledge
# and the water pocket, played by the orb in the real world they were cut from with the player
# standing where he stood. The scenario files are in the repository, so only the world can be
# absent, and an absent world files the same skip the recorded route does. The suite name carries
# the scenario, so each window keeps its own rows across runs.
for scenario in Tools/Scenarios/extracted-2026-09-14_19-55-52-468-tick-7224.txt Tools/Scenarios/extracted-2026-09-14_20-00-40-039-tick-5300.txt; do
  scenario_log=$(mktemp)
  dotnet run --project Tools/WorldRun -- --scenario="$scenario" --world="$world_run_world" >"$scenario_log" 2>&1
  scenario_status=$?
  grep -E '^(PASS|FAIL|SKIP|SCENARIO) ' "$scenario_log"
  [ $scenario_status -ne 0 ] && cat "$scenario_log"
  rm -f "$scenario_log"
  record_exit "world-run" "$scenario_status"
done

# Rerunning a red is how one observation becomes a claim about a rate. A case that fails once and
# passes once at the same commit is flaky by observation rather than by suspicion, which is the
# only definition a ledger can supply — and the arithmetic for how many runs a claim needs is in
# the scoreboard's interval, not in a number chosen here.
if [ "$rerun" -gt 0 ]; then
  reds=$(dotnet run --project Tools/Ledger -- reds "$run")
  if [ -z "$reds" ]; then
    echo "verify: --rerun-red $rerun asked for, and nothing was red"
  else
    echo "$reds" | while IFS="$(printf '\t')" read -r instrument red; do
      [ -z "$red" ] && continue
      # The red is rerun through the instrument that owns it. Sending every red to one project
      # reruns a name that project does not have, which selects nothing, files a skip, and grades a
      # real red as a case that could not be reproduced — the same silence this script spent the
      # rest of its length removing.
      case "$instrument" in
        engine-replay) project="Tools/EngineReplay"; arguments="" ;;
        ledger) project="Tools/Ledger"; arguments="--self-test" ;;
        nav-replay) project="Tools/NavReplay"; arguments="--self-test" ;;
        session-report) project="Tools/SessionReport"; arguments="--self-test" ;;
        # The world run's inputs are paths with spaces in them on this machine, so they cannot
        # travel through the unquoted $arguments the other instruments use; the loop below quotes
        # them itself for this one instrument. A red scenario row is rerun through the recorded
        # route's command, which selects nothing and files a skip: rerun a scenario by hand with
        # --scenario=<file> --world=<wld> instead.
        world-run) project="Tools/WorldRun"; arguments="" ;;
        *) echo "verify: '$red' is red under instrument '$instrument', which this script cannot rerun"; continue ;;
      esac
      echo "verify: rerunning '$red' $rerun time(s) through $project"
      i=1
      while [ "$i" -le "$rerun" ]; do
        # Each rerun is its own process. That is the isolation: a fixture that leaves a
        # process-wide static changed cannot reach the next attempt, so a case that passes alone
        # and fails in the suite is telling you about order rather than about itself.
        if [ "$instrument" = "world-run" ]; then
          AIC_LEDGER_CASE="$red" dotnet run --project "$project" -- \
            --route="$world_run_route" --world="$world_run_world" \
            --from-tick="$world_run_from" --ticks="${AIC_WORLD_RUN_TICKS:-600}" \
            --suite="recorded route $(basename "$world_run_route" .tsv)@$world_run_from" >/dev/null 2>&1
        else
          AIC_LEDGER_CASE="$red" dotnet run --project "$project" -- $arguments >/dev/null 2>&1
        fi
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
