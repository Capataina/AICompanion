#!/bin/sh
# The whole check: build the mod and every tool once, prove the build produced a fresh DLL rather than a
# cached "succeeded", then run every instrument to the end and score the result.
#
# "To the end" is the property that matters and it is worth stating plainly, because the old shape looked
# identical from outside. This script used to exit on the first instrument that failed, and EngineReplay
# used to sum thirty-eight fixtures whose assertions throw — so the first fixture to fail took the rest of
# the chain with it, and the run reported one exit code that could not tell twenty-two unrun fixtures from
# twenty-two passing ones. Every instrument runs, every case reports its own row, and the ledger's
# scoreboard is the verdict.
#
# The run has three phases, and the order is what keeps a timing honest:
#
#   parallel   the four self-tests and the engine suite split into shards, all at once. Nothing timed runs
#              here, because a timing taken beside a busy shard measures the shard.
#   timed      every case tagged timed or perf-tier, alone on the machine, its rows marked "alone".
#   serial     the world runs, one at a time, because they keep the game's own millisecond allowances and a
#              busy machine cuts their searches differently.
#
# Usage, from the repository root:
#   sh Tools/verify.sh                          everything, scored against the baseline
#   sh Tools/verify.sh --case <name>            only cases whose name contains <name>
#   sh Tools/verify.sh --rerun-red 5            rerun each red case 5 times and grade it
#   sh Tools/verify.sh --perf | --no-perf       force the perf tier on or off (default: on when due)
#   AIC_VERIFY_SHARDS=N sh Tools/verify.sh      the number of engine-suite shards (default: performance cores)
#
# Exit codes: 0 when nothing is red, 1 when something is, 2 when a check could not be asked at all
# — which is neither a pass nor a failure, and is what an absent ripgrep produces.

cd "$(dirname "$0")/.." || exit 2

case_filter=""
rerun=0
perf_choice=""
while [ $# -gt 0 ]; do
  case "$1" in
    --case) case_filter="$2"; shift 2 ;;
    --rerun-red) rerun="$2"; shift 2 ;;
    --perf) perf_choice="perf"; shift ;;
    --no-perf) perf_choice="ordinary"; shift ;;
    *) echo "verify: unknown option $1" >&2; exit 2 ;;
  esac
done

# Everything is built once, here, and every instrument below is started from its built assembly. Until 24
# September 2026 each instrument was started with `dotnet run --project`, which re-evaluates the project and
# its references on every call even when nothing changed: measured, 3.5 s per ledger call and 11 s per tool
# that references the mod, over about thirteen calls a run. `Tools/build.sh` owns the build flags
# (BuildMod=false, and the apphost workaround Directory.Build.rsp describes).
if ! sh Tools/build.sh all; then
  echo "verify: build failed"
  exit 1
fi

dll="bin/Debug/net8.0/AICompanion.dll"

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

# The native libraries the engine-backed tools load; `Tools/run-case.sh` sets the same path.
DYLD_LIBRARY_PATH="$HOME/Library/Application Support/Steam/steamapps/common/tModLoader/Libraries/Native/OSX"
export DYLD_LIBRARY_PATH
tool() {
  name="$1"; shift
  command dotnet "Tools/$name/bin/Debug/net8.0/$name.dll" "$@"
}

# The perf tier runs when anything the mod is compiled from changed since the last perf run in this branch's
# history — the owner's rule, including his example of the mastery tree, which is not under Brain. The ledger
# owns the decision (`Tools/Ledger/DecideWhetherThePerfTierIsDue.cs`) because it reads the store.
if [ -n "$perf_choice" ]; then
  tier="$perf_choice"
  tier_reason="asked for with --$([ "$tier" = perf ] && echo perf || echo no-perf)"
elif [ -n "$case_filter" ]; then
  # A run narrowed to named cases runs those cases whatever their tier: a perf-tier case asked for by name
  # and answered with "perf tier not due" is the answer to a question nobody asked. A filtered run is never
  # a baseline, so its tier cannot mislead a later comparison.
  tier="perf"
  tier_reason="--case names the cases to run, whatever their tier"
else
  tier_reason=$(tool Ledger perf-due)
  # 0 is due and 1 is not due; anything else is the question failing (a crash, git unavailable), and a
  # tier that could not be ruled out runs rather than being skipped on an unanswered question.
  case $? in
    0) tier="perf" ;;
    1) tier="ordinary" ;;
    *) tier="perf"; tier_reason="the perf-tier question could not be answered, so the tier runs: $tier_reason" ;;
  esac
fi
echo "verify: perf tier $([ "$tier" = perf ] && echo runs || echo skipped) — $tier_reason"
perf_flag=""
[ "$tier" = perf ] && perf_flag="--perf"

if [ -n "$case_filter" ]; then
  run=$(tool Ledger begin --tier "$tier" --filter "$case_filter")
else
  run=$(tool Ledger begin --tier "$tier")
fi
if [ -z "$run" ]; then
  echo "verify: could not open a ledger run, so nothing below would be recorded" >&2
  exit 2
fi
export AIC_LEDGER_RUN="$run"
[ -n "$case_filter" ] && export AIC_LEDGER_CASE="$case_filter"
echo "verify: recording to $run"

# Each instrument invocation's wall clock, recorded into the run at the end as a measure, so where a verify's
# time goes is in the run file rather than in a terminal nobody kept.
timings=$(mktemp)
record_timing() { printf '%s\t%s\n' "$1" "$2" >>"$timings"; }

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

# An instrument's exit code is normally a summary of rows it already wrote, and the ledger's rows
# are the verdict. The exception is the one this has to handle: an instrument that fails *without*
# writing a red row — a crash before its first case, a failing path that files nothing —
# contributes silence, and silence is what a clean instrument contributes too. So a non-zero exit is
# handed to the ledger, which files an error row only if that instrument's own rows do not already
# account for it. Reconciling the two is the ledger's decision rather than this script's, because
# the shell knows the status and cannot read the rows.
record_exit() {
  if [ "$2" -ne 0 ]; then
    tool Ledger error "$run" "$1" "exited $2 without filing a red row of its own; see the printed output above"
  fi
}
# The same for one process of an instrument that runs as several (the engine shards and the timed lane):
# reconciled against that process's own part file rather than the whole run, because a red in shard 0 must
# not account for shard 2 crashing, and an exit of 70 (an escaped exception) is always recorded, since the
# process stopped with cases unrun whatever it had filed.
record_process_exit() {
  if [ "$3" -ne 0 ]; then
    tool Ledger error "$run" "$1" "the $2 process exited $3; see its printed output above" --rows "$4"
  fi
}

# --- parallel phase ---------------------------------------------------------------------------------
#
# The self-tests and the engine suite's ordinary cases run at once. Each process writes its rows to a part
# file of its own, merged into the run afterwards in a fixed order, because concurrent appends to one file
# can interleave. Each gets its own TMPDIR, because every fixture that writes a file writes under the temp
# directory (the recorder names its captures by the millisecond, so two shards opening one in the same
# millisecond would otherwise write one file).
#
# A case lands in exactly one shard (`VerifyEngineMotion.CaseSelection`), and a shard files no row for a case
# it did not take, so the merged run holds one row per case. Sharding changes which cases share a process;
# a case that is red in a shard and green in the serial suite is an order dependence the per-case reset does
# not cover, and belongs in `Tools/EngineReplay/ResetProcessState.cs`.
shards="${AIC_VERIFY_SHARDS:-$(sysctl -n hw.perflevel0.physicalcpu 2>/dev/null || echo 4)}"
parts=$(mktemp -d)
# An interrupted run leaves no part files or timing list behind; each lane removes its own TMPDIR.
trap 'rm -rf "$parts"; rm -f "$timings"' EXIT
trap 'exit 130' INT TERM
launch() {
  lane="$1"; shift
  (
    AIC_LEDGER_RUN="$parts/$lane.jsonl"; export AIC_LEDGER_RUN
    AIC_LEDGER_PROCESS="$lane"; export AIC_LEDGER_PROCESS
    TMPDIR=$(mktemp -d); export TMPDIR
    started=$(date +%s)
    "$@" >"$parts/$lane.log" 2>&1
    echo $? >"$parts/$lane.status"
    echo $(( $(date +%s) - started )) >"$parts/$lane.seconds"
    rm -rf "$TMPDIR"
  ) &
}
parallel_started=$(date +%s)
launch ledger tool Ledger --self-test
launch nav-replay tool NavReplay --self-test
launch session-report tool SessionReport --self-test
launch combat-audit tool CombatAudit --self-test
i=0
while [ "$i" -lt "$shards" ]; do
  launch "engine-replay-$i" tool EngineReplay "--shard=$i/$shards"
  i=$((i + 1))
done
wait
record_timing "parallel phase: four self-tests and $shards engine-suite shards" $(( $(date +%s) - parallel_started ))

for lane in ledger nav-replay session-report combat-audit $(i=0; while [ "$i" -lt "$shards" ]; do echo "engine-replay-$i"; i=$((i + 1)); done); do
  [ -f "$parts/$lane.jsonl" ] && cat "$parts/$lane.jsonl" >>"$run"
  status=$(cat "$parts/$lane.status" 2>/dev/null || echo 2)
  record_timing "$lane" "$(cat "$parts/$lane.seconds" 2>/dev/null || echo 0)"
  # A clean lane prints its last line; a failing one prints everything, because the reason is why you ran it.
  if [ "$status" -ne 0 ]; then cat "$parts/$lane.log"; else tail -n 1 "$parts/$lane.log"; fi
  case "$lane" in
    engine-replay-*) record_process_exit "engine-replay" "$lane" "$status" "$parts/$lane.jsonl" ;;
    *) record_exit "$lane" "$status" ;;
  esac
done

# --- timed lane -------------------------------------------------------------------------------------
#
# Every case tagged timed or perf-tier, alone on the machine. Its rows' mode ends in "alone", and the
# scoreboard compares a measure only against one taken the same way. The perf tier's heavy cases run here
# when the tier is due and are skipped by name otherwise.
#
# The lane marker is set inside a subshell and exported there. Written as a prefix assignment on the call
# (`AIC_LEDGER_LANE=alone tool …`) it leaked: `tool` is a shell function, and macOS's /bin/sh (bash 3.2 in
# POSIX mode) keeps a prefix assignment on a function call in the shell afterwards, so every world run and
# every wall-clock row after this point claimed to have run alone (found by the wave-1 review).
timed_started=$(date +%s)
(
  AIC_LEDGER_LANE=alone; export AIC_LEDGER_LANE
  AIC_LEDGER_PROCESS="timed-lane"; export AIC_LEDGER_PROCESS
  AIC_LEDGER_RUN="$parts/timed-lane.jsonl"; export AIC_LEDGER_RUN
  tool EngineReplay --timed-lane $perf_flag >"$parts/timed-lane.log" 2>&1
  echo $? >"$parts/timed-lane.status"
)
timed_status=$(cat "$parts/timed-lane.status" 2>/dev/null || echo 2)
record_timing "engine-replay timed lane" $(( $(date +%s) - timed_started ))
[ -f "$parts/timed-lane.jsonl" ] && cat "$parts/timed-lane.jsonl" >>"$run"
if [ "$timed_status" -ne 0 ]; then cat "$parts/timed-lane.log"; else tail -n 1 "$parts/timed-lane.log"; fi
record_process_exit "engine-replay" "timed lane" "$timed_status" "$parts/timed-lane.jsonl"
rm -rf "$parts"

# The corpus mirror is a row of NavReplay's self-test now: the reflection's exactness over the
# committed scenarios is checked there, and the walker's replay of the corpus — the thing the
# mirror relation used to run both ways — went with the walker. A scenario is played against the
# orb by the world run below, in the real world it was cut from.

# --- serial phase: the world runs -------------------------------------------------------------------
#
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

# One world run, timed, its output shown whole (the world runs print their own row lines) or filtered by an
# optional pattern, and its exit handed to the ledger.
world_run() {
  label="$1"; pattern="$2"; shift 2
  log=$(mktemp)
  started=$(date +%s)
  tool WorldRun "$@" >"$log" 2>&1
  status=$?
  record_timing "$label" $(( $(date +%s) - started ))
  if [ -n "$pattern" ]; then
    grep -E "$pattern" "$log"
    [ $status -ne 0 ] && cat "$log"
  else
    cat "$log"
  fi
  rm -f "$log"
  record_exit "world-run" "$status"
}

world_run "world run: recorded route" "" \
  --route="$world_run_route" --world="$world_run_world" \
  --from-tick="$world_run_from" --ticks="${AIC_WORLD_RUN_TICKS:-600}" \
  --suite="recorded route $(basename "$world_run_route" .tsv)@$world_run_from"

# The combat variant: the same instrument with a frozen zombie waiting at the player's recorded
# feet thirty steps ahead, grading combat winning, no silence while threatened, and rejoining
# after the instrument retires the zombie. The route is pinned rather than newest, because the
# zombie is placed relative to the opening and a discovered route would make the same case name
# mean a different ambush every playtest. A fresh clone has no capture and the instrument files
# its own skip, the same as the route above.
world_run_combat_route="${AIC_WORLD_RUN_COMBAT_ROUTE:-Telemetry/2026-09-15_08-30-31-684.tsv}"
world_run "world run: combat" "" \
  --route="$world_run_combat_route" --world="$world_run_world" \
  --from-tick=1 --ticks=600 --combat \
  --suite="combat $(basename "$world_run_combat_route" .tsv)@1"

# The play measures: the morning of 22 September 2026 reproduced headlessly. The same instrument
# again, with three things the two runs above do not have — the recording's own hostiles and drops
# placed at their recorded ticks, the companion's settings taken from the capture rather than from
# this process's defaults, and the game's own millisecond allowances left standing instead of
# lifted — because what the play showed was a brain being cut by its deadline in a world with
# something in it, and a run missing any of those three grades a different companion.
#
# The route is pinned rather than newest, for the combat variant's reason and one of its own. The
# rows are about a specific defect on a specific scene, so a discovered route would make the same
# case name mean a different morning; and the capture has to carry an events sidecar, which is what
# the hostiles and drops are read from. It plays the whole capture rather than a slice, and a slice
# is not an option rather than merely being weaker: the defect it was built for arrived fourteen
# seconds in, at tick 827 of 2,340, and both verdicts decline a window that holds under 180 ticks of
# their own denominator, which every slice of this capture does. One pass rather than two, which is
# what keeps it to about half a minute — the determinism row belongs to the runs above, under the
# lifted allowances where two passes are comparable at all.
world_run_play_route="${AIC_WORLD_RUN_PLAY_ROUTE:-Telemetry/2026-09-22_10-05-56-125.tsv}"
world_run "world run: play measures" "" \
  --route="$world_run_play_route" --world="$world_run_world" \
  --from-tick=1 --ticks=0 --play-measures \
  --suite="play measures $(basename "$world_run_play_route" .tsv)"

# The committed scenario checkpoints: the two windows from the last walker play, the statue ledge
# and the water pocket, played by the orb in the real world they were cut from with the player
# standing where he stood. The scenario files are in the repository, so only the world can be
# absent, and an absent world files the same skip the recorded route does. The suite name carries
# the scenario, so each window keeps its own rows across runs.
for scenario in Tools/Scenarios/extracted-2026-09-14_19-55-52-468-tick-7224.txt Tools/Scenarios/extracted-2026-09-14_20-00-40-039-tick-5300.txt; do
  world_run "world run: scenario $(basename "$scenario" .txt)" '^(PASS|FAIL|SKIP|SCENARIO) ' \
    --scenario="$scenario" --world="$world_run_world"
done

# The soak's short form: the whole brain run for two minutes of play behind a seeded bot player, in the
# same world, sampled once per decision. It is here because nothing else in this script runs the brain
# long enough to see a slow climb — every fixture is seconds and the longest recording this machine holds
# is six minutes — and the play of 22 September grew its frozen observation from 150 facts to 1,603 over
# thirty-three seconds. Two minutes is what this script can carry; the hour-long form is a command
# somebody runs on purpose before a package, and `Tools/WorldRun/CLAUDE.md` carries it. The seed is fixed
# so the same two minutes are compared from run to run; a machine with no .wld files its own skip.
soak_capture_dir=$(mktemp -d)
world_run "world run: soak seed 1" '^(PASS|FAIL|SKIP|SKIPPED|MEASURE|SOAK|CAST) ' \
  --soak --world="$world_run_world" --seed=1 --ticks=7200 --suite="soak seed 1" --record-to="$soak_capture_dir"

# A capture must reproduce its own decisions from its own recorded inputs, tick for tick: the actors, the
# decision clock's answers, the random streams and the terrain edits it recorded, played back. Two shapes,
# because each grades what the other cannot. The self-consistency run records ten seconds of the 22 September
# scene with its native hostiles and replays it in a fresh process, which grades the clock, the actors and the
# random streams; the soak's own capture, just written above, carries the bot's tile breaks and the
# companion's torches, which grades the terrain edits. A divergence names its first tick and the input that
# differed (Tools/WorldRun/CLAUDE.md). Together about 40 s.
world_run "world run: self-consistency" '^(PASS|FAIL|SKIP|SKIPPED|MEASURE|RECORDED|REPRODUCED|CHILD|REMOVED|KEPT) ' \
  --self-consistency --route="$world_run_play_route" --world="$world_run_world" \
  --from-tick=1 --ticks=600 \
  --suite="self-consistency $(basename "$world_run_play_route" .tsv)@1"
soak_capture=$(ls -1t "$soak_capture_dir"/ModSources/AICompanion/Telemetry/*.tsv 2>/dev/null | head -1)
world_run "world run: soak self-consistency" '^(PASS|FAIL|SKIP|SKIPPED|MEASURE|REPRODUCE|REPRODUCED) ' \
  --reproduce="${soak_capture:-$soak_capture_dir/no-soak-capture.tsv}" \
  --world="$world_run_world" --expect-every-tick --suite="self-consistency soak seed 1"
rm -rf "$soak_capture_dir"

# The perf tier's world runs: how the brain's cost grows with load, what a smaller decision allowance costs
# in behaviour, and the ratio of in-game to headless phase cost for the capture. All three file measures only
# and together take a few minutes, which is why they run only when the perf tier does. An ordinary run files
# none of their rows, and needs no skip rows either: the coverage rule already keeps an ordinary run and a perf
# run from being each other's baseline. `Tools/WorldRun/CLAUDE.md` owns what each measures and what it cannot.
if [ "$tier" = perf ]; then
  world_run "world run: load ladder seed 1" '^(MEASURE|SKIPPED|LADDER|RUNG) ' \
    --load-ladder --world="$world_run_world" --seed=1 --suite="load ladder seed 1"
  world_run "world run: budget curve" '^(MEASURE|SKIPPED|CURVE|ALLOWANCE) ' \
    --budget-curve --route="$world_run_play_route" --world="$world_run_world" --repeats=2 \
    --suite="budget curve $(basename "$world_run_play_route" .tsv)"
  world_run "world run: calibrate" '^(MEASURE|SKIPPED|CALIBRATE) ' \
    --calibrate="$world_run_play_route" --world="$world_run_world" \
    --suite="calibrate $(basename "$world_run_play_route" .tsv)"
fi

# Rerunning a red is how one observation becomes a claim about a rate. A case that fails once and
# passes once at the same commit is flaky by observation rather than by suspicion, which is the
# only definition a ledger can supply — and the arithmetic for how many runs a claim needs is in
# the scoreboard's interval, not in a number chosen here.
if [ "$rerun" -gt 0 ]; then
  reds=$(tool Ledger reds "$run")
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
        engine-replay) project="EngineReplay"; arguments="$perf_flag" ;;
        ledger) project="Ledger"; arguments="--self-test" ;;
        nav-replay) project="NavReplay"; arguments="--self-test" ;;
        session-report) project="SessionReport"; arguments="--self-test" ;;
        combat-audit) project="CombatAudit"; arguments="--self-test" ;;
        # The world run's inputs are paths with spaces in them on this machine, so they cannot
        # travel through the unquoted $arguments the other instruments use; the loop below quotes
        # them itself for this one instrument. A red scenario row is rerun through the recorded
        # route's command, which selects nothing and files a skip: rerun a scenario by hand with
        # --scenario=<file> --world=<wld> instead. The same holds for a red play-measures row —
        # rerun it by hand with --play-measures --ticks=0 and --suite unchanged.
        world-run) project="WorldRun"; arguments="" ;;
        *) echo "verify: '$red' is red under instrument '$instrument', which this script cannot rerun"; continue ;;
      esac
      echo "verify: rerunning '$red' $rerun time(s) through Tools/$project"
      i=1
      while [ "$i" -le "$rerun" ]; do
        # Each rerun is its own process. That is the isolation: a fixture that leaves a
        # process-wide static changed cannot reach the next attempt, so a case that passes alone
        # and fails in the suite is telling you about order rather than about itself.
        if [ "$instrument" = "world-run" ]; then
          AIC_LEDGER_CASE="$red" tool "$project" \
            --route="$world_run_route" --world="$world_run_world" \
            --from-tick="$world_run_from" --ticks="${AIC_WORLD_RUN_TICKS:-600}" \
            --suite="recorded route $(basename "$world_run_route" .tsv)@$world_run_from" >/dev/null 2>&1
        else
          AIC_LEDGER_CASE="$red" tool "$project" $arguments >/dev/null 2>&1
        fi
        i=$((i + 1))
      done
    done
  fi
fi

# The machine's benchmark again, so the run says how fast the machine was at both ends of it, then every
# invocation's wall clock as a measure.
tool Ledger benchmark "$run" end
while IFS="$(printf '\t')" read -r label seconds; do
  [ -n "$label" ] && tool Ledger timing "$run" "$label" "$seconds"
done <"$timings"
rm -f "$timings"

tool Ledger scoreboard "$run"
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
