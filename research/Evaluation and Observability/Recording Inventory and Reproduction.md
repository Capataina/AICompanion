# Recording inventory and read-only reproduction

**Inventory taken 12 September 2026.** These files are local ignored runtime artefacts, not committed research data. A clone of this repository therefore cannot reproduce the measurements without the identified inputs. The hashes below identify bytes, not a guarantee that a capture is complete or unbiased. Do not publish a raw recording merely to make an architectural claim reproducible; it can contain private world and play history.

The inventory covers every `.tsv` immediately in `Telemetry/`: 30 files, 28 with data. Metadata and row counts were inspected for the whole set. The detailed behavioural diagnosis covers the named windows in [Recorded Episodes and Measurement Limits](<Recorded Episodes and Measurement Limits.md>); a corpus inventory is not an assertion that all earlier episodes were analysed.

| File in `Telemetry/` | Samples | Columns | Bytes | SHA-256 |
|---|---:|---:|---:|---|
| 2026-09-08_13-07-15.tsv | 8669 | 60 | 2522961 | 4003758a4a0dd21e4b6994ad3c9d5c9a82f6c974157b037e2d2a888a0749683e |
| 2026-09-08_13-23-48.tsv | 8868 | 60 | 2640104 | 2c9f9618c1eb6c4fad4e47417c67a0cabaa78d169a432caaed8540c9e9487d5d |
| 2026-09-08_13-48-44.tsv | 9990 | 60 | 2911186 | e6387e527a43562b6315d325548a218e195084b158b5819ea2dbab98a8aa217c |
| 2026-09-08_16-04-10.tsv | 26956 | 62 | 8523025 | 58cbadd4161e54ef427b2b48841f26e47013c33455dfb3a13f03c74309901878 |
| 2026-09-08_17-48-28.tsv | 3733 | 62 | 1155763 | 1dc8c2e4114702b3918d17706f805fd762685a65349dd62baf2ac8d2105ad9dd |
| 2026-09-08_18-42-24.tsv | 15468 | 70 | 5466057 | 812cad2d4f675da022d1e6c7176a5c19da43ed7d26a4d61c55d2b6af34bae5b9 |
| 2026-09-09_11-15-32.tsv | 11407 | 81 | 4572781 | 63fcc9d3746ce3dfb773309d44ea7af450787f907c2a3247b4e30c0094c9bd39 |
| 2026-09-09_12-29-48.tsv | 11041 | 83 | 4522539 | adee2cb71bf25919466703a86d70ba7d8a7d432e7b4bc18ce1b6b4aa7f328c7c |
| 2026-09-09_13-45-07.tsv | 2748 | 88 | 1139758 | 3e8d72d97413afe413cb5aeb152fc85c5149034b0d65b6281f0b77d067dd8d87 |
| 2026-09-09_13-46-13.tsv | 3403 | 88 | 1422737 | cafed9b3f20957b5a9193d03e931dce832d5a0b8a27265ffa5a6c01356813043 |
| 2026-09-09_14-40-59.tsv | 33573 | 90 | 15262332 | 1c363bdf878fbfed006dec3dc7dc7a274468c1b63389cc007a80d027d82cd946 |
| 2026-09-09_16-41-38.tsv | 22728 | 95 | 10624622 | 9642a51c9f048906ee8bbdbf28eb0ee7083846563544fce0ff738ceaec4dc257 |
| 2026-09-09_16-48-34.tsv | 19899 | 95 | 9614118 | fcfeca9f04e856083c85ba5c42f4cf5a00ac2650e6691b46ffac5faf142c459a |
| 2026-09-09_18-25-59.tsv | 8272 | 95 | 3921705 | 2b91f13f9c3dc9a4099551b2096d9fcf435f8c9cdbb8beb9dfdb311b283a8e98 |
| 2026-09-09_21-09-26.tsv | 39587 | 126 | 31293265 | a328de021b093db5c550e90867c191cba73fb340a4325382896a6f0fa96f3ff5 |
| 2026-09-09_23-38-22.tsv | 29217 | 126 | 23178971 | 619a5ce2715c823fc0624b8b86cefe7b3fdd0661a2a7b7ea79023452079bdfe2 |
| 2026-09-09_23-50-24.tsv | 16593 | 126 | 13081318 | 6c42bdfea3c0d6584699423c908fb3937dc868b6a4650c73ee0db8779ed0a352 |
| 2026-09-10_11-10-17.tsv | 13812 | 127 | 10786591 | 3cf5be38044da98dee07f00d8bf8b7afd18073898bb7191d559cd3749c6c9f09 |
| 2026-09-10_11-14-39.tsv | 0 | 0 | 3 | f1945cd6c19e56b3c1c78943ef5ec18116907a4ca1efc40a57d48ab1db7adfc5 |
| 2026-09-10_11-14-43.tsv | 6323 | 127 | 4968260 | 8393f13753ec1cbf76dea55a28d054abe608833ad0bd239ad72a5ac529dc4d46 |
| 2026-09-10_11-16-46.tsv | 0 | 0 | 3 | f1945cd6c19e56b3c1c78943ef5ec18116907a4ca1efc40a57d48ab1db7adfc5 |
| 2026-09-10_11-16-47.tsv | 5575 | 127 | 4333871 | 92e83cab055b5dec9ea2af730d19bf91cf0fc7514ca3aec9ba1ea38a97f552ec |
| 2026-09-10_12-15-01-512.tsv | 27032 | 148 | 26251708 | 1779717a0131def4196af22864a3529e5074dff581953b4f81f1b4c28694db2e |
| 2026-09-10_14-10-16-120.tsv | 13386 | 152 | 12561251 | a2f79f2cbcfede0f4090f09d0cf2dca4c056d4e46b3c4528a0a6b0a11da2aea8 |
| 2026-09-10_14-17-18-420.tsv | 8391 | 152 | 8094892 | 211a187b69b0243461f412c5568f17c07b0ee6ebf5729ae9b3f4f0d629fc1720 |
| 2026-09-11_12-45-43-810.tsv | 13844 | 168 | 16360759 | 183d2572d8969002ea48f01a421d7bdb6fcb87ac1e6a10301196b178e1db9e3d |
| 2026-09-11_16-53-19-585.tsv | 439 | 168 | 459721 | 5ae11a15751d161280b1bc50b47b820a5d98fc7c050531d1b267ae8ab08f1f9d |
| 2026-09-11_16-53-41-137.tsv | 31722 | 168 | 38702212 | 56f1419eabffaf8aab74fd5894ab25abfe858d120ac451e2011eaa7a60f25ae9 |
| 2026-09-11_17-22-50-710.tsv | 26716 | 168 | 34116098 | e2dcb6dc3102da3432f139cae40134cc00759acc47c141d66d898e544ef8d722 |
| 2026-09-11_18-30-04-871.tsv | 38475 | 168 | 48737073 | 889d85290f954e885827959e597d8cd1bbc21de2fcc17c72d7f73a8bd6988df2 |

## Recompute the inventory without running the game or building tools

Run from the repository root. This reads raw files only. Success is one JSON record per recording, with the counts and hashes above. Zero samples in the two three-byte files means no observations were captured, not a successful empty session.

```sh
python3 - <<'PY'
import csv, hashlib, json
from pathlib import Path
for p in sorted(Path('Telemetry').glob('*.tsv')):
    with p.open(encoding='utf-8-sig') as f:
        reader = csv.DictReader((line for line in f if not line.startswith('#')), delimiter='\t')
        count = sum(1 for _ in reader)
        columns = len(reader.fieldnames or [])
    print(json.dumps(dict(file=p.name, samples=count, columns=columns,
                         bytes=p.stat().st_size,
                         sha256=hashlib.sha256(p.read_bytes()).hexdigest())))
PY
```

## Recompute sampled action runs and event populations

A run starts at the first sample or when its label differs from the preceding sample. Transitions equal runs minus one for a nonempty file. Neither quantity counts chooser calls or job completions.

```sh
python3 - <<'PY'
import csv, json
from collections import Counter
from pathlib import Path
for p in sorted(Path('Telemetry').glob('2026-09-11_*.tsv')):
    with p.open(encoding='utf-8-sig') as f:
        rows = list(csv.DictReader((x for x in f if not x.startswith('#')), delimiter='\t'))
    labels = [r['action'] for r in rows]
    runs = sum(i == 0 or x != labels[i-1] for i, x in enumerate(labels))
    print(p.name, 'samples', len(rows), 'runs', runs,
          'transitions', max(0, runs-1), 'mean', round(len(rows)/runs, 2) if runs else None)
p = Path('Telemetry/2026-09-11_18-30-04-871-events.jsonl')
with p.open(encoding='utf-8-sig') as f:
    print('event kinds', dict(Counter(json.loads(x)['kind'] for x in f)))
PY
```

## Recompute an episode before interpreting it

The following example is deliberately an observation query. It does not convert `mine_status`, `edge_outcome` or `action` into a causal judgement. Change the file and inclusive bounds to examine another named window.

```sh
python3 - <<'PY'
import csv
from collections import Counter
p = 'Telemetry/2026-09-11_18-30-04-871.tsv'
with open(p, encoding='utf-8-sig') as f:
    rows = list(csv.DictReader((x for x in f if not x.startswith('#')), delimiter='\t'))
window = [r for r in rows if 36082 <= int(r['tick']) <= 36423]
print('samples', len(window))
for field in ('action', 'control_source', 'nav_status', 'fire'):
    if field not in rows[0]:
        print(field, 'missing; inspect schema rather than substitute silently')
    else:
        print(field, dict(Counter(r[field] for r in window)))
print('schema', list(rows[0]))
print('first', window[0])
print('last', window[-1])
PY
```

## Reproduction must retain producer semantics

The TSV header records schema/build facts, but it does not identify an exact git tree and every loaded mod/configuration. Matching a mod version to one commit is unsafe when several commits share it. Future captures should embed code revision, dirty-tree digest, package hash, configuration/capability hash, game/tModLoader versions and enabled instrumentation. A performance comparison additionally needs machine/runtime identity and recording overhead.

`Main.GameUpdateCount`, the brain's senses counter, wall elapsed milliseconds and event sequence are different clocks. The senses counter pauses while the companion is downed. `target_evidence_age` uses its producer's clock; subtracting that evidence tick from the world tick manufactures apparent staleness. Join by explicit clock type and generation. Use wall time for user waiting and performance; use simulation ticks for physics; use event sequence for within-capture ordering only.

Source reproduction is separate from runtime reproduction. Use `git show d60b92b:path/to/file` for a cited source baseline, `git show --format=fuller <hash>` for a historical claim, and the linked primary paper or pinned public repository for an external mechanism. The project may evolve without invalidating what these dated inputs showed.
