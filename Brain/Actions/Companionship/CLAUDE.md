# Companionship — being with the player

```
Companionship/
├─ CLAUDE.md
├─ WalkWithPlayerAction.cs   follow the player's predicted position while they travel; scores zero when they stand inside the calm band so wander can win; hard leash at 1400 px
├─ GuardAction.cs            danger high: stand by the player with sight lines and shoot the most urgent threat
└─ WanderAction.cs           the floor score; stand, stroll, hop inside the calm band
```

These three are what the companion does when nothing else scores, and the torch (in `../../Work/Torch/`) rides on them: it takes the hand whenever an action here leaves it empty in the dark.
