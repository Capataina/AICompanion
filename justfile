# The uniform entry point. `just test` means the same thing in every repository
# on this machine, which is the whole point of this file — a run command
# re-derived per project is the cost the convention removes.
#
# The work still lives in Tools/*.sh, which carry the things nobody remembers:
# DYLD_LIBRARY_PATH into the Steam install, -p:UseAppHost=false for the SDK
# fault, AIC_LEDGER_CASE reaching every instrument. Those were not rewritten
# into just syntax — this file is the name, the scripts are the implementation,
# and each still runs directly with `sh Tools/<name>.sh` for a human who prefers
# it. The Makefile these recipes replace was deleted in the same change, because
# two runners in one repository is the ambiguity the convention removes.
#
# Deliberately absent, and it stays absent: anything that launches the game or
# packages a .tmod. Packaging rewrites Mods/AICompanion.tmod, which is only safe
# with the game closed and is his call, never a side effect of a routine check.

# Show what is available.
default:
    @just --list

# The compile-and-boundary check. This repository's suite is the EngineReplay
# and SessionReport verifications rather than a unit-test runner, so `test` is
# the gate those stand on: if this is red, nothing else is worth running.
test:
    @sh Tools/verify.sh

# The same, with the build's own output rather than only its verdict.
test-verbose:
    @sh Tools/build.sh

# The committed scenario corpus through the real planner, no game running.
cases:
    @sh Tools/corpus.sh

# Every scenario rather than the sampled set — slower, and the one to run before
# believing a planner change did no harm.
cases-full:
    @sh Tools/corpus.sh --full

# One named case alone, outside the whole harness. This is the command behind
# every "does it still do that when nothing else is running" question, and it
# refuses to be silent about a filter fragment that matched nothing.
case NAME:
    @sh Tools/run-case.sh "{{NAME}}"

# How often an intermittent case actually fires, over a batch sized to the
# question. A single green run proves nothing about a flake.
flake NAME:
    @sh Tools/measure-flake.sh "{{NAME}}"

# Read the path a game mechanic already uses. Bare name defaults to
# Terraria.<TYPE>; a dotted name is used as it stands.
decompile TYPE:
    @sh Tools/decompile.sh "{{TYPE}}"

# Everything a commit should pass, in the order that fails cheapest first.
verify:
    @sh Tools/verify.sh
    @sh Tools/corpus.sh
