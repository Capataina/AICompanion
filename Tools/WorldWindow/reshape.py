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

    /tmp/wldenv/bin/python Tools/WorldWindow/reshape.py <plans.txt> <world.wld> [out.txt] [--pad N]

Default output: Tools/Scenarios/<plans basename>-shaped.txt, which is committed: the
scenarios are the growing database of places the companion must be able to go. Exit code 0 on success;
prints how many tiles changed per block, so a zero everywhere means the world was not the
one the run was played in.

--pad N grows every block's window by N tiles on each side and fills the new tiles from the
world, because the replay tool treats the window's edge as a wall and a window cut too close
to a pocket fails for the edge, not for the planner: the 13:48 run's dumps were padded six
tiles and the route the player took left them. A new tile is drawn the way the mod's own dump
draws it (# solid, = platform, the shape glyphs, ~ water or honey, L lava, . air), with the
game's own solid and solid-top tables deciding what a block is, so a torch or a vine is air.
The world is the end of the session and not the snapshot, so a padded tile can differ from
what the run saw; the header's "padded N" says how far that reaches.
"""

import os
import re
import sys

import lihzahrd
from lihzahrd.enums import LiquidType
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

# Terraria's Main.tileSolid and Main.tileSolidTop tables as the decompiled game sets them
# (Main.Initialize_TileAndNPCData), read off with
#   grep -oE 'tileSolid(Top)?\[[0-9]+\] = (true|false)' Main.cs
# and applied in order (270 and 78 ids on the 2026-09 decompile). lihzahrd's block ids are
# the game's tile ids. A tile id in neither is air to the body: torches, vines, chests,
# plants, stalactites, drips.
SOLID = {0, 1, 2, 6, 7, 8, 9, 10, 19, 22, 23, 25, 30, 37, 38, 39, 40, 41, 43, 44, 45, 46, 47, 48, 53, 54, 56, 57, 58, 59, 60, 63, 64, 65, 66, 67, 68, 70, 75, 76, 107, 108, 109, 111, 112, 116, 117, 118, 119, 120, 121, 122, 123, 127, 130, 137, 138, 140, 145, 146, 147, 148, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 166, 167, 168, 169, 170, 175, 176, 177, 179, 180, 181, 182, 183, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 202, 203, 204, 206, 208, 211, 221, 222, 223, 224, 225, 226, 229, 230, 232, 234, 235, 239, 248, 249, 250, 251, 252, 253, 272, 273, 274, 284, 311, 312, 313, 315, 321, 322, 325, 326, 327, 328, 329, 345, 346, 347, 348, 350, 357, 367, 368, 369, 370, 371, 379, 380, 381, 383, 384, 385, 387, 388, 396, 397, 398, 399, 400, 401, 402, 403, 404, 407, 408, 409, 415, 416, 417, 418, 421, 422, 426, 427, 430, 431, 432, 433, 434, 446, 447, 448, 458, 459, 460, 472, 473, 474, 476, 477, 478, 479, 481, 482, 483, 484, 492, 495, 496, 498, 500, 501, 502, 503, 507, 508, 512, 513, 514, 515, 516, 517, 534, 535, 536, 537, 539, 540, 541, 546, 557, 562, 563, 566, 618, 625, 626, 627, 628, 633, 635, 641, 659, 661, 662, 664, 666, 667, 668, 669, 670, 671, 672, 673, 674, 675, 676, 677, 678, 679, 680, 681, 682, 683, 684, 685, 686, 687, 688, 689, 690, 691, 692}
SOLID_TOP = {14, 16, 18, 19, 87, 88, 101, 114, 134, 239, 275, 276, 277, 278, 279, 280, 281, 285, 286, 296, 297, 298, 299, 309, 310, 339, 358, 359, 361, 362, 363, 364, 376, 380, 391, 392, 393, 394, 405, 413, 414, 427, 469, 532, 533, 538, 542, 544, 550, 551, 553, 554, 555, 556, 558, 559, 582, 599, 600, 601, 602, 603, 604, 605, 606, 607, 608, 609, 610, 611, 612, 619, 629, 632, 640, 643, 644, 645}

HEADER = re.compile(r"window x (\d+)\.\.(\d+) y (\d+)\.\.(\d+)")
EXTRA = ("companion ", "player ", "threat ", "trail ")


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


def world_glyph(world, x, y):
    """The dump character for a world tile: what the mod's own writer would draw today."""
    if not (0 <= x < world.size.x and 0 <= y < world.size.y):
        return "#"
    tile = world.tiles[x, y]
    block = tile.block
    if block is not None and block.is_active is not False:
        tid = int(block.type.value)
        if tid in SOLID_TOP:
            return "="
        if tid in SOLID:
            return GLYPH.get(block.shape, "#")
    liquid = tile.liquid
    if liquid is not None and liquid.volume > 0:
        return "L" if liquid.type == LiquidType.LAVA else "~"
    return "."


def reshape(block, world, pad):
    header = block[0]
    m = HEADER.search(header)
    if not m:
        return block, 0
    x0, x1, y0, y1 = (int(v) for v in m.groups())
    rows = [line for line in block[1:] if not line.startswith(EXTRA)]
    extras = [line for line in block[1:] if line.startswith(EXTRA)]

    # The snapshot's own tiles first: a '#' the world says is shaped becomes its glyph.
    changed = 0
    grid = {}
    for row, line in enumerate(rows):
        for col, c in enumerate(line):
            x, y = x0 + col, y0 + row
            if c == "#" and 0 <= x < world.size.x and 0 <= y < world.size.y:
                tile = world.tiles[x, y]
                block_ = tile.block
                if block_ is not None and block_.is_active is not False:
                    glyph = GLYPH.get(block_.shape)
                    if glyph:
                        c = glyph
                        changed += 1
            grid[x, y] = c

    # Then the padding, from the world alone.
    nx0, nx1, ny0, ny1 = x0 - pad, x1 + pad, y0 - pad, y1 + pad
    out_rows = []
    for y in range(ny0, ny1 + 1):
        out_rows.append("".join(grid.get((x, y)) or world_glyph(world, x, y) for x in range(nx0, nx1 + 1)))
    if pad:
        header = HEADER.sub(f"window x {nx0}..{nx1} y {ny0}..{ny1} padded {pad}", header)
    return [header] + extras + out_rows, changed


def main(argv):
    pad = 0
    if "--pad" in argv:
        i = argv.index("--pad")
        pad = int(argv[i + 1])
        argv = argv[:i] + argv[i + 2:]
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
            shaped, changed = reshape(block, world, pad)
            total += changed
            print(f"#{i} {block[0][:60]}: {changed} tiles reshaped")
            f.write("\n".join(shaped) + "\n\n")
    print(f"{total} tiles reshaped, padded {pad} -> {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
