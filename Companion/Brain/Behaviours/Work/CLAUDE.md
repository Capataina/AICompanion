# Work policy access — read the character's gathering preferences

The thin policy readers delegate to per-character preferences rather than owning another saved setting. Mining and chopping activities live in `../../PurposeFamilies/Gathering/`; their preparation, retained jobs and native outcome accounting are documented there. Nearby pot/torch methods belong to `../../PurposeFamilies/NearbyAssistance/`.

```
Work/
├─ CLAUDE.md                   preference ownership and usage
└─ WorkPolicies.cs             Disabled/Mimic/Opportunistic preference readers
```

Disabled forbids the work. Mimic requires the activity's observed player-work trigger; Opportunistic permits local discovery without that trigger. The gathering activities enforce these permissions during preparation and again before execution. Reading a preference does not authorise a particular terrain mutation; native permission, home protection and usable tool access remain separate checks.
