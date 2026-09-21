#!/bin/sh
# Compile one named target and print only what stops it compiling.
#
# The procedure this replaces was reconstructed by hand roughly two hundred and fifty times in one
# session, in at least eight spellings, because each target carries flags that are not optional and
# are not guessable. The mod needs -p:BuildMod=false, which builds without packaging and is the only
# safe form while the game may be open; every Tools project needs -p:UseAppHost=false on this
# machine for the reason Directory.Build.rsp gives. Retyping either from memory is how a build gets
# run with packaging on underneath a playtest, and retyping the filter is how a real error scrolls
# past behind two hundred lines of restore chatter.
#
# What it compresses on the way out is the answer rather than the transcript: the compiler's error
# lines, deduplicated, one block per target, with the exit code carrying whether anything is broken.
# A target that compiles prints one word. The whole log is a flag away when the error is an MSBuild
# fault rather than a C# one and the surrounding lines are the evidence.
#
# What stays with the caller is which target to build. This script knows how each one is built and
# nothing about when it is worth building.
#
# Usage, from anywhere:
#   sh Tools/build.sh                     the mod, without packaging
#   sh Tools/build.sh engine-replay       one tool project
#   sh Tools/build.sh mod engine-replay   several, in the order given
#   sh Tools/build.sh all                 the mod and every tool project
#   sh Tools/build.sh --full mod          the compiler's whole output rather than only its errors
#   sh Tools/build.sh --help              what this does, and the target names on this tree
#
# Exit codes: 0 when every target compiled, 1 when one did not, 2 when the question could not be
# asked at all — an unknown target name, or a repository root this script cannot reach.

cd "$(dirname "$0")/.." || exit 2

# 2026-09-16, environment workaround, delete when the SDK is fixed: see Tools/verify.sh.
dotnet() {
  case "$1" in
    run|build)
      sub="$1"; shift
      command dotnet "$sub" -p:UseAppHost=false "$@"
      ;;
    *) command dotnet "$@" ;;
  esac
}

# The tool targets are discovered rather than listed, so a project added under Tools/ is buildable
# through this script on the day it is created. The slug is the folder name in kebab — EngineReplay
# is engine-replay — because that is what a person types, and the folder name itself is accepted too.
tool_slugs() {
  for project in Tools/*/*.csproj; do
    [ -e "$project" ] || continue
    folder=$(basename "$(dirname "$project")")
    printf '%s\n' "$folder" | sed 's/\([a-z0-9]\)\([A-Z]\)/\1-\2/g' | tr 'A-Z' 'a-z'
  done
}

# A slug back to its folder, or empty when nothing on the tree answers to that name.
tool_folder_for() {
  wanted=$(printf '%s\n' "$1" | tr 'A-Z' 'a-z')
  for project in Tools/*/*.csproj; do
    [ -e "$project" ] || continue
    folder=$(basename "$(dirname "$project")")
    slug=$(printf '%s\n' "$folder" | sed 's/\([a-z0-9]\)\([A-Z]\)/\1-\2/g' | tr 'A-Z' 'a-z')
    plain=$(printf '%s\n' "$folder" | tr 'A-Z' 'a-z')
    if [ "$wanted" = "$slug" ] || [ "$wanted" = "$plain" ]; then
      printf '%s\n' "$folder"
      return 0
    fi
  done
  return 1
}

usage() {
  echo "usage: sh Tools/build.sh [--full] [mod | all | <target>...]"
  echo
  echo "  Compiles a target and prints only its error lines, deduplicated."
  echo "  With no target it builds the mod without packaging, which is the form"
  echo "  that is safe to run while the game is open."
  echo
  echo "  --full   print the compiler's whole output instead of only its errors"
  echo
  echo "  targets on this tree:"
  echo "    mod"
  tool_slugs | sort | sed 's/^/    /'
  echo "    all"
  echo
  echo "  exit 0 every target compiled · 1 a target did not · 2 the question could not be asked"
}

full=0
targets=""
while [ $# -gt 0 ]; do
  case "$1" in
    --full) full=1; shift ;;
    -h|--help) usage; exit 0 ;;
    -*) echo "build: unknown option $1" >&2; usage >&2; exit 2 ;;
    all) targets="$targets mod $(tool_slugs | tr '\n' ' ')"; shift ;;
    *) targets="$targets $1"; shift ;;
  esac
done
[ -z "$targets" ] && targets="mod"

# Every requested target is resolved before any of them is built, so an unknown name costs nothing
# and is reported before a five-minute build rather than after it.
for target in $targets; do
  if [ "$target" != "mod" ] && ! tool_folder_for "$target" >/dev/null; then
    echo "build: no target named '$target'" >&2
    echo "build: known targets are mod, all, and $(tool_slugs | sort | tr '\n' ' ')" >&2
    exit 2
  fi
done

status=0
for target in $targets; do
  log=$(mktemp)
  if [ "$target" = "mod" ]; then
    # BuildMod=false skips packaging on purpose: it works with the game open or closed and never
    # rewrites a .tmod underneath a playtest. Packaging is a deliberate command run with the game
    # closed, never a side effect of checking whether the tree compiles.
    dotnet build -nologo -v q -p:BuildMod=false >"$log" 2>&1
  else
    folder=$(tool_folder_for "$target")
    dotnet build "Tools/$folder" -nologo -v q >"$log" 2>&1
  fi
  build_status=$?

  if [ "$full" -eq 1 ]; then
    printf '%-18s ---\n' "$target"
    cat "$log"
  else
    # Both halves of the pattern are wanted. `: error CS0103:` is the compiler; `error MSB4018` with
    # no path in front of it is MSBuild itself, and that is the one an errors-only filter written
    # for C# drops — which is exactly the failure mode the apphost fault arrived as on this machine.
    errors=$(grep -E "error [A-Z]+[0-9]+" "$log" | sort -u)
    if [ -z "$errors" ] && [ $build_status -eq 0 ]; then
      printf '%-18s clean\n' "$target"
    elif [ -z "$errors" ]; then
      # A non-zero build with no error line is the case a grep cannot describe, so the log is shown
      # rather than summarised: reporting "clean" here would be the silence that reads as health.
      printf '%-18s failed with no error line — whole output follows\n' "$target"
      cat "$log"
    else
      printf '%-18s %s error(s)\n' "$target" "$(printf '%s\n' "$errors" | wc -l | tr -d ' ')"
      printf '%s\n' "$errors" | sed 's/^/  /'
    fi
  fi

  [ $build_status -ne 0 ] && status=1
  rm -f "$log"
done

exit $status
