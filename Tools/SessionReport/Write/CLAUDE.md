# Write — reports for playtest inspection

These classes render a session's evidence as human-readable output. Both draw only what the record captured, making no inference beyond what the read and check layers provide.

```
Write/
├─ MultiRunReport.cs  compares runs by schema, source revision and configuration; prints shared and differing facts
└─ WritePlaytestHtml.cs  self-contained HTML with scrollable event timeline, tick navigation and per-kind filtering
```

The HTML scrubber plots one companion position per sample, `npc_px`, which is the orb's centre in whole pixels. It no longer reconstructs a centre from a left edge and a width, because the record carries neither and there is no second body to plot beside the first. A sample whose `npc_px` is absent or unparseable leaves the companion trail empty for that tick rather than drawing a body at the origin.

The HTML viewer is a bounded inspection surface: it holds enough context to diagnose one session, preserves event ordering and tick identity, and discloses every loss (cut rows, malformed entries, missing sidecars, interrupted capture). The multi-run report establishes whether runs can be compared and what differed between them; both kinds exit 0 when no definitive finding exists and 1 when one does, so they are checks and not only reports.
