# Gathering — picking things up

```
Gathering/
├─ CLAUDE.md
└─ LootAction.cs   the nearest pickup that fits somewhere (player stack or bag) and has a standable tile beside it; forecasts the trip
```

The bag rules are in `../../../Inventory/`; the action only chooses which item and walks. Contact pickup happens in `CharacterBody.CompanionNPC.CollectTouchedItems` whatever the action.
