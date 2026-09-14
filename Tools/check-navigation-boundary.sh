#!/bin/sh
# Two architecture boundaries, both of which a compiler is happy to let anyone cross.
#
# The navigation core names no Terraria type outside the one file that reads the live world,
# because the replay tool compiles the core without the game and because the motor is the only
# place a traversal's controls become NPC velocity.
#
# And no activity or interaction asks whether a place is reachable by searching for a route to it.
# It reads the reach sense, which has flooded that answer once for every tile in the region, and a
# route search happens once afterwards for the single destination it chose, through the movement
# request like every other destination. The rule is here rather than only in prose because the cost
# of breaking it is invisible at the call site and enormous in play: a bounded search per candidate
# answers Unknown when it runs out of expansions, a caller that cannot remember an Unknown re-asks
# the same nearest candidates for ever, and the 2026-09-14 capture spent 79% of its lighting offers
# saying the question could not be finished while a settled flood sat beside it holding the answer.
# A job written later inherits the rule by failing this check rather than by reading a folder file.
#
# Exit 0 when both boundaries hold; otherwise every offending line, and exit 1.
# Run from the repository root:  sh Tools/check-navigation-boundary.sh
cd "$(dirname "$0")/.." || exit 2
# Comment lines are prose and may say "NPC"; only code lines count.
test -d Companion/Brain/Infrastructure/Movement || { echo 'movement source directory missing'; exit 1; }
# The search tool is checked for before it is used. A missing rg made the pipeline print nothing,
# and nothing is exactly what a held boundary prints, so an absent tool read as a green check on
# 2026-09-11. ripgrep stays the fast path, and POSIX grep carries the same search where it is
# absent, because an architecture check that depends on an optional tool is one that silently
# stops running on the machine that does not have it. The word boundary is spelled out rather
# than written \b, which BSD and GNU grep disagree about.
if command -v rg >/dev/null 2>&1; then
  found=$(rg -n 'using Terraria|Terraria\.|\bNPC\b|\bMain\.|CompanionMotor' Companion/Brain/Infrastructure/Movement -g '*.cs' -g '!TerrariaIntegration/**')
elif command -v grep >/dev/null 2>&1; then
  found=$(grep -rnE 'using Terraria|Terraria\.|(^|[^A-Za-z0-9_])NPC([^A-Za-z0-9_]|$)|Main\.|CompanionMotor' --include='*.cs' Companion/Brain/Infrastructure/Movement)
else
  echo 'movement boundary NOT checked: neither ripgrep nor grep is available'; exit 2
fi
hits=$(printf '%s\n' "$found" | grep -v '/TerrariaIntegration/' | grep -v -E '^[^:]*:[0-9]+:[[:space:]]*(///|//)')
if [ -n "$hits" ]; then
  echo "movement core names the game outside TerrariaIntegration:"
  echo "$hits"
  exit 1
fi
echo "movement boundary holds: only TerrariaIntegration names game types"

# The reach boundary. Observation owns the floods, Position and Movement are allowed to search
# because searching is what they are for, and the two folders below are the ones that must not.
#
# The pattern names the search entry points, never the verdict type: `Reachability.Reach` is the
# three-valued answer every one of these folders passes around and returns, so a pattern matching
# `Reachability\.` would fail every activity in the tree and the rule would be switched off within
# the week. Only a leading word boundary is spelled out, so `RoundTripEvidence` is caught with
# `RoundTrip`; the trailing one is left off deliberately for that reason. BSD and GNU grep disagree
# about \b, so it is written out, as above.
reach_dirs='Companion/Brain/Activities Companion/Brain/Infrastructure/Interactions'
reach_pattern='(^|[^A-Za-z0-9_])(WalkerReach|WalkerCanReach|WalkerProvenReach|RoundTrip|FlyerCanReach|ContinueRouteSearch|BreathEnvelope)|MovementQueries\.Region|AStar\.(Find|Region)|Reachability\.Verdict'
for dir in $reach_dirs; do
  test -d "$dir" || { echo "reach boundary source directory missing: $dir"; exit 1; }
done
if command -v rg >/dev/null 2>&1; then
  reach_found=$(rg -n "$reach_pattern" $reach_dirs -g '*.cs')
elif command -v grep >/dev/null 2>&1; then
  reach_found=$(grep -rnE "$reach_pattern" --include='*.cs' $reach_dirs)
else
  echo 'reach boundary NOT checked: neither ripgrep nor grep is available'; exit 2
fi
reach_hits=$(printf '%s\n' "$reach_found" | grep -v -E '^[^:]*:[0-9]+:[[:space:]]*(///|//)')
if [ -n "$reach_hits" ]; then
  echo "an activity or interaction runs its own route search instead of reading the reach sense:"
  echo "$reach_hits"
  echo "read Observation/ObserveReach.cs; the sense answers Reachable, NotYet or Unreachable for any feet tile."
  exit 1
fi
echo "reach boundary holds: activities and interactions read the reach sense, never a route search"
