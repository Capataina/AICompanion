# One allowance is borrowed by every decision consumer

These game-free owners separate computation limits from gameplay preference. A native frame owns one `DecisionWorkBudget`; nested discovery, repair, binding, combat and route work borrow it. Operation counts make forced cuts reproducible while a monotonic deadline bounds production slices. Mandatory engine observation and motor work remain separately measured. A deadline can overrun by one atomic operation, which is observable rather than claimed impossible.

```text
Computation/
├─ CLAUDE.md                 ownership and suspension contract
├─ AllocateDecisionWork.cs   borrowed clock/count allowance with named consumption and cuts
├─ ResumeDecisionWork.cs     cursor retained across frames and explicitly rebound on changed input
└─ LimitDecisionStorage.cs   capacity with pinned objects and visible eviction/refusal
```

**`Cut` is sticky and is a property of the allowance, never of the caller that reads it.** It is set the first time any consumer is refused an operation and stays set until that allowance is gone, which is what makes `FirstCutSubsystem` meaningful and what makes a late read of `Cut` answer a wider question than it looks like it asks: not "was I cut" but "was anything cut this tick". A consumer that treats its own late `budget.Cut` as evidence about its own work is reading another subsystem's exhaustion as its own refusal. Combat's plan re-pricing did exactly that and released valid committed fights for a day because of it; the rule that came out of it lives in `../../../Activities/Combat/Planning/CLAUDE.md`, and the general form is that an exhausted bound is a third answer rather than a negative.

**One allowance is installed per owner, and an owner restores what it found.** `Begin` refuses to replace a standing allowance, which is what stops a nested planner minting a second deadline after exhausting the first. `Own` is the other half and is what an actual owner uses: it saves the standing allowance, installs its own, and gives the previous one back on the way out. `Brain.Tick` is the only production owner and finds nothing standing, so `Own` is exactly the `Begin`/`End` pair it always had; the reason it exists is that a harness installs an ambient allowance per case so a fixture entering below the tick can borrow one at all, and a fixture that drives a whole tick must not destroy it. `Restart` is the blunter form — end whatever stands and install this — for a per-case reset, which has nothing to restore.

A cache capacity bounds storage, not the identities the brain may ever discover. Sources continue past evicted entries, and rescan finite stable sets. Current and in-flight work is pinned. A fully pinned cache refuses admission visibly rather than evicting the live prefix. A new frame is not a new cursor epoch. No consumer can silently buy a second production deadline after exhausting the first.

The implementation is being integrated under the canonical retained-course plan. These classes alone do not establish whole-brain budget coverage or live performance.
