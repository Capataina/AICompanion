# Gathering — picking things up

```
Gathering/
├─ CLAUDE.md
└─ LootAction.cs   the nearest pickup that fits somewhere (player stack or bag) and has a standable tile beside it; forecasts the trip
```

The bag rules are in `../../../Inventory/`; the action only chooses which item and walks. Contact pickup happens in `CharacterBody.CompanionNPC.CollectTouchedItems` whatever the action.

`Prepare` discovers a candidate and captures its position, item type, distance value, safety factor and trip duration. `Score` and `ForecastTicks` read only those captured values; repeating a comparison cannot rescan the pickup list or silently switch targets. The item reference is the execution binding rather than a source of scoring facts. Before requesting movement, execution checks that the item remains active, has quantity, retains its type and still fits in cargo. This guards ordinary disappearance and replacement, but does not establish a spawn generation for same-type item-slot reuse. The next preparation refreshes moving-item positions and permissions.
