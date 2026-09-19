# Compare several motion models when one forecast repeatedly loses useful actions

This is an optional extension of `ObserveForecastErrors` and the tactical prediction contract in the [core plan](<../Implement the Retained Course Brain.md>). Ordinary jumping, dashing, teleports and external displacement already trigger core observation and repair. This extension becomes relevant when a single nominal forecast plus residuals is consistently too broad or confidently wrong for an enemy with several observable movement regimes.

**Activation evidence:** matched traces show that distinct histories predict distinct subsequent motion, the core repeatedly misses otherwise executable shots or makes unnecessary repositioning trips, and a held-out multi-model predictor reduces that error. An unobservable random teleport with no informative cue does not satisfy this gate; adding models cannot manufacture information.

Maintain a bounded set of supported movement hypotheses with likelihood/evidence status. Each consumes the same immutable snapshot and exports successor distributions or envelopes through the existing prediction interface. Observations update the hypotheses; the global course compares consequences once. No hypothesis chooses its own enemy, firing step or course.

| Benefit | Cost and failure trigger | Acceptance |
|---|---|---|
| A jumping and a grounded regime need not be averaged into an impossible middle trajectory. | More state, calibration and simulation; sparse observations can overfit a regime and make confidence worse. | Held-out forecast error and calibration improve; earliest useful shot, harm and planning cost do not regress under the same total allowance. |
| A credible alternative can survive one surprising observation. | Preserving every hypothesis expands work indefinitely. | Bounded hypothesis storage and recorded retirement/unknown mass; adversarial switching remains inside the computation envelope. |

Telemetry adds model IDs, per-hypothesis evidence, omitted probability/unknown status and the observation that changed support. Native intrinsic/interference learning remains separate. Mutate the selected regime, remove an observation and force an unseen enemy: the uncertainty must widen and the core must still act legally. Remove this extension if the same gain comes from correcting one deficient native forecast, or if extra compute reduces completed work more than better prediction helps.
