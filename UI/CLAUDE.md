# UI — HUD

`CompanionHealthBar.cs`: a layer after "Vanilla: Resource Bars" in raw screen pixels (sizes scaled by `Main.UIScale` by hand, because `Main.mouseX/Y` are screen pixels). Drag with the left mouse, right-click resets; position saved on `Players/CompanionPlayer.cs`. Shows life, or the revive percentage while downed. The bag window is in `../Inventory/`, next to the bag it shows.
