# Activities — the seven competing jobs

The chooser prepares concrete activities, applies shared factors once, and compares the best nomination from each family. Family folders organise those responsibilities; they do not contain separate movement or weapon implementations.

```
Activities/
├─ CLAUDE.md
├─ CompanionAction.cs            the shared contract: prepare, score, execute, a place
├─ ClassifyOffersAndAttempts.cs  offer eligibility and attempt outcomes
├─ WorkPolicies.cs               mining/chopping mimic vs opportunistic vs off
├─ Combat/                       protecting the player and pursuing useful attacks
├─ Gathering/                    retained ore veins and tree jobs
└─ NearbyAssistance/             accompanying the player and useful local help
```

All seven ordinary activities live in this tree. Their declared PurposeFamily, rather than their folder, determines nomination. Nearby assistance also contains the reusable pot/torch interaction adapter; it is not another selectable activity. Safety, recovery, movement, native tools, hands and activity lifecycle retain their existing owners.
