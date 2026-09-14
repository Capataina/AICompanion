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

A baseline is the nearest ancestor commit with a run that ran everything and came back clean, so a run is refused when its tree was dirty (nothing identifies what it ran against), when any row is `fail` or `error`, and when it was taken under `--case`. That third one is the least obvious and the most damaging: a filtered run on a clean tree is clean and non-dirty, so without the header's own filter field it would resolve as the baseline for the next full run, and every case the filter excluded would then read on the scoreboard as a case that disappeared. A `skipped` row does not refuse a run, because this repository's captures are gitignored and the play measures therefore skip in every fresh checkout — refusing on a skip would leave the store with no baseline at all.

**An instrument that fails without writing a row is invisible to the scoreboard, and that is what the `error` command exists for.** A crash before the first case, a project that will not build, a failing path that files nothing — each contributes silence, and silence is what a healthy instrument contributes too. `verify.sh` hands every non-zero exit to `ledger error`, which files an error row only when that instrument's own rows do not already account for it; the reconciliation lives here rather than in the shell because the shell knows the status and cannot read the rows.

## The six verdicts are not interchangeable

`pass` and `fail` are the thing under test. `error` is the check itself breaking, which is a different repair and must never be read as the thing under test failing. `skipped` carries its reason and is never a pass, because zero coverage and a clean run look identical in any report that folds them together. `sealed` is a model-closed search answer — the search proved nothing, rather than proving impossibility. `measure` carries a number instead of a verdict and is **never graded here**: whether a number is bad is a question about the design, which the verification plan answers and this tool does not, so a declared pass line travels as a tag on the row rather than as a comparison in the code.

That last rule is the one most likely to be undone by somebody trying to be helpful. A measure that decides pass or fail from a threshold nobody declared collapses "ever green" into "green now" and cannot separate a flake from a regression, which is why the plan refuses per-instrument thresholds outright.

## Two rules from the research live in the scoreboard rather than in prose

**A pass rate is printed with its interval, never as a point.** The arithmetic is Wilson's score interval at 95 percent, chosen over the normal approximation because that one is wrong exactly where this suite lives: at zero failures it has zero width, so five green runs would read as proof. What it buys is the batch size any claim about an intermittent fixture has to carry — three green of five is consistent with a true rate anywhere from 12 to 77 percent, and thirty green bound the failure rate below about ten.

**A measure's delta is judged against a band computed from repeats of one commit**, and fewer than three repeats prints no band rather than a fake one, because a band from two points is a line through two points. Where the delta sits inside the band there is no conclusion to draw, and the scoreboard says so rather than calling it an improvement.

## Traps

- **A commit identifier in this store can be seven characters or forty, so it is compared with `Git.Same` and never with a bare `StartsWith`.** The header stores `rev-parse --short` output and `Git.Ancestry` returns `rev-list`'s full hashes, so a one-way comparison from the stored short hash to a full ancestor is always false. It made `Baseline` unresolvable for every run in the store while `compare` kept working, because that path compared both directions — so the suite printed "no baseline" with a perfectly good clean run sitting at the parent commit, which is a feature that silently did nothing rather than a feature that failed. `SelfTestTheStore` pins both directions and fails on the one-way rule by name.
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

The plan's pass line is thirty consecutive idle runs, which bounds failure below ten percent. It has not been taken: three other lanes were building throughout, and a batch taken under load measures the load rather than the fixture, which is the hypothesis under test.

## What is not established

The Wilson arithmetic here reproduces two of the three figures the verification plan quotes from it — the 12-to-77 band for three of five, and thirty runs bounding failure below ten percent — and not the third: five green runs bound the failure rate below 32.6%, where the plan says "about 43 percent". Standard Wilson at z = 1.96 is what is implemented and the two agreeing figures are strong evidence it is the intended formula, so the plan's 43 is the suspect number. It is not settled.

A scoreboard establishes that a case's verdict or a measure's value moved between two runs. It establishes nothing about why, and with one repeat per commit it cannot yet separate a move from run-to-run variation at all.
