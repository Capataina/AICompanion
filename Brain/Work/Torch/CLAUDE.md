# Torch — a light in the dark

```
Torch/
├─ CLAUDE.md
└─ TorchBearer.cs   lit when the light sense's ambient reading falls under 0.22, out when it rises over 0.42, never switching within 3 s of the last switch; emits TorchID.Torch's colour at the hand; reveals the map through ../../../Map/TorchMapReveal.cs every 10 ticks
```

The ambient reading is measured away from the companion's own glow (see `../../DecisionMatrix/Senses/LightSense.cs`), which with the hysteresis is what stops the torch talking itself off. A mod ability, no item consumed, by ruling. Later, from the mastery tree: a brighter torch (raise `ReachTiles`), a torch hat that makes it permanent, and placing torches as an opportunistic task.
