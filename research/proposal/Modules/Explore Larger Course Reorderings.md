# Explore larger reorderings when bounded local repair misses worthwhile trips

The [core plan](<../Implement the Retained Course Brain.md>) already inserts, removes, rebinds, exchanges adjacent steps and compares concrete region alternatives. This module is for repeated measured local minima: the core finds a stable legal course, yet changing several connected choices together would deliver the same work substantially sooner or make an otherwise missed enabling sequence worthwhile.

**Activation evidence:** exact small-instance enumeration or a bounded offline challenger finds the better route using the same candidates, effects, policy and available information. A missing torch candidate, false reach refusal or wrong value is not a search-neighbourhood problem and is repaired at its source first.

Add a resumable larger-neighbourhood operator to `SearchCourseOrders`: remove a connected group of steps, preserve its boundary state/resources, and rebuild that group. Operator choice gets the same fair scheduler and borrowed budget. The executable current binding continues while alternatives are incomplete. Publish only a completely validated replacement prefix; a partially better internal fragment cannot displace execution.

| Benefit | Cost and failure trigger | Acceptance |
|---|---|---|
| Cross a local-order trap without factorial enumeration of the whole world. | Larger temporary overlays, more invalidation and longer useful-result latency. | Matched traps show lower delivery loss or route time without worsening harm/reunion or first-use latency. |
| Reorder a connected cluster whose benefit requires several moves. | Selective neighbourhoods can become hidden authored recipes. | Operator is defined over dependency/route structure, tested across lighting, gathering and combat consequences. |

Record neighbourhood members, preserved boundary facts, expansions, discarded alternatives and improvement per operation. Kill tests invalidate one boundary resource, edit its terrain mid-search and exhaust the budget before completion. Remove the operator if simpler candidate ordering achieves the same results, or if its recorded yield does not justify its budget share.
