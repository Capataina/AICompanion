#!/bin/sh
# The navigation core names no Terraria type outside the one file that reads the live world,
# because the replay tool compiles the core without the game and because the motor is the only
# place a traversal's controls become NPC velocity. Exit 0 when the boundary holds; otherwise
# every offending line, and exit 1. Run from the repository root:  sh Tools/check-navigation-boundary.sh
cd "$(dirname "$0")/.." || exit 2
# Comment lines are prose and may say "NPC"; only code lines count.
hits=$(grep -rn -E 'using Terraria|Terraria\.|\bNPC\b|\bMain\.|CompanionMotor' Brain/DecisionMatrix/Navigation --include='*.cs' | grep -v '/World/GameTileWorld.cs:' | grep -v -E '^[^:]*:[0-9]+:[[:space:]]*(///|//)')
if [ -n "$hits" ]; then
  echo "navigation names the game outside GameTileWorld.cs:"
  echo "$hits"
  exit 1
fi
echo "navigation boundary holds: only World/GameTileWorld.cs names a Terraria type"
