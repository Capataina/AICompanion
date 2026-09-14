# Lifecycle and interface fixtures

NPC attach, downing and revival, doors, HUD icons, the native card and offscreen UI. `--lifecycle`, `--doors` and `--render-ui` select subsets.

## Downing and revival

`VerifyDowningAndRevival` (inside `--lifecycle` and the default run) downs the companion through `CheckDead` and watches the real AI entry point with the player a body-width away and twelve tiles away. The player beside must revive it, the player away must not revive it as soon, and the companion must still get up on its own; every downed tick must pass the shared one-application-one-grant check with the hand revoked and no ordinary decision, and the tick after revival must be an ordinary grant with the hand back. On the tick the companion gets up, the published presentation still says downed, because `UpdateDowned` applies the downed controls first, `FinaliseControls` builds the presentation from `IsDowned` at that moment, and revival is decided afterwards.

## Stat mirroring

`VerifyStatMirroring` (inside `--lifecycle` and the default run) ticks the real AI entry point with the local player's maximum life and defence changed between ticks and asserts the companion's follow on the next tick: a raise carries current life up by the same amount, a cut clamps it without killing, and defence tracks both ways. It is the proof behind the owner's ruling that the companion's toughness is the player's and nothing is equipped on it; the mirror itself lives in `CompanionNPC.MirrorStats`. The player's defence is a `Player.DefenseStat` struct built as `Default + n`, not an int, which is why the fixture sets it that way.

## UI rendering

`--render-ui` uses a hidden SDL surface to render the actual profile, inventory, mastery and inspector pages with installed game assets. PNGs go under the process temporary directory's `aic-native-ui` folder. On macOS, provide `DYLD_LIBRARY_PATH` pointing to the installed loader's `Libraries/Native/OSX`.

The inspector's evidence views are checked: **No solver call** (neither DrawBrainOverlay nor DescribeExecutionEvidence may call the positioner, a route search, the local planner or the aimer); **Box geometry** (points half a pixel inside and one pixel outside every edge must agree with region's containment test); **Box pixels** (drawn reach box must paint every perimeter pixel, nothing outside and nothing in corner interiors).

The HUD sheet invokes the actual notch drawing for all seven family/activity pairs, suspended mining, recovery, no activity and downing. It checks ordered family/health/activity geometry and screen-edge containment at multiple viewports. `VerifyNativeCard` drives title dragging, permanent bottom tiles, back navigation, minimising and restoration.

## Input composition

The input-composition fixture passes a native inventory probe and the actual card through production `ModifyInterfaceLayers`, then calls each real `GameInterfaceLayer.Draw` in order. The underlying probe uses the native inventory's slot bounds and mouse predicate with its real `ItemSlot.LeftClick`; the card draws and handles its own real slot. It reaches an overlapping pair, verifies that only the visible bag slot receives the press, and checks raw/UI pointer restoration and ownership of player-inventory open/close state.

Co-Authored-By: Claude Haiku 4.5 <noreply@anthropic.com>
