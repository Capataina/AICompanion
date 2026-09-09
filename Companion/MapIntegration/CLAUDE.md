# Map integration — companion presence and torch-limited reveal

```
MapIntegration/
├─ CLAUDE.md              this guide
├─ CompanionMapLayer.cs   companion head on the world map
└─ TorchMapReveal.cs      reveal only tiles reached by shown torch light
```

The companion is shown as its own map head so distance and route remain visible. Map reveal runs only while `../Brain/WorldInteractions/Torch/` shows a torch in a free hand. It floods through air to the torch’s reach and stops at the first solid face, so it never reveals behind walls or pretends the companion sees an entire screen.
