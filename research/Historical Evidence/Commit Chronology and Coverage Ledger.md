# Commit Chronology and Coverage Ledger

Source revision: `d60b92b10008d478073f10f2c545d332b4819dde`, read 2026-09-12. This ledger covers the 176 commits reachable at `4296f851`, then the documentation-only research commits `d6b353d` and `d60b92b`. It records historical claims, not fresh verification.

## Complete coverage method

`git rev-list --reverse 4296f851b13e49ccd557d7255ec24bc223a29929` returned 176 hashes, from `48f8709` to `4296f851`. The full chronological subject/body stream was read as 744,680 bytes in contiguous 20,000-byte chunks; chunks 0–36 were full and chunk 37 was 4,680 bytes. `git log --reverse --format='===%H%n%ad%n%s%n%B' --date=short 4296f851b13e49ccd557d7255ec24bc223a29929` reproduces that byte count. The revision argument is a single full hash: appending three dots would change it into a symmetric-difference query and would not reproduce this corpus. The two later research commits are `d6b353d` and `d60b92b`.

## Chronology by complete commit ranges

| Range | Accounted-for commits | Historical result |
|---|---:|---|
| `48f8709`–`2ec6802` | 1–7 | The initial NPC, then the priority FSM (`Downed > Shoot > Chop > Wander > Follow`). The first play evidence corrected rendering, chopping and companionship details. |
| `85961e`–`11764c6` | 8–15 | A second playtest made competing needs explicit. Senses, utility chooser, positioning, A* and reflexes replaced the FSM; early adversarial reviews corrected scoring floors, danger facts and work/tool seams. |
| `9500e18`–`f4da390` | 16–47 | Shape-aware terrain, replay capture and performance instrumentation grew around navigation defects. These commits are largely route/body work, not evidence against chooser selection. |
| `985195e`–`67cdf38` | 48–77 | Drops, returnability, jump run-ups and stranded-pocket behaviour were made explicit. The record repeatedly separated a route's abstract promise from what the NPC body could execute. |
| `b5fa96e`–`ba6c146` | 78–104 | Platform descent, independent firing, tri-state reachability, urgency/commitment and session reading were repaired or instrumented. |
| `09fe0d7`–`3273d0f` | 105–112 | Procedures, actual-entry movement validation, native parity and persisted/retained travel landed. The portable corpus remained mixed and live acceptance open. |
| `30851ce`–`4cd88e1` | 113–139 | Observation, weapon/route memory, native interface work and activity retention expanded; reporting increasingly distinguished contracts from gameplay acceptance. |
| `9b6e1fd`–`4296f85` | 140–176 | The 11 September telemetry-led repair batch tightened movement completion, hunt admission, ore approach and commitment. README work explicitly reopened utility scoring and A* for architectural discussion. |
| `d6b353d`, `d60b92b` | 177–178 | Research-only framing: preference/activity continuity/physical execution are separate, and the architecture agenda is scoped; no gameplay conclusion changed. |

The ledger's main caution is chronological: an implementation commit and its playtest evidence are distinct events. Builds, portable replays and native fixtures establish narrower claims than recorded gameplay.

## Individual commit index

Each row preserves the exact historical subject, including claims later qualified or reversed. A subject is evidence of what the commit said, not a current behavioural assertion. The final column counts whitespace-delimited words in Git’s `%B` subject-and-body field; it documents the reading surface, not result quality. Recover a complete message with `git show -s --format=fuller <hash>`.

| Index | Commit | Date | Historical subject | Message words |
|---:|---|---|---|---:|
| 1 | `48f8709fe3d7a4b4af80a8fc5690d748e57f3980` | 2026-09-07 | The companion mod exists: a friendly NPC that follows the player, spawned by /companion, building clean against tModLoader | 245 |
| 2 | `292e1949a0daf9469cf5cb9d22197eff1ba1e693` | 2026-09-07 | The mod is singleplayer only, and the follow AI reads Main.LocalPlayer instead of searching for the nearest player | 122 |
| 3 | `0f8a5b02cf3a50d3e35a4ce81f835c54209e419a` | 2026-09-07 | tModLoader flattens the localisation file on its own build, and that shape is the committed one | 69 |
| 4 | `7125353e0944140283861809ece592aea51bfe11` | 2026-09-07 | The companion gets a body, health, wood-chopping and a bow in one batch, and every mechanic reuses the game's own path for the player | 1328 |
| 5 | `33e34c92e3cffd1372c41b9eeb2e8081184e6297` | 2026-09-07 | The folder file and README describe the companion that now exists, and record the read-the-game-first rule | 201 |
| 6 | `1e26bbb687d9bce1d3c68554c7525d91875ad7f6` | 2026-09-07 | The first in-game run: trees are chopped at the trunk instead of the dirt, the tool shows in hand, the body is the player's own look made female, and the leash is most of the screen | 588 |
| 7 | `2ec680243a3f405c7786797e715891d282ca9f8a` | 2026-09-07 | An adversarial read of the first batch against the decompiled game: the body draws in a closed batch, cracks land on the tile, palms and cactus count as trees, the aimer stops spinning, and the companion keeps moving while it shoots | 838 |
| 8 | `85961eeba2ec847cb367064120e890696b47c4a1` | 2026-09-08 | The companion gets a brain: senses, a utility chooser, a position scorer, an A* navigator and reflexes replace the state machine, with a bag it fills by picking things up and a right-click panel to open it | 1091 |
| 9 | `0b7e0c37fafead5b5b616786e231fd229bfcf3c9` | 2026-09-08 | Two adversarial reads of the brain against the decompiled game: the bag opens the inventory instead of closing it, wander can win, Demon Eyes stop pinning the horizon, a failed plan waits before retrying, and pickups route like the player's own | 1061 |
| 10 | `a02c0d52b3b19991b14d092d1895b71ce3946b92` | 2026-09-08 | Build + Reload kills the game inside tModLoader's own unload, so the mod is built from the shell and launched fresh, every hook logs its unload, and the overlay key moves to the key left of 1 | 504 |
| 11 | `2c3ff7cd4bd3d27d753b44ab4e6d01af1ef8800b` | 2026-09-08 | First run of the brain: the companion walks kerbs instead of jumping them, only dodges when a simulated jump or step actually clears the threat, takes knockback, and the bag draws all its slots behind a scrollbar and sorts itself | 879 |
| 12 | `67a8c96b5c64bd995c7c7989ee34efa631ff0e5f` | 2026-09-08 | The companion holds a torch in the dark and reveals only what it lights, mines ore beside the player, shows on the map as its own head, and wears its health as a notch | 832 |
| 13 | `5319a7e7de10d8067cdc9b741db293b349a1f618` | 2026-09-08 | The brain folder now separates how a choice is made from what can be chosen from how it is carried out, so a new action family is a folder rather than a longer list | 453 |
| 14 | `67fcf3517fef0eafc3a7aa80198ba996acdcf908` | 2026-09-08 | Brain/CLAUDE.md is now the account of how the companion thinks, and the tool comes out only in position so the torch is held on the walk | 351 |
| 15 | `11764c6f3dc792b1a6f35adc7bb7f18797cb025e` | 2026-09-08 | An adversarial read of the torch, mining, map and notch commit: the map head never drew, the copied pick formula was wrong on three real ores, the torch read the player's light instead of its own, and the reveal redrew the whole map | 1149 |
| 16 | `9500e1861fb5ac432a24c506bf3c8d4920515fd6` | 2026-09-08 | Brain/CLAUDE.md keeps the shape of every rule and drops every tunable, so the account of how the companion thinks stays true when a number moves | 433 |
| 17 | `880fedc4e5409eccb88fe4536ded4c43f659098d` | 2026-09-08 | The ambient light window is clipped to the screen, and the chop search runs on a trigger and a cooldown like the mine search | 568 |
| 18 | `1e2d85ebc39991a684a5c1e4076677d0c8051ba3` | 2026-09-08 | Navigation: kerbs stop being jumps, the motor moves with the player's momentum, and the grid learns platforms fall through and water is slow | 783 |
| 19 | `634e7da6ebde216b90957e46a3b8ff0bbfeafbc4` | 2026-09-08 | The overlay key goes back to F6, because the key left of 1 is dropped by FNA before it can become a key on this keyboard | 271 |
| 20 | `9e2fda04290174ad0556d3ab8c801973bc2a8be2` | 2026-09-08 | HUD: the notch has a hairline outline that reads at night, and clicking it opens the bag while dragging needs a held press that travels | 276 |
| 21 | `b0c042de00b76c078ae700b7a096c2e797f889e2` | 2026-09-08 | Telemetry: every companion tick is written to a file under the repository, so a playtest is diagnosed from the record instead of from memory | 449 |
| 22 | `67b8cebf1ce7f415ab0566ffe7f14db6b8632b44` | 2026-09-08 | HUD: a mode icon beside the notch shows what the companion is doing, drawn from the game's own item sprites | 243 |
| 23 | `a024975fd66d33826448859a276e5b03a075e904` | 2026-09-08 | Enemies hunt the companion: its stand-in player sits in a player slot and is visible to a hostile's AI for exactly the span that AI runs | 639 |
| 24 | `3c9fc2de7d769837a530ac349a5f10633133fbb2` | 2026-09-08 | The companion cares about its own body: it breathes and drowns like a player, a self-danger sense reads what is happening to it, a survive action gets it to air or out of fire, and the grid prices water and lava instead of banning them | 745 |
| 25 | `81116f9238540552b6ff6cf485d581457d6851d9` | 2026-09-08 | The dodge steers away from the threat and its simulation carries the body sideways against every threat, so a jump no longer lands on the enemy it was chasing | 259 |
| 26 | `849983ee283c3ae831ec00c6f929b436d80b462b` | 2026-09-08 | The notch releases its textures on the main thread at unload, because unload runs on a worker and FNA3D throws there; the mod unloads cleanly again | 230 |
| 27 | `2e9b01f6a9708575927ccbfc870ce454a0ae3aad` | 2026-09-08 | Bag: it stays open at any distance, sorting no longer paints the player's inventory, and a hundred coins roll up into the next coin | 589 |
| 28 | `df9ac149b833a9280c83de0beedc695172f1b349` | 2026-09-08 | Companion: a held torch sits in the hand, because the body applies the game's hold style when no swing is playing | 208 |
| 29 | `6f3ceb8d64febbf9ebeeeae928e5422c6bf7f5c3` | 2026-09-08 | Brain: an enemy anywhere on screen is worth the full hunt, so a zombie beats a tree and the torch stays away during a fight | 375 |
| 30 | `1c72ab769964e585c5ead26eb0b951e40d50216f` | 2026-09-08 | Navigation: the companion drops into the player's shaft, walks toward a goal it cannot fully reach, routes round enemies, and dumps the terrain of every plan that fails | 839 |
| 31 | `17349385fce1f44ee00eb62752a31a9727c5a572` | 2026-09-08 | Five findings from the Codex review of the aggro batch fixed: the motor no longer reverses instantly, the stand-in is hidden after every NPC pass and emptied on world exit, and the telemetry survives a bad write and reports the player's death | 576 |
| 32 | `18dacb4b98b355be8eaecfc8e9bd888e36533531` | 2026-09-08 | The reload-death trap is rewritten: the notch was not the cause, the deaths have two signatures, and a thread dump is the next instrument | 261 |
| 33 | `2c021e08c01b77d659254d0afb70dda1110d77e7` | 2026-09-08 | Navigation: the body plans only from the ground and a jump never ties with a walk, which ends the hop-along-flat-ground loop the last batch introduced | 427 |
| 34 | `1b4bcbd7b6feccaf30bcb0e703981ebb0c321474` | 2026-09-08 | Eight findings from the Codex review of the afternoon batch fixed: a partial path no longer counts as reachable, a fall-through keeps its platform, actuated blocks are air, and an on-screen enemy is always a hunt candidate | 671 |
| 35 | `f6c3171f4ba67b84ee7de8ab13fedff2d33de243` | 2026-09-08 | Navigation: the planner reads tiles through an interface and a console tool replays every failed plan from the telemetry dump without the game running | 908 |
| 36 | `807df2ee92d0c7aaa671da521597925ad6023aa5` | 2026-09-08 | Navigation: a node is a place the real body fits against the tiles' shapes, so a worldgen staircase of slopes is walked, and the replay tool passes only what it truly ran | 1207 |
| 37 | `305a05a9eb7686175122ef1af5ac736917cec359` | 2026-09-08 | Docs: the folder files describe the shape-aware grid, the world interface, the replay and reshape tools and the scenario database | 219 |
| 38 | `c189af4f005863845596b7433eae0746a3f2e78a` | 2026-09-08 | Navigation: Stand refuses a full block before probing, because the feet can never be inside one | 216 |
| 39 | `1087a450c58c9877a8786a991dc8aee2adbd6083` | 2026-09-08 | Docs: the unreachable-goal trap names the positioner's scoring, not the navigator's snap, and points at AIC-135 | 116 |
| 40 | `5086ab760d3998d99211b3bd7321b60a8ec7073c` | 2026-09-08 | Inventory: a picked-up coin drains into the purse until nothing fits, because a conversion frees the slot the bag was handed the remainder of | 269 |
| 41 | `37d74a2e7b129bb0ce04cbae20b5f0b68807f06e` | 2026-09-08 | Positioning: a spot the walker cannot reach is not a spot, so the positioner floods the walker's edges from the companion's feet and scores only what lies inside | 580 |
| 42 | `58687e973fb5845e550d15d00e2ca30fb1ff3fda` | 2026-09-08 | Navigation: a jump edge is the follower's own jump simulated against the tiles, and the follower coasts onto the landing instead of hunting past it | 728 |
| 43 | `100711e0f6393f7767a14f6152249eaf213a94a6` | 2026-09-08 | Telemetry: a follow failure, a stuck run, a hit through a dodge and a missed mode each write a replayable scenario, and every dump carries the player's trail | 435 |
| 44 | `32ebee351f17ad0df26136225d3cff57758e059c` | 2026-09-08 | Navigation: six findings from the Codex review of the shape-aware grid are fixed forward, two of them regressions on ordinary terrain | 848 |
| 45 | `548e863525647b7781e1a0d0933098a107f08e03` | 2026-09-08 | Telemetry: the last plan's and the last reach flood's wall-clock are two columns of the per-tick record, so the next recording measures the pose grid's frame cost | 140 |
| 46 | `f10e7c39f31134237fe29f95e5cbf41062a46b8b` | 2026-09-08 | The fifth playtest build is 0.4.9: coins drain into the purse, the positioner offers only reachable spots, jump edges are simulated, four scenario detectors write replayable blocks, six grid-review defects are fixed forward, and plan and flood wall-clock are in the record | 97 |
| 47 | `f4da390caf70e5e0a0ff7808d1983da336d8d66b` | 2026-09-08 | Navigation and telemetry: the Codex review of the batch lands twelve findings, nine fixed here, the frame cost cut sixfold and filed for the rest | 911 |
| 48 | `985195ebfb52b548149066f6388dc5cf6612ae5d` | 2026-09-08 | Navigation: every edge carries how the body makes it, the follower makes it that way, and a body that stands still changes the plan instead of repeating it | 1489 |
| 49 | `baa1e58662dfc271b90eac869ecc162b7e1d634c` | 2026-09-08 | Scenarios: the fifth run's two ledge climbs and its platform lip join the corpus, so the jump profiles and the body-lowered drop are replayed on every planner change | 187 |
| 50 | `6a87298d931cf855c653e8c2478324e1979b56f7` | 2026-09-08 | Inventory: a coin the companion picks up takes the player's own pickup path, so it lands in the purse and rolls up the way the player's coins do | 285 |
| 51 | `3e0da59e7aeea50115e0341658a04ee20f43cbdc` | 2026-09-08 | Enemies spawn twice as often while a companion is up, the description says so, and the sixth playtest build is 0.4.10 | 210 |
| 52 | `e419262b74f4795e398995e99cad9b981c8af9dd` | 2026-09-08 | Senses: the threat sense's walker search takes any drop, because the brain's per-request one-way rule was leaking into it and a zombie above a cave read as unreachable during a hunt | 289 |
| 53 | `e1026b09959dab86dc87c5a7b100ffb563d7f2de` | 2026-09-08 | Navigation: the run-up coasts back onto its mark and belongs to the jump edge, so a floor-capped runway is not walked off the back and a replan does not hand one jump's run-up to another | 345 |
| 54 | `d508cfe6c9945563e7f98d854b9d196ac5af3443` | 2026-09-08 | Inventory: every coin pickup logs where the coin went, so the next playtest measures the purse instead of the report guessing a third time | 136 |
| 55 | `e607783023e2d94da2341a4164ca634da210495c` | 2026-09-08 | Scenarios: a window is widened from the saved world and the replay tells a pocket the world seals from one the capture cut short, and the run-4 pit turns out to be the grid's and never the world's | 924 |
| 56 | `65c1f21812889d3c5a69e36af962d90eb70689c7` | 2026-09-08 | Tools: the reshape script keeps the dump's markers line out of the tile rows, and a sealed block prints SEALED as its first line | 380 |
| 57 | `420b7464aee4bbc853de2f18e97f24604fcb96f7` | 2026-09-08 | Scenarios: the fifth run's dumps join the corpus, and its two pits were sealed in the world and built out of by hand, which is the ruling's own case | 583 |
| 58 | `7afefe1f3b5f5afd87e984b6673f75c9aa5733de` | 2026-09-08 | The replay reads the player's trail with his double jump in mind, by his own note | 129 |
| 59 | `3f8d07975522027b8c5e13a7e35d8b29334b9f07` | 2026-09-08 | Telemetry: a plan dump's window is wider than the screen by default, so the replay sees the way out and the routes not taken without widening afterwards | 215 |
| 60 | `68ccd5e69d810f7dd0e0f10bd46c8c877b952ff0` | 2026-09-08 | The reload hang is the collector's mark phase spinning inside the loader's own unload collection, sampled alive | 231 |
| 61 | `404981b1823ad35c2f39dd0b7f0635b252b1aba9` | 2026-09-08 | Telemetry: every phase of the brain tick is timed, so the lag has a column per suspect and the overlay shows the tick's cost live | 380 |
| 62 | `84520d285235f4261c6401e378c509a9ba582d1e` | 2026-09-08 | Navigation: a drop is never offered onto a platform, and every tile's edges are cached between searches, which is the run-6 regression and the run-6 lag in one rewrite of the edge generator | 816 |
| 63 | `87e8d204bd73e3449054121f48c50f402cd8bfba` | 2026-09-08 | Navigation: a killed or placed tile drops only the cached edges that could have read it, and the replay breaks every tile under a path to prove the box | 829 |
| 64 | `230380268d6a6e18193563b780331fa724003ac7` | 2026-09-08 | Brain: a companion sealed off from the player walks its pocket instead of pressing the nearest wall, on a roam-then-retry cycle the brain counts | 759 |
| 65 | `b33ed4c49f9e20b098c927c6005123e4169da3fa` | 2026-09-08 | Navigation: the Codex review of the edge cache and the stranded roam is fixed forward, and a running roam no longer outscores every fight | 876 |
| 66 | `6b165d7ea7e3940e5d43dd024f8957f9982a99fb` | 2026-09-08 | Navigation: the folder splits into World, Body, Planning and Following, one owner each, with no behaviour change | 409 |
| 67 | `2eb7ad2afb6c480130eac31585a855f28d9f4bca` | 2026-09-08 | Body: one tick of the body is a function, BodyMotion.Step, and the jump simulation is a loop over it | 654 |
| 68 | `20b080a880441642338b02f6dd635a7ee19d0c41` | 2026-09-08 | Navigation: each move is a traversal that proves and performs itself, the navigator runs on a body seam, and the replay walks every path it plans | 1033 |
| 69 | `bd2edb6208f51e987bfdbcc55093d6af4a91de3b` | 2026-09-08 | Navigation: every move is proven by driving the body, the kerb rules are the game's, and the follow harness walks 66 of the corpus's plans against the last commit's 30 | 2302 |
| 70 | `7525a1b566eaccf971d28eeebd508924126a97c9` | 2026-09-08 | Navigation: the search is keyed by a body state, a tile and the mobility the body arrives with, so an air jump or a dash plans as an edge without a second rewrite | 659 |
| 71 | `23318efe8537bae2f4612ef476c361bca708e1ec` | 2026-09-08 | Navigation: a leftward jump asked for a fifth of the runway its mirror asked for, and four more places where the proof and the performance of a move had drifted apart | 1501 |
| 72 | `802268e8c406be2d2d4994b727bebfc3e0d4ffd3` | 2026-09-08 | Navigation: walking a partial plan to its end is progress rather than a failure to wait out, and a body going nowhere counts as stuck even when it has no step in hand | 1029 |
| 73 | `1ed43aed6f1680d00267863191a03a4c088a2e8d` | 2026-09-08 | Survival: a companion in a flooded pocket treads water instead of standing on the bottom until its breath runs out | 397 |
| 74 | `aae82c53cd2ca804b0bc3b51e9c02d822b86b38b` | 2026-09-08 | Navigation: whether a drop has a way back is a question asked of the search rather than a depth of seven tiles, and the positioner's reachability tier is what decides whether to take one | 1619 |
| 75 | `3cf719c1ada23dd1d83e2a98bf7806f3d90078aa` | 2026-09-08 | Navigation: a corpus fixture and a review each broke the drop-prevention rule that landed an hour earlier, so the probe reads its endpoints under the search's own lava rule, proves pockets eight times larger, keys its verdicts on that rule, and the flood keeps its refusal only while it still reaches the player | 2066 |
| 76 | `66005242f87b22d4ec9a9dce70f19fc037ffec68` | 2026-09-08 | Debug: the reachability tier is the one decision no offline pass can watch, so a session records it per tick and dumps a window when the body commits to a place with no way back | 1025 |
| 77 | `67cdf38daa03f7860635bdef5735ef1c4be8583d` | 2026-09-09 | Positioning: the player exception asked only whether he was outside the returnable region, which is also true of a player nothing reaches, so a companion walled off from him opened the tier against itself | 959 |
| 78 | `b5fa96ef4d81abdca7c8ab1ee3191196848647f1` | 2026-09-09 | Navigation: the fall-through press was released on the first airborne tick, so the game put the platform back under feet that had fallen half a pixel and no companion has ever descended a shaft capped with platforms | 942 |
| 79 | `4a74b78596fe95b064c93c83ab87c93fb2d13667` | 2026-09-09 | The root map lists Traversals/, which has owned every move kind since the traversal work landed | 126 |
| 80 | `e5882419fd5a78018e39a884a47a884cf8cbf92a` | 2026-09-09 | The agent harness's run logs are ignored, because one is 29 MB and nothing was stopping it reaching a public repository | 138 |
| 81 | `bcd1e372ccfb970f3b4bcf11d91b47603f968ad5` | 2026-09-09 | Combat: the weapons stop holding opinions about when to use them and the arsenal picks by the damage a shot would actually land in the next three seconds, which is what makes a roster of ninety weapons need no per-weapon AI | 1542 |
| 82 | `7fb1bb49e097a39dd6e67ee576cf9461b1d55135` | 2026-09-09 | Positioning: guarding the player was scored by distance to the player and nothing else, so with the threats standing on him every good guard spot was inside the melee and the companion walked into enemies it could already shoot | 1134 |
| 83 | `e539fe8a2940504d15f8b4f9402fc6681ad42e6e` | 2026-09-09 | Debug: the overlay moves to the left square bracket, and every key it answers to is read raw, because a binding saved by an earlier version outranks the registered default for ever and that is what actually killed it | 634 |
| 84 | `7e89e982ddfae7f883127ceb9a24a3aaa4dee5f6` | 2026-09-09 | Navigation: the fall-through press is held until the platform's own row is cleared, not the lip's | 699 |
| 85 | `787832fe41ad73e0b56a76eacd39a35a048c671c` | 2026-09-09 | Senses: the companion has a sense of its own danger, and both danger numbers aggregate as independent hazards instead of a maximum | 899 |
| 86 | `b2965678d532d1fe04614cc9c1b390af6a47446a` | 2026-09-09 | Combat: attacking stopped being a mode — the hands shoot every tick whatever the feet were told, and hunting is only the decision to walk toward something | 1558 |
| 87 | `0281c2bf8d7fea2c3c24f4bd4baf26ff60faac54` | 2026-09-09 | Tools: a session is now read by a tool that sorts what it finds into definitive issues, potential issues and oddities — and its first run found the recorder inventing scenarios | 1410 |
| 88 | `ce15ec26626c556355ddf96835222f17f86b3855` | 2026-09-09 | Tools: three of the reader's own checks would have misfired on the first session written by the recorder it shipped beside | 1013 |
| 89 | `8ee21ab7ddccfcb5d33dc981ff4fcb1ba7ee804c` | 2026-09-09 | Debug: the recorder writes the companion as a rectangle at a pixel and says what the engine did with it, because three attempts at the platform freeze were argued from cells that describe the previous tick | 1512 |
| 90 | `96735de69e71c94b376c2fb6e7457a92e43407d6` | 2026-09-09 | Packaging: the mod stopped shipping the agent harness's session logs, which were 97% of the .tmod | 308 |
| 91 | `b64c1394069d290a566b25f472441a9441b4bfb2` | 2026-09-09 | Navigation: the motor stops climbing what the plan is descending, the follower gets a reactive floor, and a decision survives longer than seventeen ticks | 2614 |
| 92 | `f623975f6e5f14a234e67e7cd78e04c083de3353` | 2026-09-09 | Debug: the recorder answers "was that competent" and not only "was that consistent", and two of the defects it was built to confirm turn out not to exist | 1870 |
| 93 | `e625e5d754420c195054102f84adf5b80f02051f` | 2026-09-09 | Docs: the folder files stop describing the behaviour that was replaced, and 0.8.0 packages | 884 |
| 94 | `2a6b816a978d6f434cb5899625640bc4cf4dc687` | 2026-09-09 | The body's box says where it came from, because two files declare it and nothing said they must agree | 359 |
| 95 | `b1fafe868db1b3e3d72f45fd72d2cfe09b9439fd` | 2026-09-09 | The folder files say what each system is for, not only what its files contain | 966 |
| 96 | `b10a9887e294d4ab1075e87a5f0a4b0c9de88d68` | 2026-09-09 | Survival: breaking the surface is a floor under the escape plan, not the alternative to having one | 1253 |
| 97 | `0eb08bdc83f693a424b096a1d2cf6e5b65dd39e1` | 2026-09-09 | Urgency is a ladder scaled against the commitment bonus, not three actions each topping out at one, and a reflex no longer freezes the hands | 1087 |
| 98 | `c4bc752a8112a21f096b180897e27d10696a132a` | 2026-09-09 | Navigation: the descent's entry brake was latched, measured against the corpus and torn out, because clamping to rest is what hides the pose the body actually started from | 1013 |
| 99 | `544a0e91e133e48721ea8062c2260badfa1e9177` | 2026-09-09 | Reachability answers yes, no or "I could not tell", and self-rescue is the caller that reads unknown as no | 899 |
| 100 | `32f85f4879f0e00d231ff8514f520731f4370cf2` | 2026-09-09 | A partial path that ends on the tile it started from is discarded, because a non-empty result that achieved nothing disables every guard written against emptiness | 722 |
| 101 | `51dd4b6810e52063874af43518bb4adb963632a5` | 2026-09-09 | A stair is a sloped platform and the game does let you press down through one, so the comment claiming otherwise is replaced with the decompile that settles it | 754 |
| 102 | `84fed673c00491af8c7de4ef8850183cbc1dadf5` | 2026-09-09 | The version moves to 0.8.3 for the batch that lands before the fifth playtest | 113 |
| 103 | `65153c70026fb0b1fdf8c88c5ecc77c49dfa86d2` | 2026-09-09 | A move that ends a route is counted, because the census was reporting Jump completed 0 while jumps were landing | 556 |
| 104 | `ba6c146c15531f1cf3b92c63d2ff5e3d7cdd3fc1` | 2026-09-09 | The folder files catch up with the urgency ladder, the tri-state reachability and the stair gap, and the root stops claiming a commit count | 795 |
| 105 | `09fe0d7e6a765661f0f71d5d643e18e50fa641c4` | 2026-09-09 | Three procedures this session rebuilt by hand fifty-seven, nineteen and eleven times become three commands | 609 |
| 106 | `bfd09dab1d535cef87f0188f332d27166aa38f63` | 2026-09-09 | The 0.8.3 playtest dump joins the corpus raw, because reshaping it from the saved world would bake in the rescue | 613 |
| 107 | `caf1a842e99e6f1461a225cbc9309ae697c0110f` | 2026-09-09 | The refuge ring search got more expensive the day it got stricter, recorded before the session ends rather than measured | 275 |
| 108 | `2fc66e28f25cf8384ede793727a1e290e2298226` | 2026-09-09 | Every movement request is validated from the live body and recorded with its outcome | 907 |
| 109 | `08d36ec3dbaa5f9c88f746d6bd62e21302e103c9` | 2026-09-09 | Failed movement attempts stay observable across retries without accumulating search cost | 510 |
| 110 | `7b983c66fc6a93eaa43190ab9bd36614e7b0e417` | 2026-09-09 | The brain documentation describes the shared movement boundary and its measured limits | 442 |
| 111 | `a34eb015a335c086911c010356350c1d1dc3f763` | 2026-09-09 | The companion feature has one repository home with explicit integration boundaries | 275 |
| 112 | `3273d0f435a1dbcdb083f7951563136efa90e9f4` | 2026-09-09 | Travel validates the actual entry and optional excursions yield to regroup pressure | 539 |
| 113 | `30851ce4db27f501195b69bfeecaf913d0010c77` | 2026-09-09 | Unnarrated sessions preserve decision evidence and native outcomes beside continuous motion | 525 |
| 114 | `3e7770855808be39449ab6b436a9e08904df6497` | 2026-09-09 | Projectile choice and final launch share the native flight model and collision sweep | 402 |
| 115 | `373080aba5553b42ed3b305c49f07197f9bfb68e` | 2026-09-09 | Player damage events distinguish pre-hit health from the expected remaining health | 291 |
| 116 | `19b0a100140c9b4ba2051db5c2b2b99cfafc3d6e` | 2026-09-09 | The repository guide describes automatic observation and preserves unresolved navigation acceptance | 238 |
| 117 | `be4275bd3a7f84d94069f4e2af8a31debf1a5a88` | 2026-09-09 | Companion danger uses reachability to its own body independently of the player | 445 |
| 118 | `869f8201f6963e1880426395bee1f03d95724c0f` | 2026-09-10 | Weapon design gets a theorycrafting page, and the roster is twenty weapons with upgrade branches rather than one per class per boss | 1066 |
| 119 | `0f9d7f25fe3a1fcdd51289de3b5fb3907fee186e` | 2026-09-10 | The companion retains navigation work, remembers executed routes and acts on its own survival | 889 |
| 120 | `a72846a5b5a78a30993a2a8a35852f82ab8a084e` | 2026-09-10 | God's eye explains multiple playtests in a bounded interactive timeline | 487 |
| 121 | `28f5eb54f7f36f0cebf6f3d2aa59dea2193820c5` | 2026-09-10 | The repository guide records the navigation evidence and the unnarrated playtest boundary | 233 |
| 122 | `b570cab99a0f3a746e0876323cc6b8e6f57b8243` | 2026-09-10 | Weapon Experiments: the roster is rebuilt from scratch after a total rejection, every weapon gains two upgrade levels, and the arenas now have to prove the property rather than illustrate it | 1242 |
| 123 | `d10e9755c5b8616a47103a894c9cf943f44e43cc` | 2026-09-10 | Weapon Experiments: upgrades become mechanics rather than counters, two weapons are replaced, and post-Moon-Lord melee finally throws something | 1108 |
| 124 | `c8f4525c1c92b7c188a257f72bca033cc2703dcd` | 2026-09-10 | Route memory survives the native world-save format at full capacity | 257 |
| 125 | `449a79bbe212516481fac1dcd0d290b05d9dbcfa` | 2026-09-10 | Interface Experiments: the health notch opens a companion profile card, with the bag demoted to one view inside it | 1019 |
| 126 | `09d15a39d9e94ea6bd0c6f22cf49a4495b94adf7` | 2026-09-10 | Nearby resource work retains its policies and resumes the same ore vein | 418 |
| 127 | `08c0b480b056dba2008f0707dc6f17acf5f8311e` | 2026-09-10 | Following recognises vertical separation and recovers visibly without teaching flight routes | 639 |
| 128 | `5aabdd6ab7cbef1c79c0b3bb93389c4aef1860b2` | 2026-09-10 | Playtest reports distinguish follow progress, recovery and incomplete recordings | 470 |
| 129 | `723e4c8a74c6c25e4d04079d8d2bdb26691b58dc` | 2026-09-10 | The project summary describes the tested follow recovery and retained work release | 254 |
| 130 | `e215e0e21d6cf7ed5306facd472125200cd90b2c` | 2026-09-10 | Follow diagnosis ignores retained decisions while the companion is downed | 250 |
| 131 | `f0e058b1fd8c7a7bbc3fd70476766146a02968dc` | 2026-09-10 | Interface Experiments: the behaviour panel only offers what can actually be set, and the card is rebuilt in Terraria's own interface language | 859 |
| 132 | `175c48dece5dd09c3510244c919d162b68bd0396` | 2026-09-10 | Weapon Experiments: four weapons replaced or rewritten against the arsenal's own scoring model, and the model recorded as the design rule | 1397 |
| 133 | `253f438264f4d190a37fa3ccd8480aed14b606fe` | 2026-09-10 | Companion activities are configurable through a native card and a visual brain inspector | 687 |
| 134 | `0748bf12a1e0886ac315f1419578d3d507cee6b2` | 2026-09-10 | Folder knowledge describes configurable companion work and the permanent inspector | 255 |
| 135 | `426bddf8404bde2f845810fe9f936ce6f8fbc285` | 2026-09-10 | Movement and combat decisions are checked against the outcomes they promise | 686 |
| 136 | `9b3b46cb25dc6cf1ecb8b43ff8e6d01c58e16caf` | 2026-09-10 | The native companion card unifies behaviour controls, cargo and mastery preview | 484 |
| 137 | `09a9c77a89d491bf3db43eb946f45326df97d5f0` | 2026-09-10 | Session reports expose arrival deadlocks and underwater stalls across selected runs | 390 |
| 138 | `2f09f25738b4930cb4249e1ad7c0285ad8db1239` | 2026-09-10 | The project overview distinguishes tested contracts from live acceptance | 184 |
| 139 | `4cd88e1b08d42b14aa42fdde39cd0467b9eed28f` | 2026-09-10 | The saved interface references distinguish prototypes from the native menus | 257 |
| 140 | `9b6e1fda2b5fd81cc2718943f268b7d01d399ea8` | 2026-09-11 | The inspector's layers keep drawing after its menu closes, and capture follows the layers rather than the panel | 470 |
| 141 | `98a587a60e769902318585b11bf49d573768b96b` | 2026-09-11 | The move-completion check asks for a rate instead of firing only at zero, which is how it missed jumps failing nine times in ten | 719 |
| 142 | `656e94de44eb48c14062c45885e164c35d2d8a70` | 2026-09-11 | Switching hunt target no longer counts as engagement progress, so a crowd can no longer defeat the give-up guard | 585 |
| 143 | `231a478d7e6037efa2b06c57d97a0189722d9626` | 2026-09-11 | Firing spots are shortlisted with a sight test instead of a constant, so the chosen spot is one that can actually shoot | 932 |
| 144 | `b821afa82d1f4f43f9b54fda00e0068d9e9bf236` | 2026-09-11 | A hunt target now needs a reachable position that can shoot it, and how possible a shot is became a term in the hunt's score | 1053 |
| 145 | `ac0c23c4ee892a4dcf8a3f8d99585ea02d6dfda0` | 2026-09-11 | Mining walks at an ore whose approach the search could not decide, because scoring it zero was what stopped the search ever deciding | 935 |
| 146 | `0fcd7f48b544a8a70f3f075d15cc9cf0528e4c51` | 2026-09-11 | A session now reports a hunt that never got in reach, which the stationary check could not see | 521 |
| 147 | `c0ec32eb63713f4c8cd5b704e9dece2e791f0d79` | 2026-09-11 | The two things deliberately not changed are written down as properties, so neither gets rebuilt from the same symptom | 483 |
| 148 | `ea4a800dc1b60f59297abbb3b37d4d7748fd60e7` | 2026-09-11 | Establishing a firing opportunity holds its verdict across a few tiles of travel, because an exact-tile key missed on every tick of every approach | 485 |
| 149 | `f517b1a66333134a171a26d5f0ea5828a95553e2` | 2026-09-11 | Merge the combat and priorities lane: engaging only what a reachable position can shoot, and work that stops abstaining | 716 |
| 150 | `6a24df4a0852e2f83db630a087558507d2bc80a5` | 2026-09-11 | The companion card becomes one draggable native panel with an authored mastery graph | 1377 |
| 151 | `0d56ad6c1c37678b0ecff5c29fa4efcc7698fa44` | 2026-09-11 | The native companion card lands from the UI lane as one draggable panel with an authored mastery graph | 302 |
| 152 | `69c0166e72e0d19025f228e4b11a7d888e681d64` | 2026-09-11 | Following distance steps through its named stops again instead of wearing the work policy's segments | 416 |
| 153 | `791c0be19a2f748988919c69a8392ed846149719` | 2026-09-11 | Tools: the navigation boundary check refuses to run rather than passing when ripgrep is absent | 298 |
| 154 | `f1c424842efa58fbf80c2e3e018d502945e69e16` | 2026-09-11 | The documentation stops describing a tab row and a page called Cargo, and the version moves with the card rebuild | 356 |
| 155 | `9c52ec8d17b8b12e9fd61e344243713d43b5fae7` | 2026-09-11 | Movement: a running jump is proven from the take-off its runway actually reaches, and a step refused before its first tick now prices its tile | 2215 |
| 156 | `7c54db1f9e609d569c8d05d4a306c4ee51cb5395` | 2026-09-11 | Movement: two jump comments described the runway pre-filter that the previous commit deleted, and the clamp one of them justified went with them | 412 |
| 157 | `0dc2b9fad6cf8467e5bc1b16d517efac60ed5399` | 2026-09-11 | Movement execution: the rule paragraph still taught the runway length test that the rest of the file now says was the defect | 243 |
| 158 | `ce9b3b312399febcd47d8538e95a195295710e09` | 2026-09-11 | Tools: verification reports an unaskable check as unaskable and runs the rest, instead of calling a missing ripgrep a broken boundary | 436 |
| 159 | `87fd960fcda291d6e140a71c493215efc8fcc0e1` | 2026-09-11 | The jump lane lands: an arc is proven from the take-off the body actually reaches, and a refusal before the first tick is now learned from | 805 |
| 160 | `2e15e7c8813417b30db875461891dafc49c8e160` | 2026-09-11 | The boundary check searches with plain grep where ripgrep is absent, so it runs everywhere instead of refusing on this machine | 340 |
| 161 | `5f197e982cf5bbb8b3a30d940e2cd10342e24521` | 2026-09-11 | The jump audit exits on the property it measures instead of on a corpus pass count it never fills in | 304 |
| 162 | `e25a0d4af484e99ad00d28a7989639bdba9ba0b2` | 2026-09-11 | The root guide's state section is dated today and names the two mechanisms whose absence blocks work | 251 |
| 163 | `6e1d572c092123fff0ebb982adafeb4f69125b53` | 2026-09-11 | Work can fail, guard goes to the threat, and the hands stay free on the walk: six defects the 2026-09-11 playtest recorded and one that answers the player's oldest complaint | 1883 |
| 164 | `0f9394e9fedd95f400d6480f8fd1c81afe063fda` | 2026-09-11 | Mining hops at a vein tile no standable position can swing at, which is how ore in a ceiling stops being permanently out of reach | 598 |
| 165 | `11338a16a4e23f2e4845dc833dc8bff8dd068c9a` | 2026-09-11 | The reader measures how long a decision survives, because the number that explained most of a playtest had to be computed by hand and no check would ever have fired on it | 678 |
| 166 | `9b402cb5986239cb85aa3e1d3617b02213c4ced3` | 2026-09-11 | The running behaviour keeps a flat commitment bonus again, because conditioning it on the body's progress tripled the churn it was written to cure | 440 |
| 167 | `5f4c9127173dff38b6f81514596ab1e49bc031fd` | 2026-09-11 | Which diagnostic layers you had switched on survives a relaunch, because nine plain statics meant re-picking them every session | 303 |
| 168 | `d2c12ecf038724a585f2db764bd482aba6464aa3` | 2026-09-11 | The companion gets up on its own after ten seconds, so a down no longer ends its share of the session | 349 |
| 169 | `f6c1127cd4a1fae15f168a157601edad23f26a07` | 2026-09-11 | The README carries the three-way comparison the project needs to stop patching symptoms: what the companion should do, what it does, and what is built | 1034 |
| 170 | `16b460020ffc806af77d776ef993707e1bf40719` | 2026-09-11 | Expected Behaviour argues with itself at every scene, and a new section scores each responsibility against what the code actually produces | 861 |
| 171 | `9047553f707c0bb1d59917f42ae54b22a9a8749a` | 2026-09-11 | Behaviour By Behaviour becomes one table with its definitions in the column headers, because it is the file's source of truth and has to be editable in one place | 527 |
| 172 | `050186b7f174a931986a1cf97a5cd4fb5a4ddcb5` | 2026-09-11 | Finding a route becomes its own row, because the table had the move and not the plan | 440 |
| 173 | `ff93735199ef011b12c1b0b44b77deac010096f2` | 2026-09-11 | The table drops its alignment and blame columns, because one of them measured a constant and the other had to keep admitting it did not know | 513 |
| 174 | `d5016ecf5edf40556b3f1b106585814797844710` | 2026-09-11 | Current says only what was observed, System carries the hypothesis and its check, and every section gains a part on how to maintain it | 989 |
| 175 | `4aab76b890398ad442eb026ec5fc9900d06d24cc` | 2026-09-11 | The root guide points at the README as the behaviour specification, and marks the utility-scoring ruling as open rather than settled | 314 |
| 176 | `4296f851b13e49ccd557d7255ec24bc223a29929` | 2026-09-11 | Reading the roadmap into the README adds movement abilities, doors and a closed set of world edits, and drops three proposals that failed the brain test | 914 |
| 177 | `d6b353d3b39aaac8e14c8da337fcef4922a63f42` | 2026-09-12 | Architectural research separates preference, activity continuity and physical execution | — |
| 178 | `d60b92b10008d478073f10f2c545d332b4819dde` | 2026-09-12 | The architecture investigation has a scoped agenda and clarified behavioural constraints | — |
