# Tests — synthetic validation of readers and checks

The real parser and chronology reader are driven against synthetic records that exercise all the edge cases a real session could produce. `ChronicleTests.cs` builds these fixtures from declared timestamps, state transitions and occurrence payloads, then verifies they parse and coalesce correctly.

```
Tests/
├─ ChronicleTests.cs    synthetic fixtures for parser, state intervals, event ordering and check validation
└─ PlayMeasureTests.cs  every play measure against a real capture, with the number it must produce
```

**Both halves file a ledger row, and the reason is that they did not.** A red self-test used to print its failures, return 1, and contribute nothing the run file could hold, so `verify.sh` scored a run whose reader's own tests were failing and exited 0 — demonstrated by pointing `AIC_PLAY_CAPTURE` at a capture the pinned numbers cannot match, which produced "23 of 25 pinned numbers did not reproduce" and exit 0. The chronology half builds its own fixtures and is wrapped in `EmitLedgerRows.Case`; the measures half files its own row instead, because `Telemetry/` is gitignored and so "could not look" is the ordinary outcome in a fresh checkout, which `Case` can only turn into a pass. A pass row for a run that read no capture would make an unverified instrument indistinguishable from a verified one on every scoreboard afterwards, so the absent capture is a `skipped` row carrying the path it wanted.

A fixture is made from the schema version it needs to test. `TravelEvidence.First` is the gate that decides whether new checks fire on new captures (they skip until real data exists to grade them), and the fixtures derive their declared schema from that gate — the ones that should run read the current gate value, the one that should skip reads a minor below it. That ensures that as the schema advances, the tests still run the right tests: old checks still test against old data, new checks wait for new data, and the skip fixture keeps validating that new checks correctly skip old captures. Damaged captures, truncated rows, malformed identities and closed-capture metadata are all tested here so that the readers refuse to report Definitive for them and instead report the reduced coverage.

Run with `dotnet run --project Tools/SessionReport -- --self-test`.
