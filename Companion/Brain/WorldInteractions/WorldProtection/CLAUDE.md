# Home protection — autonomous edits respect bed rooms

```
WorldProtection/
├─ CLAUDE.md                 this guide
└─ ProtectCompanionHomes.cs  bounded bed discovery and protected room footprints
```

The brain refreshes nearby bed rooms before choosing work. Mining, chopping, pot breaking and torch placement consult these footprints both during discovery and immediately before native terrain mutations. The guard belongs at the mutation boundary because a job may outlive the room it was discovered beside. Wood or platforms without a bed do not protect an arbitrary cave.

Terraria's final housing verdict requires furniture, size and walls that an unfinished or deliberately dark home may not have. Protection therefore walks the bed's enclosed space using solid boundaries, preserving the surrounding wall as well as the interior. Open or oversized areas use a conservative neighbourhood around the bed rather than labelling an entire connected cavern as home. It is bed-room protection, not a perfect classification of every room in a sprawling base. Discovery uses nearby actors and a bounded cadence, invalidates on observed terrain edits, and resets with the brain's world lifetime. Native bed styles share the same multi-tile frame origin; arbitrary mod-defined beds are not yet classified.
