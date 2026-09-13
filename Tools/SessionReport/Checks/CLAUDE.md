# Checks — finding questions the record can answer

Each question the record can answer is one class implementing `ICheck`. `Program.cs` registers them; a missing schema column skips the check as reduced coverage via `ICheckCoverage`.

```
Checks/
├─ CheckDecisionContracts.cs          selected activity offer must exist and be usable
├─ CheckDecisionStability.cs          activity changes only with a new comparison  
├─ CheckFailedMethods.cs              a method tried more than once with no credited effect
├─ CheckFollowProgress.cs             follow objective open but body is stalled or receding
├─ CheckOffersAttemptsAndGrants.cs    selections, attempts and grants read by process-wide identity
├─ CheckTheBody.cs                    control grants are self-consistent and owned correctly
├─ CheckTheChoices.cs                 choice rows reflect what the activity owner recorded
├─ CheckTheFight.cs                   hands work while threatened; hunt refusals with reasons are not hand refusal
├─ CheckTheInstrument.cs              closure and recording loss are discovered and reported
├─ CheckTheRecord.cs                  identities parse correctly and stay consistent
└─ CheckTravelEpisodes.cs             journeys take the time they were proven; stops have reasons
```

Each check earns a finding class at `Definitive` (a contract contradiction the recorder and the check cannot both be right about), `Potential` (evidence reduced by capture damage or missing schema), or no finding when the schema is too old. An Oddity signals that the recording never exercised that finding kind; it is not a defect. The capture's arrival radius, stand-site tile spans and torch-illumination hysteresis are measured against the checks that verify them, making them part of the recorder's own contract. `TravelEvidence.First` is the one place the gate lives for new schema features.

`CheckTravelEpisodes.cs` asks about a whole journey rather than about one move, which is the gap `ProvenMovesTakeTheirProvenTime` leaves open: every move in a journey can land inside its proven ticks while the journey costs four times its price, because the time goes into the gaps between the moves. `JourneysTakeTheTimeTheyWereProven` reports per request kind what the journeys took against their proven total and against the player's own ticks over the same ground, and `TheBodyStopsOnItsOwnRoute` reports the stops, their rate per minute of travel and their split by reason. The player figure is the only reference in this tool the brain did not produce; a `-` there is missing trail coverage, never a player who was slower. A journey's duration excludes the ticks the body spent downed and the finding says how many were excluded, because a death inside a journey is not the journey being slow and a downing outlasts most journeys.

Both are Oddity throughout on purpose. No capture yet carries one of these occurrences, so any threshold would be grown from an imagined failure and would fire on the shape its author imagined — which the folder's own rule names as the thing to avoid. They state the ratios; the first real capture showing a journey costing several times its price is what earns a graded check, and each finding says what such a check would have to separate before it could grade anything. `TravelEvidence.First` is the one place the gate lives, and the fixtures derive their capture stamps from it — the running captures from the gate itself and the skipping one from a minor below — because a fixture holding the literal schema reads as older than the gate the moment the mod's schema passes it, and the captures meant to run would then skip while the assertions on their findings failed for a reason unconnected to what they test. Neither new kind appears in `DescribeGodsEyeEvents`'s fixed list of meaningful kinds, so a journey and a stop are absent from the per-episode narrative and reachable only through these two checks and the HTML's own by-kind grouping, which builds its groups from whatever kinds the file holds.

The stop rate's denominator is counted from the rows under the producer's own predicate — the ordinary `travel` owner with an `Executable` or `Partial` navigator — and the finding prints the recorder's own running `stops_per_minute` beside it, so the two disagreeing is itself visible rather than silent.
