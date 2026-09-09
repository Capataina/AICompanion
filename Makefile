# Thin entry points over the three procedures this session reconstructed by hand, over and
# over, from prose in CLAUDE.md: verify the mod still compiles and holds its navigation
# boundary, decompile a game type to read the path a mechanic already uses, and run the
# committed scenario corpus through the real planner with no game running. Each target is a
# plain `sh` script under Tools/ and can be run directly without make; this file exists so a
# session can also type `make verify` rather than recall the script's name and path.
#
# Deliberately absent: a target that launches the game or packages a .tmod. Packaging rewrites
# Mods/AICompanion.tmod, which is only safe with the game closed and is his call to make, never
# a side effect of a routine check.

.PHONY: verify decompile corpus corpus-full

verify:
	sh Tools/verify.sh

# make decompile TYPE=Collision   (bare name defaults to Terraria.<TYPE>; a dotted name is used as-is)
decompile:
	@if [ -z "$(TYPE)" ]; then \
		echo "usage: make decompile TYPE=Collision   (or TYPE=Terraria.Collision)"; \
		exit 2; \
	fi
	sh Tools/decompile.sh "$(TYPE)"

corpus:
	sh Tools/corpus.sh

corpus-full:
	sh Tools/corpus.sh --full
