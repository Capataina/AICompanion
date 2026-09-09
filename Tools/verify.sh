#!/bin/sh
# The compile-and-boundary check reconstructed by hand dozens of times a session: build the
# mod without packaging it, prove the build actually produced a fresh DLL rather than a cached
# "succeeded" (the root CLAUDE.md's own trap — a no-op build reports success in under two
# seconds), then run the navigation boundary check. Exit 0 only when all three hold; prints
# only the lines the next decision needs, never the whole build log.
#
# Usage, from the repository root:  sh Tools/verify.sh
# Success looks like:               "verify: build fresh, boundary holds"  and exit 0

cd "$(dirname "$0")/.." || exit 2

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

# BuildMod=false skips packaging on purpose (works with the game open or closed, never rewrites
# a .tmod underneath a playtest), so "Build succeeded" alone is not proof the DLL reflects the
# source that is actually on disk — the root CLAUDE.md's own trap: a no-op build reports
# success in under two seconds without writing anything. The mod project compiles every .cs
# file in the tree except Tools/ (see AICompanion.csproj's own Compile Remove), so the
# invariant checked here is the one that actually matters: the DLL must be no older than the
# newest source file that feeds it. A DLL older than the source it is meant to contain is
# stale regardless of what the build log claimed, whether that is a genuinely broken build or
# this script running with no real edit since the last one.
if [ ! -f "$dll" ]; then
  echo "verify: build reported success but $dll does not exist"
  exit 1
fi
newest_source=$(find . -path ./bin -prune -o -path ./obj -prune -o -path ./Tools -prune -o -name "*.cs" -newer "$dll" -print 2>/dev/null | head -1)
if [ -n "$newest_source" ]; then
  echo "verify: build reported success but $dll is stale — $newest_source is newer than the DLL"
  exit 1
fi

boundary_log=$(mktemp)
sh Tools/check-navigation-boundary.sh >"$boundary_log" 2>&1
boundary_status=$?
if [ $boundary_status -ne 0 ]; then
  echo "verify: navigation boundary broken"
  cat "$boundary_log"
  rm -f "$boundary_log"
  exit 1
fi
rm -f "$boundary_log"

echo "verify: build fresh, boundary holds"
exit 0
