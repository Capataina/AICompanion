# Activity coordination — one grant for movement and the hand

The brain's branches propose controls and the common finaliser applies them through the companion motor. The granted record binds the current activity, its phase, the request and the motor's actual AI-phase output. A request for downed holding can become recovery clearance inside terrain, so requested owner and applied owner remain distinct.

```
ActivityCoordination/
├─ CLAUDE.md                  this boundary and its interpretation
├─ GrantActivityControls.cs   movement application and the compatible hand permission snapshot
├─ DescribeActivitySnapshot.cs completed family/activity and shared-controller presentation state
├─ ConsiderIncidentalInteractions.cs cheap compatible pot and torch interactions from the current pose, credited to no activity
└─ RecoverDistantCompanion.cs continuous distant reunion admission, steering and release
```

An incidental interaction is a proposal handled at this boundary, not another planner. After the hands run, on an ordinary execution tick only (safety, recovery and downed grants belong to responses that own the body for another purpose), `ConsiderIncidentalInteractions` may act when the final grant left the hand Available and the arm did not fire that tick. On a BehaviourWeights cadence it asks library instances of the pot and lighting methods (never the chooser's activities, so an activity's discovery is untouched) for the nearest target within actual reach of the current pose that their own enablement and candidate checks allow: pot breaking with cargo space and home protection, torch placement in measured darkness with supply and the lit-neighbourhood veto. It skips the executing activity's own method and target, because that activity will do it and one benefit must not be counted twice. It acts through the method's native operation, which rechecks permission at mutation, and marks the interaction event's detail as incidental with the activity it happened during. It never requests a position, writes movement or records productive work, so no activity's value or attempt conclusion includes the effect. It keeps the last successful interaction for readers. A detour that needs a new destination is not incidental; it is an ordinary activity choice. Contact pickup of loose items already behaves this way in `CharacterBody` and is not part of this component.

Each grant also names the activity attempt open when it was issued, or zero when none is executing. A safety or recovery grant issued while the ordinary attempt is suspended therefore carries zero, which is what stops a reader charging that movement to the interrupted work.

Available hands permit the independent arsenal and later held light; they do not prove a shot or torch use. WorkTool reserves the hand throughout the activity's coherent tool phase, including strike cooldowns. Unavailable is the downed lifecycle grant. Native interaction effects and the subsequently integrated body remain separate observations. Diagnostics consume the retained grant rather than making another decision or applying controls themselves.

The brain publishes ActivitySnapshot after its final control, hand and progress handling, including safety, recovery and downed paths. Family, activity, activity identity, phase and shared-controller flags therefore refer to one completed coordination step. The HUD consumes that record without invoking a chooser or inspecting an independent set of mutable controllers. The snapshot's tick denotes publication, not a fresh utility comparison or a successful interaction.

Distant recovery requires an explicit WithPlayer reunion request with no occupied tool, beyond the recovery distance in the current companion preferences. Admission depends on that purpose rather than the action's class. Guarding, hunting, collecting and work destinations do not grant recovery even when their coordinates equal the player's. The coordinator interrupts the route before asking the sole motor for continuous flight through terrain. Flight ends near the owner only with a clear body, cancels on downing or owner death, and never becomes a traversal or archive entry. This is reunion recovery, separate from shared tactical safety and future mastery movement abilities.
