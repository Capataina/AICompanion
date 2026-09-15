# Read — session parsing and chronology

The parser, stretch finder, God's Eye sidecar, attempt-identity join, chronicle and session description. Checks import these; they do not decide findings. These modules resolve raw telemetry into a queryable session structure and establish what the recording captured and in what order it arrived.

```
Read/
├─ Chronicle.cs             coalesces identical state into intervals and names the inference it earns
├─ DescribeGodsEyeEvents.cs reads the `-events.jsonl` sidecar for projectile and terrain contact records
├─ DescribeSession.cs       session metadata, schema witness, closure status, recording cost and loss distribution
├─ FindStretches.cs         runs of consecutive rows satisfying a predicate, with an optional gap allowance
├─ JoinAttemptEvidence.cs   reads attempt outcomes, grants and strikes by their process-wide identity counters
└─ ReadGodsEyeEvents.cs     keeps the last file read while path, write time and length are unchanged; streams events by line
```

## There is one body, and the chronicle reads one position for it

The companion's state comes from `npc_px`, the orb's centre in whole pixels, and `npc_vel` beside it. That is the whole of it: no second predicted body, no divergence reading to qualify, no support to change, and no left edge and width to reconstruct a centre from. A capture that predates this reads as unavailable rather than being narrated from the columns that happen to share a name, which is what `liquid` in the chronicle's required list is doing — it is the witness that this capture recorded the orb.

The player is still a walking body and keeps everything that implies. The slope ascent and descent inference is the player's alone, and it survives for that reason: he rests on a support the recorder names, and the inference is labelled as an inference rather than asserted. Nothing equivalent exists for the companion.

Two cell formats are worth naming because a reader built on the old ones stays silent rather than failing:

- **`control` reads `desired=x,y`.** A zero desire is a brake and not a neutral, so any non-zero component is an active movement request. A predicate looking for `move=`, `jump=1` or `fall=1` is false on every row of every capture this reader will see again.
- **`spot` is a destination tile and the body is a centre**, so a distance between them converts the tile to its own centre on both axes. The walker's conversion put the target on the tile's floor because its position was a pair of feet; mixing the two leaves this reader and the arrival check measuring from points eight pixels apart.

## What the sidecar no longer carries

A `movement-state` sample carries status, search stop, goal, route points, segment index, remaining steps, progress reason, stuck ticks, stuck strikes and attempt ending. It does not carry `last-rejection`, and the narration that unpacked one is deleted with the check that read it: a refusal was the walking body's macro proof turning a step down before its first tick, and the orb's search either returns a corridor of free space or does not.

A `stop` occurrence carries `against-wall-throughout`, `same-segment-throughout`, `replanned-during` and `fastest-px-per-tick`. The walker's `grounded-throughout`, `airborne-throughout`, `same-step-throughout`, `next-step-from-rest` and `fastest-sideways-px-per-tick` are all gone.

A control grant still embeds whole control strings, so `Field` takes the first occurrence of a key on purpose. The embedded string is `requested-controls=desired=1.00,-2.00` now rather than a semicolon-separated control list, which makes the shadowing hazard smaller rather than absent; the rule holds whatever shape the motor's controls take next.
