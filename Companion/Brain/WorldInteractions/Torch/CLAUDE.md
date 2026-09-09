# Torch — a dark-place hand state

```
Torch/
├─ CLAUDE.md
└─ TorchBearer.cs   decides lit and shown, then drives held item, light and map reveal
```

`Lit` comes from world observation’s ambient light outside the companion’s own glow, with hysteresis and a minimum hold. `Shown` additionally requires a free hand. Light, map reveal and held item follow that one answer, so a tool swing removes all three until the arm clears. A brighter or permanent torch may be a future mastery capability; placing torches would be an opportunistic behaviour, not a mission.
