# These probes preserve what the course investigation actually checked

One instrument reads real captures; the other runs small mathematical counterexamples. Neither is the production brain, a native gameplay fixture or a performance benchmark. [Proposal 05](<../../proposal/05 Retain a Course and Repair Its Future.md>) records results and implementation gates.

```text
Probes/
├─ CLAUDE.md                             purpose, commands and evidence limits
├─ ReproduceDecisionCaptureCounts.py     raw capture counts and fresh component timings
└─ ChallengeDecisionRules.py             deterministic counterexamples and static routing
```

Run from the repository root:

```sh
python3 'research/Evaluation and Observability/Probes/ChallengeDecisionRules.py'
python3 'research/Evaluation and Observability/Probes/ReproduceDecisionCaptureCounts.py' --root .
```

The first exits zero after assertions and prints six counterexamples plus a narrow positive routing result. The second requires the named 18 September TSV/event files in gitignored `Telemetry/`; absence is an error. It handles the BOM/comment preamble and prints source hashes, event counts and timing freshness. It is an exploratory raw reader, not a SessionReport measure or schema-general analyser.

The routing model assumes exact costs, finite mandatory outputs, one-dimensional unit-speed travel and unit interaction time. Zero reversals establish only that model property. The other probe rewards and times are constructed examples, never proposed gameplay weights. Capture-shaped native tests remain separate.
