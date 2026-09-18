# Path 4 — designate a course of action and follow it

**Status, 18 September 2026: a work-in-progress discussion, not an implementation plan.** This file is the handoff for the next sitting. It is the mix of Path 2 and Path 3 that this sitting reached on paper, plus the receipts, the owner's corrections, the sentinels' Fail, and the holes that sitting did not close. It is not complete. Combining 02 and 03 has not been shown to be the right next move. A later sitting still has to attack it, run `where-next` against the history, and decide whether to upgrade, simplify, abstract, extend, keep, replace, or patch — or to throw this mix out. Do not implement from this file tomorrow.

Path 1 is implemented and remains the generator of what work exists. The text below combines Path 2's continuing-activity ownership with Path 3's bounded comparison of sequences, under corrections the owner made the same night after three independent sentinels failed a narrower merge. That combination is a candidate. It is not a whole-brain GOAP rewrite, not a task queue, and not a torch-versus-pickup patch, and it is also not settled.

Confidence is low on completeness. The leftover inequality, frozen identity, and "no stickers" constraints are the sitting's refusals, not a proof that a course assembler is the system. Logical errors remain. The ranking of 02 and 03 as sequential investments still stands; this file does not promote 04 above them as a decided bet.

In plain language: the companion should have a game plan, start following it, and when it thinks the plan should change, ask whether finishing what it is already doing is easier than starting the new thing, including the cost of the switch. That question is the same at every scale the product cares about — which of seven torches, which of three enemies, which fifteen combat moves, whether to pick up a gel on the way to ore, whether to mine two copper on the way home, whether to drop a slime and come because the player has gone down a cave. Last night's collect-versus-torch ping-pong is the most primitive member of that class, not the class.

The [folder synthesis](CLAUDE.md) ranks the original three paths. This file is the live discussion of the gap those three left: Path 1 picks a job every tick, Path 2 would hold the job, Path 3 would compare short sequences, and neither 2 nor 3 as written is the product the owner described on 18 September 2026. The mix is what we have on paper. The next sitting starts by attacking it, not by typing it.

## This file is a handoff, not a licence to build

The owner, same session, after the first draft of 04: this is not "save the plan and implement tomorrow." It is the context dump so the next sitting can pick up without re-deriving last night, the sentinels, the shrink, and the corrections. The receipts are here because a sitting that forgets them will churn the same mistakes. The holes are here because a sitting that treats 04 as finished will ship keep-the-job, or a queue, or family-level sequencing, and call it the jungle.

The objective, as the owner named it this sitting: **maximise the amount of work actually done.** Work, in this product, is a closed list: collecting drops, placing torches, breaking pots, combat, chopping wood, mining ore. Keeping company is not work. Following the player is leftover. A body that ping-pongs between two jobs does **zero** work, and that is the worst case — last night's 68 collect↔torch swaps in `2026-09-18_18-57-09-481`, 53 torch attempts all `Attempted`, Exact 56 asked 7 reached. A body that picks the slower chain still does the work; that is better than zero. A body that picks the faster chain is better still. Urgency can make the slower chain the right one (a torch that is more urgent than a drop, even if drop-then-torch-at-his-feet is fewer tiles). Those three outcomes — zero, slower, faster-or-more-urgent — are the scale the next sitting has to hold. 04's leftover inequality is one candidate for that scale. It has not been shown to be the one.

### League of Legends, Drake and Baron — the two pictures this sitting uses

**Finish the worse C rather than get neither.** You are mid. Your jungler is bot-side and pings Drake. You go down. Your ADC lands vision on Baron and the enemy has started it. If you had stayed mid you could have walked up and stopped Baron. You did not stay mid. You are at Drake. Abandoning Drake now means you get nothing and they get Baron — the worst case, last night's swap. Finishing Drake is making the best of the decision you already paid for. Collect-then-torch versus torch-then-collect is the same shape: once you have flown twenty tiles toward one of them, swapping to the other and then back is getting neither. The owner named this earlier in the sitting as finish-C / League Drake. It belongs in this file as a constraint on *whatever* system the next sitting picks, not as a proof that a course assembler is that system.

**A better step 1 of the same plan is not a ping-pong.** You are the jungler. You commit to Drake. You see they are on Baron. You do not run Drake ↔ Baron. You tell mid to go farm, you invade their bot-side camps, then you take Drake. Drake was step 1. The camps became a better step 1 of the *same* list. You never treat the global objective and the camps as two jobs that swap. Last night was the other thing: the same two jobs swapping 1 and 2. A genuinely new identity becoming step 1 of an updated course is this picture. 04's leftover comparison is supposed to tell them apart. It has not been walked through the slime-on-the-drop case below, and until it has, it is a hope.

### Holes this sitting named and did not close

These are not "open questions with a default." They are reasons 04 is unfinished. A sitting that implements leftover-vs-leftover without answering them is jumping.

1. **Stick versus come.** How does the companion stick to mining without wasting the trip when it should have come? A hold that cannot drop is the slime-then-you-went-down-the-cave picture. A hold that drops too early is the surface zombie. Leftover of finishing-then-coming versus coming is the candidate. It has not been scored against a live vein and a walking player.

2. **Combat as an endless step 1.** How do we stop a fight from eating the tick forever so torches and drops never happen? Combat-always-step-1 freezes on a Super Dummy. A dummy that deals nothing should not be work. A 1,500 HP / 5 DPS body versus two-hit ore is the same arithmetic. 04 says remaining harm arriving before remaining work ends. That sentence has not been made into a quantity the brain already computes, against a fight that is *real* work (kills, not a dummy) that still outlasts a torch.

3. **Urgency versus raw time.** Drop, then fly to the player, then torch at his feet, can be fewer tiles than torch, then drop, then fly home. The torch can still be more urgent. 04's leftover is time and travel. Urgency is not leftover. Nothing in this file says how a dark tile next to the player outranks a drop that is closer if the two disagree. Inventing a sticker for urgency is the 15% failure again.

4. **A slime lands on the drop.** The companion went to the drop because being with the player soon, hands free, beat torch-first. A slime drops on it. Kill the slime then its drops? Leave the slime and torch? Torch then come back for the slime? 04 says leftover. It does not say which leftover, frozen when, against a new identity that appeared on top of step 1.

5. **Family sequencing is the wrong grain.** "Combat, then lighting, then collect" is not the product. The product is which torch tile, which enemy, which drop, which vein, in one list, at the same time. Nearby assistance as a family, lighting as a task, torch tiles as sub-tasks, is the nested shape 04 has not chosen (open question 4 still has a default). Sequencing families is a lower-level plan than the owner asked for. Sequencing specific sites is the higher-level one. This file still reads, in places, as if leftover of *jobs* is enough.

6. **Is 02+03 even the system?** Maybe the next move is simplify (OrderNearbyTasks already permutes; keep the order, freeze identity, stop). Maybe it is keep Path 1 and only freeze collect/torch membership. Maybe it is abstract a missing fact (work delivered per remaining tick, urgency as a sense). Maybe it is replace utility for this layer. `where-next` is the skill that picks from that palette against the history. This sitting did not run it to a committed pick. 04 is one candidate on that palette, written down so the pick has something to attack.

Until those are closed, "designate a course and follow it" is a direction, not a design complete enough to type.

## Sources for this file, so a claim can be re-checked

Nothing in this file is allowed to live only in the sitting that wrote it. Each kind of claim has a home:

| Kind of claim | Where it lives | How to re-read it |
|---|---|---|
| What the owner asked for, in his words | Session `01a0b174-9a46-7631-a405-61f10cd975a7`, compaction `segment_010.md` (the 0.30.6 play report and the "too much emphasis" turn), and the same session's later user turn that asked for this file | The compaction INDEX is `compaction/INDEX.md` under that session. Do not treat a summary of a turn as the turn. |
| What last night's body did | `Telemetry/2026-09-18_18-57-09-481.tsv` (schema 0.40.0, 12,062 rows), its `-events.jsonl`, its `-census.txt` | Header comments name mod 0.30.6, world Lilalio, started 18:57:09 UTC. Columns cited below. Telemetry is gitignored; a clone without the capture cannot re-derive the numbers. |
| What the afternoon before did | `Telemetry/2026-09-18_16-35-26-353.tsv` (0.30.5, 22,919 rows) | Combat median run 1; tick 2402/2403 dump; ticks 19,446–20,379 crowd. |
| What a commit was for, and what it proved | `git log -1 --format='%B' <hash>` | Bodies cited below. A subject line is not the proof. |
| What the three sentinels returned | Session `01a0b174-9a46-7631-a405-61f10cd975a7`, seats `01a0b628-1904-7be1-809c-0acc435aae10` (Terraria-play), `01a0b628-1904-7be1-809c-0ad3909a657b` (git-history), `01a0b628-1904-7be1-809c-0ae9f4786879` (logic) | All three Fail. Convergence is in `segment_010.md` under Problem Solving. |
| What the three 0.30.5 log readers returned | Seats `01a0b56d-d274-7f23-bb6e-60a02c3ebc3c`, `01a0b56d-d274-7f23-bb6e-60b81f59f131`, `01a0b56d-d274-7f23-bb6e-60ced5c3e46f` | Flicker and crowd as two roots, not one "commit more" patch. |
| What the fixture already holds | `Tools/EngineReplay/Observation/VerifyPreparedActivities.cs` line 327, `two even jobs must not trade the lead while the body flies toward the one it chose` | Green on the Euclidean leftover. Last night is the live negative. |
| What Path 1/2/3 were | `01`, `02`, `03` in this folder; ranking in `CLAUDE.md` | 04 does not rewrite them. |

Replay of last night's swap, from this directory, against the capture that is on disk:

    python3 -c "
    from pathlib import Path
    from collections import Counter
    p=Path('Telemetry/2026-09-18_18-57-09-481.tsv')
    lines=[l for l in p.read_text(encoding='utf-8-sig').splitlines() if l and not l.startswith('#')]
    cols=lines[0].split('\t'); i={c:k for k,c in enumerate(cols)}
    rows=[l.split('\t') for l in lines[1:]]
    print(Counter(r[i['action']] for r in rows))
    "

Expected: `combat` 5886, `keep-company` 3590, `collect` 1347, `place-torches` 1239. Census file beside it: `Exact: asked 56, reached 7, abandoned 49`. Events: 53 `place-torches` attempt-outcomes, all channel `Attempted`.

---

## Owner criticisms of the drafts this sitting, kept as constraints

The sitting produced three combined plans on paper before this file. Each died for a named reason. The reasons are constraints, not flavour. The quoted sentences are the owner's, from session `01a0b174-9a46-7631-a405-61f10cd975a7` (compaction `segment_010.md` turn 6 for the play report, and the later turn that asked for this file). A paraphrase is not a source.

1. **Too much emphasis on last night's torch and pickup.** "it just feels and looks like you are putting a bit too much emphasis on purely just this torch and pickup like collecting torch and ping pong because the fix that I'm thinking is more generic. I wanted a universal fix that's going to prevent future problems like these from also becoming problematic." A design whose acceptance is "collect and torch no longer swap" has implemented the shrink.
2. **Lighting on the way home as a special case.** "you're talking about it as if lighting is just like this special thing that can be done on the way back. How about we kill a slime on the way back? How about we place torches? How about we collect loot? What if there is only two ores, two blocks of copper ore? That's going to take like the same amount of time to pick up as coming back anyway." One gate, leftover versus pass.
3. **Hardcoded values.** "There shouldn't be any hard-coded values in this system. … the second you add a hard-coded value here, right, like 15%, you will just run into scenarios in which this breaks. … How do you even decide a value like that? How do you decide a parameter? That's going to be infinite fine-tuning." That kills `Commitment = 1.15` in `Companion/Brain/Infrastructure/Selection/BehaviourWeights.cs`, `TaskOrderShare = 0.4`, `TaskOrderMaximum = 5`, combat-always-step-1, and new-task-always-step-1 as rules.
4. **New-task-becomes-step-1 would not recreate last night.** "a new task becoming step one, I don't think would recreate what happened, and the reason why is because what happened was not a new task becoming step one, it was the same task swapping between one and two." Forbidding new-as-step-1 does not fix last night and does forbid the jungle's new slime.
5. **Sticking is not always best.** "let's say the AI takes on a task to kill a slime, and then I go into the cave and I go down and I go down and I go down. And the AI finally kills the slime, and now it has to … make its way all the way back to me. … sticking to a task is not always the best option."
6. **Combat is a sequence of moves, not a stand.** "rather than going if I stand right here and choose this weapon and attack this enemy this many times … combat should be thinking here are the 15 next moves that I should be making … float up slightly, go above the eye, shoot the eye so that it gets knocked downwards, and then I should position myself and shoot shurikens so that I align the eye with the three zombies."
7. **Waiting for the perfect moment doing nothing.** "one issue that, for example, we saw before where the AI would try to wait for the perfect moment, and while it's waiting for the perfect moment, it wouldn't do anything." On last night that is column `fire` = `no-use-worth-firing` on 1,304 of 5,886 combat ticks in `Telemetry/2026-09-18_18-57-09-481.tsv`.
8. **Keep-the-job is the primitive, not the product.** "This is not just some primitive: do I place torches first or do I pick up the drops first? We need a proper planning system." The sentinels were right about last night's first node. They were not writing Expected Behaviour.
9. **No "always" rules that name a kind of task.** "without going, oh, okay, like you know, if a task is this, then it's always this. Because that's a very bad generalization to make."

The parent drafts that died on (1), (2), (3) as combat-always-step-1, (4) as a forbid-rule, and (8) as the shrink, are not pending simplifications. The ask that produced this file, same session: "I think the best thing to do right now is actually to not take action. I think the best thing to do right now is to update our README file. … make a new proposal file. Right, this is going to be proposal 04 with the course of action … about 500 to a thousand lines … keep everything that I said, what the agents returned, what my criticisms were, how I want this to be a generic, generalized, genericized, universal system that is globally applicable … then we'll go through everything again."

---

## What the owner asked for

A competent friend playing Terraria is not asking "what is the best single action from where I stand." They are thinking several steps ahead. The extreme version of that, said as a picture rather than as a mission system, is: go right, drop into the cave, enter the caverns, mine this ore, grind these, craft this, come back, spawn the boss, kill it, go to the biome, break that ore. The product version, the one this companion is for, is the same shape of thinking applied to the scene it is actually in.

The scene that is the acceptance picture is a jungle fight, not a torch and a gel, and not a boss. A hornet top-left, a snapping eater bottom-left, two jungle slimes dropping, a Demon Eye (the ordinary night flyer, NPC id 2, not the Eye of Cthulhu) and three zombies to the right, standing on ore, the area poorly lit, hearts and mana and ordinary drops everywhere, pots, projectiles from all sides. What a person does in that scene is a course of action: kill the slimes that are dropping on us, pick up, rise to align the Demon Eye with the zombies, throw shurikens down the line, kill the hornet so it does not enter, place torches on the way back while getting into position, three more shurikens to take both zombies, dive for drops while placing torches, move on the snapping eater while closing on the wall drops, evade everything while kiting the eater and dodging hornet thorns, then go to the ore instead of the drop because the drop will fall on us, and if a new slime drops while mining, a brief fight with the bow for knockback off the body then shurikens for damage, pick up, back to the ore.

Inside a job the same shape holds. Seven torches have an order. Three enemies have an order. Combat is not "stand here, choose this weapon, attack this enemy this many times, maximise the front." Combat is the next fifteen moves: float up slightly, go above the Demon Eye, shoot so it is knocked down onto the zombies, position, throw shurikens so the eye and the zombies share a line. A weapon switch is a step in that sequence (the bow's knockback, then the shuriken's damage), not a separate chooser that runs after the feet have already committed.

Opportunistic work on a committed path is universal. Lighting on the way home is one member. Killing a slime on the way home is the same member. Collecting loot is the same member. Two blocks of copper that take as long as coming back anyway are the same member. The question is always: is it better to come back because there is work around the player, to stay and work here, or to do work while going back? Nothing in that question is lighting-specific, combat-specific, or leftover-specific.

There must be no decision fatigue and no ping-pong. The companion designates a plan and follows it. A better plan can exist; the switching cost of doing it can still be too high, in which case it is too late to switch. When the plan should change, the comparison is: finish this, or continue from the updated plan, including what it costs to switch. Dropping a task is sometimes right — the companion takes a slime, the player goes down and down and down, the slime dies, and walking all the way back is worse than having dropped. Sticking is not always best. There is no rule of the form "if a task is this, then it is always this."

There must be no hardcoded values in this system. A 15% sticker, a commitment of 1.15, a share of 0.4, a maximum of five, any of them, will find a scene that breaks it, and deciding the number is infinite fine-tuning. Everything is relative to everything else: leftover of this against leftover of that, remaining harm arriving before remaining work ends, a two-hit ore against a five-minute fight, a two-block copper against the flight home.

A reading, cheap to reject: the ten-steps-ahead picture is the shape of thinking, not a licence to revive missions, crafting, boss-summoning or independent gathering. Those were abandoned. The companion hangs around where the player is because that is where the game is happening. Session-scale intent ("I am going to spawn a boss") belongs to the player; the companion reads where the player is going and plays the same scene. Scene-scale, job-scale and move-scale courses of action are this proposal. If that reading is wrong, the next paragraph of Expected Behaviour has to say the companion invents a playthrough.

---

## Origin

Path 1 was selected on 12 September 2026 (`ad8343b`): three purpose families, seven activities, lightweight ownership, shared safety. Path 2 was the stronger executor if lifecycle defects survived that. Path 3 was bounded planning if immediate evaluation missed enabling consequences. They were sequential investments, not a menu. The owner authorised Path 1 in full. It landed 13 September 2026. The three-family chooser is production.

The same week, and then the orb week, produced a pile of continuation machinery that is not a course of action:

| When | Hash | What the body of that commit established | What it actually holds today |
|---|---|---|---|
| 8 Sep 2026 | Slate ruling | Interrupted chop and mine finish inside the action. A queue was refused because it would recreate priority. | Still the rule. 04 must not bring a queue back. |
| 8 Sep | `85961ee` | "The incumbent action keeps a 1.15 bonus so scores do not flicker." Utility chooser replaces the state machine. | Tick comparison. No sequence. The 1.15 is still `Weights.Commitment`. |
| 11 Sep | `9b402cb` | Stall-conditioned commitment "tripled the churn it was written to cure": 0.14.0 17:22 held 55.5 ticks/decision, 0.15.0 18:30 held 22.3; 1,197 of 1,727 switches fired on a stalled predecessor while stall was 4.5% of ticks. "the *body not covering ground* is the wrong evidence." | Flat 1.15 again. Last night the raw collapsed 3×, so the sticker still loses. |
| 12 Sep | `b89abee` | Primary activities retain identity across shared movement. | `OwnCurrentActivity`: one purpose, no intention stack. Resume-after-combat as a stored tail is this hash forgotten. |
| 12 Sep | `ad8343b` | Family plan. Seven activities, safety shared. | Production chooser. Hunting and guarding later merged into one combat activity (`03986f0`). |
| 14 Sep | `6892189` | "a chosen destination is kept until it stops belonging to its own region." | A follow spot, a firing stand, a partial-progress tile. Not a collect item. Not a torch tile. |
| 14 Sep | `7a6b473` | "keep-company deciding it has arrived mid-jump no longer drops the jump in the air." | The body finishes the committed move. Not a job plan. |
| 15 Sep | `16a39de` | `VerifyResponsiveFollowing` settle row: 39 method changes in 600 ticks at 3× speed, ceiling of one. Four rounds to wait for arrival. | Company methods, not jobs. |
| 15 Sep | `573d9d4` | Capture `2026-09-15_10-27-40-531`: slime-then-torch because lighting inherited a zero forecast; surface zombie ticks 7308–7735 paid no separation. Added worth-per-time, reunion on all tasks, `OrderNearbyTasks`. Fixture: "two even jobs with the body flying toward the incumbent over 12 rescores produce no lead change." | The order is discarded every comparison. Last night is the live negative of that fixture. |
| 18 Sep | `457b168` | Combat holds a plan. 4 ms budget is 4 ms. | A fight keeps its plan. Collect and torch do not. |
| 18 Sep | `145be5a` | Capture `2026-09-18_16-35-26-353`: "a 10-tick local stand became a 227-tick FireFrom and reunion hit 0." Crowd: empty pool after 4 ms. Hitting `ServesPlayerDirectly` keeps reunion at 1; from-here fallback on an empty cut. 400 px drop still zeros. | Combat only. Last night's collect/torch swap is not this dump. |
| 18 Sep | `9bfc67e` | Same 0.30.5 play: hearts walked to, gel Arrived 22 s without transfer. PrepareDrop skips `IsAPickup`; CollectTouchedItems +SettleRadius. | Collect identity is still the live item, re-prepared every tick. |

`OrderNearbyTasks` is the one piece that already tries to order jobs. It exists because on 15 September 2026 the owner said a slime on the way to a dark corner should be killed, its drops collected, and then the corner lit, and a chooser that valued each job only from where the body stood flew past the slime. What it does: every task within a share of the best, every permutation up to a maximum, first step of the best order leads, the order is thrown away, leftover is Euclidean distance over speed plus work, flying toward the first job is supposed to shorten its time so the next comparison favours it, company is not a task. The fixture "two even jobs, no lead change over 12 rescores" is green. Last night falsified the flying-shortens-time assumption: collect raw collapsed from about 0.77 to 0.23 at the same tiles, and the body sat in the middle of two Exact destinations swapping every fifteen ticks.

On 18 September 2026 the owner played 0.30.6 (`Telemetry/2026-09-18_18-57-09-481`, schema 0.40.0, Lilalio, 12,062 ticks, about three minutes twenty seconds). The question was a go-away-come-back loop. Three isolated log readers and the parent agreed it was collect versus torch, not reunion. The owner asked whether fleshed Path 2 and Path 3 would cut the class; then to combine them into one named proposal with no magic multipliers, relative leftover, finish-C, optional opportunistic inserts, adaptive interrupts rather than "only combat." Three sentinels then attacked a frozen course-of-action list. All three returned Fail. The parent shrank the design to "keep the job": identity membership on collect and torch, kill the live tail, kill new-task-rewrites-step-1, kill combat-always-step-1. The owner overruled the shrink the same sitting: that was too much emphasis on last night's primitive, and the product is a universal planning system that prevents the class.

This file is that overruling, written down.

The sitting that produced the overruling ran as diagnosis, not as a patch. After the 0.30.6 play the owner asked to confirm a go-away-come-back loop. Three isolated log readers plus the parent agreed the loop was collect versus torch at about fifteen ticks, Exact 3404,570 against Exact 3433,606, collect raw collapsing 0.77 → 0.23 at the same tiles, company final about 0.08, zero companion torches. They disagreed with the owner's first picture only on the yank: it was not reunion pulling the body home. The owner then asked whether fleshed 02 and 03 would cut the class; then to combine them under the name course of action, with no magic multipliers, relative leftover, finish-C, opportunistic inserts, adaptive interrupts rather than "only combat." A first combined plan still had combat-always-step-1, new-task-rewrites-step-1, and lighting-on-the-way-home as special. Three sentinels failed that plan. A second combined plan shrank to keep-the-job. The owner failed the shrink for being the primitive. 04 is the third combined plan, and it is the first that treats last night as a member of the class the owner named rather than as the class.

---

## The class, not the instance

A companion that re-picks the locally best first step every tick will ping-pong whenever two jobs are close, abandon leftover work whenever a slightly better first step appears, wait for a perfect combat moment with its hands empty, treat lighting-on-the-way-home as a special rule, freeze on a Super Dummy because combat is "the most important," and fail to knock a Demon Eye onto three zombies because nobody asked it for fifteen moves.

Last night is one picture of that:

```text
same two jobs, set unchanged
        collect Exact 3404,570
        torch   Exact 3433,606
                │
                ▼
     every comparison re-orders them
                │
                ▼
     collect_raw 0.77 → 0.23 at the same tiles
     (PrepareDrop re-auditions the live item;
      lighting identity is the nearest point)
                │
                ▼
     body sits at 3414,580, between the two
     68 collect↔torch transitions in ticks 7773–11125
     longest pure stretch 9401–10100, 41 runs, ~15 ticks each
     Exact asked 56, reached 7, abandoned 49
     53 place-torches attempts, all Attempted / replaced-before-interaction
     0 companion torches; 4 torch credits, all earner=player
```

Those numbers are columns of `Telemetry/2026-09-18_18-57-09-481.tsv` (`action`, `request`, `anchor`, `spot`, `npc_tile`, `collect_raw`, `place-torches_raw`, `keep-company_fin`, `task_order`, `task_order_runner_up`) plus `Telemetry/2026-09-18_18-57-09-481-census.txt` (`Exact: asked 56, reached 7, abandoned 49`) plus `Telemetry/2026-09-18_18-57-09-481-events.jsonl` (`kind=attempt-outcome`, `label=place-torches`, channel `Attempted`, n=53; `kind=experience-credit`, `label=torch`, `earner=player`, n=4).

The owner reported this as a go-away-come-back loop before anyone opened the file. Session `01a0b174-9a46-7631-a405-61f10cd975a7`, compaction `segment_010.md` turn 6: "the agent kept like trying to go away and then come back and go away and come back and it got stuck where like it just kept going back and forth in the same area" and "if you look at the very end of the log you'll realize I was digging the wall to the right." Three isolated log readers plus the parent agreed the loop was collect versus torch, not reunion. They disagreed with the owner's first picture only on the yank: `keep-company_fin` sat 0.07–0.12 through the 9,401–10,100 stretch, which is `FollowDuringUsefulWork`, not a reunion pull.

Company final was about 0.08 through that stretch. Reunion did not yank it. Commitment 1.15 did not hold it, because the raw collapsed by a factor of three. `OrderNearbyTasks` did not hold it, because the order is discarded and the leftover it would have used as hysteresis is the Euclidean leftover the live scores were not using. On those 700 ticks `task_order` is empty on 342 rows and otherwise reads `collect>place-torches`; `task_order_runner_up` is `place-torches>collect`. The permutation was producing an order. The body was not following it. The incumbent bonus is a constant. The collapse is a fact of preparation.

The same class, other pictures, already in the record:

- **Combat as one stand.** Capture `2026-09-18_16-35-26-353` (0.30.5, 22,919 rows). Re-read this sitting: tick 2402 `action=combat` `combat_raw=1.02` `combat_fin=1.02`; tick 2403 `action=keep-company` `combat_raw=1.02` `combat_fin=0.00` `keep-company_fin=0.05` `combat_offer=Usable:planned-attack`. That pair repeats at 2405/2406 and 2408/2409. `145be5a`'s body names the mechanism as a 10-tick stand becoming a 227-tick FireFrom, reunion multiplying the fight to nothing. Crowd window ticks 19,446–20,379: 934 rows, `combat_offer=Unresolved:budget-cut` on 922, `action=combat` on 5, `action=keep-company` on 858, player life 100/100 on 593 of them and 40/100 on 165. Combat median run 1 tick (780 runs). 0.30.6 moved that median to 13 and the share to 48.8%; that is hold of a fight, not a course of fifteen moves. `fire=no-use-worth-firing` on 1,304 of 5,886 combat ticks of `2026-09-18_18-57-09-481` is the wait-for-the-perfect-moment picture: while it waits, it does not place a torch, pick up, or take a worse shot that still does something. Capture `2026-09-18_12-37-47-939` (0.30.3): company 73.9%, combat 12.4%, a never-in-horizon plan clamped to the floor. 0.30.6 moved combat's median run from 1 to 13 and its share to 48.8%; that is hold of a fight, not a course of fifteen moves. `fire=no-use-worth-firing` on 1,304 of 5,886 combat ticks is the wait-for-the-perfect-moment picture: while it waits, it does not place a torch, pick up, or take a worse shot that still does something.
- **A leftover that is not lighting.** Two copper blocks on the way home, a slime, a drop. The parent draft treated torches on the way home as the leftover fix. The owner named that as the special-case failure.
- **A long fight as a tangent.** Super Dummy (0 damage, 9.99 million life) as step 1 of every course is a five-minute freeze. 1,500 HP at 5 DPS versus a two-hit ore is the same arithmetic: remaining fight time against remaining ore time, remaining harm arriving before remaining work ends.
- **A job that should have been dropped.** Kill a slime, player has gone down the cave. Finishing was right; walking back after is not. A hold that cannot drop recreates the gel-in-a-pit-while-the-player-dies picture.
- **Same-two swap versus a new task becoming step 1.** Last night was the same two tasks swapping 1 and 2. A genuinely new task becoming step 1 is a different class; a rule that forbids it would not have prevented last night, and a rule that always allows it would recreate last night only if the "new" task is the other of the same two.

---

## What the tree has instead

```text
Terraria world
      │
      ▼
observe (player, threats, light, reach, intent region, loot)
      │
      ▼
prepare 6 activities, 3 families nominate
      │
      ▼
EvaluatePreparedActivities
  protection × commitment 1.15 × horizon × useful-work × reunion × time-window
      │
      ▼
OrderNearbyTasks
  permute close jobs (share 0.4, max 5)
  first step only, order discarded
  leftover = Euclidean / speed + work
      │
      ▼
one winner → OwnCurrentActivity (one purpose, no stack)
      │
      ├─ mining keeps a vein
      ├─ combat holds a plan (stands, weapons, aims, a short timeline)
      ├─ collect re-prepares the live item (PrepareDrop nulls the candidate)
      ├─ lighting identity is the nearest point
      └─ company is leftover, cap ~half a job
      │
      ▼
incidental pot/torch already in reach, no movement, no credit
hands fire only while combat is current
evade bends the flight, keeps the job
```

The combat stance already searches a short timeline of stands, weapons, targets and aims, and commits the undominated front. That is a course of action *inside combat*, discarded when combat is not the job, and not compared against "torch then gel then the ore underfoot." Path 3's "bounded planning for consequential choices" was supposed to be the layer that compared those. It was never built. Path 2's continuing activity was supposed to be the layer that kept collect and torch as jobs. It was never built for them. What collect and torch have is the 15% incumbent, and last night the raw moved more than 15%.

Incidental in-reach use is already the right shape for a pot or a torch the body is on top of. It must not become a list step. Hands-and-contact pickup while overlapping a gel is not a tangent. Delaying a one-hit ore for a distant gel is last night again.

What 0.30.x already does properly, so 04 does not redo it: the combat stance is one activity that fires, a hitting fight can keep its score (`145be5a`), a crowd still offers from here, a worm is one danger and still many records, hearts are not a collection trip, an arrived gel still transfers, company is leftover rather than a rival that outbids work inside the region, lighting reads the world's own light, every liquid is air. What it does not do is order. Combat median run went 1 (0.30.5) → 13 (0.30.6) and the share went to 48.8% with 115 shots; collect and torch still swapped 68 times in one window; 53 torch attempts placed nothing; Exact reached 7 of 56. Hold without a course is the 0.30.x week.

---

## How many times this class was "fixed"

The historical record the folder synthesis already names is the same pattern at a different layer: a graph result promoted into a physical promise, a body-level signal into task progress, a retained diagnostic into a current outcome. Continuation has the same pattern.

```text
symptom moves, edit location stays "make it stick"
├─ 11 Sep  stall-commitment          tripled churn; reverted to 1.15
├─ 15 Sep  company reunion/hover     39 flips / 600 ticks; wait for arrival
├─ 14 Sep  mid-jump Hold             keep-company arrived in the air
├─ 15 Sep  OrderNearbyTasks          fixture green; live raw collapse
├─ 15 Sep  flying-shortens-time      the hysteresis OrderNearbyTasks relies on
├─ 18 Sep  combat plan hold          fights hold; collect/torch do not
├─ 18 Sep  hitting-fight reunion     combat_fin 1.02 → 0.00 on a 10→227 tick stand
└─ 18 Sep  collect/torch ping-pong   1.15 and the discarded order both present
```

A constant that exists so near-ties do not flip (`Commitment = 1.15`, and `GuardUrgency = 1.25` so guard can beat `1.00 × Commitment`) is the ladder the owner refused as the ping-pong fix. `TaskOrderShare = 0.4` and `TaskOrderMaximum = 5` are the same ladder. Last night's collect raw moved 0.77 → 0.23. No sticker on the incumbent survives a threefold collapse of the thing it stickers.

Slate on 8 September 2026 already refused a queue. Interrupted chop and mine finish through memory inside the action. A stored list of jobs that the companion is "on" recreates priority: whatever is at the front runs, whatever is behind waits, and the player watching sees a companion that will not drop a slime to come down the cave. `OwnCurrentActivity` retains the current purpose only, not a stack, for the same reason. Resume-after-combat as "go back to the vein" is a stack. The vein still being there and still worth having is a fresh comparison, which is what 10:00 of Expected Behaviour already says.

---

## What the three sentinels found

Three independent sentinels attacked the merged Path-2/3 course-of-action list on 18 September 2026, in session `01a0b174-9a46-7631-a405-61f10cd975a7`. They did not see each other's briefs.

| Seat | Id | Lens |
|---|---|---|
| Terraria-play | `01a0b628-1904-7be1-809c-0acc435aae10` | Would this survive a jungle, a dummy, last night's cave |
| git-history | `01a0b628-1904-7be1-809c-0ad3909a657b` | What the log already tried and tore out |
| logic | `01a0b628-1904-7be1-809c-0ae9f4786879` | Whether the rules contradict each other |

All three Fail. Convergence, as filed in compaction `segment_010.md` under Problem Solving: first node is collect/torch not keeping a membership identity, not a missing planner; rule "new task becomes step 1" plus lighting/collect nearest-identity re-enacts last night; combat-always-step-1 versus the five-minute dummy is unresolved; switching-cost leftover is the Euclidean leftover `OrderNearbyTasks` throws away, and route estimates jump (10 versus 227, the `145be5a` dump); way-home fights `AllowsTarget` and company-as-scored-rival; resume-after-combat is a stack `OwnCurrentActivity` forbids; Path 1 already has the cheaper move for last night. They converged on findings the shrink then treated as the design, and which the owner then treated as a diagnosis of last night rather than as the product.

The 0.30.5 sitting the same afternoon is the prior member of the same class, not a different bug. Three log readers (`01a0b56d-d274-7f23-bb6e-60a02c3ebc3c`, `01a0b56d-d274-7f23-bb6e-60b81f59f131`, `01a0b56d-d274-7f23-bb6e-60ced5c3e46f`) plus the parent split flicker and crowd as two roots and refused a mixed "commit more" patch. `145be5a` landed the hitting-fight keep and the from-here fallback; `9bfc67e` landed hearts, gel, bag. 0.30.6 play then showed collect/torch swapping. Hold of a fight improved. Ordering did not. That is why 04 is not another combat-hold patch.

| Finding they converged on | What it is true of | What it is not |
|---|---|---|
| The first node that is wrong for last night is collect/torch not keeping a membership identity, unlike mining's vein and combat's plan. | Last night's swap. PrepareDrop nulls the candidate; lighting identity is the nearest point. | The product. Membership hold on two activities is the primitive, not the jungle. |
| Rule "a new task may become step 1" plus nearest-identity re-enacts last night. | If the "new" task is the other of the same two. | A genuinely new task becoming step 1. The owner named this split: last night was the same two swapping, a different class. |
| Resume-after-combat is a stack `OwnCurrentActivity` forbids. | A stored "and then the vein" behind a fight. | A fight that is step 1 of a course, after which the remaining steps are regenerated from what is still there. |
| Combat-always-step-1 versus the five-minute dummy is unresolved. | A priority rule. | Relative leftover: remaining harm arriving before remaining work ends. |
| Switching-cost leftover is the Euclidean leftover `OrderNearbyTasks` throws away, and route estimates jump (10 ticks versus 227). | A leftover that jitters will swap for the same reason the scores swapped. | The idea of leftover. Freeze the leftover with the identity, the way a vein's remaining hits do not re-count the flood every tick. |
| Way-home fights `AllowsTarget` and company-as-scored-rival. | A job that has to win the chooser to exist on the way home. | Opportunistic work on a path that is already being flown, valued against the leftover of flying past it. |
| Path 1 already has the cheaper move for last night: give collect/torch the hold vein and combat have. | Last night, cheaply. | Seven torches' order, three enemies' order, fifteen combat moves, two copper on the way home, the jungle. |

The sentinels did their job. Last night's first node is collect/torch re-auditioning. A keep-the-job patch would stop that swap. It would not knock a Demon Eye onto three zombies, would not place seven torches in an order, would not mine two copper on the way home unless lighting had a special case, and would not drop a slime when the player had gone. The owner said so. The shrink is recorded here as a rejected alternative, not as a pending simplification.

---

## What to take from Path 2 and Path 3, and what to refuse

Path 2's attraction was never a tidier diagram. It was one place that can say what the system is trying to achieve, what has actually advanced, which resources it needs and why it stopped. Collect and torch do not have that place. Mining does (a vein). Combat does (a held plan). The missing property for last night is finish-C on identities that currently re-prepare. That is Path 2's continuing activity, applied to every job, not to two nearby-assistance children.

Path 3's attraction was never "script mine then collect then guard then resume." The owner's examples have always rejected unconditional sequences. A plan is a contingent hypothesis about useful future actions, revised or abandoned when its assumptions or value change. It may choose not to collect newly mined ore, not to finish an old intention, and not to make a return trip. That is Path 3's bounded comparison of sequences, applied to every scale, not to "consequential lighting detours" as a special subproblem.

```text
take from 2                         take from 3
├─ identity, remaining work,        ├─ a sequence is a hypothesis
│  invalidation, cancellation       ├─ compare legal ways when an
├─ uniform interrupt / resume         early step changes later options
│  without a stack                  ├─ re-evaluate the goal, then the
├─ incidental is not a trip           next action's assumptions
├─ remaining work is native         ├─ bound the horizon by the model
└─ one output owner                 └─ do not plan routine following,
                                      a current shot, or a contact pickup

refuse from 2                       refuse from 3
├─ a common executor that adds      ├─ whole-brain GOAP / HTN as the
│  structure without removing a       companion
│  demonstrated defect              ├─ authored methods per scene
├─ parent × child commitment        ├─ a horizon long enough to invent
├─ stubborn hold with no drop         a future enemy or a playthrough
└─ resume stack                     └─ planning every local decision
```

The mix is one course of action: Path 2's identity is each step, Path 3's sequence is the course, Path 1's families still generate the candidates, and the comparison that used to pick a job now picks a course — or, more cheaply, keeps the current course until a rival course wins on leftover, not on a sticker.

---

## The proposed structure

```text
observe
   │
   ▼
Path 1 families prepare candidates
  (what work exists: a vein, a drop, a dark site, a fight, a tree, company)
   │
   ▼
steps
  a step is a candidate with a frozen identity and a frozen leftover
  identity is membership: this item, this tile, this vein, this enemy generation,
                          this combat timeline
  leftover is remaining work + remaining travel from where the body will be,
             frozen when the step is admitted so a flood jitter is not a rewrite
   │
   ▼
a course is an ordered list of steps
  it spans jobs and the order inside a job
  seven torch sites are seven steps of one lighting stretch, or one lighting
  step whose inside-order is the same comparison
  three enemies and a knockback alignment are combat steps
  a bow-then-shuriken switch is two combat steps
   │
   ▼
designate: the best course from here, once
follow: execute step 1; when its identity is gone, step 2 is step 1
   │
   ▼
reconsider when the world changes, not when the scores flicker
  same set of identities, step 1 does not reorder          last night's class
  a genuinely new identity may become step 1 only if
      leftover(new course) + cost of switching < leftover(current course)
  dropping is a course whose leftover is the flight to the player
      and it wins when that flight is cheaper than finishing
  genuine danger is remaining harm arriving before remaining work ends,
      not "an enemy is on screen", not "combat is always step 1"
   │
   ▼
opportunistic insert on the committed path
  a step whose leftover (do it now, then continue) is less than
  the leftover of flying past it and coming back later
  any kind of work: slime, torch, loot, two copper, a pot
  incidental-in-reach stays off the list (already built)
   │
   ▼
Path 1 chooser again when the course is empty
  company is leftover, as now: what it does when nothing is worth doing
```

The course is flat. There is no nested tangent stack. A fight that interrupts mining is either a new course that won on leftover (remaining harm arrives before the vein ends) or an opportunistic insert on the path to the vein (the slime is on the way). After the fight the vein is still there or it is not; if it is, and it still wins against everything else including coming back to the player, it is the next course, not a resumed stack frame.

Company is not a step. Reunion is not a job. The meeting place is a destination the body is already flying toward when the course is "come back"; sites on that path are opportunistic inserts. Flood growth is not a new identity. A site the flood has just claimed was not absent and is not new; it was unanswered, and unanswered work does not start.

---

## No hardcoded values

The failure mode of a sticker is specific. `Commitment = 1.15` exists so a near-tie does not oscillate. Last night the two jobs were not a near-tie on the live raw: one collapsed by a factor of three because preparation re-aimed. `GuardUrgency = 1.25` exists so guard can beat `1.00 × Commitment`. `TaskOrderShare = 0.4` decides who even enters the permutation. `TaskOrderMaximum = 5` decides how many. Each of those is a scene waiting to happen.

Relative leftover does not need a sticker.

```text
leftover(course) = remaining work of its steps
                 + remaining travel along the order, from where the body is
                   (frozen at designation, against the route the body is
                   actually flying, not a fresh Euclidean)

switch(current → rival) = leftover(rival starting from here)
                        + the travel the current course has already paid
                          that the rival does not reuse

keep current when leftover(current) ≤ switch(current → rival)
switch when the inequality flips
```

A body that has flown twenty tiles toward a torch has paid that travel. Switching to a drop thirty tiles the other way pays those twenty again plus thirty. The drop can still win: if the torch's remaining work grew (the tile is now rock), if the drop is a heart the player needs, if remaining harm from an enemy arrives before the torch would land. The inequality says so. Nobody picked 15%.

Last night's numbers, as a worked member. During ticks 9401–10100 the body sat around tile 3414,580. Collect's Exact was 3404,570. Torch's Exact walked 3433,607 / 3434,606 / 3435,606 / 3432,606. While collect ran, collect raw sat 0.72–0.79 and torch raw sat about 0.62. While torch ran, torch raw sat 0.62–0.67 and collect raw sat 0.21–0.23. Company final sat 0.07–0.12. `task_order` was empty on 342 of the 700 ticks and read `collect>place-torches` on the rest, never the other way as the winner, while `task_order_runner_up` was `place-torches>collect`. The permutation was producing an order. The body was not following it. The live winner was whichever raw Path 1 had just prepared, and collect's preparation was allowed to collapse the thing the order had just ranked first.

Frozen leftover on that stretch would have looked like this, as a sketch from the census rather than a claim the recorder already writes: Exact asked 56 times, reached 7, abandoned 49. Each abandon is a switch. A course that had designated collect at 3404,570 would have kept flying there until that item was gone or leftover of a rival course, including the already-paid flight, beat leftover of finishing. Torch at 3433,606 would have been step 2, or an insert if it lay on the corridor, which at thirty tiles off and thirty-six down it did not. The body would not have sat at 3414,580. The 53 place-torches attempts that all ended `replaced-before-interaction` would have been one attempt that either placed or was dropped once, not fifty-three.

The 0.30.5 capture the night before, `2026-09-18_16-35-26-353`, 22,919 ticks, is the combat member of the same class: company 50.2%, combat 36.4%, combat median run 1 tick, 3,359 `Unresolved:budget-cut`. 0.30.6 moved that median to 13 and the share to 48.8% with 115 shots and 24 enemy deaths credited. Hold of a fight improved. A course of fifteen moves is not that hold. `fire=no-use-worth-firing` on 1,304 of 5,886 combat ticks is the hole in the course: while the stance waited for a use it liked, it did not insert a torch, a pickup, or a worse shot. At tick 11,339 combat raw 1.02 became combat final 0.00 and company took the body with a follow of 756,353 px. That dump is leftover of a fight whose stand had jumped, not leftover of a course. Freezing the fight's leftover with its identity is `145be5a`'s hitting-fight keep, which 0.30.6 already has and which did not cover collect, torch, or a dummy.

Bounding the permutation without a share: the candidates are the opportunities already prepared this tick, which Path 1 already bounds by the family's planning share and by the intent region / work radius. That radius is a fact of where the player is, not a "how close in score" sticker. Bounding without a maximum of five: the tick already has a planning deadline. A course search that runs out of time keeps the current course (it is unfinished, not absent). That is the same three-valued answer every sense already uses.

Genuine danger without a combat-always rule:

```text
interrupt remaining work when
    the predicted hit that work does not prevent
    arrives before that work ends
```

A Super Dummy that deals nothing never interrupts. A 1,500 HP body at 5 DPS interrupts a two-hit ore only if its remaining harm arrives before those two hits land. A bat one second out interrupts a five-piece vein; a slime five seconds out does not. The numbers in that sentence are the threat sense's arrival and the activity's remaining hits, both already computed. They are not tunables of this proposal.

World-change recommit is not a sticker either. A player-placed torch, water breaking a torch, a drop disappearing, a vein running out, an enemy dying: the identity is gone, so the step is gone, so the course is shorter. That is not last night. Last night the identities were still there.

---

## Inside a job, and inside combat

Ordering seven torch sites is the same comparison as ordering a torch and a gel. Each site is a step with leftover (placement plus travel from the previous site). The course is the order that delivers soonest, which is what `OrderNearbyTasks` already scores, except the order is kept, the leftover is frozen, and the identity of each site is the tile, not "nearest dark point" which becomes a different tile every time the body moves.

Ordering three enemies is the same. Kill-order is not "highest DPS on the current target." It is leftover of this kill-order versus leftover of that one, including knockback that moves one body onto the line of the others. The owner's Demon-Eye-then-zombies picture is a combat course: float up, knock the eye down, shurikens through the line. The eye in that picture is the ordinary flyer, not the boss; a boss fight is README 26:00, a different scene. The combat stance's existing timeline of stands, weapons and aims is the right *kind* of object. Today it is the whole of combat, discarded when combat is not the job, and not allowed to contain "then pick up, then torch, then the ore." This proposal says that timeline is a course like any other, and a course may mix combat steps with other steps when leftover says so.

Weapon switch is a step. Bow (knockback off the body) then shurikens (damage) is two steps whose leftover is better than shurikens-only or bow-only. The arsenal already prices a stand by the best attack every handed weapon could make from there, and it already learns a pair (one leaves a burn, the other hurts a burning body more). What it does not do is commit to "this, then that" across uses while something else is also on the course. The wait-for-the-perfect-moment failure (`no-use-worth-firing` on 1,304 combat ticks last night) is a course with a hole in it. The hole is filled by a worse shot that still does something, or by an opportunistic insert (a torch, a pickup) whose leftover is less than waiting, never by standing still.

A reading of the combat stance: File 8's held plan, the seven generators, the undominated front, are Path 3 applied inside one activity. This proposal is that application, lifted to every activity and to the joints between them, so the jungle is one course rather than a combat plan that wins the chooser and then ignores the ore underfoot until the last body is dead.

---

## Opportunistic work is universal

The committed path is a corridor the body is already flying. Anything whose leftover as an insert is less than flying past it is worth inserting. The kind of work does not matter.

```text
on the way home (course = the player)
├─ a torch site on the corridor          insert if leftover(place) < leftover(pass)
├─ a slime on the corridor               insert if leftover(kill)  < leftover(pass)
├─ a drop on the corridor                insert if leftover(pick)  < leftover(pass)
└─ two copper whose work ≤ the flight    insert; staying for a vein that outlasts
   home                                    the trip home is a different course,
                                           and it wins or loses on leftover
                                           against coming back
```

`AllowsTarget` (body and target inside the activity radius) is the reason a way-home fight currently has to win the chooser as a job. An insert is not a new job. It is work on a path already paid for. Contact pickup and in-reach incidental already work this way for gels under the body and torches in reach. This proposal is that shape for anything on the corridor, with leftover as the gate instead of "in reach of the hover."

The brown-versus-pink leftover picture from earlier in the sitting is this gate. Pink is the work the body is already on; brown is something on the way. If doing brown now and then pink is a shorter leftover than doing pink and then coming back for brown, brown inserts. If brown is a five-minute dummy on the way to a two-hit ore, it does not insert. If brown is two copper and pink is the flight home, it inserts. The colours were a drawing of last night's leftovers, not a lighting rule. No share. No "at most one delay of step 1" as a count — a count is a sticker. An insert that makes leftover worse is not inserted. Several inserts that each make leftover better are all inserted, in the order leftover names.

---

## Dropping is a first-class course

Sticking to a task is not always best. The owner's scene: the companion takes a slime, the player goes into the cave and down and down, the slime dies, the companion now has to make its way all the way back. A hold that cannot drop produces that. A rule "always drop when the player moves" produces the surface-zombie of 15 September, where a fight worth having was dumped because he took a step.

Leftover already names the drop:

```text
course A: finish the slime, then fly to where the player now is
course B: drop the slime, fly to where the player now is

keep A while leftover(A) ≤ leftover(B)
  (the slime is almost dead, the player has not gone far)
switch to B when leftover(B) is smaller
  (the player is down a cave, the slime is a long fight, or both)
```

The surface-zombie after a four-hundred-pixel drop lost because reunion zeroed the fight, not because leftover of finishing was compared with leftover of coming. `ServesPlayerDirectly` now keeps a hitting fight's score; a surface zombie he has walked away from is still `ServesPlayerDirectly` false, which is the 15 September ruling that a fight up where he used to be should lose. This proposal does not reverse that. It makes the comparison the leftover of finishing-then-coming versus coming, so a slime one hit from dead can still be finished on the way, and a dummy cannot.

---

## Same-set stability, new identities, world change

Three classes the owner split, because treating them as one recreates last night or forbids the jungle.

1. **The set of identities is unchanged, and step 1 swaps with step 2.** Last night. Forbidden by construction: the course is kept; leftover is frozen; preparation may not replace the identity of a live step with "nearest." Collect's identity is the item, not whichever drop `PrepareDrop` likes this tick. Lighting's identity is the committed tile, not the nearest dark point. Combat's identity is the held timeline, already.

2. **A genuinely new identity appears.** A hornet enters. A slime drops from the ceiling. The player exposes a vein. This may become step 1, and it should, when leftover of the updated course plus the switch is better than leftover of the current course. A rule that forbids new-task-as-step-1 would not have prevented last night (nothing new appeared) and would prevent the jungle (the new slime mid-ore is exactly this). The sentinels were right that a *rule* "new task becomes step 1" plus nearest-identity re-enacts last night. They were right about the rule. The comparison is not the rule.

3. **An identity is gone because the world changed.** The drop was picked up, the torch tile is now lit, the player placed a torch, water broke a torch, the vein is mined, the enemy died. The step ends. The course is the tail. This is not a rewrite of step 1. Re-preparing a live step because a global edit counter ticked somewhere else is the combat-target invalidation the tree already files as a defect; it must not become the course's invalidation.

---

## What Path 1 still owns

Path 1 still prepares candidates, still nominates per family, still refuses unusable offers, still charges protection and the time-apart slope, still keeps company as leftover. This proposal does not restore Survival, exploration, or a fourth family. It does not let a course override a full bag, a protected edit, or downed control. It does not plan a route; movement is still the one request surface. It does not choose the weapon independently of the course; a combat step names the weapon the way the stance already names it.

What Path 1 stops owning is the tick winner among live jobs. The winner is a course. The running step of that course is the activity `OwnCurrentActivity` holds. Hands still fire only during a combat step. Incidental still runs after, in reach, with a free hand.

If the course assembler, given truthful leftovers, produces the same first step Path 1 already produces on ordinary single-job ticks, that is success, not a reason to skip the assembler. Last night, the jungle, seven torches, and two copper on the way home are the ticks where they differ.

---

## Hard cases, named so they cannot be "fixed" with a rule

**Super Dummy / 1,500 HP at 5 DPS while two-hit ore sits underfoot.** Remaining harm is zero or arrives after the ore. The ore's leftover is two hits. The dummy's leftover is minutes. The ore wins. Combat-always-step-1 loses this case by construction and is refused.

**Last night's collect and torch, set unchanged.** Identities freeze. Leftover freezes. Step 1 does not swap. The body flies to one, finishes it, then the other. If collect raw collapses because preparation re-aimed, the frozen identity ignores the collapse. The first node of last night is this freeze, and it is necessary, and it is not sufficient for the jungle.

**A new slime drops while mining.** New identity. Leftover of "ball for knockback, shurikens for damage, pick up, back to ore" versus leftover of "finish the vein while the slime lands on us." Remaining harm arriving before the vein ends decides. Not a combat-always rule. Not a never-interrupt-mining rule.

**Player goes down the cave during a slime.** Leftover of finishing-then-coming versus coming. A one-hit slime is finished. A long slime is dropped. No "always stick." No "always drop when he moves."

**Two copper on the way home versus a vein that outlasts the trip.** Insert the two blocks (leftover of mining them is the same order as the flight). The vein is a different course, compared against coming back because there is work around the player. Relative. Not a lighting-on-the-way-home special.

**Waiting for the perfect shuriken line while doing nothing.** A course with a hole. Fill with a worse shot, or with an insert, never with still.

**Illusion of a better first step.** The owner already ruled: finishing a slightly worse C beats the illusion. Frozen leftover is that ruling as a quantity.

---

## Acceptance

The README Expected Behaviour carries the player-language scenes. This list is the same claims as checks, so a build cannot pass the primitive and miss the class.

- **Last night does not recur.** On a recording that has a drop and a dark site as the only two jobs, the companion finishes one then the other. The action label may change when the first identity is gone. It does not swap every fifteen ticks while both identities remain. Capture `2026-09-18_18-57-09-481` ticks 9401–10100 is the negative fixture; a replay or a planted pair of Exact destinations is the check.
- **Seven torches have an order, and it is kept.** A dark chamber with seven legal sites is worked through. The companion does not fly to nearest, place, re-pick nearest, fly across what it just lit.
- **Three enemies have an order, and knockback is a step.** An Eye above three zombies is knocked down onto them and the line is shot. The companion does not stand still choosing the single highest-DPS body.
- **Bow then shurikens for a slime dropping onto the ore.** Knockback off, then damage, then pickup, then the ore. Not shurikens-only, not a five-minute combat hold.
- **Two copper on the way home are taken; a long vein is compared, not special-cased.** The same insert path takes a slime, a torch, a drop. No lighting-only leftover.
- **The dummy does not freeze the body.** A 0-damage immortal is not step 1. A two-hit ore underfoot is taken.
- **The player descending during a slime is dropped or finished by leftover, not by a rule.** Both branches exist: one-hit finish, long-fight drop.
- **The jungle scene is the live judgement.** Hornet, eater, slimes, Eye, zombies, ore, dark, drops, pots, projectiles. The owner watches. A headless fixture can hold pieces (order of two jobs, insert on a path, leftover of a dummy). It cannot hold the jungle. The play is the gate.

A fixture that passes last-night-does-not-recur and fails seven-torches-have-an-order has implemented the shrink, not this proposal.

---

## What would refute this route

- The same two identities swap again on a recording whose leftover was frozen. Then the first node is not identity, and this file's diagnosis of last night is wrong.
- Leftover frozen against a stale route sends the body into a wall or a now-absent tile. Then freeze has to be membership-plus-a-spatial-invalidation, which the tree already knows how to do for follow spots.
- Course search regularly spends the tick and the current course is kept so often that the companion looks stuck. Then the bound is wrong, and the cheaper Path-1 first step is the fallback the three-valued answer already names (unfinished, not absent).
- A constant appears (a share, a maximum, a 15%, a "combat is this much more") and a named hard case breaks it. Then the constant is this proposal failing its own constraint.
- Missions, crafting, boss-summoning or independent gathering return wearing this file's clothes. Then the ten-steps picture was taken as a mission system, which the reading at the top refuses.
- A queue returns. Then 8 September 2026 was forgotten.
- A resume stack returns. Then `OwnCurrentActivity` was forgotten.
- Keep-the-job ships as 04. Then the owner's overruling of the shrink was forgotten.

---

## Recorder and inspector

A course that cannot be seen will be tuned by watching the body, which is how last night's swap lasted a whole sitting as "go-away-come-back." Record, per tick while a course is live:

- course identity and the list of step identities in order
- leftover of the current course, leftover of the best rival, the switch inequality and which side won
- why a step ended (identity gone, leftover lost, player-left drop, remaining harm)
- why an insert was taken or refused (leftover versus pass)
- the frozen leftover of each step, next to the live score Path 1 would have given it, so a collapse like 0.77 → 0.23 is visible without being obeyed

The inspector shows the course as the companion's intent, the next step, and the condition that would change it. A large plan graph is not the player-facing explanation. The notch already names the running activity; it does not need the tail.

God's-eye and SessionReport gain one check family: a pair of identities that remain in the world must not swap as the running action more than once without an identity actually leaving. Last night is the seed. The 0.30.6 capture is still on disk.

---

## Conditions for promoting, shrinking, or withdrawing

Do not promote this to an implementation plan in the sitting that opens this file. Attack it first. Run `where-next`. Close the holes above, or name a different move. Promote a *later* revision of this mix above "give collect/torch a membership hold and stop" only when the owner still wants the jungle after that attack, and the leftover-versus-urgency and site-grain questions have answers that are not stickers.

Shrink to keep-the-job only if a later sitting explicitly takes back the jungle as acceptance. The sentinels' Fail remains true of last night under that shrink. It does not become the product by being true.

Withdraw the course assembler from a scope when Path 1 plus frozen identity already produces the accepted sequence, which Path 3 already named as the E13 ordinary-opportunism outcome. Keep the identity freeze in that scope; throw away the sequence search. Do not withdraw the freeze because the sequence search lost.

Complete through a recorded play of the jungle picture and the leftover-on-the-way-home picture, on a build whose recorder writes the course. Headless fixtures for last-night-does-not-recur, dummy-is-not-step-1, and insert-on-a-path land first, because a play of an uninstrumented course is last night again: a body that looks busy and a sitting spent arguing about why.

---

## Open questions for the sitting that goes through this file

These are forks that change the design. Defaults below are what *this draft* would pick if forced, not what the next sitting is bound to. Several of them are the holes named above; a default does not close a hole.

1. **Session-scale intent.** Does the companion invent a playthrough (craft, spawn a boss, travel to a biome), or does it apply several-steps-ahead thinking only to the scene it is in, reading the player's journey as the intent region already does? Draft default: scene, job, and move. The ten-steps picture is the shape, not a mission. Independent gathering was abandoned.

2. **Is the combat stance's timeline the combat course, or does a later mix replace it?** Draft default: it is the combat course. Extend it so a course may mix combat steps with other steps, and so a weapon switch is a step. Do not run two planners.

3. **Who is the tick winner — a course assembler, or Path 1 among courses?** Draft default: Path 1 prepares candidates; the assembler sequences them and holds the running course. Unresolved: leftover does not yet carry urgency, so this default can pick the faster chain when the slower urgent one was right.

4. **Inside-job order as steps versus as a nested order on one step.** The owner this sitting: lighting is a task, torch tiles are within-task, and the list has to hold specific tiles, specific enemies, specific veins at once — not "combat then lighting then collect." Draft default: the same comparison at that grain. Unresolved: 04 still talks in jobs more than in sites.

5. **Computational bound.** Draft default: the tick's planning deadline, three-valued (kept current course if unfinished). No maximum of five, no share of 0.4.

6. **Urgency versus leftover.** When drop-then-torch-at-his-feet is fewer tiles, and torch-then-drop is more urgent, which wins? No default. A sticker is refused.

7. **A new identity on top of step 1.** Slime on the drop. Kill, skip, torch-first? No default that survives both last night and the jungle.

8. **Is 02+03 the move at all?** `where-next` against the history: replace, simplify, upgrade, abstract, extend, keep, patch. This file is one candidate. The pick has not been made.

Nothing in those is a licence to add a constant, a combat-always rule, a lighting-only leftover, a queue, a stack, or to start typing.

---

## How this file relates to the README

Expected Behaviour is the product. A row goes into Behaviour By Behaviour first, then a scene. 04 does not replace that. The rows this proposal is for, as of the same sitting:

- **Playing several steps ahead** — the new row. Designate a plan, follow it, weigh switching by leftover, drop when leftover says so.
- **Chaining several jobs into one trip** — opportunistic inserts on a committed path, any kind of work, order of seven torches and of three enemies.
- **Committing to a decision** — same-set stability; the nearer a job is to done the more reason to finish it, as leftover, not as a 15% bonus.
- **Choosing a place to work** — a rich place is a course that sits together, not a family preference.
- **Enemy selection** and **Weapon selection** — kill order, knockback as a step, bow-then-shuriken, the next fifteen moves rather than the best stand.
- **Lighting**, **Looting**, **Opportunistic mining**, **Staying with the player** — leftover on the way home is universal; coming back because there is work around the player is a course, not a yank.

Current Behaviour of 18 September 2026, 0.30.6, is the negative of those rows. Potential Improvements used to say chaining is Proposal 3 only. That sentence is what this file retires.

---

## Appendix: why this is a fourth file rather than an edit of 02 or 03

02 still describes a stronger executor beneath the three families, ranked second, conditional on lifecycle defects Path 1 could not economically prevent. 03 still describes bounded planning for consequential choices, ranked third, conditional on E13. Both remain true as the documents of those bets. Last night and the owner's overruling changed the *product* those bets were aimed at: not "hold collect" and not "plan a lighting detour," but a universal course of action with relative leftover and no stickers.

Editing 02 to say that, or 03 to say that, would falsify the ranking they were written under and hide the sentinels' Fail behind a rewrite. 01 stays the implemented chooser. 02 and 03 stay the source bets. 04 is what combining them looks like on paper, now that combining them has been tried, attacked, shrunk, and overruled — and it is still unfinished. The next sitting attacks it. It does not type it.
