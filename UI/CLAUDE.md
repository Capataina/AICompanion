# UI — HUD

`CompanionHealthBar.cs`: a layer after "Vanilla: Resource Bars" in raw screen pixels (sizes scaled by `Main.UIScale` by hand, because `Main.mouseX/Y` are screen pixels). It is drawn as a notch: docked at top-centre it hangs from the screen edge with square top corners, rounded bottom corners and two concave fillets outside the top corners; dragged elsewhere it is a rounded card with a one-pixel border and no fillets. Inside, a pill track and a pill fill (red to green by health fraction, grey while downed) under the label. Drag with the left mouse, right-click docks it; position saved on `Players/CompanionPlayer.cs`. The bag window is in `../Inventory/`, next to the bag it shows.

## Traps

- **There are no rounded primitives on the HUD.** The shapes are alpha masks built once per pixel size (`RoundedMask`, `FilletMask`) on the graphics device and cached; `Unload` disposes them. Building a texture must happen on the main thread, which the draw layer is.
- **Text is `DrawBorderStringFourWay` in the body colour**, so the label has no dark halo on the dark notch; `DrawBorderString` would.
