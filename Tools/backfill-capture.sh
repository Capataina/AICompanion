#!/bin/sh
# Turn a playtest recording into a ledger run, stored under the revision that *wrote* it rather
# than under whatever happens to be checked out now.
#
# That distinction is the whole point and it is the mechanism by which history gets a trend at all.
# A capture's preamble carries the source revision of the build that produced it, so a recording
# made before the ledger existed still becomes a run at its own commit, and `ledger compare` between
# two captures is then a comparison between two builds rather than between two afternoons.
#
# Usage, from the repository root:
#   sh Tools/backfill-capture.sh Telemetry/<stamp>.tsv ["a note about this play"]
#
# It prints the run file it wrote. Exit 0 when the capture was read and its rows emitted, 2 when
# the capture or its revision could not be established — never 0 on a run that recorded nothing,
# because an empty run file would resolve as a clean baseline and silence every later comparison.

capture="$1"
note="$2"

if [ -z "$capture" ] || [ ! -f "$capture" ]; then
  echo "backfill: no capture at '$capture'" >&2
  exit 2
fi

# The revision the recorder stamped, and whether the tree it was built from was clean. A build from
# a dirty tree records the revision it started from and says dirty; that run is kept but never
# becomes anybody's baseline, because nothing identifies what it actually ran.
revision=$(grep -m1 '^# source_revision=' "$capture" | sed 's/^# source_revision=//' | cut -d';' -f1)
tree=$(grep -m1 '^# source_revision=' "$capture" | sed 's/.*tree=//' | cut -d';' -f1)

if [ -z "$revision" ] || [ "$revision" = "unknown" ]; then
  echo "backfill: $capture records no usable source revision (found '$revision'), so its rows cannot be stored under the build that wrote them" >&2
  exit 2
fi

short=$(printf '%s' "$revision" | cut -c1-7)
dirty=""
if [ "$tree" = "dirty" ]; then
  dirty="--dirty"
  echo "backfill: $capture was recorded by a build from a dirty tree; the run is stored and will never be used as a baseline"
fi

run=$(dotnet run --project Tools/Ledger -- begin --commit "$short" $dirty --note "${note:-backfilled from $capture}")
if [ -z "$run" ]; then
  echo "backfill: could not open a run file" >&2
  exit 2
fi

AIC_LEDGER_RUN="$run" dotnet run --project Tools/SessionReport -- --measures "$capture"
status=$?
if [ $status -ne 0 ]; then
  echo "backfill: reading $capture failed with exit $status" >&2
  exit 2
fi

rows=$(grep -c '"kind":"row"' "$run")
if [ "$rows" -eq 0 ]; then
  echo "backfill: $capture produced no rows, so the run records nothing and would resolve as a clean baseline; removing it" >&2
  rm -f "$run"
  exit 2
fi

echo "backfill: $rows row(s) from $capture stored at $short"
echo "$run"
