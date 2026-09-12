# Research records the questions before they become architecture

This is the owner's requested home for architectural research, alternatives, trade-offs and decisions reached in discussion. Start with [Architecture and Behaviour Map](<Architecture and Behaviour Map.md>). These documents examine the system; they do not authorise implementation or replace the README's product requirements and the source folders' operating knowledge.

```
research/
├─ CLAUDE.md                            scope, reading order and evidence conventions
├─ Questions for the Next Architecture Investigation.md  proposed questions and clarified behaviour
├─ Architecture and Behaviour Map.md    responsibilities, boundaries and the first assessment of each layer
├─ History of Decisions and Failures.md how the FSM became the current system and what the attempts established
├─ Utility AI and Its Alternatives.md   comparison of decision families against this companion's requirements
└─ Evidence and Open Questions.md       reproducible observations, disputed claims and separating experiments
```

The history explains why an idea was chosen, not whether it must remain. A commit's reported test result stays a historical claim unless rerun. A recorded playtest describes its captured build, not the present checkout. Source inspection proves implementation shape, not enjoyable or reliable play. External production experience transfers only with its domain differences stated.

Keep a dated source revision on each investigation. Separate observations, inferences, hypotheses and accepted decisions in prose. Each proposed direction needs its strongest counterargument and the evidence that would change the judgement. Decisions become accepted only through the owner's discussion or instruction; a recommendation in a report remains provisional. Update an investigation when its evidence changes, preserving the reason a conclusion changed rather than turning a failed hypothesis into something nobody ever held.

The first investigation leaves both utility selection and route-search architecture open. It makes no claim that a different architecture, or a short list of repairs, will fulfil the Expected Behaviour story. Its raw telemetry is local and ignored by git; the evidence document explains that reproducibility boundary.
