# Keeping company includes reunion and relaxed nearby movement

Keeping company is one ordinary activity in NearbyAssistance. It offers positive value while the player is alive and competes with other useful work; it is not merely a fallback for an empty board.

```
NearbyAssistance/
├─ CLAUDE.md           activity purpose, method choice and evidence limits
├─ KeepCompany.cs      reunion, resting, nearby movement and sealed-pocket roaming
└─ CollectNearbyItems.cs known drops and uncertain pot-content opportunities
```

Preparation compares the existing reunion urgency with the value of staying locally active. Execution requests WithPlayer when reunion wins, or rests and selects small nearby movements otherwise. A sealed pocket uses the existing bounded roaming/retry policy. These are methods of the same purpose and preserve its activity identity. Entry and reunion discard old local destinations, so resuming after an interruption cannot revive a stroll in the previous neighbourhood.

Every request passes through position selection and shared movement. Only an explicit reunion request can qualify for distant recovery; a local stroll near player coordinates cannot. Shared safety can interrupt any method, and compatible shooting and held light remain independent hands work.

Keeping company retains the existing short-horizon destination prediction, local movement distribution and geometric neighbourhood limits. It does not establish useful meeting regions across alternate routes, final contextual separation policy or safe local exploration in every terrain. Lighting retains its existing adapter during migration; there is no separate exploration activity.

A companion in a proven sealed pocket uses local movement without forgetting reunion. The coordinator exposes that state on the action context and cycles local roaming with reunion retries using BehaviourWeights. Keeping company's local method requests Roam and its reunion method requests WithPlayer. An incomplete path is not proof of a sealed pocket, and a model-closed region is not proof of physical impossibility in Terraria.

Collection prepares a fitting known drop and a bounded pot-content opportunity, compares them with the same shared factors as the parent, and publishes the chosen raw value and forecast. Publishing an already-discounted value would charge the trip twice. Neither comparison repeats discovery. Known drops bind the item, type and captured position, revalidating world-slot membership, availability and cargo acceptance before movement. A detached object can remain active after its slot is replaced; it is no longer a world drop. A newly observed replacement receives its own object identity. An arbitrary same-object mutation without a lifecycle signal is not detected as a new generation.

Pot contents remain unknown until the native operation produces them. Their prior value and handling allowance live in BehaviourWeights; approach contributes a geometric estimate rather than a certified pickup/return duration. Unknown contents require empty cargo space conservatively, whereas a known drop may fit an existing player or bag stack. Native home protection covers the pot's footprint and is rechecked at the operation, along with pot policy and capacity. The current method uses native KillTile at interaction range; weapon-driven breaking and prediction of the resulting drop locations remain separate implementation obligations.

Pot discovery and approach reuse PerformNearbyWorldWork. Dropping a method candidate does not release the collection activity's admission; the activity owner handles that lifecycle. Actual known-drop pickup remains the NPC's independent contact operation and can occur while another behaviour is active. A completed worksite can widen the allowance for its known drops, without granting nearby unknown pots the same allowance. Breaking a pot does not certify any pickup or obligate a subsequent trip.
