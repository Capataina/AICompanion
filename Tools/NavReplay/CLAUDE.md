# NavReplay — the game-free movement core, proved without Terraria

`Program.cs` compiles the movement core under `Companion/Brain/Infrastructure/Movement/` — everything except `TerrariaIntegration/` — with no game assembly at all, and runs it against text worlds. It is the first check for a navigation claim, because the search, the contact, the smoothing and the steering it runs are the same source the mod runs rather than a copy: the core is one compile unit here and one in the mod, and a property proved here is a property of the orb the game moves. It launches nothing and infers nothing from a picture.

```
NavReplay/
├─ CLAUDE.md                     this guide
├─ NavReplay.csproj              lists its own sources by name, then globs the movement core and the tunables it reads
├─ Program.cs                    the self-test's three ledger rows, and the scenario extractor's entry
├─ ReadScenarioBlocks.cs         a scenario file as blocks: header keys, the glyph grid, the actors and the trail
├─ VerifyCorridorMiddle.cs       the clearance cost, the smoother and the steering law, each measured against the same corridor
├─ VerifyArrivalHolds.cs         a navigator driven with the motor's own law to its goal, then held there and watched
├─ VerifyMirrorExactness.cs      the reflection transform proved an involution over the committed corpus and a flood proved symmetric
├─ MirrorScenarioWorlds.cs       a block reflected left to right: tiles, glyph pairs, the actors, the trail and the orb's centre
└─ ExtractScenarioFromCapture.cs a recorded tick's terrain cut out of a capture into a committed-format scenario
```

**`NavReplay.csproj` lists its own sources**, because `EnableDefaultCompileItems` is off: a file added to this folder and not added there does not fail to build, it is simply absent, and whatever row it carried reports nothing. The movement core arrives through a glob, so a new core file is compiled here without anyone editing the project, and a core file that names a Terraria type breaks this build before it breaks the boundary script.

## Two commands

```
dotnet run --project Tools/NavReplay -- --self-test
dotnet run --project Tools/NavReplay -- --extract-scenario <capture.tsv> <tick> [--size WxH]
```

The self-test is three ledger rows, each a property of the core with no scene of the walker's kind behind it, and `sh Tools/verify.sh` runs it after the ledger's own self-test. It runs with the millisecond allowances lifted, because a corpus verdict is an oracle only when it cannot depend on how busy the machine was; nothing here is about a deadline.

## The three rows, and what kills each

**The route through a corridor sits nearer its middle than its walls, and the steered body stays there.** One corridor with a bend, flooded twice: once with the clearance term switched off, once with it on. The unpriced route must hug the wall it starts beside — a premise assertion, because without it the comparison measures nothing — and the priced route must sit nearer the middle by a clear margin and within a tile of it on average. Smoothing is then applied and must not pull the route back toward the walls, and every smoothed segment must be clear of them by the contact's own swept test. Finally the navigator steers a body along the result under the motor's law, and the body must arrive, fly within a tile of the middle on average, and never overlap a wall. Two measures travel with the row: the priced route's mean offset from the middle and the steered body's, both in pixels and both better when lower. The row is what refused the first smoother, which skipped from one wall-hugging end of the corridor to the other and undid the search, and what raised the acceleration multiple until the steered body kept clearance through the bend.

**A body the navigator has called arrived stays arrived, on one search, and comes to rest.** A navigator is driven along a straight corridor tick by tick with the motor's momentum law; once it reports arrived the row keeps driving for a hundred and twenty ticks and requires the status to hold, the search identity not to change, the body never to coast past the arrival radius, and the speed to fall under the settled threshold in `BehaviourWeights`. The measure is the ticks held. It fails on the walker's own mislanding shape — a body coasting out of the radius and replanning — and it was made to fail before it passed: a navigator that pushed at the pace after arrival lost the arrival after six ticks and moved to a second search.

**The corpus mirror is exact.** Every committed block is reflected twice and must come back as itself line for line, beside a check that at least one block actually changed once, because a transform that returned its input would satisfy an identity perfectly. Floor slopes must flip to their pairs, a symmetric glyph must survive, a short row is padded with solid on the right before it is reversed or the reflection moves that wall across the window, and the header's recorded tiles and the orb's centre reflect through the window's own span. A left-right symmetric room is then flooded both ways and must reach the same corners and the same goal, which is what makes the mirror mean something about the search rather than only about the file.

The mirror is here as a transform and a proof of the transform, not as a replay of the corpus: the walker's corpus verdict — plan every block, follow every route, compare the two directions — was a verdict about a body and a graph that no longer exist. What is kept is the property that nothing about a tile world prefers a direction, so a route proven one way must be proven the other, and it is checked on one symmetric room rather than across a corpus of walker fixtures.

## Cutting a scenario out of a recording

`--extract-scenario` cuts the terrain around the companion at a recorded tick out of a capture's own `terrain-snapshot` events, with the orb's centre from `npc_px`, the destination the brain asked for on that tick, the player's feet and their trail, and writes it in the committed scenario format `Tools/Scenarios/CLAUDE.md` describes. It is how a failure the player can feel becomes a window the world run replays, and it is what produced both committed checkpoints. **A tile no snapshot covered is written solid and counted in the reported coverage, never left as air**: the glyph alphabet reads anything outside it as air, so missing terrain would become open sky and a window with holes in it would read as a window with an easy route. Snapshots are joined to the row on the recorder's own stopwatch rather than on the tick, because the two streams have different producers and only the elapsed millisecond means the same thing in both. **Coverage is coverage as *last written*, not as it stood at the tick**, because the recorder writes a chunk only when it has changed since it last wrote one, so the header carries how far behind the tick the oldest contributing snapshot was, and that number prices a full-coverage window rather than the percentage. Which recorded columns are pixels and which are tiles is read off the column and never inferred from the text.

The extractor's own liquid fidelity is the text world's: a wet tile is water or lava with a full cell, and the walker's captures carry `~` in their snapshots. Both committed checkpoint windows hold no wet tile in the saved world they are replayed in, and the water-pocket row says so when it runs; the snapshots' `~` tiles lie outside those windows.

## Traps

- Marker glyphs are air. Move actors through the header keys — `orb x,y` is the centre in pixels — never by writing a marker over a slope or a platform.
- `MovementQueries.World`, `FreeSpaceSearch.WorldOverride` and `ClearanceField.Shared` are process statics the core reads; a case that plants a world and does not clear it hands the next case its terrain. The self-test resets them per row and any new row does the same.
- `OrbPace` is written by the motor in the game and by nothing here unless a row writes it; a row that does not is steering the body a plain player would produce, from the fallback in `BehaviourWeights`.
- A green self-test is evidence about the core. It never substitutes for the native suite, where the same contact runs against Terraria's own tiles, or for a world run, where the whole brain runs behind a recorded player.
