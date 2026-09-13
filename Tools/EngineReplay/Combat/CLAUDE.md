# Combat fixtures

Hunting, guarding, arsenal valuation, combined safety, encounter context and actor-specific danger. `VerifyEngineMotion` runs these in the default suite; `--combat-purpose`, `--combat-cost` and `--safety-aftermath` select subsets.

## Combat purpose

`VerifyCombatPurpose` (`--combat-purpose`) holds P08's matched combat scenes. Every scene asserts who it threatens before reading an outcome. The consequence matrix holds one zombie between player and companion and varies one input per scene — player defence, difficulty, endurance, companion defence, or either actor's remaining life — requiring each to move only its own actor's urgency in the direction the game's damage arithmetic moves it. The baseline row recomputes the replaced raw-damage share from public facts and requires equality at zero defence and full health.

## Combat cost

`--combat-cost` runs one full-brain scenario with four stationary hostiles (full-health zombie, nearly dead zombie, slime and boss-flagged Eye) while the player stands, walks away and returns, under production millisecond allowances. It prices threat consequence, hunt target choice, protection, the arsenal's hand steps and shared safety but not work selection. Reports per-phase p50/p95/max/mean and the three costliest ticks with activity, request, safety kind, aim target and encounter source.

## Safety aftermath

`VerifySafetyAftermath` (`--safety-aftermath`, in the default run) holds P08's combined-safety scenes. The retreat row requires one tick where combat spacing owns the feet while hands are granted, aimed at a zombie and report a real shot. The reflex row requires that tick's single grant to be owned by `combat-reflex` with hands available and guarding suspended. These establish callback ownership across interruption and resumption.

## Encounter context

`VerifyEncounterContext` saves and restores every world-event global it sets and runs fresh real `ThreatSense` and `EncounterSense` per row with the player's depth set by moving the surface line. Blood moon must be recognised on the surface and absent underground; Old One's Army and an observed Eye of Cthulhu at any depth. Invasion rows follow the spawner's gate; the pressure rows write the spawn cap the game would compute because the spawner cannot run headless. Every observation in the suite is its own engine tick because the pressure baseline is the admission ceiling.

Co-Authored-By: Claude Haiku 4.5 <noreply@anthropic.com>
