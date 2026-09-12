# Implementation evidence is a dated account of the running contracts

This folder describes source and recordings at the research baseline `d60b92b`; gameplay source at that revision is unchanged from `4296f85`. Its claims do not automatically describe a later checkout. The durable design questions and conditional proposals refer here for their starting evidence.

```text
Implementation Evidence/
├─ CLAUDE.md                                      evidence boundary and reading order
├─ Decisions, Activities and Shared Controls.md   observation, selection, work and control ownership
└─ Routes, Returnability and Physical Execution.md navigation contracts and their limitations
```

Read a producer before interpreting its consumers or recorded fields. Source establishes what a mechanism computes, a recording establishes what a captured run emitted, and an isolated intervention would establish whether changing that mechanism changes the symptom. These are different claims. Each suspected defect below states which claim has actually been established.
