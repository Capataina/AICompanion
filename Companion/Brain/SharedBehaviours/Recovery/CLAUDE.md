# Recovery — distant flight home

Visible catch-up outside navigation. Flight ignores terrain until a clear landing near the owner exists. It supplies velocity to the sole motor, never a route edge, so waiting or flying cannot enter the executed-route archive. No teleport. Recovery is an explicit coordinator branch, not a movement ability — the unbuilt mastery tree may add movement methods like flight, but those would be methods the activities invoke, not a system that owns the feet at coordinator level.

```
Recovery/
├─ CLAUDE.md
└─ RecoverDistantCompanion.cs
```

Guarding, hunting, collecting and work destinations do not grant recovery even when their coordinates equal the player's. Flight ends near the owner only with a clear body, cancels on downing or owner death, and never becomes a traversal or archive entry. Independent weapon targeting continues while the feet fly.

## Traps

- Recovery requires two conditions: a WithPlayer reunion request from an activity with a free hand, and a distance beyond the recovery threshold. Missing either, recovery does not start.
- Downing and owner death both cancel recovery, but downing also suspends the activity that issued the reunion request, so resuming that activity on stand-up is a fresh decision.
- Recovery is not a mastery ability. The unbuilt mastery tree's flight would be a movement method an activity scores and invokes; recovery is a fallback the coordinator runs when no ordinary work exists.

## Current state — 2026-09-14

Recovery flight is a coordinator-level reflex that activates when the companion is beyond the recovery distance and has no ordinary activity to execute. It requires an activity to have issued a WithPlayer reunion request with an available hand. The separate mastery movement abilities are unbuilt and will be methods activities invoke if built, not independent systems that own the feet.
