# Navigation research separates the graph, search and execution

This folder compares navigation representations and algorithms against a changing platform world and a companion whose capabilities can change. A search algorithm's guarantee applies to its mathematical assumptions; it does not validate a game's collision model, generated transitions or control execution.

Distinguish durable theoretical results from implementations and measured experiments. Describe applicability conditions, costs and counterexamples beside each algorithm. Connect claimed benefits to an experiment that could distinguish them on this project, including the instrumentation needed to observe the result. Source inspection is not a native execution test.

```text
Navigation Research/
├─ CLAUDE.md                         the representation, search and execution distinction
├─ Dynamic Platformer Navigation.md  external algorithm contracts, platformer cases and separating experiments
└─ Momentum, Stops and the One-Mover Design.md  why the companion stops on every descent, the ledger of movement attempts, prior art on momentum-keeping travel, and the entry-speed graph experiment
```

`Dynamic Platformer Navigation.md` establishes a durable constraint for this area: a shortest-path or incremental-repair claim is only about the supplied directed graph. State abstraction, transition generation, native execution and safe-prefix policy need their own evidence. It records conditional experiments rather than selecting a replacement search architecture.

A speed class cannot carry speed accurately enough to land a descent because class gap multiplied by the longest steered move must fit inside arrival slack, which needs finer quantisation than planning can afford (3.81× at five classes; roughly twice that for soundness).
