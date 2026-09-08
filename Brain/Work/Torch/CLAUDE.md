# Torch — a light in the dark

```
Torch/
├─ CLAUDE.md
└─ TorchBearer.cs   lit when the light sense's ambient reading falls under one level, out when it rises over a higher one, never switching within a minimum hold of the last switch; shown only while the hand is free; shown, it emits a torch's colour at the hand and reveals the map through ../../../Map/TorchMapReveal.cs on a cadence
```

Two answers, kept apart. `Lit` is the decision, from ambient light measured away from the companion's own glow (see `../../DecisionMatrix/Senses/LightSense.cs`) with hysteresis and a hold, which is what stops the torch talking itself off. `Shown` is that decision and a free hand together, and it is the one thing the light, the map reveal and the held item all follow, so a companion swinging a pickaxe in the dark neither glows nor reveals until the pickaxe goes away. A mod ability, no item consumed, by ruling. Later, from the mastery tree: a brighter torch (raise `ReachTiles`), a torch hat that makes it permanent, and placing torches as an opportunistic task.
