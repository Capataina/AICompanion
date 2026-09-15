# Case studies compare mechanisms in their original domains

This folder examines authored game AI and companion or scheduling mods as evidence for possible mechanisms. Establish what the base game does before attributing a behaviour to a mod. Prefer author documentation, original talks, public source at an identified revision, and maintainer issue discussions. A feature description establishes intended behaviour; it does not establish a measured improvement.

Every study explains the original problem, mechanism, evidence of outcomes, limitations, compatibility interactions and what would have to change for the lesson to apply to this companion. Preserve counterexamples. Do not infer that a mod uses utility scoring from its name or apparent autonomy.

```text
Game and Mod Case Studies/
├─ CLAUDE.md    comparison method and source authority
├─ RimWorld Work Scheduling.md
│                 source-level job queues, detours, priority transformation,
│                 continuation and real compatibility countercases
├─ Starsector Ship and Weapon AI.md
│                 public ship/autofire/recovery mechanisms and weapon-owner
│                 compatibility boundary
├─ Terraria NPC and Companion Boundaries.md
│                 installed tModLoader hook contract and historical/current
│                 companion action, combat and recovery evidence
└─ Modded Weapon Behaviours.md
                  a closed taxonomy of weapon behaviour properties drawn from
                  Calamity, Spirit Mod, Starlight River, TerraGuardians and
                  vanilla source plus wiki survey of nine other mods; owner-
                  player and cursor reads as the recurring compatibility risk
```

## Current state — 2026-09-12

The case studies establish ownership and revalidation as the common questions, rather than a shared scheduler algorithm. Pick Up And Haul changes carried-inventory batching; Common Sense has a concrete path-cost detour ratio and branch-specific queue/reentry guards; While You're Up bounds inserted work at a job boundary and attempts resumption. The 2025 WYAN author says its nearby-work rule changes priority rather than pathfinding, and withdrew WYU as a recommendation after a reported 1.6 conflict. Current Free Will is source-verified as scalar work-priority scoring; the exact link to its older YouDoYou workshop implementation remains unproved. Starsector sources show independent weapons/movement cadence, commitment through recovery, and a one-writer weapon-AI compatibility condition. The installed, version-matched tModLoader 1.4.4.9 XML now verifies the `PreAI`/vanilla/`AI`/`PostAI` order; AICompanion’s current `aiStyle=-1` NPC, tick coordinator and motor show how the ownership boundary is realised. TerraGuardians' historical author warns about the old pathfinder; current pinned 1.4.4.9 source instead establishes branch-based follow/combat, inventory weapon selection and a recovery mode that includes teleports. The historical warning does not establish current movement quality. Vanilla RimWorld/Starsector internals, WYAN's exact algorithm, Terraria base-AI source, and measured mod outcomes remain gaps.
