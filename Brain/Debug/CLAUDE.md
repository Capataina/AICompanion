# Debug — the overlay

`BrainOverlay.cs` registers the F6 keybind and, when on, draws above the companion: the running action and reflex, danger, horizon and intent, every action's raw and final score, threat and loot counts, the request kind, the chosen spot and its score, the path length and last plan cost, and the chosen weapon. It also draws the path as dots (green walk, orange jump, blue drop), the chosen spot in magenta, and a label over every threat (class, urgency, ticks to player, shooter flag, `x` when unreachable). Telemetry (AIC-51) will write the same fields to a file.
