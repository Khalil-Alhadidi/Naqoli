# Security review before the freeze: branch milestone-11 at 4c01680 (the code changed since main)

Reviewer: the `dotnet-security-reviewer` agent, 6 Nov 2026, read-only, on the code changed since `main`: the typed-amount reader (`AmountText`, `AmountTextWords`, `amount-words.json`), the reason guard (`ReasonIntentGuard`, `ParseGuards`), the two `appsettings.json` files (the default candidate and its provider fields) and their tests, with the callers needed to judge them (`Conversation`, `Orchestrator`, `AmountRule`, `ParsedRequestValidator`). The held-out file, the workbook and the user secrets were not opened. Nothing was run and no file was modified by the review. It was asked for before the tag `milestone-11-freeze` because no code may change after it.

**Verdict: pass with changes.** Critical 0, Important 2, Minor 5, Recommendations 2. The two Important findings were confirmed by the controller in the code and fixed the same day (commit `fix(core): the pre-freeze review of 6 Nov 2026 ...`), before the tag.

## Critical

None. No path reaches the execute without the tap, the guard trace carries counts only, and neither `appsettings.json` holds a secret.

## Important

**I1. Three typed amounts read as a figure the user probably did not mean** (`src/Agent.Core/Text/AmountText.cs`; traced through `AmountRule.Resolve`).

- A bare number straight after the millions ("a million and five hundred") was added as dinars: 1,000,500 IQD, where the user may mean 1,500,000. Older than this branch (17 Oct 2026).
- A short counting form (three to ten) before the singular scale word read as a count: 3,000,000 or 5,000,000, where the same words name a third or a fifth of a million. Older than this branch; made inconsistent by the fraction words, which read a quarter of a million as 250,000.
- A fraction after a bare hundred ("a hundred and a half") read as 100.5 thousand, 100,500 IQD, where the user may mean 150,000. New with the fraction words; it matches the prompt's rule for a number and a fraction.

*Decision:* what each form means is Khalil's ruling, so none of the three is read in code any more: `TryRead` returns false and the text goes to the model, as any message does. Twelve examples with `source: review` in `tests/Agent.Core.Tests/Text/amount-text-examples.json` hold this. Known gap 49 waits for his ruling.

**I2. The reason guard could turn a parse that was not a transfer into a transfer card** (`ReasonIntentGuard.cs` with `Orchestrator.IsChooseAccountTransfer`). A model returning `purchase`, `bill` or `unclear` with `unsupported_reason` `choose_account`, a recipient and an amount became `unsupported`, which the orchestrator turns into a transfer card with the account note. Narrow (it needs an inconsistent parse) and still behind the tap, but it broke the guards' stated property that they never make a card the parse did not ask for.

*Decision:* fixed. For `choose_account` on a request whose intent was not `transfer`, the guard drops the amount, so the request is refused with its reason. `ReasonIntentGuardTests.ChooseAccountOnARequestThatWasNotATransferLosesItsAmount` (four intents) and `ABillWithPartOfBillKeepsItsAmount`.

## Minor

- **M1. The guard widens refusals, on the safe side.** Any of the eleven reasons on an otherwise good request now refuses it; a typed answer parsed as a transfer with a reason no longer merges into the open question, and `unclear` with a reason no longer keeps a waiting card. *Accepted:* a refusal, never a card; the dev runs show no case affected. The count of `guard:reason_intent` on the held-out run is the thing to watch.
- **M2. No bypass.** Both parsers call `ParseGuards.Apply` on the whole list. *Nothing to do.*
- **M3. An overflow in the reader.** A count at the top of the decimal range with a fraction threw `OverflowException` out of `TryRead`; the conversation's catch answered `not_done_nothing_taken`, so no money was at risk. *Fixed:* the addition is caught and the text does not read (one example).
- **M4. The reader is otherwise sound.** The `is ... or ...` patterns bind as written, every word check requires a non-digit token, zero and negative results do not read, a fraction with two groups or with anything after it does not read, and a result that is not whole dinars is refused by `AmountRule`. *Nothing to do.*
- **M5. The two `Model` sections agree** by reading, and `EvalSettingsTests.TheModelCandidatesMatchTheWebConsole` holds it from the console to the runner; a setting present only in the runner's candidate would pass unnoticed. *Left as noted.*

## Recommendations

- **R1. `allow_fallbacks: true`.** OpenRouter's routing page describes `quantizations` and `data_collection` as filters and `order` as a priority list, so with fallbacks on any other fp8 endpoint that does not collect data may serve a call: the pin is to the precision and the policy, not to the two named providers. The page does not say outright that a fallback obeys the filters; that is an inference. The repository cannot show which provider served each call. *Decision:* kept as it is. With fallbacks off, an outage at both named providers would fail cases on a run that happens once. The disclosure states the pin as precision and policy, and that the runner does not record the provider.
- **R2. Tests to add before the tag.** The inputs of I1, the overflow input, and the guard on `purchase`, `bill` and `unclear` origins. *Done* at the unit level (the examples and `ReasonIntentGuardTests`); not added through the orchestrator.
