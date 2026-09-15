# Combat System Plan — how the companion learns its weapons and fights with them

This folder is the implementation plan for replacing the companion's combat: the always-on hands, the two combat activities, the four-number arc learner and the positioner's firing-stand scoring. It was written on 15 September 2026 against main at `c15c140`, after a survey of modded weapons (`../Game and Mod Case Studies/Modded Weapon Behaviours.md`), two rounds of review, and three owner rulings. **Nothing in it is built.** Building waits for the owner's playtest of the current package and the mastery session.

The plan exists because the owner wants any weapon from any mod to be used well, and judged that only one system can do that: one in which learning how a weapon behaves powers choosing where to stand, which weapon, which target and how to aim, all at once, inside one fighting stance. Everything else in the companion stays as it is, apart from the named seams where combat plugs in.

```
Combat System Plan/
├─ CLAUDE.md                                              this guide: status, reading order, rulings, maintenance
├─ 1 The Combat Stance and Its Rulings.md                 what is being built and why, the behaviours it must produce, what lost, questions for the owner
├─ 2 Weapon Knowledge.md                                  layer 1: what is learned about a weapon, from which observations, stored how
├─ 3 Simulating a Use.md                                  the forward model: one use flown through learned knowledge against predicted enemies
├─ 4 Attack Planning.md                                   layer 2: stand proposals, timed plans across weapons, the objective vector, commitment, budget
├─ 5 The Combat Activity, the Hands and the Positioner.md the brain seams: one Combat activity, hands that fire only in it, stand verdicts
├─ 6 Repository Layout and File Fates.md                  the target folder tree and what happens to every file that exists today
├─ 7 Observability.md                                     god's-eye records, recorder columns, report checks, overlay layers
├─ 8 Verification.md                                      fixture rows with the mutation each must fail, cost measures, play acceptance
└─ 9 Build Phases.md                                      the ordered work packages, their acceptance, lanes and reviews
```

## Read it in this order

File 1 first, because every later file implements a decision made there and a reader who starts in file 4 will re-argue the layering. Files 2 to 5 are the four components in the direction data flows: knowledge, simulation, planning, then the brain seams. File 6 is the map an implementer keeps open. Files 7 and 8 are how each component is watched and proved. File 9 is the only file a lane brief should quote from directly, and it names which of the others each phase needs.

## What is ruled, and by whom

- **One system in three layers** — weapon knowledge beneath one attack planner, with the hands and the Combat activity executing it. Accepted by the owner on 15 September 2026 after the draft.
- **Hunting and guarding are one Combat activity, and only Combat fires.** Owner ruling, 15 September 2026. Mining, chopping, lighting, collecting and keeping company never shoot; the evade layer bends the body in every activity.
- **Combat is a stance, not a detour.** Owner ruling, 15 September 2026: fighting weighs staying with the player among its objectives, so an enemy behind a travelling player is fought from where the fight costs the player least, and a fight is a held episode rather than something the companion blips in and out of.
- **A firing stand has one valuer, the planner**; the positioner returns verdicts and routes. From the second review.
- **Handed gear is still read, never run.** Standing law; the plan keeps it by learning volleys from the player's own uses rather than calling item code.

Open questions with their defaults are at the end of file 1. Slate's `architecture.laws` carries the Combat law and `record.decisions` carries the rulings.

## What this plan makes stale once built, and nothing before

`Companion/Weapons/CLAUDE.md`, `Companion/Brain/Activities/Combat/CLAUDE.md`, `Companion/Brain/Infrastructure/Aiming/CLAUDE.md` and the firing-stand sections of `Companion/Brain/Infrastructure/Position/CLAUDE.md` describe the code as it is and stay true until the phase that replaces each lands; each carries a planned-work pointer here. README's Expected Behaviour already describes the combat this plan builds; its System In Place and Current Behaviour change only as phases land.

## Maintaining this folder

Strike a phase in file 9 in the commit that lands it, and move what that phase made true into the owning code folder's CLAUDE.md in the same commit, because this folder is a plan and the folder guides are the description of the system. A decision that changes during building is changed here with its reason, not appended as a correction. When the last phase lands, the folder is deleted in the commit that lands it; its dead ends go to the folder guides as properties, and its history is the log.
