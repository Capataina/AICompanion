# Ledger — the harness's memory, one row per named case per run

Every other tool here answers "is it broken now". This one answers "was it broken last time, and by how much", which is the question none of them could answer before, because the suite returned one exit code for thirty-nine fixture files and a playtest returned prose.

The unit is the row: an instrument, a suite, a case, a verdict, and for a measure a value with the direction that counts as good. Rows live one file per run under `runs/`, committed, named for the commit the rows describe and the instant the run began. One file per run rather than one file appended to, because several lanes run the suite in their own worktrees at once and a shared file conflicts on every one of them.

```
Ledger/
├─ CLAUDE.md
├─ Ledger.csproj          a plain console project; compiles nothing from the mod tree
├─ EmitLedgerRows.cs      the row, the six verdicts, and the one writer — included as a source file by every other Tools project
├─ ReadAndWriteRuns.cs    the run header, the store, and baseline resolution through git ancestry
├─ CompareRunsAndScore.cs the Wilson interval, the noise band, and the printed scoreboard
├─ SelfTestTheStore.cs    the store's own rules: commit widths, the baseline refusals, the round trip
├─ Program.cs             begin · scoreboard · compare · baseline · reds · error · list · --self-test
└─ runs/                  committed run files, one per run
```

## A run is refused as a baseline for four separate reasons, and each is its own rule

The run being scored is excluded inside the walk and the walk continues past it, which is not the same operation as nulling a self-match on the way out. A clean run at a new commit matches itself first, so the nulling shape reported "no baseline" with the parent's run sitting in the store, and the entire comparison worked only for dirty runs — which resolve from `ran_at` and so never match themselves.

A baseline is the nearest ancestor commit with a run that ran everything and came back clean, so a run is refused when its tree was dirty (nothing identifies what it ran against), when any row is `fail` or `error`, and when it was taken under `--case`. That third one is the least obvious and the most damaging: a filtered run on a clean tree is clean and non-dirty, so without the header's own filter field it would resolve as the baseline for the next full run, and every case the filter excluded would then read on the scoreboard as a case that disappeared. A `skipped` row does not refuse a run, because this repository's captures are gitignored and the play measures therefore skip in every fresh checkout — refusing on a skip would leave the store with no baseline at all.

**An instrument that fails without writing a row is invisible to the scoreboard, and that is what the `error` command exists for.** A crash before the first case, a project that will not build, a failing path that files nothing — each contributes silence, and silence is what a healthy instrument contributes too. `verify.sh` hands every non-zero exit to `ledger error`, which files an error row only when that instrument's own rows do not already account for it; the reconciliation lives here rather than in the shell because the shell knows the status and cannot read the rows.

## The six verdicts are not interchangeable

`pass` and `fail` are the thing under test. `error` is the check itself breaking, which is a different repair and must never be read as the thing under test failing. `skipped` carries its reason and is never a pass, because zero coverage and a clean run look identical in any report that folds them together. `sealed` is a model-closed search answer — the search proved nothing, rather than proving impossibility. `measure` carries a number instead of a verdict and is **never graded here**: whether a number is bad is a question about the design, which the verification plan answers and this tool does not, so a declared pass line travels as a tag on the row rather than as a comparison in the code.

That last rule is the one most likely to be undone by somebody trying to be helpful. A measure that decides pass or fail from a threshold nobody declared collapses "ever green" into "green now" and cannot separate a flake from a regression, which is why the plan refuses per-instrument thresholds outright.

## A baseline must have measured at least what the run being scored measured

Two rules decide it, and both were written from runs that actually reached the scoreboard rather than from a worry. A candidate is refused when it holds as a *skip* anything the new run measured, and when it measures cases the new run does not report at all. The header's own `Filter` flag cannot catch either, because neither producer sets it: `measure-flake.sh` opens a run with no filter and drives every repeat under `AIC_LEDGER_CASE`, so its file carries one real case and forty-odd skips, and `backfill-capture.sh` opens a run over a capture whose rows are play measures and no fixtures. Both are clean and non-dirty, so both resolved as baselines — the first observed doing it during this lane's own work, the second measured by the sentinel at `new 41, gone 46, nothing red`, exit 0.

A case the new run measures and the baseline never held is deliberately not covered by either rule: that is a case being added, which has to stay possible without disqualifying every ancestor in the store.

The mirror, a case being retired, is where this rule and the two coverage blocks below contradict each other, and the contradiction is real rather than a reading. A `STOPPED REPORTING` row needs a baseline that reported a case this run only skips, and a `gone` row one that reported a case this run never filed, and both are precisely the candidate the second rule refuses — so for any case the baseline actually measured, neither block can print. A run that deletes a fixture is therefore scored against no baseline at all, and the names it retired are read off the store by hand, as each earlier run's reporting set against the new one's. The lane that retired the walker's fixtures on 2026-09-15 met this with dozens of clean, full, non-dirty ancestor runs in the store, every one refused for reporting the walker's nine cases, and the scoreboard's refusal sentence then said no ancestor had a clean run, which was the wrong mechanism and is corrected to name this rule. What the rule should do with a retirement — admit a candidate whose surplus is bounded, or admit it and print the surplus as `gone` — is a design question left open here rather than loosened in passing, because the rule was written against a backfill run whose surplus was the entire fixture suite, and any bound would have to separate that from a lane deleting nine.

## A verdict moving to or from `skipped` is never `unchanged`

It used to be, and it is the one transition a diff must not fold away, because a case that passed yesterday and is skipped today has stopped measuring while looking in every total exactly like a case that measured and passed. `Telemetry/` is gitignored, so the play measures skip in every fresh checkout and the diff said "unchanged 47" over a case that had gone from twenty-four reproduced numbers to none. They are `STOPPED REPORTING` and `now reporting` now, each in its own block, and the closing line carries the count so "nothing red" is never printed alone on a run whose coverage fell.

The exit code still comes from this run's own red rows and not from coverage. That is deliberate: a fresh clone with no capture would otherwise be red for having no gitignored file, which punishes the clone for the store's shape. Coverage falling is a different event from a check failing, and conflating them would make neither fixable on its own.

**A red is a stop whatever the reruns show.** A case that fails once and then passes four times prints `FLAKY` with its interval and still exits 1, because the original fail row is a row and the scoreboard's verdict is the run's rows. Measured: `AIC_LEDGER_FORCE_RED=flaky sh Tools/verify.sh --rerun-red 4 --case "deliberately red"` gives `4/5 = 80% (95% CI 37.6–96.4%)`, `FLAKY`, exit 1. Whether an intermittent case should stop a run is a question for the verification plan's owner rather than a behaviour to change here.

## Two rules from the research live in the scoreboard rather than in prose

**A pass rate is printed with its interval, never as a point.** The arithmetic is Wilson's score interval at 95 percent, chosen over the normal approximation because that one is wrong exactly where this suite lives: at zero failures it has zero width, so five green runs would read as proof. What it buys is the batch size any claim about an intermittent fixture has to carry — three green of five bounds the true rate only to 23.1–88.2 percent, five green bound the failure rate below 43.45, thirty below 11.4 and a hundred below 3.7. Every one of those figures is pinned by `SelfTestTheStore.QuotedIntervals` against what `Wilson.Of` returns, so a sentence here that drifts from the arithmetic turns a case red instead of standing as prose nobody rechecks.

**A measure's delta is judged against a band computed from repeats of one commit**, and fewer than three repeats prints no band rather than a fake one, because a band from two points is a line through two points. Where the delta sits inside the band there is no conclusion to draw, and the scoreboard says so rather than calling it an improvement.

## Traps

- **A commit identifier in this store can be seven characters or forty, so it is compared with `Git.Same` and never with a bare `StartsWith`.** The header stores `rev-parse --short` output and `Git.Ancestry` returns `rev-list`'s full hashes, so a one-way comparison from the stored short hash to a full ancestor is always false. It made `Baseline` unresolvable for every run in the store while `compare` kept working, because that path compared both directions — so the suite printed "no baseline" with a perfectly good clean run sitting at the parent commit, which is a feature that silently did nothing rather than a feature that failed. `SelfTestTheStore` pins both directions and fails on the one-way rule by name.
- **The dirty flag excludes `runs/`, or the ledger defeats itself.** A run leaves an untracked file in the store, which makes the tree dirty, which makes the next run dirty, which disqualifies it as a baseline — so after the very first run no run can ever be clean again unless somebody commits in between, which is how this was found. A run file is the record of a run rather than a change to the code the run measures. It is verified by observation (`git status --porcelain -- . ':(exclude)Tools/Ledger/runs'` reports a source edit and not the run files) and deliberately not by the self-test, because any test of it would read the live working tree and go red for reasons unconnected to the rule.
- **`reds` prints `instrument<TAB>case`, and a rerun dispatches on the instrument.** Sending every red to one project reruns a case that project does not own, which selects nothing, files a skip, and grades a real red as a case that could not be reproduced.
- **`runs/` in the repository's `.gitignore` has no leading slash, so git matches a directory of that name at every depth.** The ledger's run files were invisible to `git add`, which reported nothing and said nothing — the exact failure a gitignored ledger was refused for. Two negation lines re-include this folder, and they have to be two: a parent directory that is excluded cannot have its contents re-included. **`git check-ignore` cannot verify this**, because it exits 0 when a path matches a negation pattern just as it does for a real exclusion; stage a file and read `git status` instead.
- **The static class is `EmitLedgerRows` and not `Ledger`**, because a class named `Ledger` inside the namespace `AICompanion.Tools.Ledger` is ambiguous with the namespace from any other namespace, and the error it produces names a missing assembly reference rather than the collision.
- **A payload field is parsed by hand and never by a regular expression that stops at a closing brace.** The movement-state detail nests braces — an `EdgeReport` contains `From = {X:… Y:…}` — so the obvious `\{([^}]*)\}` matches the inner one and silently finds nothing, which reads in a report exactly like a capture with no cancellations in it. That pattern cost a full measurement pass.
- **A run file whose header line never parsed is not a run**, and the store returns nothing rather than inventing a header. Every comparison is keyed on the header's commit, so a guessed one files rows under a commit nobody ran them at.
- **The emitter writes nothing unless `AIC_LEDGER_RUN` names a file**, and prints regardless. A tool run by hand behaves exactly as it did before the ledger existed; the recording is an addition, never a replacement for the output a person reads.
- **A run over a recorded capture stores under the capture's own `source_revision`, not under the checkout.** `Tools/backfill-capture.sh` reads it from the preamble. Without that, comparing two captures compares two afternoons rather than two builds, and a capture recorded before the ledger existed could never be benchmarked at all.

## The first thing it measured

The raised-lip ore-work fixture has been "three of five" and "thirteen of fifteen" in this repository's notes for days, neither of which bounds anything. Twelve runs of it at one commit through `../measure-flake.sh`, on a machine carrying three concurrent dotnet processes, give **9 of 12 = 75% (95% CI 46.8–91.1%)** — intermittent by observation rather than by suspicion, because it passed and failed at one commit within one batch.

Two readings follow from that and both are worth keeping. It settles attribution cheaply: a red on that fixture inside a change that touches no mod code is the flake, and the batch proves it at the change's own commit rather than by checking out the parent. And the native-collision case in the same batch went 12 of 12, which reads as certainty and is not — its interval is 75.7% to 100%, so twelve green runs bound the failure rate only below about a quarter. That is the whole reason the interval is printed rather than the rate.

The plan's pass line is thirty consecutive idle runs, which bounds failure below 11.4 percent rather than the ten the plan names. It was taken on 2026-09-14 at `ca69344`, with one dotnet process on the machine: 30 of 30, 95% CI 88.6–100%, in `runs/ca69344-20260914-193027.jsonl`. Every batch before it ran beside other lanes' builds, and a batch taken under load measures the load rather than the fixture, which is the hypothesis under test; the idle batch bounds the rate and leaves the cause where it was.

The next pair of batches is the one to read carefully, because it is the one that looks like an answer and is not. Taken either side of lifting the wall-clock allowances across the suite, on 2026-09-14 at `1a64ef0`, ten runs each:

```
before the lift, load 2.74    10/10 = 100% (95% CI 72.2–100%)
after  the lift, load 3.58    10/10 = 100%, rows recording mode "in-suite; unbounded-allowances"
```

The fixture did not fail once in twenty runs, so **neither batch tested the load hypothesis at all** — there was nothing to attribute. Ten green runs bound the failure rate only below 27.8 percent, which does not separate a fixture failing one run in four from one that never fails. The earlier twelve-run batch found 9 of 12 on a machine carrying three concurrent builds; this session's machine carried two and produced twenty green. That is consistent with a load effect and equally consistent with the twelve-run batch having been unlucky, and a ledger that reported the second reading as progress would be doing the thing this whole tool exists to stop.

## What is not established

The paragraph that stood here called the verification plan's "about 43 percent" the suspect number and put this file's own 32.6 against it. It had that backwards, and the correction is worth keeping because it is the exact failure this tool exists to make impossible: a number nobody rechecked, standing in prose, contradicting working code. `Wilson.Of(5, 5)` returns a pass rate of 56.55 to 100 percent, so five green runs bound the failure rate below **43.45** and the plan was right. 32.6 is not a bound on anything — it is the half-width of the three-of-five interval, which is the neighbouring sentence's subject, so two wrong figures were one transposition. The docstring's "12 to 77 percent for three of five" was wrong the same way: that band is `Of(2, 5)`, and three of five is 23.1 to 88.2.

Every figure this file and that docstring quote is now pinned by `SelfTestTheStore.QuotedIntervals` against what `Wilson.Of` returns, so a sentence that drifts turns a case red. The pin caught its own author on its first run, red on 43.5 where the arithmetic says 43.45.

A scoreboard establishes that a case's verdict or a measure's value moved between two runs. It establishes nothing about why, and with one repeat per commit it cannot yet separate a move from run-to-run variation at all.
