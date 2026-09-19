# Sample uncertain futures when a calibrated model changes the decision

The [core plan](<../Implement the Retained Course Brain.md>) already carries uncertainty and repairs after random events. This module adds bounded stochastic branch comparison only when the decision depends on several possible outcomes that a supported model can distinguish. It is not a prerequisite for recovering from an ordinary missed shot.

**Activation evidence:** a calibrated outcome model exists, held-out traces show a recurring decision where nominal prediction or conservative bounds choose poorly, and a same-budget sampling experiment improves useful effects or risk on those traces. More rollouts through a wrong simulator do not qualify.

Implement a local sampler behind `Predict` or a search operator, using the same immutable snapshot, sparse effects and shared loss. Samples carry reproducible random state and explicit support limits. Retain current execution while samples accumulate; a statistical stopping rule reports unresolved when it cannot discriminate. The sampler never requests a private deadline or publishes a partially validated first action.

| Benefit | Cost and failure trigger | Acceptance |
|---|---|---|
| Compare genuinely different consequences of a risky shot or timing choice. | Simulation/variance cost and rare-event blindness; a small sample can miss the dangerous branch. | Calibration and held-out policy outcomes improve with uncertainty disclosed; rare-harm stress cases remain inside admission constraints. |
| Allocate refinement to a close consequential choice. | Sampling every routine pickup consumes time that could finish work. | An explicit value-of-information gate limits invocation; measure useful improvement per operation against core refinement. |

Record model revision, random state, sample count, represented outcomes, bounds, omitted support and stop reason. Tests include a rare harmful outcome, deliberately biased model, zero sample budget and a changed world halfway through sampling. Remove the sampler if model error dominates sampling error or if a deterministic local correction gives the same behavioural gain at lower cost. Never describe finite samples as proof that all future outcomes are safe.
