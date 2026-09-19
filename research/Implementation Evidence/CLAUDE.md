# Implementation evidence is a dated account of the running contracts

Each document names the source revision or working-tree checkpoint its evidence describes. The original architecture survey uses research baseline `d60b92b`, whose gameplay source is unchanged from `4296f85`; subsequent course investigations name their own baselines. None of these claims automatically describes a later checkout. The durable design questions and conditional proposals refer here for their starting evidence.

```text
Implementation Evidence/
├─ CLAUDE.md                                      evidence boundary and reading order
├─ Decisions, Activities and Shared Controls.md   observation, selection, work and control ownership
├─ Routes, Returnability and Physical Execution.md navigation contracts and their limitations
├─ Player Motion and External Effects for Course Repair.md intent, native receipts and repair inputs at d315bf2
├─ Retained Course Core Counterexamples and Verification.md first implementation attacks, regression evidence and integration limits
└─ Trace Course Decisions Through Existing Instruments.md telemetry, replay and evidence limits at d315bf2
```

Read a producer before interpreting its consumers or recorded fields. Source establishes what a mechanism computes, a recording establishes what a captured run emitted, and an isolated intervention would establish whether changing that mechanism changes the symptom. These are different claims. Each suspected defect below states which claim has actually been established.
