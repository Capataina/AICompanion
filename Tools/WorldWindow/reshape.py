#!/usr/bin/env python3
"""Rewrite a telemetry plan dump with the tile shapes from the saved world.

A dump written before the mod knew about slopes draws every hammered or worldgen-smoothed
tile as a full block, so a staircase of slopes the player walked reads as a wall to the
replay tool. The saved world carries the real shape bits. For every block in a plans file
this keeps the header and the markers, and for every tile the dump drew as '#' that the
world says is a slope or a half block, writes the shape glyph the dump would write today
(\\ / < > _). A tile the dump drew solid and the world says is air was dug after the dump
and stays solid; a tile the dump drew as air is left alone whatever the world says now,
because the dump is the snapshot and the world is the end of the session.

Usage, from the repository root, with lihzahrd installed (python3 -m venv /tmp/wldenv &&
/tmp/wldenv/bin/pip install lihzahrd):

    /tmp/wldenv/bin/python Tools/WorldWindow/reshape.py <plans.txt> <world.wld> [out.txt]

Default output: Tools/Scenarios/<plans basename>-shaped.txt, which is committed: the
scenarios are the growing database of places the companion must be able to go. Exit code 0 on success;
prints how many tiles changed per block, so a zero everywhere means the world was not the
one the run was played in.
"""

import os
import re
import sys

import lihzahrd
from lihzahrd.tiles.shape import Shape

# lihzahrd names the cut corner; the game numbers the solid triangle. Both orders agree:
# game slope 1 is solid lower-left (cut top-right), and so on round.
GLYPH = {
    Shape.HALF_TILE: "_",
    Shape.TOP_RIGHT_SLOPE: "\\",
    Shape.TOP_LEFT_SLOPE: "/",
    Shape.BOTTOM_RIGHT_SLOPE: "<",
    Shape.BOTTOM_LEFT_SLOPE: ">",
}

HEADER = re.compile(r"window x (\d+)\.\.(\d+) y (\d+)\.\.(\d+)")
EXTRA = ("companion ", "player ", "threat ")


def blocks(lines):
    block = []
    for line in lines:
        if line.strip() == "":
            if block:
                yield block
                block = []
            continue
        block.append(line.rstrip("\n"))
    if block:
        yield block


def reshape(block, world):
    header = block[0]
    m = HEADER.search(header)
    if not m:
        return block, 0
    x0, _, y0, _ = (int(v) for v in m.groups())
    out = [header]
    changed = 0
    row = 0
    for line in block[1:]:
        if line.startswith(EXTRA):
            out.append(line)
            continue
        chars = list(line)
        for col, c in enumerate(chars):
            if c != "#":
                continue
            x, y = x0 + col, y0 + row
            if not (0 <= x < world.size.x and 0 <= y < world.size.y):
                continue
            tile = world.tiles[x, y]
            block_ = tile.block
            if block_ is None or block_.is_active is False:
                continue
            glyph = GLYPH.get(block_.shape)
            if glyph:
                chars[col] = glyph
                changed += 1
        out.append("".join(chars))
        row += 1
    return out, changed


def main(argv):
    if len(argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2
    plans, wld = argv[1], argv[2]
    out_path = argv[3] if len(argv) > 3 else os.path.join(
        "Tools", "Scenarios", os.path.basename(plans).replace(".txt", "") + "-shaped.txt")
    world = lihzahrd.World.create_from_file(wld)
    with open(plans, encoding="utf-8") as f:
        lines = f.readlines()
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    total = 0
    with open(out_path, "w", encoding="utf-8") as f:
        for i, block in enumerate(blocks(lines)):
            shaped, changed = reshape(block, world)
            total += changed
            print(f"#{i} {block[0][:60]}: {changed} tiles reshaped")
            f.write("\n".join(shaped) + "\n\n")
    print(f"{total} tiles reshaped -> {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
