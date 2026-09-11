# Profile Card — the companion's truthful player-facing control surface

```
ProfileCard/
├─ CLAUDE.md                     this guide
├─ CompanionProfileCardSystem.cs  draggable frame, permanent status tiles and page lifetime
├─ DrawCardPrimitives.cs          native panels and shared hard-shadow text
├─ DrawCompanionStatus.cs         portrait, health and retained action evidence
├─ BlockCoveredInventoryInput.cs  scoped pointer exclusion around the underlying native layer
├─ DefineMasteryGraph.cs          authored node coordinates, kinds and directed edges
└─ PreviewMasteryTree.cs          local preview ranks, weapon subnodes and pan/zoom canvas
```

The profile card is the HUD notch's primary click target. Its title bar drags the frame, with a grip on the profile and a back arrow on subpages. Minimise retains the current page and restores the same frame. The position survives closing and reopening during the loaded mod session, but is not character save data. The identity strip remains visible above every page: the companion portrait, health, selected activity, retained target direction and policy or activity reason. A movement stall or downed state supersedes ordinary work; an active reflex supplements the activity rather than hiding it. Hovering the strip exposes the full explanation when a long name needs truncation. Display code reads the brain's public evidence and never decides behaviour.

The profile controls only choices the implementation honours: mining and chopping work policy, voluntary hunting, pot breaking, supplied torch placement and following distance. Survival, fighting, dodging and pickups are automatic. Explicit segmented buttons show Off/Mimic/Auto, On/Off and Close/Standard/Free; selecting the current value does not cycle unexpectedly. Row spacing follows the available height and the native list scrolls when necessary. Preferences belong to `PlayerIntegration/`, where native per-character persistence owns them. Ore whitelists remain unimplemented; the card does not expose a control that pretends otherwise.

The card uses one `UserInterface`, one `UIState` and one frame. Only the page content is replaced. Inventory and Mastery are the two permanent bottom status tiles, with occupied storage and opened preview-node counts respectively; the tiles condense on subpages. There is no top tab row. Inventory embeds `../Inventory/` and opens the player's inventory for native transfers without resizing or relocating the frame. The close button, Escape and world unload end the open state. Input is consumed before item use, including the notch's opening press. Unload drops references; the card owns no graphics resources. Native panel assets use a translucent blue-violet tint; shared text uses the game font with a hard black shadow offset down-right.

Mastery is explicitly a preview: eight connected regions contain circular repeatable stats, diamond abilities and smaller repeatable path ranks. The small ranks model cheap traversal in the proposed progression without charging anything. Weapon diamonds open nested ranks. Every node position and directed connection is authored in `DefineMasteryGraph`; the renderer applies only uniform viewing scale and pan. Branches fork and return through local clusters, while shared junctions accept any one incoming opened rank. The same edge table governs availability and drawing, so visual connections cannot promise an unavailable path. Selection changes only local UI state, never stats, abilities, inventory, experience, points or saves. Reset view restores the fitted main graph while preserving preview ranks. Closing the card discards those ranks.

The graph clips to its viewport; its legend and controls sit outside that clip. Labels have authored positions too. Zoom centres on the pointer for the wheel and on the viewport for the buttons. The canvas refits when its available size changes.

Nested weapon ranks use their own authored edge table in `DefineMasteryGraph`, shared by drawing and availability. Opening a weapon's details is inspection, so it does not require selecting its main-graph rank first; advancing within the nested path does require an incoming opened rank. The behaviour list's clipped bottom leaves a separate gap before its fixed explanatory note: a partially visible row must not run directly into the note.

## Native coordinate and drawing traps

`Main.UpdateUIStates` runs after `PlayerInput.SetZoom_UI`, and UI-scaled drawing layers use the same conversion. Mouse coordinates there are already UI coordinates: dividing `Main.MouseScreen` again misaligns dragging and hit tests. Layout reads `PlayerInput.OriginalScreenSize / Main.UIScale` because the frame can also be opened from the unscaled health-notch layer. On 2026-09-11 the native 150% fixture reproduced the double conversion as a 686-pixel frame where the contract required 780; the fixture now asserts frame width and drag displacement.

`mouseInterface` stops world use, but does not stop vanilla inventory slots behind the card from handling the same click. The native inventory draws before this card. `BlockCoveredInventoryInput` wraps that layer: a pointer covered by any card page is temporarily moved offscreen in the engine's raw coordinate cache while the underlying layer draws, then restored in `finally`. Changing only the current mouse coordinates fails because each layer restores them from that cache. Uncovered native inventory remains interactive. Closing the card closes player inventory only when the card opened it; a player inventory that was already open stays open.

MagicPixel is an atlas. Lines and fills select a one-pixel source rectangle; stretching the complete texture turns a line into a rectangle.

`Tools/EngineReplay --render-ui` renders the production pages with installed Terraria fonts, a representative attached companion and populated inventory through a hidden graphics surface. It exercises native button events, frame lifetime, filters, transfer conservation, path junctions and pan/zoom/reset, including a UI-scale case. The fixture portrait uses the NPC's Guide fallback; live player-rendered portrait animation and the feel of interaction remain playtest acceptance surfaces.
