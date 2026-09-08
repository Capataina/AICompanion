# Reflexes — the fast path

`Reflexes.cs` runs before the chooser every tick. It looks 20 ticks ahead along each reachable threat's velocity; if the predicted hitbox meets the standing body, it simulates both dodges against that same prediction, the jump arc from the motor's jump velocity and gravity, and the step-back with the motor's acceleration, and takes the first that never intersects: jump, then step-back if there is body room two tiles that way. If neither clears the threat it takes the hit and rests a third of the refractory period rather than moving into it. A reflex holds the body for 8 ticks and then rests 45. It bypasses scoring on purpose: a reflex that waits for a score arrives late. It knows nothing about projectiles yet; hostile projectiles are the next thing to add, as a second loop over `Main.ActiveProjectiles`.

## Traps

- **"Jump at anything approaching" lifted the body into flyers.** The first run (2026-09-08) had the companion jumping into a Demon Eye at head height, because the only check was "not coming from above". The simulation replaced it; a rule about approach direction cannot express "where will my body be".
