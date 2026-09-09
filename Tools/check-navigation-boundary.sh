#!/bin/sh
# The navigation core names no Terraria type outside the one file that reads the live world,
# because the replay tool compiles the core without the game and because the motor is the only
# place a traversal's controls become NPC velocity. Exit 0 when the boundary holds; otherwise
# every offending line, and exit 1. Run from the repository root:  sh Tools/check-navigation-boundary.sh
cd "$(dirname "$0")/.." || exit 2
# Comment lines are prose and may say "NPC"; only code lines count.
test -d Companion/Brain/SharedMovementSystem || { echo 'movement source directory missing'; exit 1; }
hits=$(rg -n 'using Terraria|Terraria\.|\bNPC\b|\bMain\.|CompanionMotor' Companion/Brain/SharedMovementSystem -g '*.cs' -g '!TerrariaIntegration/**' | grep -v '/TerrariaIntegration/' | grep -v -E '^[^:]*:[0-9]+:[[:space:]]*(///|//)')
if [ -n "$hits" ]; then
  echo "movement core names the game outside TerrariaIntegration:"
  echo "$hits"
  exit 1
fi
echo "movement boundary holds: only TerrariaIntegration names game types"
