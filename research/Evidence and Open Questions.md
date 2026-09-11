# Several architectural conclusions need a stronger reading of the evidence

Investigation date: 12 September 2026. Source baseline: `4296f851b13e49ccd557d7255ec24bc223a29929`, version `0.15.1`. The observations below were recomputed or inspected during this investigation. No gameplay was run, and no historical replay or collision suite was rerun.

The README's Expected Behaviour remains the starting product description. Its Current Behaviour and explanatory columns are claims to check. Four substantial September 11 recordings underpin much of that description; a fifth 439-row capture also exists. Those historical captures precede the latest baseline changes. Their behaviour must not be reported as a measured result for the current checkout.

## The cited survival score was nonzero, and most of the window did not run selection

In `Telemetry/2026-09-11_18-30-04-871.tsv`, ticks 21,400 through 21,476 inclusive contain 77 rows. Both `survive_raw` and `survive_fin` are **0.35 in every row**, whereas the README says the survival score was 0.000 throughout.

| Field | Recomputed observation | Meaning and limit |
|---|---|---|
| `action` | `guard` in all 77 rows | A retained action label; not proof that guard freshly beat survival 77 times. |
| `control_source` | 13 travel, 63 combat-reflex, 1 downed | Most rows assign the body to another control path. |
| `decide_ms` | 13 nonzero values; 64 values of 0.00 | Consistent with selection running on the travel rows and being bypassed elsewhere. Timing alone is not a universal freshness flag. |
| `survive_raw`, `survive_fin` | 0.35 throughout | The asserted zero-score premise is contradicted by the raw file. |
| `danger` | 0.85–0.97 | This is the player-danger field, not the companion's own danger. |
| `self_danger` | 0.23–0.52 | The separately recorded personal danger is lower and changes within the window. |

The baseline [coordinator](../Companion/Brain/CoordinateBrainTick.cs) returns from the reflex branch before `Chooser.Choose`. The [recorder](../Companion/Brain/BehaviourDiagnostics/RecordBrainTelemetry.cs) reads retained scores. This supplies a mechanism for stale decision fields, consistent with the historical rows. Exact historical execution should still be reconstructed from the captured build before attributing every interruption to a particular branch.

The current [ObserveCompanion.cs](../Companion/Brain/WorldObservation/ObserveCompanion.cs) incorporates recent damage and low health into personal danger, alongside environmental danger. [SurviveAction.cs](../Companion/Brain/Behaviours/Survival/SurviveAction.cs) scores personal danger and escape pressure. Thus “add damage as a missing term” is not justified as a current-code diagnosis without examining the existing term's values and use.

**What remains open:** the companion did go down in the cited capture, and the relationship between avoidance, interrupted movement, breath and harm deserves investigation. Nonzero survival does not prove adequate survival policy. The data correction removes a particular explanation; it does not solve the death.

## A final `no-target` can follow a rejected trajectory

The last substantial capture contains 25,767 `no-target` rows and 175 `no-arc` rows. Of the `no-target` rows, **23,451 also contain `no-clear-trajectory` in the retained target-evidence payload**. They contain at least one recorded trajectory rejection, not a proof that every nearby enemy was evaluated or that the rejection was physically correct.

The baseline [Arsenal.cs](../Companion/Weapons/Arsenal.cs) explains how those statuses coexist. `BestTarget` evaluates candidates through attack forecasting. A failed trajectory can reject a candidate before it is selected. If no evaluated attack is accepted, `BestTarget` returns null and `TryFire` records `no-target`. The final outcome `no-arc` therefore counts a different stage from all trajectory failures.

The README's inference that rare `no-arc` excludes terrain-blocked shots is unsupported. Candidate rejection evidence already exists, although it is bounded to evaluated alternatives and is not a complete account of every excluded hostile. The next question is whether the solver correctly rejects those trajectories, whether candidate filtering misses viable attacks, or whether the companion chooses destinations from which no useful attack exists.

The 126 `fired` rows are shot instants, and 10,940 `cooldown` rows are occupied between-shot time. Dividing shot instants by all ticks describes firing duty cycle, not hit accuracy or combat effectiveness. Effectiveness needs native shot/contact/damage outcomes and the opportunities that could have been taken. A capture full of enemies is not by itself a capture full of legal shots.

## Action runs reproduce, but they are not a clean decision-frequency metric

These counts use the exact four substantial files discussed in the README. A run is a contiguous sequence of identical `action` strings; transitions equal runs minus one.

| September 11 capture | Rows | Action runs | Transitions | Mean rows per run |
|---|---:|---:|---:|---:|
| 12:45:43.810 | 13,844 | 154 | 153 | 89.90 |
| 16:53:41.137 | 31,722 | 500 | 499 | 63.44 |
| 17:22:50.710 | 26,716 | 481 | 480 | 55.54 |
| 18:30:04.871 | 38,475 | 1,727 | 1,726 | 22.28 |

There are 110,757 rows across these four files. The separate `16-53-19-585` file contains 439 rows and is excluded from this four-run comparison. The last substantial capture includes 6,101 rows with `control_source=downed`, including the transition tick; retained labels during downing lengthen a run without representing fresh decisions. Reflex ticks pose a related problem.

The pattern is consistent with worsening action-label churn across the selected captures, especially the final one. The captures differ in duration, terrain, enemies, player movement and code. They are not a matched experiment establishing that a named architecture became worse at each version. Future comparisons should distinguish changes of action class, target/job identity, fresh chooser result and actual body control.

## The source contains more observation and less global dynamic state than the README says

| Statement in baseline documentation | Baseline source observation | Consequence for research |
|---|---|---|
| Light is only one number at the companion and nothing is known elsewhere. | [ObserveLight.cs](../Companion/Brain/WorldObservation/ObserveLight.cs) has `AtPlayer`, `AtCompanion` and an ambient neighbourhood sample. | The missing capability is a retained spatial representation of useful unlit regions, not the absence of every non-companion brightness measurement. Offscreen unknown light also needs care. |
| There is no player trail of any kind. | [ObservePlayer.cs](../Companion/Brain/WorldObservation/ObservePlayer.cs) keeps a bounded sequence of changed grounded feet tiles; it is written to diagnostic windows. | A trail exists. It is not gameplay exploration coverage or proof that a particular directed traversal was performed with capabilities the companion possesses. |
| The route graph already carries pose and velocity in each node. | [NavPath.cs](../Companion/Brain/SharedMovementSystem/RoutePlanning/NavPath.cs) defines `NavNode` as tile plus `MobilityState`; full `BodyState` is separate. | Assess the coarse graph and local dynamic search separately. Future abilities cannot be declared solved because a few resource fields exist. |
| A fixed small A* budget describes navigation as a whole. | [Reachability.cs](../Companion/Brain/SharedMovementSystem/RoutePlanning/Reachability.cs) has bounded synchronous queries; live navigation also retains search across ticks. | Query starvation and travel planning can fail differently. Increasing one budget is not a test of all search. |
| All normalised actions share a simple 0–1 range. | [ChooseBehaviour.cs](../Companion/Brain/BehaviourSelection/ChooseBehaviour.cs) applies multiple modifiers; action urgency can exceed one. | Normalised inputs do not establish comparable final preferences. |
| Equal scores are broken by something independent of registration order. | `ChooseBehaviour.Choose` uses strict `final > bestScore` while iterating the registered list. | For equal positive maxima, the first encountered winner remains. This is an implementation detail to include in policy comparisons. |
| There are twenty-five named responsibility rows. | The current README table contains 27. | The [system map](<Architecture and Behaviour Map.md>) includes all 27 rather than silently omitting two. |

The investigation records these discrepancies rather than rewriting the product story and its historical interpretation before discussion. A future documentation correction should trace each affected assertion, especially the repeated survival and target-selection interpretations.

## Planned moves and movement outcomes need a common denominator

The README lists 213 planned drops beside 230 interrupted drops in the last capture. Whatever the correct event interpretation, those cannot be treated as a single population of mutually exclusive outcomes without reconciling identities and counting semantics. Plans can contain steps that are never begun; attempts can be replaced; a useful route can be interrupted because the goal changed.

A completed/planned ratio is therefore not automatically a physical success rate. The minimum useful comparison connects search, route, step and attempt identity to the control owner and terminal reason. Successful arrival, timely bounded refusal and harmless cancellation should remain separate outcomes. So should an avoidable interruption and a physically impossible move.

## The raw checks are reproducible without rebuilding or launching the game

Run this from the repository root. It reads local ignored TSV files, makes no changes and prints the measurements used above. It requires the named recordings to still exist. A clone without `Telemetry/` can verify source and history claims but cannot independently reproduce these capture counts.

```sh
python3 - <<'PY'
import collections
import csv
from pathlib import Path

stems = (
    '2026-09-11_12-45-43-810',
    '2026-09-11_16-53-41-137',
    '2026-09-11_17-22-50-710',
    '2026-09-11_18-30-04-871',
)
captures = {}
for stem in stems:
    with (Path('Telemetry') / (stem + '.tsv')).open(encoding='utf-8-sig') as f:
        rows = list(csv.DictReader(
            (line for line in f if not line.startswith('#')), delimiter='\t'))
    captures[stem] = rows
    transitions = sum(a['action'] != b['action'] for a, b in zip(rows, rows[1:]))
    runs = transitions + bool(rows)
    print(stem, 'rows', len(rows), 'runs', runs, 'transitions', transitions,
          'mean rows/run', round(len(rows) / runs, 2))

rows = captures[stems[-1]]
window = [r for r in rows if 21400 <= int(r['tick']) <= 21476]
for key in ('action', 'control_source', 'decide_ms', 'survive_raw', 'survive_fin'):
    print('death window', key, dict(collections.Counter(r[key] for r in window)))
for key in ('danger', 'self_danger'):
    values = [float(r[key]) for r in window]
    print('death window', key, min(values), max(values))
print('fire', dict(collections.Counter(r['fire'] for r in rows)))
print('no-target with trajectory rejection', sum(
    r['fire'] == 'no-target' and 'no-clear-trajectory' in r['target_evidence']
    for r in rows))
print('downed controls', sum(r['control_source'] == 'downed' for r in rows))
print('four-capture rows', sum(map(len, captures.values())))
PY
```

To inspect current representation directly, these searches identify the relevant declarations and consumers; their output needs reading rather than treating a match as a behavioural test:

```sh
rg -n 'AtPlayer|AtCompanion|Ambient' Companion/Brain/WorldObservation/ObserveLight.cs
rg -n '\bTrail\b' Companion --glob '*.cs'
rg -n 'record struct NavNode|struct NavNode|MobilityState' Companion/Brain/SharedMovementSystem/RoutePlanning/NavPath.cs
rg -n 'LastScores|Chooser.Choose|Engage|TryAssess' Companion/Brain/CoordinateBrainTick.cs Companion/Brain/BehaviourDiagnostics/RecordBrainTelemetry.cs
rg -n 'no-clear-trajectory|no-target|BestTarget|ForecastAttack' Companion/Weapons/Arsenal.cs
```

## The next checks should separate rival explanations

These are proposed research experiments, not an implementation plan. Their purpose is to make a later decision cheaper to reverse and harder to rationalise after the fact.

| Question and hypothesis | Confirming evidence | Refuting evidence | Cheapest discriminating check |
|---|---|---|---|
| Does the selector rank a good available activity badly? | A feasible, relevant candidate is present with a losing score contrary to the owner's judgement. | The desired candidate was never generated or cannot execute. | Label a small set of captured choice points before reading their scores; reconstruct eligibility, raw factors and fresh final choice. |
| Is commitment missing activity-owned progress? | Switching is driven by a body signal unrelated to the current job, or a failed job retains its bonus indefinitely. | Correct task progress is available and a different preference explains the switch. | Trace one pot/mining alternation and one stationary successful job, including job identity and control ownership. |
| Do reflex handoffs destroy an otherwise feasible escape? | The same native escape succeeds under a matched uninterrupted controller and loses through the recorded handoff. | It fails without interruption, or the reflex avoids a greater imminent harm. | Reconstruct the cited death window with captured geometry and capabilities; first audit what terrain and enemy motion were actually recorded. |
| Does the candidate set hide useful shots? | A native/legal shot exists for an excluded or rejected candidate under the same muzzle, target and terrain state. | All candidates fail the same physical/legality check independently. | Inspect rejected target evidence and re-solve representative states, preserving the exact recorded geometry where available. |
| Does short-term selection miss worthwhile multi-step opportunities? | A locally unattractive action enables a later benefit the owner consistently wants, and no existing activity represents it. | Nearby reactive choices achieve the desired trip without an extra goal or sequence. | Write out the README's tree → drop → pot → ore trip with interruptions; compare explicit alternatives and their retained state on paper. |
| Is search work the limiting factor? | Increasing work on the same model discovers valid routes that the baseline leaves unknown. | No route exists in the graph even with sufficient work, or the found route fails native execution. | Classify captured windows by search stop reason before changing the algorithm. |
| Would a learned component earn its training machinery? | A bounded reward or labelled preference set has a repeatable deficit against a competent authored baseline. | The apparent deficit disappears with corrected sensing, feasibility or control ownership. | Define the observation/action/evaluation contract and audit which parts the existing replay environment can actually supply. |

For comparative implementation experiments, choose inputs and pass criteria before running either candidate. Keep capabilities, terrain, player trace and hostile conditions matched wherever the harness permits. Report per-scenario results, control cost, interruptions and actual outcomes, alongside the owner's judgement of coherence. A faster route that loses a formerly reachable case and a safer policy that never helps are both trade-offs, not automatic improvements.

## The accepted decisions stop at the research boundary

The owner has accepted the purpose of this work: research the space, preserve the development history, write the trade-offs here, and discuss the direction without changing gameplay. No replacement for utility or A*, no new task framework and no priority policy has been selected.

The provisional views are that utility remains credible for immediate preference, that activity continuity and control ownership deserve explicit comparison, and that the graph/body contract deserves its own navigation investigation. Each has counterevidence and a separating check above or in the utility report. A proposal becomes an architectural decision only when the discussion resolves its scope, alternatives and remaining uncertainty.
