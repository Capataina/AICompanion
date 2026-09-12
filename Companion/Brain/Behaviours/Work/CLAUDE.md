# Work adapters — nearby interaction methods and policy access

This folder retains the bounded pot/torch method executor and thin gathering-policy readers. Mining and chopping activities live in `../../PurposeFamilies/Gathering/`; their preparation, retained jobs and native outcome accounting are documented there. Policy readers delegate to per-character preferences rather than owning another saved setting.

```
Work/
├─ CLAUDE.md                   shared interaction ownership and migration boundary
├─ PerformNearbyWorldWork.cs   pot/torch discovery, approach and interaction methods
└─ WorkPolicies.cs             Disabled/Mimic/Opportunistic preference readers
```

Collection's pot method and permanent torches share one bounded candidate/approach executor. Collection owns pot discovery and uncertain contents under PurposeFamilies/NearbyAssistance. Torch candidates use the game's torch Smart Cursor rules through `WorldInteractions/Torch/`, rank elevated left/right sites, and may use a ground jump proven to reach the interaction and land safely. An interruption invalidates a prepared interaction jump. Both recheck home protection at the actual mutation, so discovery permission is never treated as permission to edit a changed world.

Their candidate search and deferred-approach cleanup run during Prepare. Comparison reads the captured value and target without invoking native torch rules or repeating a jump proof. Execution retains its own validation and progress accounting; a comparison never counts as another attempted interaction.

Method discovery clears its own candidate without releasing the activity's continuation allowance. A collection activity may still have a valid known drop when pot discovery is disabled. The shared activity owner releases admission on replacement; a failed or unavailable method cannot erase another method's admitted purpose. Execution rechecks enabled policy and candidate validity before either approach or mutation.

An ineligible-to-eligible transition refreshes discovery immediately. Preparation and execution share this eligibility observation: a permission revoked after preparation must invalidate the same discovery state as one observed before preparation. Otherwise a policy or supply change can clear the target yet leave the old search deadline blocking reconsideration. Unchanged eligibility retains the usual bounded search cadence, and per-target physical deferrals remain separate evidence.
