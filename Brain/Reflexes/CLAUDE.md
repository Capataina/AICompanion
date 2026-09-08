# Reflexes — the fast path

`Reflexes.cs` runs before the chooser every tick. It looks 20 ticks ahead along each reachable threat's velocity; if the predicted hitbox meets the companion, a sideways approach gets a dodge-jump and anything else a step-back, and the reflex holds the body for 8 to 10 ticks. It bypasses scoring on purpose: a reflex that waits for a score arrives late. It knows nothing about projectiles yet; hostile projectiles are the next thing to add, as a second loop over `Main.ActiveProjectiles`.
