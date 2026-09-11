#!/bin/sh
# The navigation core names no Terraria type outside the one file that reads the live world,
# because the replay tool compiles the core without the game and because the motor is the only
# place a traversal's controls become NPC velocity. Exit 0 when the boundary holds; otherwise
# every offending line, and exit 1. Run from the repository root:  sh Tools/check-navigation-boundary.sh
cd "$(dirname "$0")/.." || exit 2
# Comment lines are prose and may say "NPC"; only code lines count.
test -d Companion/Brain/SharedMovementSystem || { echo 'movement source directory missing'; exit 1; }
# The search tool is checked for before it is used. A missing rg made the pipeline print nothing,
# and nothing is exactly what a held boundary prints, so an absent tool read as a green check on
# 2026-09-11. ripgrep stays the fast path, and POSIX grep carries the same search where it is
# absent, because an architecture check that depends on an optional tool is one that silently
# stops running on the machine that does not have it. The word boundary is spelled out rather
# than written \b, which BSD and GNU grep disagree about.
if command -v rg >/dev/null 2>&1; then
  found=$(rg -n 'using Terraria|Terraria\.|\bNPC\b|\bMain\.|CompanionMotor' Companion/Brain/SharedMovementSystem -g '*.cs' -g '!TerrariaIntegration/**')
elif command -v grep >/dev/null 2>&1; then
  found=$(grep -rnE 'using Terraria|Terraria\.|(^|[^A-Za-z0-9_])NPC([^A-Za-z0-9_]|$)|Main\.|CompanionMotor' --include='*.cs' Companion/Brain/SharedMovementSystem)
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
