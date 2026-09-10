# Torch — a dark-place hand state

```
Torch/
├─ CLAUDE.md
└─ TorchBearer.cs   decides lit and shown, then drives held item, light and map reveal
```

`Lit` comes from world observation’s ambient light outside the companion’s own glow, with hysteresis and a minimum hold. `Shown` additionally requires a free hand and a dry body, following vanilla's ordinary-torch wet restriction. The NPC clears last tick's hand claim and calls `Hide` before resolving this tick, so a previous torch cannot veto its own fallback. Light, map reveal and held item follow that one answer; downed state never shows the torch. A brighter or permanent torch may be a future mastery capability; placing torches would be an opportunistic behaviour, not a mission.
