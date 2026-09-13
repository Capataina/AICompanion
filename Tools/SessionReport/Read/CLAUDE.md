# Read — session parsing and chronology

The parser, stretch finder, God's Eye sidecar, attempt-identity join, chronicle and session description. Checks import these; they do not decide findings. These modules resolve raw telemetry into a queryable session structure and establish what the recording captured and in what order it arrived.

```
Read/
├─ Chronicle.cs             coalesces identical state into intervals, names movement inference (slope ascent/descent, stalls)
├─ DescribeGodsEyeEvents.cs reads the `.events.jsonl` sidecar for projectile and terrain contact records
├─ DescribeSession.cs       session metadata, schema witness, closure status, recording cost and loss distribution
├─ FindStretches.cs         segments rows into activities by `activity_id` and their termination reasons
├─ JoinAttemptEvidence.cs   reads attempt outcomes, grants and strikes by their process-wide identity counters
└─ ReadGodsEyeEvents.cs     keeps the last file read while path, write time and length are unchanged; streams events by line
```
