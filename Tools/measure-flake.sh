#!/bin/sh
# Run one case many times at one commit and report what the result actually bounds.
#
# It exists because "three of five runs were green" was the best this repository could say about its
# one known intermittent fixture, and that sentence is consistent with a true pass rate anywhere
# from 12 to 77 percent — so it cannot distinguish a fixture that fails one run in twenty from one
# that fails one in three. Every repeat lands in a single run file at one commit, which is what lets
# the scoreboard summarise the case as flaky by observation and print its Wilson interval.
#
# Usage, from the repository root:
#   sh Tools/measure-flake.sh <runs> "<case name fragment>"
#   sh Tools/measure-flake.sh 30 "ore work"
#
# Take it on an idle machine. Load is recorded in the run header, and the whole hypothesis about
# this suite's flake is that a wall-clock planning deadline decides how far a search gets, so a
# batch taken while other work builds measures the load rather than the fixture.

runs="$1"
name="$2"

if [ -z "$runs" ] || [ -z "$name" ]; then
  echo "usage: sh Tools/measure-flake.sh <runs> \"<case name fragment>\"" >&2
  exit 2
fi

cd "$(dirname "$0")/.." || exit 2

run=$(dotnet run --project Tools/Ledger -- begin --note "flake batch of $runs on '$name'")
if [ -z "$run" ]; then
  echo "measure-flake: could not open a ledger run" >&2
  exit 2
fi

echo "measure-flake: $runs run(s) of '$name' into $run"
i=1
while [ "$i" -le "$runs" ]; do
  # One process per repeat. Process isolation is the point rather than a convenience: a fixture
  # that leaves a process-wide static changed cannot reach the next attempt, so what this measures
  # is the case against the machine and not the case against its predecessors.
  AIC_LEDGER_RUN="$run" AIC_LEDGER_CASE="$name" dotnet run --project Tools/EngineReplay >/dev/null 2>&1
  printf '.'
  i=$((i + 1))
done
printf '\n'

dotnet run --project Tools/Ledger -- scoreboard "$run"
