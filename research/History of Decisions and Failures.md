# The history contains several different kinds of failure

Investigation date: 12 September 2026. Baseline: `4296f851b13e49ccd557d7255ec24bc223a29929`. This report covers the 176 reachable commits from 7–11 September, including subjects and bodies back to the first companion. The history reader covered the chronological full-body stream; the main investigation separately inspected the decisive chooser changes and the current implementations. This is a synthesis of the whole progression, not a fresh rerun of every historical test.

**The record supports questioning the architecture, but it does not support counting every regression as a failure of utility AI.** It contains genuine scoring defects, mistaken action boundaries, incomplete opportunity models, invalid movement assumptions and misleading diagnostics. A different selector would inherit several of these unchanged.

The complete corpus can be reproduced from the repository root with:

```sh
git rev-list --count 4296f851b13e49ccd557d7255ec24bc223a29929
git log --reverse --date=iso-strict --format='%H%n%ad%n%s%n%b' 4296f851b13e49ccd557d7255ec24bc223a29929
git show -s --format=fuller 85961ee
```

The first command returns 176. Hashes below identify durable source records, not verdicts guaranteed correct because they were committed. “Reported” measurements are historical claims; the separately recomputed telemetry observations are in [Evidence and Open Questions](<Evidence and Open Questions.md>).

## A priority state machine preceded utility selection

The initial companion (`48f8709`, `7125353`, 7 September) used a priority state machine with downing, shooting, chopping, wandering and following. This was a small implementation of a small repertoire. The closed ability set and NPC identity already existed; utility did not create those product boundaries.

The second playtest motivated `85961ee` on 8 September. Its body reports trailing at the leash edge, inability to get past a pillar, and demands that varied together: remain closer under threat, maintain the player's heading, and collect drops while it was safe. It introduced observation, reflexes, utility choice, action execution, position scoring and A* navigation together, with an incumbent bonus and forecasts against the player's safety horizon.

That breadth limits what can be inferred. The transition changed the decision architecture, positioning and navigation in the same commit. The body explicitly says the replacement had not yet been watched running. It is evidence of the problem framing and implemented response; it is not an experiment isolating utility's advantage over the old FSM.

The rejected alternatives were described too broadly. “A behaviour tree is still a priority chain” describes a common selector arrangement, not every tree or hierarchy. “A mod has no training loop” accurately identified a missing asset at the time; it is not a technical impossibility of learning inside a mod. These remain useful records of why the project moved quickly, but should not preclude the [present comparison](<Utility AI and Its Alternatives.md>).

One detail already present in that first utility design matters now: private state machines were retained inside actions. The project has never been purely stateless reactive scoring.

## The apparent navigation problem repeatedly moved below route search

The September 8–10 movement progression repeatedly found that an edge in the route graph was not enough to establish a move the real NPC could perform. `2c3ff7c`, `985195e`, `2fc66e2`, `08d36ec` and `3273d0f` are useful points in that progression.

The failure class was concrete: a proof built from an ideal standing location or nominal velocity could be handed to a body arriving with another position, momentum or collision state. A search could correctly connect its abstract nodes and still produce unusable movement. Changes to descent entry, braking, jump take-off, live validation and failure accounting attacked different parts of that mismatch.

| Attempt | What the history reports | What it establishes and what it leaves open |
|---|---|---|
| Descent braking, `c4bc752` | Around 25 routes improved and some completed 4–11% faster, but walked blocks fell from 67 to 63 and not-walked blocks rose from 11 to 16; the change was torn out. | An aggregate speed gain can coexist with lost capability. These are recorded historical figures, not a result rerun for this report. |
| Actual-entry validation, `2fc66e2` | Validation was centralised around the body state that really reaches the move. The commit reports 2,032 native collision comparisons without mismatch and bounded route checks. | A meaningful execution contract and fixture evidence. It does not establish reliable live traversal of arbitrary caves. |
| Failure accounting, `08d36ec` | A stuck-jump case had accumulated 503 internal faults but emitted one outcome. The repaired case terminated in bounded time and emitted repeated stuck outcomes. | Better diagnosis and bounded failure. It would be misleading to count this as a successful jump. |
| Actual starting state, `3273d0f` | Changing the entry treatment recovered routes that longer alignment did not. The historical portable result retained 86 full arrivals alongside partial and incomplete cases, with exit 1. | The starting-state hypothesis gained evidence; the corpus remained non-green and live acceptance stayed open. |

Terraria-specific defects complicate this further. The root guide records a `StepUp` flag that lifted a descending body back onto a platform, collision differences on slopes and half blocks, and telemetry fields that could not see AI-phase position writes. None of those are repaired by replacing an activity selector. They justify preserving the native-body boundary and a reliable observation of the real body in any architecture.

The inference for the next investigation is specific: separate graph sufficiency, search efficiency and physical execution. The record does not settle whether the current coarse graph plus local control is ultimately the right compromise.

## Independent shooting removed an impossible choice

`b296567`, 9 September, is one of the strongest architectural lessons because the defect was visible in the call graph. Only hunt, kite and guard called the firing method. Following or collecting could not shoot because their code never reached the trigger.

Making combat behaviours win more often would have increased the exclusivity the owner disliked. The repair put firing in the tick coordinator, while hunting became a decision to move towards a worthwhile enemy. The action occupying a tool hand could still prevent firing. This fits the intended simultaneous movement and shooting by changing resource ownership.

The commit's build and unchanged navigation replay did not prove the resulting combat felt correct. They established integration and that a specific navigation corpus did not change. The architectural lesson is narrower and durable: a single winning activity must not imply exclusive ownership of all the actor's capabilities.

`0eb08bd` later found a hole in that new arrangement. A reflex returned before the firing step, so the independent hand channel stopped exactly while the feet dodged. The repair restored firing on that branch. This shows why drawing a boundary once is insufficient: every early return and exceptional control owner must honour it.

## A numerical ceiling made guarding lose by construction

The same `0eb08bd` commit records a score-range defect. A committed ordinary action could reach `1 × 1.15`, while guard's raw product could reach only `1`. At that incumbent score, increasing danger inside the existing bounded product could never make guard win.

The change introduced an urgency ladder sized to clear the incumbent bonus. The historical discussion rejected explicit priority tiers or an interrupt flag because it wanted context-sensitive trade-offs. That decision solved one ordering problem while making a new obligation visible: all relevant score ranges and multipliers must remain comparable as behaviours are added.

This is evidence against assuming that normalising individual considerations normalises the final policies. It is not proof that all preferences should be hard priorities. An immediate lethal hazard and a slightly more valuable nearby pickup may require different kinds of arbitration; the research should decide which relationships are hard requirements and which remain negotiable.

## Unknown reachability exposed a policy problem, not a missing boolean

`544a0e9` introduced `Yes`, `No` and `Unknown` after a survivor could hold a refuge without sufficient proof. Search ending because it exhausted its available work does not prove a route exists or that none exists. Threat sensing, voluntary work and self-rescue have different costs for being wrong, so the callers were given explicit policies.

The mining sequence later returned to this tension. `ac0c23c` admitted an approach towards ore whose reachability was unknown, because scoring unknown as zero prevented work from getting far enough to resolve it. `0f9394e` subsequently addressed ore without a standable swing position.

This does not make optimism or pessimism universally correct. The Expected Behaviour currently asks for stronger pre-commitment assurance than some of this implementation provides. The question is what safe information-gathering or intermediate movement is permitted while the complete answer remains unknown. A new chooser family still needs that policy and an adequate feasibility interface.

## Retention accumulated in several places

The September 10 work (`0f9d7f2`, `09d15a3`, `08c0b48`) developed retained route search, executed traversal memory, continuing work and continuous distant-follow recovery. These are different kinds of memory with different validity conditions: a search frontier, a learned physical connection, an unfinished nearby job and an active recovery manoeuvre.

The resulting system already has persistence. Calling the present problem “no memory” would erase both useful construction and the distinctions that matter. There is still no established gameplay representation of explored coverage or a certificate that the player has taken a particular one-way traversal. A short player-position trail exists, but that is not equivalent to either of those capabilities.

The architectural risk is fragmented ownership: one component retains the target, another retains a useful position, another retains a route, and a coordinator may cancel or interrupt their control. These can all be individually reasonable and still disagree about whether the same activity is continuing. That is a hypothesis to investigate against traces, not a finding that every retained object needs a new common framework.

## September 11 moved attention towards opportunity, progress and interpretation

The target-admission sequence (`231a478`, `b821afa`, `ea4a800`) refined hunting from an enemy identity into a more meaningful opportunity: a shot from here, or a reachable position that offers one. `656e94d` prevented switching targets from disguising a lack of engagement progress. These changes address whether a behaviour is worth offering and whether it is accomplishing anything, before asking how highly it should score.

The commitment experiment then exposed another ownership mismatch. A long-lived pot attempt remained preferred while mining was available. The attempted response reduced the incumbent bonus whenever a body-level stalled flag was set. `9b402cb` reverted that rule after the next capture showed substantially more switching.

The commit reports 481 action runs in the 17:22 capture and 1,727 in the 18:30 capture; the raw columns reproduce those run counts. The captures have different durations, and include retained labels on skipped/downed ticks, so this is not a controlled 3.59-fold rate comparison. The average recorded run length fell from about 55.5 to 22.3 rows. More importantly, the recorded mechanism explains a possible feedback loop: the stalled flag belongs to the body, survives an activity change, and penalises the newcomer before it has an opportunity to make progress.

The revert does not prove the original flat commitment bonus is good. It leaves the stalled-pot problem open. It rejects one source of progress evidence for one switching rule. The general question is what each activity can legitimately claim about its progress, remaining value and failure, including stationary activities for which no displacement is expected.

`11338a1` added decision-survival reporting, part of the wider shift towards examining the evidence rather than only the behaviour. Even those measurements need care: an action-name change, a fresh chooser evaluation, an activity identity change and a movement-control handoff are different events.

## The history narrows experiments more reliably than it chooses a winner

| Historical lesson | Tempting overgeneralisation | What survives scrutiny |
|---|---|---|
| The flat priority FSM could not express the desired simultaneous activity. | All state machines or trees are unsuitable. | The whole-actor exclusive mode and its transition policy were inadequate; local state and hierarchical composition remain candidates. |
| Utility had a guard ceiling and commitment regression. | Utility cannot express the product. | Current score ranges and switching evidence failed in named ways. Compare those policies rather than the label alone. |
| Planned jumps and drops failed live. | A* is the wrong algorithm. | The graph and physical execution contract were sometimes inconsistent. Search replacement is one possible response, not an established necessity. |
| A change improved many replay cases and regressed others. | The aggregate was better, therefore the change worked. | Retain per-case capability and regression evidence, especially physically distinct terrain. |
| A test became green or a failure became bounded. | Live behaviour is accepted. | Name the tested surface: compiler, portable model, native collision, full brain fixture or actual gameplay. |
| The current documents tell a coherent story. | Their causal explanations are verified. | Several raw-data and source discrepancies remain; inspect the writer and captured revision before drawing architectural conclusions. |

The work has produced useful boundaries and instruments as well as churn. The research should preserve those assets while remaining willing to replace the responsibilities that the evidence identifies as poorly represented. No part of this history establishes a clean three-change route to the desired companion.
