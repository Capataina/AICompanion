# Scenarios — preserved inputs for navigation claims

These files preserve terrain and actor states from reported failures, plus small fixtures isolating the geometry involved. NavReplay runs the portable movement model against them; EngineReplay reconstructs selected windows as native Terraria tiles. A portable model-closed result does not prove the real world physically sealed. A file containing several blocks represents several independent inputs, and each begins with cold route memory.

```
Scenarios/
├─ CLAUDE.md
├─ 2026-09-08_13-48-44-plans-shaped.txt earlier captured failures reshaped from a saved world
├─ 2026-09-08_16-04-10-plans-shaped.txt later shaped failures including the wet arrival regression
├─ 2026-09-09_18-25-59-plans-underground-house-0.8.3.txt raw structure and pool failures
├─ 2026-09-09_18-25-59-census-0.8.3.txt companion traversal census accompanying that recording
├─ actual-entry-drop-run-9.txt recorded body offset before a cave descent
├─ captured-pool-23-50-24-14964.txt full underwater-awning window used by native survival verification
├─ gap-three-wide-two-up-from-run-4-case-9.txt gap traversal isolated from a captured failure
├─ half-block-floor-landed-through-a-platform.txt platform descent onto half-height support
├─ jump-from-a-one-tile-runway-under-a-ceiling.txt the raw 2026-09-11 window where a jump proven at a speed its one-tile runway could not build was refused before every attempt
├─ ledge-four-up-lowest-arc-lands-run-5.txt jump-profile landing height
├─ ledge-two-tiles-needs-one-jump.txt ordinary ledge climb
├─ ledge-under-overhang-to-corridor-run-5.txt clearance under a ceiling
├─ markers-line-keeps-half-block-support.txt actor markers must preserve the terrain beneath them
├─ platform-cap-fall-through-beside-a-block-run-7.txt descent beside adjacent solid support
├─ platform-intercepts-a-jump-from-below.txt intermediate platform contact
├─ platform-lip-one-row-down-is-a-walk-run-5.txt a shallow landing belongs to walking
├─ shaft-the-body-freezes-in.txt historical platform-lift failure
├─ slope-lip-one-row-up-is-a-walk-not-a-hop.txt the lip at 3496,479 cut out of run 4 block 15, where a body sitting low on a floor slope made a one-row walk step read as a two-tile rise and the walker hopped it: 83 ticks with that jump against 48 without
└─ shaft-two-wide-descended-from-the-lip.txt narrow-shaft entry and body clearance
```

## A scenario arrives three ways now, and two of them are written by a tool

A fixture used to exist only where somebody cut one by hand or where one of the recorder's detectors
fired, so a failure nobody had thought to threshold produced no fixture at all. Two commands close
that gap and both write into this folder.

`NavReplay --extract-scenario <capture> <tick>` cuts the terrain around the companion at a recorded
tick out of the capture's own terrain snapshots, with the recorded body box, the destination the
brain asked for, the player's feet and their trail. **Its header carries what the snapshots did not
cover**, because a tile no snapshot reached is written solid — unknown is closed, the same rule the
native water replay states — and a fixture at low coverage is a fixture whose sealed verdict is
about the recording rather than about the world. Read the coverage before reading the verdict.

`NavReplay --shrink <scenario>` writes a reduced copy beside the original, named for it, carrying in
its header the transforms applied and the signature the reduction preserved. **A reduced file is a
diagnostic artefact and not automatically a fixture**: whether one is worth committing is a judgement
for whoever is investigating that failure, because every committed red enlarges what the corpus
reports as broken. The reduction never adds support to a fixture and never deletes an interior
column, so a reduced file's tiles still name the places the original named.

Every block is also run reflected left to right by `NavReplay --mirror`, which is a property of the
corpus rather than a file in it: a route proven one way must be proven the other, so the reflection
doubles this folder for the cost of a transform and a block that answers differently is an asymmetry
in our code. A fixture cut by hand is therefore two cases, and a left-right symmetric fixture is
still worth having because it exercises the transform end to end.

Run `dotnet run --project Tools/NavReplay -- --follow Tools/Scenarios` from the repository root for the full corpus. The corpus contains known incomplete and model-closed cases; its non-zero exit is classified per case, never replaced with a green claim. A comparison must check every previously arriving case, not merely an improved aggregate count. `dotnet run --project Tools/EngineReplay -- --escape` exercises production survival selection, its normal search budget and native collision on the captured pool, then mirrored synthetic awnings.

Preserve the capture's body box and tile offset when translating into a native fixture. Actor glyphs represent air, so marker metadata must carry an actor standing over a slope or half block without erasing support. Reshaping from a saved world can introduce terrain edited after the failure; raw shape-aware captures avoid that ambiguity. Do not alter a fixture to make a proposed movement succeed. Missing terrain or unsupported liquid fidelity is missing coverage and belongs in the result.
