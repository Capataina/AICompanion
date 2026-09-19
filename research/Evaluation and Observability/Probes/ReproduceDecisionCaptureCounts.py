#!/usr/bin/env python3
"""Reproduce selected raw counts from the 18 September 2026 AICompanion captures.

This reads the named TSV and JSONL files and prints direct file counts.  It is
not a SessionReport measure and makes no claim about behaviour beyond the rows.
Run from the repository root, or pass --root /absolute/path/to/AICompanion.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from collections import Counter
from pathlib import Path
from statistics import median


CAPTURES = {
    "0.30.5": "2026-09-18_16-35-26-353",
    "0.30.6": "2026-09-18_18-57-09-481",
}
PURE_WINDOW = (9401, 10100)
SWAP_WINDOW = (7773, 11125)


def read_tsv(path: Path) -> list[dict[str, str]]:
    lines = [line for line in path.read_text(encoding="utf-8-sig").splitlines()
             if line and not line.startswith("#")]
    header = lines[0].split("\t")
    return [dict(zip(header, line.split("\t"))) for line in lines[1:]]


def combat_runs(rows: list[dict[str, str]]) -> list[int]:
    runs: list[int] = []
    start: int | None = None
    for row in rows:
        tick = int(row["tick"])
        if row["action"] == "combat":
            if start is None:
                start = tick
        elif start is not None:
            runs.append(tick - start)
            start = None
    if start is not None:
        runs.append(int(rows[-1]["tick"]) - start + 1)
    return runs


def percentile_floor_index(values: list[int], percentile: float) -> int:
    """Return the indexed p90 used in the original exploratory count."""
    ordered = sorted(values)
    return ordered[int(percentile * (len(ordered) - 1))]


def transitions(rows: list[dict[str, str]], lo: int, hi: int) -> Counter[tuple[str, str]]:
    window = [row for row in rows if lo <= int(row["tick"]) <= hi]
    return Counter((before["action"], after["action"])
                   for before, after in zip(window, window[1:])
                   if before["action"] != after["action"])


def attempt_causes(events_path: Path, lo: int, hi: int) -> Counter[tuple[str, str, str]]:
    counts: Counter[tuple[str, str, str]] = Counter()
    for line in events_path.open(encoding="utf-8"):
        event = json.loads(line)
        if event.get("kind") != "attempt-outcome" or not lo <= event.get("tick", -1) <= hi:
            continue
        label = event.get("label", "")
        if label not in {"collect", "place-torches"}:
            continue
        detail = event.get("detail", "")
        cause = next((part[6:] for part in detail.split(";") if part.startswith("cause=")), "")
        counts[(label, event.get("channel", ""), cause)] += 1
    return counts


def torch_credits(events_path: Path) -> Counter[tuple[str, str, str]]:
    counts: Counter[tuple[str, str, str]] = Counter()
    for line in events_path.open(encoding="utf-8"):
        event = json.loads(line)
        if event.get("kind") != "experience-credit" or event.get("label") != "torch":
            continue
        detail = event.get("detail", "")
        earner = next((part[7:] for part in detail.split(";") if part.startswith("earner=")), "")
        counts[(earner, event.get("channel", ""), event.get("related", ""))] += 1
    return counts


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="AICompanion repository root")
    args = parser.parse_args()
    telemetry = args.root / "Telemetry"
    loaded = {version: read_tsv(telemetry / f"{stem}.tsv") for version, stem in CAPTURES.items()}

    for version, rows in loaded.items():
        runs = combat_runs(rows)
        combat = [row for row in rows if row["action"] == "combat"]
        print(f"{version}: rows={len(rows)} ticks={rows[0]['tick']}..{rows[-1]['tick']} "
              f"combat_ticks={len(combat)} combat_runs={len(runs)} median_run={median(runs):.1f} "
              f"p90_run={percentile_floor_index(runs, .9)} max_run={max(runs)} "
              f"no_use_worth_firing={sum(row['fire'] == 'no-use-worth-firing' for row in combat)}")

    latest = loaded["0.30.6"]
    for stem in CAPTURES.values():
        for suffix in (".tsv", "-events.jsonl"):
            path = telemetry / f"{stem}{suffix}"
            print(f"source={path.name} sha256={hashlib.sha256(path.read_bytes()).hexdigest()}")
    fresh = sum(row["choice_fresh"] == "1" for row in latest)
    unique_choices = len({row["choice_id"] for row in latest})
    print(f"0.30.6 timing_freshness rows={len(latest)} fresh={fresh} unique_choices={unique_choices}")
    if fresh != len(latest) or unique_choices != len(latest):
        raise ValueError("This timing distribution requires one fresh distinct choice per row")
    for column in ("combat_prepare_ms", "brain_ms", "plan_ms"):
        values = sorted(float(row[column]) for row in latest)
        print(f"0.30.6 {column} max={values[-1]} p99_floor_index={values[int(.99 * (len(values) - 1))]}")
    print(f"0.30.6 action_totals={dict(Counter(row['action'] for row in latest))}")
    swap = transitions(latest, *SWAP_WINDOW)
    print("0.30.6 swap_window=" + ", ".join(
        f"{left}->{right}={swap[(left, right)]}" for left, right in
        (("collect", "place-torches"), ("place-torches", "collect"))))
    pure_rows = [row for row in latest if PURE_WINDOW[0] <= int(row["tick"]) <= PURE_WINDOW[1]]
    print(f"0.30.6 pure_window={PURE_WINDOW[0]}..{PURE_WINDOW[1]} rows={len(pure_rows)} "
          f"actions={dict(Counter(row['action'] for row in pure_rows))}")

    events = telemetry / f"{CAPTURES['0.30.6']}-events.jsonl"
    causes = attempt_causes(events, *PURE_WINDOW)
    print("0.30.6 pure_window_attempt_outcomes=" + ", ".join(
        f"{activity}/{status}/{cause}={count}"
        for (activity, status, cause), count in sorted(causes.items())))
    all_torch = attempt_causes(events, *SWAP_WINDOW)
    torch_attempts = sum(count for (activity, status, _), count in all_torch.items()
                         if activity == "place-torches" and status == "Attempted")
    print(f"0.30.6 swap_window_torch_attempted={torch_attempts}")
    print("0.30.6 torch_experience_credits=" + ", ".join(
        f"earner={earner}/channel={channel}/related={related}={count}"
        for (earner, channel, related), count in sorted(torch_credits(events).items())))


if __name__ == "__main__":
    main()
