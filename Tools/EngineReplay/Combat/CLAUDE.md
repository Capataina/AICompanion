# Combat fixtures

Hunting, guarding, arsenal valuation, combined safety, encounter context, actor-specific danger, and offer validity with destination retention. `VerifyEngineMotion` runs these in the default suite; `--combat-purpose`, `--combat-cost`, `--safety-aftermath` and `--offer-validity` select subsets. A subset flag reaches its fixture before the default suite's own setup has run, so a fixture that touches `Main` must set Terraria's save path itself or the static constructor throws on a null path.

## Combat purpose

`VerifyCombatPurpose` (`--combat-purpose`) holds P08's matched combat scenes. Every scene asserts who it threatens before reading an outcome. The consequence matrix holds one zombie between player and companion and varies one input per scene — player defence, difficulty, endurance, companion defence, or either actor's remaining life — requiring each to move only its own actor's urgency in the direction the game's damage arithmetic moves it. The baseline row recomputes the replaced raw-damage share from public facts and requires equality at zero defence and full health.

## Offer validity and destination retention

`VerifyOfferValidity` (`--offer-validity`, in the default run) holds the three-valued offer, the arrival-time shot and the partial-progress fixed point. Its cut row buries an enemy in rock so far more standable candidates surround it than a rescore may solve, and reads the reason after *every* resolve rather than every twelfth: with nothing held the rescore gate never fires, so each tick runs a whole scored pass and sampling one in twelve reads a pass by which the refusal memory has already absorbed the shortlist and the cut has happened unwatched. Measured on 2026-09-14, between three and seven undecided passes precede the proven absence over 29 scored candidates, and the row asserts the shape rather than that count: a warm suite floods further per resolve than a cold standalone run, so how many candidates each pass reaches — and therefore how many passes exhaust the shortlist — is a property of what ran before it. One undecided pass is presented to the chooser to confirm it carries a null destination with an undecided reason.

Its arrival row needs a scene where "where the target is" and "where it will be" differ across a wall, and that costs two flags. An ordinary walker's forecast applies Terraria's own tile collision, so it stops dead against the pillar and never reaches the far side; a phasing walker then falls through the floor it no longer collides with, because the forecast still applies its gravity, and ends up inside rock where nothing can shoot it. The enemy therefore both phases and flies. The scene asserts it is discriminating before it reads any result, and the row asserts the recorded evidence says `clear-arc` rather than `clear-arc-unforecast`, because a solve that fell back to the current position would otherwise pass the row while proving nothing about the forecast. The body starts a real walk from the stands that matter: standing on top of them makes every trip zero ticks, at which arrival and now are the same instant.

Two costs are worth knowing before adding a row here. Resolving a firing request repeatedly is bounded by a millisecond budget, so how deep one pass gets differs between a warm suite and a cold standalone run — a row that resolves a fixed number of times is machine-dependent, and the fix is to resolve until the search settles. And the motion track only accumulates error samples across consecutive observations, so a setup loop must end on an observation rather than on a position advance, or the forecast carries no measured confidence and every solve honestly falls back to the current position.

## Combat cost

`--combat-cost` runs one full-brain scenario with four stationary hostiles (full-health zombie, nearly dead zombie, slime and boss-flagged Eye) while the player stands, walks away and returns, under production millisecond allowances. It prices threat consequence, hunt target choice, protection, the arsenal's hand steps and shared safety but not work selection. Reports per-phase p50/p95/max/mean and the three costliest ticks with activity, request, safety kind, aim target and encounter source.

## Safety aftermath

`VerifySafetyAftermath` (`--safety-aftermath`, in the default run) holds P08's combined-safety scenes. The retreat row requires one tick where combat spacing owns the feet while hands are granted, aimed at a zombie and report a real shot. The reflex row requires that tick's single grant to be owned by `combat-reflex` with hands available and guarding suspended. These establish callback ownership across interruption and resumption.

## Encounter context

`VerifyEncounterContext` saves and restores every world-event global it sets and runs fresh real `ThreatSense` and `EncounterSense` per row with the player's depth set by moving the surface line. Blood moon must be recognised on the surface and absent underground; Old One's Army and an observed Eye of Cthulhu at any depth. Invasion rows follow the spawner's gate; the pressure rows write the spawn cap the game would compute because the spawner cannot run headless. Every observation in the suite is its own engine tick because the pressure baseline is the admission ceiling.

Co-Authored-By: Claude Haiku 4.5 <noreply@anthropic.com>
