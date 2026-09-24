#!/bin/sh
# What a change changed in the companion's behaviour: the same recorded scene played by two builds, their
# per-tick decisions compared, and their play measures scored against each other.
#
# Usage, from the repository root:
#   sh Tools/compare-commits.sh                      HEAD against the working tree (uncommitted changes)
#   sh Tools/compare-commits.sh <A>                  commit A against the working tree
#   sh Tools/compare-commits.sh <A> <B>              commit A against commit B
#   AIC_COMPARE_CAPTURE=<tsv> AIC_COMPARE_TICKS=<n>  the scene (default: the 22 September capture, 600 ticks)
#
# Two questions, answered two ways, because the two runs it uses keep different clocks:
#
#   decisions   the recorded-route world run with the millisecond allowances lifted, which is deterministic
#               (its own determinism row proves two passes agree), so every tick on which the two builds'
#               action or movement request differs is the code and not the machine. The first divergence is
#               printed with both builds' lines, because that tick is where to start reading.
#   measures    the play-measures world run under the game's own allowances, staged with the capture's own
#               hostiles and drops. Its measures are samples, so the two runs are scored by the ledger's own
#               compare, which prints each delta beside its noise band rather than calling it a change.
#
# A commit is built in a detached worktree beside the repository (it must sit in ModSources, because the
# project imports ../tModLoader.targets) and removed afterwards; the working tree is built in place. Nothing
# here writes to the repository's run store: each side's ledger run lands in its own worktree or a temp file.
#
# Exit codes: 0 when both sides ran, 1 when a side failed to build or run, 2 when the question could not be
# asked (an unknown commit, a missing capture or world).

cd "$(dirname "$0")/.." || exit 2
repo=$(pwd)

side_a="${1:-HEAD}"
side_b="${2:-}"
capture="${AIC_COMPARE_CAPTURE:-$repo/Telemetry/2026-09-22_10-05-56-125.tsv}"
ticks="${AIC_COMPARE_TICKS:-600}"
world="${AIC_WORLD_RUN_WORLD:-$(ls -1t "$HOME/Library/Application Support/Terraria/tModLoader/Worlds"/*.wld 2>/dev/null | head -1)}"
case "$capture" in /*) ;; *) capture="$repo/$capture" ;; esac

[ -f "$capture" ] || { echo "compare-commits: no capture at $capture (Telemetry/ is gitignored, so a clone has none)" >&2; exit 2; }
[ -f "$world" ] || { echo "compare-commits: no saved world found; set AIC_WORLD_RUN_WORLD" >&2; exit 2; }
git rev-parse --verify --quiet "$side_a^{commit}" >/dev/null || { echo "compare-commits: $side_a is not a commit" >&2; exit 2; }
[ -z "$side_b" ] || git rev-parse --verify --quiet "$side_b^{commit}" >/dev/null || { echo "compare-commits: $side_b is not a commit" >&2; exit 2; }

DYLD_LIBRARY_PATH="$HOME/Library/Application Support/Steam/steamapps/common/tModLoader/Libraries/Native/OSX"
export DYLD_LIBRARY_PATH
scratch=$(mktemp -d)
# One worktree path per line, read back whole: the repository lives under "Application Support", and a
# space-separated list split every path in two, so the first version removed nothing and left each
# worktree behind to collide with the next run's checkout at the same path.
: >"$scratch/worktrees"
cleanup() {
  while IFS= read -r tree; do
    [ -n "$tree" ] && git -C "$repo" worktree remove --force "$tree" >/dev/null 2>&1
  done <"$scratch/worktrees"
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# Build one side and play the scene with it. $1 is a label, $2 a commit or empty for the working tree.
play_side() {
  label="$1"; commit="$2"
  if [ -n "$commit" ]; then
    short=$(git rev-parse --short "$commit")
    tree="$repo/../AICompanion-compare-$short-$label"
    git -C "$repo" worktree add --detach --force "$tree" "$commit" >/dev/null 2>&1 || { echo "compare-commits: could not check out $commit" >&2; return 1; }
    echo "$tree" >>"$scratch/worktrees"
    name="$short"
  else
    tree="$repo"
    name="working tree"
  fi
  echo "compare-commits: building $label ($name)"
  ( cd "$tree" && sh Tools/build.sh world-run ledger ) >"$scratch/$label.build" 2>&1 || { cat "$scratch/$label.build"; return 1; }
  echo "$name" >"$scratch/$label.name"

  echo "compare-commits: $label plays $ticks ticks of $(basename "$capture") with the allowances lifted"
  ( cd "$tree" && command dotnet Tools/WorldRun/bin/Debug/net8.0/WorldRun.dll \
      --route="$capture" --world="$world" --from-tick=1 --ticks="$ticks" --print-trace \
      --suite="compare $label" ) >"$scratch/$label.route" 2>&1
  # The trace is printed as indented lines after its header; keep tick|action|request, the fields a decision
  # is made of, beside the position for the distance column.
  sed -n '/^TRACE /,$p' "$scratch/$label.route" | sed -n 's/^  //p' | grep -E '^[0-9]+\|' >"$scratch/$label.trace"
  [ -s "$scratch/$label.trace" ] || { echo "compare-commits: $label printed no trace"; tail -20 "$scratch/$label.route"; return 1; }

  echo "compare-commits: $label plays the whole capture under the game's allowances with its own scene"
  run_file=$( cd "$tree" && command dotnet Tools/Ledger/bin/Debug/net8.0/Ledger.dll begin --note "compare-commits $label" )
  ( cd "$tree" && AIC_LEDGER_RUN="$run_file" command dotnet Tools/WorldRun/bin/Debug/net8.0/WorldRun.dll \
      --route="$capture" --world="$world" --from-tick=1 --ticks=0 --play-measures \
      --suite="compare play measures" ) >"$scratch/$label.play" 2>&1
  cp "$run_file" "$scratch/$label.jsonl"
  [ "$tree" = "$repo" ] && rm -f "$run_file"
  return 0
}

play_side A "$side_a" || exit 1
play_side B "$side_b" || exit 1

echo
echo "decisions: $(cat "$scratch/A.name") against $(cat "$scratch/B.name"), $ticks ticks of $(basename "$capture"), allowances lifted"
# Field 2 is the action, 3 the movement request, 6 the position. A tick agrees when both builds took the same
# action with the same request; the distance column says how far apart the two bodies were.
paste -d '\n' "$scratch/A.trace" "$scratch/B.trace" | awk -F'|' '
  NR % 2 == 1 { a = $0; at = $1; aa = $2; ar = $3; split($6, ap, ","); next }
  {
    ticks++
    if (aa == $2 && ar == $3) same++
    else if (!first) { first = at; firstA = a; firstB = $0 }
    split($6, bp, ","); dx = ap[1] - bp[1]; dy = ap[2] - bp[2]; d = sqrt(dx * dx + dy * dy)
    if (d > worst) { worst = d; worstAt = at }
    # A change to how the body moves inside one decision (a steering tunable, the hover) leaves every
    # action and request equal and moves the body, so the first tick the bodies part is a divergence of
    # its own. Positions are whole pixels in the trace, so any difference is a real one.
    if (d > 0 && !parted) { parted = at; partedA = a; partedB = $0 }
  }
  END {
    if (ticks == 0) { print "  no ticks to compare"; exit }
    printf "  %d of %d ticks took the same action and request (%.1f%%)\n", same, ticks, 100 * same / ticks
    if (first) { print "  first decision divergence at tick " first ":"; print "    A  " firstA; print "    B  " firstB }
    else print "  the two builds made the same decision on every tick"
    if (parted) {
      printf "  the bodies first parted at tick %s and were furthest apart, %.1f px, at tick %s:\n", parted, worst, worstAt
      print "    A  " partedA; print "    B  " partedB
    }
    else print "  the two bodies were at the same position on every tick"
    if (!first && !parted) print "  no divergence"
  }'

echo
echo "measures: the play-measures run, B scored against A (each delta is one sample against one sample)"
# The scoreboard is printed whole: its section labels come from the ledger's own Label(), and a filter
# written against them here went silent the first time it ran.
[ -s "$scratch/A.jsonl" ] && [ -s "$scratch/B.jsonl" ] || { echo "  a side filed no play-measures run; its log:"; tail -20 "$scratch/A.play" "$scratch/B.play"; exit 1; }
command dotnet "$repo/Tools/Ledger/bin/Debug/net8.0/Ledger.dll" compare "$scratch/A.jsonl" "$scratch/B.jsonl" | sed 's/^/  /'
exit 0
