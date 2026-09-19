# These probes preserve what the course investigation actually checked

One instrument reads real captures; the others run small mathematical counterexamples. None is the production brain, a native gameplay fixture or a performance benchmark. The [rationale](<../../Decision Architecture/Retained Course Design Rationale.md>) records the investigation; the [implementation plan](<../../proposal/Implement the Retained Course Brain.md>) owns the selected design and future native gates.

```text
Probes/
├─ CLAUDE.md                             purpose, commands and evidence limits
├─ ReproduceDecisionCaptureCounts.py     raw capture counts and fresh component timings
├─ ChallengeDecisionRules.py             deterministic counterexamples and static routing
└─ CheckCourseObjectiveContracts.py      rejected endpoint loss and selected relative objective arithmetic
```

Run from the repository root:

```sh
python3 'research/Evaluation and Observability/Probes/ChallengeDecisionRules.py'
python3 'research/Evaluation and Observability/Probes/ReproduceDecisionCaptureCounts.py' --root .
python3 'research/Evaluation and Observability/Probes/CheckCourseObjectiveContracts.py'
```

The first exits zero after assertions and prints six counterexamples plus a narrow positive routing result. The second requires the named 18 September TSV/event files in gitignored `Telemetry/`; absence is an error. It handles the BOM/comment preamble and prints source hashes, event counts and timing freshness. It is an exploratory raw reader, not a SessionReport measure or schema-general analyser.

The routing model assumes exact costs, finite mandatory outputs, one-dimensional unit-speed travel and unit interaction time. Zero reversals establish only that model property. The other probe rewards and times are constructed examples, never proposed gameplay weights. Capture-shaped native tests remain separate.

The objective probe prints eight assertions: the rejected endpoint objective's completed/omitted tie, finite effect value, componentwise timing dominance, an explicit discounted-versus-linear timing trade-off, common-kernel rebasing, stack partition conservation, harm independence from unrelated jobs and event-harm versus one-tick delay units. Its fixed illustrative scale is an input to the arithmetic, not a gameplay constant. It does not implement the proposal's world-derived scale, observation census or native effect models.
