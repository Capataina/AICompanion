# Lifecycle and interface fixtures — the NPC, the doors, and everything drawn

Spawning and attachment, downing and revival, stat mirroring, doors, per-character preferences, the HUD notch and the native card. `--lifecycle`, `--doors` and `--render-ui` select subsets; the default suite runs each as its own named case except `--render-ui`, which is offscreen rendering and is not part of it.

```
Lifecycle/
├─ CLAUDE.md
├─ VerifyCompanionLifecycle.cs    spawn, both-way attachment, and the shared tick helper
├─ VerifyDowningAndRevival.cs     downing through CheckDead, revival beside and alone
├─ VerifyStatMirroring.cs         maximum life and defence tracking the player's
├─ VerifyDoorPassage.cs           the native door helper, announced toggles, locked doors
├─ VerifyCompanionPreferences.cs  per-character preferences reaching the brain
├─ VerifyCompanionHud.cs          the notch's own drawing, for every family and activity
├─ VerifyNativeCard.cs            the profile card: pages, slots, transfers, the mastery wheel
└─ RenderNativeInterface.cs       the offscreen renderer and the inspector's evidence views
```

## Attachment is bidirectional, and half of it passes every direct test

`ModNPC.Entity` points at the body and `NPC.ModNPC` points back at the behaviour. Assigning only `Entity` lets direct AI calls pass while **native lethal damage bypasses the companion's `CheckDead`** and enters vanilla NPC loot and death handling. A drowning-to-downing test must preserve both links rather than suppressing lethal strikes, which is why `VerifyCompanionLifecycle` asserts the attachment both ways rather than assuming the one it set.

`VerifyCompanionLifecycle.Create` starts with a **dead player**, deliberately, to exercise autonomous lifecycle handling; a fixture that needs ordinary companionship must explicitly make that player alive before its first brain update. A last-registered zero-score fallback used to hide this precondition.

## Downing, revival, and one finding printed rather than asserted

`VerifyDowningAndRevival` downs the companion through `CheckDead` and watches the real AI entry point with the player a body-width away and twelve tiles away. The player beside must revive it; the player away must not revive it as soon, and the companion must still get up on its own. Every downed tick must pass the shared one-application-one-grant check with the hand revoked and no ordinary decision, and the tick after revival must be an ordinary grant with the hand back. Both runs are printed.

It also **names a finding rather than hiding it**: on the tick the companion gets up, the published presentation still says downed, because `UpdateDowned` applies the downed controls first, `FinaliseControls` builds the presentation from `IsDowned` at that moment, and revival is decided afterwards. On that one tick the fixture checks the application and the grant itself and prints the finding as `MEASURE`; the ordering belongs to the character body and the presentation's owners, not to this fixture.

`VerifyStatMirroring` ticks the real AI entry point with the local player's maximum life and defence changed between ticks and asserts the companion's follow on the next tick: a raise carries current life up by the same amount, a cut clamps it without killing, and defence tracks both ways. It is the proof behind the ruling that the companion's toughness is the player's and nothing is equipped on it; the mirror itself lives in `CompanionNPC.MirrorStats`. The player's defence is a `Player.DefenseStat` struct built as `Default + n`, not an int, which is why the fixture sets it that way.

## Doors, and the wall that was not a wall

`VerifyDoorPassage` holds `WorldGen.OpenDoor`'s swing matrix — open both sides swings east, east blocked swings west, both blocked and the locked Lihzahrd style refuse. It asks the live route query across a closed door (no), opens the door through the real `DoorOpener` for a body moving into it, and asks again with the edge cache clock unmoved (yes), then closes it behind the body (no). That last one is the check that fails when the door interaction stops announcing its terrain change.

Through the whole brain, a locked door with no detour must be refused without the body pressing against it, and a locked door beside a passage under the wall must be routed around and never opened.

**The scenes' walls run to row 0.** A wall that stopped at some row above the floor sealed a doorway for a body that walked through it; a flying body goes over the top, and every door scene silently became a scene about open sky. `WallTop` is a named constant for exactly that reason.

**Two scenes are printed as `MEASURE` and not asserted, and the reason is a brain finding rather than a fixture problem.** The planner treats a closed door as a wall, so when the door is the *only* passage the flood proves the goal unreachable through it, the body never travels there, and the opener never sees it:

```
MEASURE  an openable door as the only passage:  opened=-1 crossed=-1 pushTicks=0
MEASURE  openable door beside a detour:         opened=36 crossed=44 pushTicks=0
```

The second line is the same rule seen from the other side: with a detour available the body takes the detour while one exists. `opened=-1 crossed=-1` is the evidence that nothing was opened and nothing crossed, not an error code. Route search treating a door it could open as passable is open work on the roadmap; until it lands, a door as the sole passage is measured rather than asserted, because asserting it would be asserting a behaviour the brain does not have.

Native `WorldGen.CloseDoor` walks every player slot in `Collision.EmptyTile`, so the whole-brain door scenes populate them. Every type the live brain reads is imported through the `live` alias, because this project compiles its own copy of the movement core and a fixture that sets that copy's `LimitPlanningWork` or reads its `TerrainChanges` measures statics the live code never touches.

## Offscreen rendering

`--render-ui` uses a hidden SDL surface to render the actual profile, inventory, mastery and inspector pages with installed game assets. **It creates no visible game window.** PNGs go under the process temporary directory's `aic-native-ui` folder. On macOS, provide `DYLD_LIBRARY_PATH` pointing to the installed loader's `Libraries/Native/OSX`; a macOS sandbox may refuse SDL video initialisation before any drawing, because the hidden renderer needs graphics access even though it never shows its surface.

The UI matrix covers 1280×720, 960×540, 800×600 and 640×480 at normal scale, plus 1600×1000 at 150% UI scale. The scale case calls Terraria's `PlayerInput.SetZoom_UI` rather than pretending only the drawing matrix scales. Native UI setup must supply cached original screen dimensions, the UI and world matrices, inventory gamepad link points, language, text brightness and rarity colours.

The inspector's evidence views are checked in the same run:

- **No solver call.** Neither `DrawBrainOverlay.cs` nor `DescribeExecutionEvidence.cs` may call the positioner, a route or reach search, the local planner or the aimer.
- **Box geometry.** Points half a pixel inside and one pixel outside every edge of a tool region's box and a follow region's two boxes must agree with the region's own containment test, and the tool box with the reach arithmetic `InReach` uses.
- **Box pixels.** A drawn reach box must paint every perimeter pixel, nothing outside and nothing in its corner interiors.
- **Execution page.** The fixture seeds retained state by reflection — a Gathering nomination, a usable and an unresolved offer, a tool region, a downed grant applied as recovery clearance, a completed attempt — renders at every viewport, and requires the evidence to carry each seeded fact, the last visible line to end above the footer, the tabs to cover the strip exactly, and at scale 1 gold heading text with the Execution tab highlighted and the background outside the panel untouched.

The HUD sheet invokes the actual notch drawing for all seven family/activity pairs, suspended mining, recovery, no activity and downing, the docked state at the top edge and the rest in two columns under it. It checks ordered family/health/activity geometry and screen-edge containment at the same viewports, and that the three bars sit health above mana-left and experience-right inside the notch through the notch's own geometry function. It pins the mana pool at half and the experience at a quarter of the second level through their private setters, restored afterwards, and counts the blue and gold pixels along each small pill's centre row against the pinned fraction; a rounded fill loses its two blended end pixels, which is the slack the count allows. Production tick fixtures separately require the published presentation to match the completed activity and control state, including early returns; injecting a presentation into a render only tests its display. Native mask textures belong to the hidden renderer's device and are released before that device is disposed.

`VerifyNativeCard` drives title dragging, permanent bottom tiles, back navigation, minimising and restoration; every page must preserve the same frame. It verifies fixed slot bounds, category filtering without reindexing, and full/partial/empty player inventory transfers through the real button and `Player.GetItem`. Mastery checks the generated wheel's structure — the diamond roles, the edge count, every shared node opening from two neighbouring spokes and continuing into both, no two nodes within a node's width — directed connectivity, each incoming path to every convergence independently, unavailable rank rejection, pan, zoom, reset and fitted node positions. A diamond is opened through its canvas events by a second click and again by the real button, closed by the back button, and its tree's ranks are driven by the real rank button without consuming inventory. `ShowMasteryTree` PNGs cover that nested page at every viewport. The original debug-line check still bounds the painted pixels of a real production line. Passing these checks establishes those inputs, not all live interaction sequences.

**The overlay's success-region box check runs after every page and the notch have been rendered and saved**, so its throw leaves the images to look at. As of 15 September 2026 it throws on the follow-comfort region, whose drawn boxes and containment test disagree at one probe point — `{X:841 Y:400}` is inside the drawn boxes and outside the region's own test — a defect of the intent-region positioning that the orb's positioning rewrite replaces. The scene builds that region from literals, so it is independent of terrain and of the body: reproducing it needs nothing but the flag.

## Input composition

The input-composition fixture passes a native inventory probe and the actual card through production `ModifyInterfaceLayers`, then calls each real `GameInterfaceLayer.Draw` in order. The underlying probe uses the native inventory's slot bounds and mouse predicate with its real `ItemSlot.LeftClick`; the card draws and handles its own real slot. At every supported viewport it reaches an overlapping pair, verifies that only the visible bag slot receives the press, repeats the underlying exclusion after returning to Profile and Mastery, then verifies an uncovered native slot remains usable. It also checks raw and UI pointer restoration and ownership of player-inventory open/close state. This exercises the native handlers and the layer lifecycle; it does not draw all vanilla inventory art or third-party mod layers.

## Traps

- **Populated slots need services an empty-slot screenshot never exercises.** `ItemSlot.DrawItemIcon` calls `Main.instance.LoadItem` even for a cached texture, so the harness supplies an unconstructed `Main` service shell and no `Game` constructor opens a window. `Player.GetItem` refreshes recipes, whose empty native recipe sentinel must exist — a null recipe array entry is an invalid fixture, not evidence about transfer logic.
- **Native audio is disabled only during transfer fixtures**, and every transfer fixture restores its inventory snapshots before rendering.
- **The Guide fallback portrait is intentional**; this harness does not initialise the live player renderer.
- **Rendering and first-tick logging that need unavailable graphics or loader services are disabled** on the attached `CompanionNPC`, while the production brain, senses and movement components are kept.
