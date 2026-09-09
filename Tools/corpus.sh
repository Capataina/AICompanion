#!/bin/sh
# Runs the committed scenario corpus (Tools/Scenarios) through the real planner with no game
# running, and prints the verdict line rather than the whole per-scenario output. Reconstructed
# about eleven times this session, four of them the full cache/churn/follow suite typed out by
# hand.
#
# Usage, from the repository root:
#   sh Tools/corpus.sh            plain run — the everyday check after touching navigation
#   sh Tools/corpus.sh --full     plain, then --no-cache, --churn and --follow, cross-checked
#
# NavReplay itself already exits non-zero on any failed, wrongly-sealed, missing, skipped,
# stale-churn or failed-follow scenario (see its own summary line, printed here verbatim), so
# this wrapper's only added job in --full mode is the one thing NavReplay cannot check about
# itself: that a cold cache (--no-cache) reaches the same PASS/FAIL/SEALED verdict on every
# scenario as the warm one, since two invocations of the same binary is the only way to
# compare them.
#
# Success looks like each mode's own summary line, e.g.:
#   plain:    67/71 passed, 34 sealed (no route exists in the world), 0 skipped, 0 missing
#             inputs, planner NN ms in total
#   --full also prints "cache: verdicts match" (or names every scenario that diverged).

cd "$(dirname "$0")/.." || exit 2

overall=0
plain_log=$(mktemp)
nocache_log=$(mktemp)

dotnet run --project Tools/NavReplay -- Tools/Scenarios >"$plain_log" 2>&1
plain_status=$?
echo "plain: $(tail -1 "$plain_log")"
if [ $plain_status -ne 0 ]; then
  grep -E '^FAIL |^no such file:' "$plain_log"
  overall=1
fi

if [ "$1" != "--full" ]; then
  rm -f "$plain_log" "$nocache_log"
  exit $plain_status
fi

dotnet run --project Tools/NavReplay -- --no-cache Tools/Scenarios >"$nocache_log" 2>&1
nocache_status=$?
echo "no-cache: $(tail -1 "$nocache_log")"
if [ $nocache_status -ne 0 ]; then
  grep -E '^FAIL |^no such file:' "$nocache_log"
  overall=1
fi

# Cross-check: every PASS/FAIL/SEALED verdict line (name and header, timing stripped) must
# match between the warm-cache plain run and the cold --no-cache run. Compared via temp files
# rather than <(...), because sh here is dash and process substitution is a bash-only feature.
warm_verdicts=$(mktemp)
cold_verdicts=$(mktemp)
grep -E '^(PASS|FAIL|SEALED) ' "$plain_log" >"$warm_verdicts"
grep -E '^(PASS|FAIL|SEALED) ' "$nocache_log" >"$cold_verdicts"
if ! diff -q "$warm_verdicts" "$cold_verdicts" >/dev/null; then
  echo "cache: verdicts DIVERGE between warm and cold cache"
  diff "$warm_verdicts" "$cold_verdicts"
  overall=1
else
  echo "cache: verdicts match"
fi
rm -f "$plain_log" "$nocache_log" "$warm_verdicts" "$cold_verdicts"

churn_log=$(mktemp)
dotnet run --project Tools/NavReplay -- --churn Tools/Scenarios >"$churn_log" 2>&1
churn_status=$?
echo "churn: $(tail -1 "$churn_log")"
if [ $churn_status -ne 0 ]; then
  grep -E '^CHURN |^FAIL |^no such file:' "$churn_log"
  overall=1
fi
rm -f "$churn_log"

follow_log=$(mktemp)
dotnet run --project Tools/NavReplay -- --follow Tools/Scenarios >"$follow_log" 2>&1
follow_status=$?
echo "follow: $(tail -1 "$follow_log")"
if [ $follow_status -ne 0 ]; then
  grep -B1 -E 'follow:.*FAIL|^no such file:' "$follow_log"
  overall=1
fi
rm -f "$follow_log"

exit $overall
