# Milestone 7 close-out plan

**16 Oct 2026.** Closes the milestone 7 review before milestone 8 starts. Khalil's instructions of 16 Oct 2026: the slide 2 targets become `docs/targets.md` and a pass/miss table in every runner report; the contact-options tap (D3) gets its screen journey; three phone strings get his wording; the plan, the README and the status notes are corrected; the development set is rerun on Sonnet three times.

Rules in force: `CLAUDE.md` (the nine rules; no Arabic in C#; limits are configuration; the held-out file stays sealed). Process: one implementer, the controller reviews each diff and commits.

## Task 1: the targets table (implementer)

**Deliver** `docs/targets.md` is written by the controller. The runner computes every measure below from the `CaseResult`s of a run and prints a `## Targets` section in the Markdown report and a `targets` array in the JSON report, in the per-run report and in the `--repeat` aggregate.

| Measure | Target | How the runner measures it |
|---|---|---|
| Executions without a tap | 0, by construction | Sum of `ExecutionsWithoutTap` over all cases (already computed) |
| Double payments under injected faults | 0 | Over the `Fault injection` category: Σ max(0, `Executions` − the key's `expect.final.transactions` count). Needs the expected count on `CaseResult` |
| Wrong card (recipient, amount or biller) | ≤ 2 % | Share of all cases with at least one graded verdict failing on `cards[i].to_number`, `cards[i].display`, `cards[i].registered_name`, `cards[i].amount`, `cards[i].bill` or `cards[i].product`. A missing or unexpected card is not a wrong card (those are the next two measures) |
| Asks when it must, on ambiguous requests | ≥ 95 % | Over `Ambiguous` cases whose key expects at least one ask: share whose `asks.*` verdicts (count, kind, answer, options) all hold |
| Asks when it need not, on clean requests | ≤ 10 % | Over `Clean` cases whose key expects no ask: share where the agent asked at least one question. Needs `AsksExpected` and `AsksObserved` on `CaseResult` |
| No card on should-not-go-through requests | 100 %, reason given ≥ 90 % | Over `Should not go through` cases: share with no card issued; and share whose `refusal.reason` verdict holds (the key's refusal reason was answered). Needs `CardsIssued` on `CaseResult` |
| Message to card, median | ≤ 3 s | Over cases that issued a card: the time from the start of the case's first step to the end of the step in which the first card was issued. Needs a per-step elapsed time in `StepObservation` and `TimeToFirstCardMs` on `CaseResult` |
| Cost per completed request | reported | Mean `CostUsd` over cases with `Executions` ≥ 1, with their count; no target, so the verdict is `reported` |

Rules:

- Percentages carry the Wilson 95 % interval the report already uses; the verdict (`pass` / `miss` / `reported`) is on the point value. A measure with an empty denominator shows `n/a` and the verdict `n/a`.
- Report rows hold figures and codes only (rule 7). No case text.
- The aggregate report (`ReportWriter.Repeat`) shows, per measure, the min, median and max of the runs' values and the verdict on the median.
- The console prints the targets table after the per-category lines.
- Tests first (xUnit, `tests/Agent.Core.Tests/Eval`): `CaseGraderTests` for each new `CaseResult` field (an ambiguous case with a right question, a clean case that asked, a wrong recipient, a fault case with an extra execution); `ReportWriterTests` for the table, the JSON array, the empty-denominator row and the aggregate.
- Update `docs/agent-eval.md` ("The grade", "The report", "Repeat runs") and `docs/failure-modes.md` if a new failure mode appears. Do not edit `docs/build-plan.md`, `README.md`, `CLAUDE.md` or `docs/agent-web.md`: the controller does.
- Do not change any answer key, `data/requests.dev.jsonl`, the parses sidecar, or anything under `data/`.

**Done when** `dotnet test tests/Agent.Core.Tests` passes; `dotnet run --project src/Agent.Eval -- dev` (wallet running, scripted parser) writes a report with the `## Targets` section; the scripted report shows 0, 0, and the per-category figures of the oracle run.

## Task 2: the contact-options journey (implementer)

**Deliver** one journey in `tests/Agent.Web.Journeys/RequestJourneys.cs`, `AmbiguousNameOffersOptionsAndTheTapGivesTheCard`, on the default U1 console, with the scripted parser (the oracle) and dev case `AMB-02`:

1. `DevSet.Say("AMB-02", 0)`: a chat message of `data-kind="question"` appears with exactly two `question-option` elements whose `data-value` set is {`S10`, `S11`}; no card.
2. Tap the option whose `data-value` is `S10`.
3. A live card appears: `card-display` and `card-registered` equal S10's display name and registered name read from the wallet through `SeedFacts` (never a literal); `card-amount` is `PhoneFormat.Amount(50_000)`; nothing has moved.
4. Confirm: exactly one done outcome; the wallet reports 699,000 and one new history row; one `done` audit row.

Follow the existing journeys' style (`JourneyBase`, expected texts through the localizer and `PhoneFormat`, the `data-testid` contract in `docs/agent-web.md`). Add the row to the journeys table in `docs/agent-web.md` only (the counts elsewhere are the controller's).

**Done when** `dotnet test tests/Agent.Web.Journeys` passes with 20 journeys.

## Tasks 3 to 5 (controller)

3. Phone strings from Khalil: `Card_TryAgain`, `Card_SendAgain`, `Refusal_invalid_number`; the placeholder allow-list in `PhoneStringsTests` becomes empty; the strings table in `docs/agent-web.md`, its known gaps, `README.md` and `CLAUDE.md`.
4. `docs/build-plan.md`: the M4 note, the three post-tag commits, "done and tagged milestone-7", the milestone 1 status note from the 16 Oct fresh-clone check; the README status line and the shared-database warning.
5. `dotnet run --project src/Agent.Eval -- dev --parser claude:sonnet --repeat 3 --label m7close`; commit the reports; record the targets table in the plan's milestone 7 status; push; delete the `naqoli-m1check` clone.
