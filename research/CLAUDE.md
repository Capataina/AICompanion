# Research compares routes to the expected behaviour

This is the owner's requested home for research, alternatives, trade-offs and provisional architectural decisions. The work asks how to close the gap between the README's expected companion and recorded behaviour. It does not treat the present implementation as a constraint on what may be proposed. The owner separately authorised full implementation of Proposal 1; that ongoing implementation must be assessed against the plan's complete acceptance scope.

Start with [the ranked proposal synthesis](proposal/CLAUDE.md), then follow the evidence supporting the branch under discussion. For the full question inventory, read [Questions for the Next Architecture Investigation](<Questions for the Next Architecture Investigation.md>) beside the [answer and evidence ledger](<Evaluation and Observability/Question Answers and Remaining Evidence.md>). [Research Scope and Verification](<Evaluation and Observability/Research Scope and Verification.md>) records coverage, reproducible checks, review corrections and unavailable evidence. The original four surveys remain at their existing paths as dated entry points; the expanded investigations below carry the deeper evidence and current qualifications.

```text
research/
├─ CLAUDE.md                              scope, reading routes and evidence conventions
├─ Questions for the Next Architecture Investigation.md  the 140-question inventory
├─ Architecture and Behaviour Map.md      initial responsibility survey
├─ History of Decisions and Failures.md   initial history synthesis
├─ Utility AI and Its Alternatives.md     initial decision-family comparison
├─ Evidence and Open Questions.md         initial recording corrections
├─ Historical Evidence/                  full commit coverage and pivotal conversation evidence
├─ Decision Architecture/                behavioural requirements, comparison criteria and decision mechanisms
├─ Game and Mod Case Studies/             inspected scheduling, ship-control and NPC integration examples
├─ Navigation Research/                  platformer state, search, control and experience alternatives
├─ Implementation Evidence/              dated source contracts and source-level defects
├─ Evaluation and Observability/         recordings, reproducible measurements and proposed experiments
└─ proposal/                             ranked synthesis: Path 1 implemented, 02/03 source bets, 04 unfinished discussion
```

## Different evidence has different authority

The README's Expected Behaviour and explicit owner clarifications define the product. The source describes a dated implementation. A commit or transcript establishes what was tried, reported or decided at the time; it does not certify that a fix worked. A recording describes its captured build, not the present checkout. External code and papers establish mechanisms under their stated assumptions, not Terraria outcomes. Keep those origins distinct even when they agree.

The expanded investigation is anchored to research baseline `d60b92b` and gameplay source `4296f85`, which are identical on gameplay/tool code. Every later implementation comparison must refresh affected source findings against its own revision. Historical measurements remain valid for their identified files; proposed explanations remain hypotheses until a discriminating experiment tests them.

Claims belong to their derivation: historical progression in Historical Evidence; source contracts in Implementation Evidence; measurements and future acceptance conditions in Evaluation and Observability; external mechanisms in the literature and case-study folders. Proposal files link those owners and explain the judgement, rather than creating another source ledger. The answer ledger routes every question to evidence or an explicit unresolved experiment.

## The roadmaps are alternatives that can earn promotion

The final Path 1 contract explicitly connects player-intent interpretation to alternative-route companionship and separates danger to each actor from the cost of delayed help. Its implementation completion gate includes a current-reality README write-out verified against the resulting source, while observed gameplay claims still require named recordings.

Proposal 1 carries the concrete implementation roadmap, including target folders, existing-code migration, activity/offer/control records, tick order, P00–P14 dependencies, observer changes and failure-case traceability. Treat its source paths as a design anchored to its stated checkout, and refresh them before implementation. No planned package is marked complete by the presence of its documentation.

The owner-selected starting direction combines three purpose families with Path 1's separation of candidate evaluation, chosen activity, control grants and productive outcomes. Each family uses shared utility machinery to expose its best eligible concrete activity before the parent chooses. Seven activities remain; shared safety owns escape and avoidance, local movement belongs to useful assistance/company, and contextual separation and cross-system cases J01–J16 are documented in the discussion record. The [follow-up discussion](<Decision Architecture/Purpose Families and Shared Companionship.md>) owns the grouping, shared safety/companionship, alternatives and corrections. Path 2 now considers a stronger shared execution framework beyond Path 1's lightweight activity ownership. Path 3 adds bounded planning only where delayed consequences defeat an accurate immediate comparison. Path 4, written 18 September 2026, is those two combined on paper as an unfinished discussion, not a decided bet: leftover against leftover, no stickers, last night's collect/torch swap as a member of the class rather than the class, holes still open. The next sitting attacks it. These are ranked next investments, not measured probabilities of final success.

The original surveys, 140-question ledger and dated evidence reports preserve the research as assessed before this follow-up. Where they describe family grouping as belonging only to Path 2, use the current proposal synthesis and discussion record for the selected direction. Their historical observations and sources are not retroactively changed by this architectural preference.

All routes share observer verification, physical feasibility, explicit uncertainty, changing capabilities and the same behavioural acceptance cases. Evidence can move the preferred route to another. No route promises that a short sequence of changes finishes a general companion, and no name such as utility, behaviour tree or A* is itself a diagnosis.

## Maintain the reasoning without rewriting the past

Separate accepted requirements, observed facts, source claims, inferences and proposals in prose. A recommendation carries its strongest counterargument, the observation that would refute it and the next discriminating test. When evidence changes, record why the judgement changed; retain historical claims with their dates rather than relabelling them as current truth.

Raw telemetry and private conversations remain local. Public research includes recording identities, hashes, bounded measurements and retrieval descriptions without private conversation identifiers. A clone alone cannot reproduce unavailable recordings. Source-linked commands must name the exact schema they read; human-friendly synonyms for machine fields have already produced a broken example and an analyser misclassification.

The research-only change does not run proposed experiments, alter gameplay, update external roadmaps or establish live acceptance. Those limits are part of the evidence, not omissions to conceal.
