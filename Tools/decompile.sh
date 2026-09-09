#!/bin/sh
# The standing rule is: before implementing any mechanic, read the decompiled game for the
# path that already does it. This wraps the four things that were re-derived from memory
# nineteen times this session — finding the Steam install, finding ilspycmd, building the
# type name, writing the output somewhere findable — into one call.
#
# Usage, from the repository root:
#   sh Tools/decompile.sh <Type>              e.g. Collision, or Terraria.Collision
# A bare type name (no dot) is assumed to live in the Terraria namespace, because that is
# where nearly every lookup this project makes actually is.
#
# Success looks like: the decompiled source printed on its own single stdout line as a path,
# e.g. "Tools/Decompiled/Terraria.Collision.cs", with that file containing real C# source.
# Failure names exactly what was searched for and not found, rather than a bare tool error.

cd "$(dirname "$0")/.." || exit 2

type_arg="$1"
if [ -z "$type_arg" ]; then
  echo "usage: sh Tools/decompile.sh <Type>   (e.g. Collision, or Terraria.Collision)" >&2
  exit 2
fi
case "$type_arg" in
  *.*) full_type="$type_arg" ;;
  *) full_type="Terraria.$type_arg" ;;
esac

# ilspycmd is a dotnet tool; it is installed but the shell that runs this script does not
# reliably have ~/.dotnet/tools on PATH (observed: `which ilspycmd` fails in a fresh bash
# even though `dotnet tool list -g` shows it installed there), so both are tried before
# failing loudly.
ilspycmd_bin=""
if command -v ilspycmd >/dev/null 2>&1; then
  ilspycmd_bin="ilspycmd"
elif [ -x "$HOME/.dotnet/tools/ilspycmd" ]; then
  ilspycmd_bin="$HOME/.dotnet/tools/ilspycmd"
else
  echo "decompile: ilspycmd not found on PATH or at $HOME/.dotnet/tools/ilspycmd — install with: dotnet tool install -g ilspycmd" >&2
  exit 1
fi

# The Steam install path is discovered rather than hard-coded, because a path present while
# this script was written is not a fact about every machine that will run it. Every Steam
# library folder is read from libraryfolders.vdf; the common default is checked first since
# it resolves without parsing anything on the machine this was built on.
dll=""
default_root="$HOME/Library/Application Support/Steam/steamapps/common/tModLoader"
if [ -f "$default_root/tModLoader.dll" ]; then
  dll="$default_root/tModLoader.dll"
else
  vdf="$HOME/Library/Application Support/Steam/steamapps/libraryfolders.vdf"
  if [ -f "$vdf" ]; then
    for lib in $(grep -oE '"path"[[:space:]]*"[^"]+"' "$vdf" | sed -E 's/.*"path"[[:space:]]*"([^"]+)"/\1/'); do
      candidate="$lib/steamapps/common/tModLoader/tModLoader.dll"
      if [ -f "$candidate" ]; then
        dll="$candidate"
        break
      fi
    done
  fi
fi
if [ -z "$dll" ]; then
  echo "decompile: tModLoader.dll not found. Looked at:" >&2
  echo "  $default_root/tModLoader.dll" >&2
  echo "  every Steam library in $HOME/Library/Application Support/Steam/steamapps/libraryfolders.vdf" >&2
  exit 1
fi

out_dir="Tools/Decompiled"
mkdir -p "$out_dir"
out_file="$out_dir/$full_type.cs"

if ! "$ilspycmd_bin" -t "$full_type" "$dll" >"$out_file" 2>"$out_file.err"; then
  echo "decompile: ilspycmd failed for $full_type against $dll" >&2
  cat "$out_file.err" >&2
  rm -f "$out_file" "$out_file.err"
  exit 1
fi
rm -f "$out_file.err"

if [ ! -s "$out_file" ]; then
  echo "decompile: $full_type produced no output — check the type name (case and namespace)" >&2
  rm -f "$out_file"
  exit 1
fi

echo "$out_file"
