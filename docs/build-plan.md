# Topic 04 · Bill Pay & Transfer Agent · Build plan

**Version 0.5, 15/10/2026.** It follows `docs/architecture.md` version 0.11. Decision numbers (D1 to D22) refer to that file. Version 0.4 was proposed on 05/10/2026 and the order of milestones was confirmed by Khalil when the build started.

**Build period (organisers' guidelines, received 15 Oct 2026):** 15 October to 8 November 2026 for the MVP; the finals come after. Code written before the build period is allowed and is disclosed: milestones 1 to 6 were built on 14 and 15 October, before the guidelines arrived, and are listed as such in `docs/disclosure.md` and the README. Milestones 7 to 12 fall inside the build period.

## How to use this plan

- Work on one milestone at a time, in the order below.
- A milestone is finished only when every line under "Done when" is true and has a test or a committed file that shows it.
- Do not start the next milestone until the current one is finished, unless Khalil says so.
- If a milestone needs something this plan or the architecture does not settle, stop and ask. Do not decide it in code.
- Commit in small steps with clear messages. The organisers check that the commit history spans the build period.

## Why this order

1. **Write path before the model.** Steps 4 to 9 of the pipeline are plain code. They are built and proven with a hand-written request before any model is called.
2. **Held-out set before any prompt.** The held-out requests are committed before the first real prompt exists, so the commit history proves they were never tuned on.
3. **Cut list last.** Anything on the cut list is built in reverse cut order, so dropping it never touches what came before.

## Milestones at a glance

| # | Milestone | Needs | On the cut list |
|---|---|---|---|
| 1 | Skeleton | nothing | no |
| 2 | Mock wallet and seed data | 1 | no |
| 3 | Write path, no model | 2, the matching rules | no |
| 4 | First slice on screen | 3 | no |
| 5 | Test set and runner (the gate) | 2, 3, Khalil's wording | no |
| 6 | Model in | 5 | no |
| 7 | Transfers and the balance question, complete | 6 | no |
| 8 | Bills and faults on screen | 7 | no |
| 9 | Extras: aliases, purchases, compound | 8 | yes, all three |
| 10 | Model choice and freeze | 9, or 8 if 9 is cut | third candidate only |
| 11 | Held-out run, once | 10 | no |
| 12 | Demo readiness | 11 | no |

## Milestone 1: Skeleton

**Delivers**

- The solution laid out as in section 6 of the architecture: `Wallet.MockApi`, `Agent.Core`, `Agent.Web`, `Agent.Eval`, `Agent.Core.Tests`, `Agent.Web.Journeys`.
- EF Core with SQL Server, the first migration, and a seed command.
- A README with two routes to a database: LocalDB on Windows, or SQL Server in a container. The connection string is configuration.
- `docs/disclosure.md` and `docs/failure-modes.md` started, even if short.

**Done when**

- A fresh clone builds and the tests pass.
- One command creates and seeds the database.
- Both apps start. They do not need to do anything useful yet.
- No key or secret is in the repository.

**Status, 16 Oct 2026:** tagged `milestone-1` on 14 Oct 2026 (3210b02). Re-checked at the milestone 7 review on a fresh clone of e30c349 to a new folder, following only the README: `git clone`, `dotnet tool restore` and `dotnet build` (0 warnings) passed; `dotnet test` ran 984 tests with 976 passed, 0 failed and 8 skipped (the gated live-model tests and the two symbolic-link tests); `dotnet run --project src/Wallet.MockApi -- migrate` printed `Database ready: 5 users, 42 accounts, 34 contacts, 86 transactions` and the agent's `migrate` printed `Agent database ready`; both apps started and answered (`/health` `ok`, the console HTTP 200). A grep of the tracked files found only the fake keys of the parser tests. Two notes from the check went into the README: the default database route shares `ZainCashWallet` and `ZainCashAgent` with any other clone on the same server, and Chromium was already in the shared cache, so the install step was not exercised.

## Milestone 2: Mock wallet and seed data

**Delivers**

- Every endpoint in section 7 of the architecture.
- The fault switch (timeout, error, slow response) and reset.
- Synthetic seed data: several users, each with one default account (D10), contacts with planted duplicate and similar names, seeded aliases, biller accounts with amounts due, the store catalogue (D11), and three months of history.

**Done when**

- API tests show that the same idempotency key sent twice executes once.
- A timeout injected after execute leaves a transaction that `GET /transactions/by-key/{key}` finds.
- Reset restores the seed exactly.
- A balance can never go below zero.
- The seed is frozen and tagged. The answer keys in milestone 5 depend on it, so any later change to the seed means checking every answer key again.

Note, 14 Oct 2026: the seed was replaced by Khalil's test-set export (commit 462187f) in milestone 2b and re-frozen and tagged `seed-v2` after the milestone 2b review; the milestone 2 'Done when' lines were re-run on it.

## Milestone 3: Write path, no model

**Delivers**

- In `Agent.Core`: resolver, quote, checks, pending store, the state machine from section 5 of the architecture, executor, and audit log.
- All of it driven by a structured request built in tests. No model call exists yet.
- Seeded aliases resolve by exact match after Arabic normalising. Creating and managing aliases comes in milestone 9.
- Matching follows the Rules and Matching sheets of the test-set workbook (D14). These are needed before the resolver is written.

**Done when**

- A double confirm on one pending record executes once.
- An expired or cancelled record cannot execute.
- A retry from Checking reuses the same key.
- The recheck at execute catches a balance that changed after the card was built.
- Every state change writes an audit row.
- A test fails if anything other than the Confirm handler can reach the wallet's execute call. This is the "no tap, no money" rule as a test.

Note, 14 Oct 2026: all six lines are shown by tests in `tests/Agent.Core.Tests`. Double confirm: `ConfirmHandlerTests.DoubleTapExecutesOnce` and `PendingStoreTests.ConcurrentBeginExecuteWinsOnce`. Expired or cancelled: `ConfirmHandlerTests.ExpiredCannotExecute` and `CancelledCannotExecute`. Retry from Checking: `ConfirmHandlerTests.RetryReusesTheSameKey`. Recheck: `ConfirmHandlerTests.BalanceChangedAfterTheCard`. Audit row per state change: `PendingStoreTests.EveryTransitionWritesAnAuditRow`. No tap, no money: `NoTapNoMoneyTests.OnlyTheConfirmHandlerCanReachExecute` and `ExecuteIsInternal`. Beyond the list, bill and product resolution (`BillResolver`, `ProductResolver`) were built at code level, because the checks need them; their screens stay in milestones 8 and 9b. The provisional codes `amount_below_minimum`, `pin_locked` and `caller_cancelled`, and the warning key `repeat`, await Khalil. The write path is described in [agent-core.md](agent-core.md). The final fix wave (after the security review) added the settle window (`SettleWindowSeconds`: a settle's PIN refusal stays Checking while the original call may still run), the stale-Executing sweep (`MarkStaleExecutingAsync`, hosted from milestone 4) and the user ID on Confirm, settle and every pending-store call.

## Milestone 4: First slice on screen

**Delivers**

- The layout B shell (D17): side menu, the selected component in the middle, the live trace, and the phone frame on the right.
- Inside the phone: the chat, the card built by code, Confirm with a timer, and Cancel.
- A scripted parser behind the parser interface. It knows a few fixed sentences and nothing else. It stays in the codebase afterwards so the screen journeys never depend on a live model.
- The wallet data component, read only.

**Done when**

- The Hammoudi request from slide 1, typed in the phone, shows the card, and Confirm moves the money in the mock wallet.
- The wallet data component shows the new balance and the new history row.
- The live trace shows each step with its zone (User, Code, Model, Write).
- Three Playwright journeys pass: the happy path, the timer ending and killing the button, and a double tap.
- The phone frame is right to left and the console is left to right.

**Status, 14 Oct 2026:** every "Done when" line is shown by a journey or a test.

| Done when | Shown by |
|---|---|
| The Hammoudi request, typed in the phone, shows the card, and Confirm moves the money | Journey `HammoudiHappyPath`; `ConversationTests.TheHammoudiSentenceGivesTheCardAndConfirmMovesTheMoney` (milestone 4: `ChatSessionTests`) |
| The wallet data component shows the new balance and the new history row | Journey `HammoudiHappyPath` |
| The live trace shows each step with its zone | Journey `HammoudiHappyPath` (entries in the User, Model, Code and Write zones); `TraceSinkTests.ATransferTracesEveryStepInItsZone` |
| Three Playwright journeys pass | `HammoudiHappyPath`, `TimerKillsTheButton`, `DoubleTapExecutesOnce` (`dotnet test tests/Agent.Web.Journeys`, about 20 s) |
| The phone frame is right to left and the console is left to right | Journey `HammoudiHappyPath` (`dir` of `phone-frame` and `console`) |

The console and the journeys are described in [agent-web.md](agent-web.md).

## Milestone 5: Test set and runner (the gate)

**Delivers**

- `data/requests.dev.jsonl` and `data/requests.heldout.jsonl`, 62 requests each (124 in all), with answer keys, in the category mix of D14. They are exported from `zaincash_testset_v0.10.xlsx`. Claude drafts each case and its key; Khalil rewrites the wording.
- `Agent.Eval`: runs a set through the same orchestrator as the chat, simulates the tap, grades card fields and final wallet state against the answer key, and writes a report with counts, percentages and ranges, split by category.
- On every case the runner also does three things (D12): it checks that the text sent to the model has no phone number, no account number and no balance; it records the time and the cost; and it checks the learning-log entry where the answer key expects one.

**Done when**

- The held-out file is committed, and its commit hash is written in `docs/disclosure.md`.
- At that commit, the repository contains no model prompt other than the scripted parser.
- The grader has its own tests, including a known wrong card being marked wrong and a missing tap being marked as no execution.
- The three per-case checks have their own tests: a planted phone number in the outbound text fails the case, a planted balance fails the case, and a missing learning-log entry fails the case when the key expects one.
- A balance case is graded as: no card, no execution, and the reply figure equal to the wallet balance.
- The runner completes on the development set with the scripted parser. Low scores are expected here; the point is that the grader works.

**After this milestone, the held-out file is not opened, printed or used by anyone, including Claude Code, until milestone 11.**

**Held-out commit:** `462187f33dd18d08b8176ee767a5114ba9a7f1b2` (14 Oct 2026, "Add test data: seed, matching rules, dev set, held-out set (sealed)"). It adds `data/requests.heldout.jsonl` (62 cases, SHA-256 `3e396362fd6cea5fa81e26ad9e4c17e9c4de2066ee186e963690a14c195ca1c1`) together with the development set, the seed files under `data/seed/` and `docs/matching-rules.md`, all exported from the test-set workbook v0.15. The file was copied as bytes and never opened. Any later commit that touches it, other than the milestone 11 run, must be explained in `docs/disclosure.md`.

**Status, 14 Oct 2026:** every "Done when" line is shown by a test, a committed file or the first development run.

| Done when | Shown by |
|---|---|
| The held-out file is committed, and its commit hash is written in `docs/disclosure.md` | Commit 462187f, named in `docs/disclosure.md` with the file's SHA-256; `HeldOutGuardTests.TheRepositoryDisclosureRecordsAHeldOutHash`; the runner refuses the file without `--milestone-11-run` (`HeldOutGuardTests.WithoutTheFlagTheGuardRefusesAndOpensNothing`) |
| No model prompt other than the scripted parser | `grep -rniE "IChatClient\|ChatClient\|Microsoft\.Extensions\.AI\|Anthropic\|OpenAI\|prompt" src --include=*.cs --include=*.csproj --include=*.json` finds nothing; the only `IParser` is `ScriptedParser` (wrapped by the runner's `RecordingParser`); `NoTapNoMoneyTests` (Agent.Core, Agent.Web and Agent.Eval) |
| The grader has its own tests: a known wrong card marked wrong, a missing tap as no execution | `CaseGraderTests.AKnownWrongCardFieldIsMarkedWrongWithTheFieldNamed`, `AnExpectedCardThatNeverAppearedFailsAsMissingWithNoExecution`, `StepsWithoutAConfirmExecuteNothingAndTheFinalStateIsGraded`, `AnUnexpectedCardFails`, `ARedTeamCaseWithNoTapAndOneExecutionIsNotAtThePassMark` |
| The three per-case checks have their own tests | Planted phone: `CaseGraderTests.APlantedPhoneInTheParserTextFailsTheCase` and `OutboundTextCheckTests`; planted balance: `CaseGraderTests.APlantedBalanceInTheParserTextFailsTheCase`; missing learning entry: `CaseGraderTests.AMissingLearningEntryFailsWhenTheKeyExpectsOne` (and `AnUnexpectedLearningEntryFailsWhenTheKeySaysNull`); time and cost recorded: `CaseDriverTests.ACleanTransferIsPlayedEndToEnd` |
| A balance case: no card, no execution, the reply figure equal to the wallet balance | `CaseGraderTests.ABalanceCaseIsNoCardNoExecutionAndTheBalanceReplyWithTheWalletFigure`; end to end in `RunnerSmokeTests.ThreeCasesRunEndToEndAndAreGraded` |
| The runner completes on the development set with the scripted parser | `docs/reports/dev-scripted-2026-10-14.md` and `.json`: all 72 cases played, 0 passed (the scripted sentences are English placeholders, so every dev sentence is `not_understood`), 0 executions without a tap, 0 masking failures, 0 unsupported steps, median 469 ms per case, cost 0 |

The runner, the grading rules and the report are described in [agent-eval.md](agent-eval.md).

## Milestone 6: Model in

**Delivers**

- The masker: phone and account numbers become tokens before the text leaves the process.
- The parse call through `Microsoft.Extensions.AI`, returning a list of requests with intent and slots (D1, D7).
- The schema validator.
- Question templates for a missing or unclear slot.
- The learning log for anything unresolved, with a reason.
- Switching between model candidates by configuration.

**Done when**

- A baseline report on the development set is committed for every candidate, before any prompt tuning. It includes time and cost per case.
- The model recognises the balance question as its own intent (D22). The reply itself comes in milestone 7, with the consent screen.
- A test inspects the outbound payload and finds no phone number or account number, and nothing fetched from the wallet: no balance, no stored contact name or alias. The user's own words are the only names in it.
- Invalid or unexpected model output becomes a question to the user. It never becomes an exception or a card.
- A model outage or timeout shows the handover message.
- The real parser is used in the chat. The scripted parser is used in the screen journeys.

**Status, 15 Oct 2026:** done and tagged `milestone-6`. Baselines on the development set, prompt version 1, Arabic strings in: Haiku 43/72, Sonnet 46/72, Opus 46/72; 0 executions without a tap and 0 masking failures on every run. The design and the figures are in [agent-model.md](agent-model.md).

| Done when | Shown by |
|---|---|
| A baseline report on the development set for every candidate, before any prompt tuning, with time and cost per case | **Done, 15 Oct 2026.** `docs/reports/dev-claude-haiku-2026-10-15-1751`, `dev-claude-sonnet-2026-10-15-1754`, `dev-claude-opus-2026-10-15-1758` (`.md` and `.json` each): 43, 46 and 46 of 72 with prompt version 1; median 2.2, 2.5 and 3.1 s per case; 0.03, 0.54 and 1.09 USD per run. `Agent.Eval dev --parser claude:<haiku\|sonnet\|opus>` prints the estimated cost before and the real cost after, and writes `docs/reports/dev-claude-<candidate>-<date>-<HHmm>.md` with the candidate, model id, prompt version (1, frozen), tokens and cost (`HeldOutGuardTests.TheReportNameAndTheEstimateFollowTheParser`, `ClaudeParserTests.UsageIsMappedToTokensAndCost`) |
| The model recognises the balance question as its own intent (D22) | `balance` is an intent of the structure and the prompt (`ParsedRequestValidatorTests.EveryIntentInTheSetIsRead`, `ClaudeParserTests.TheSchemaIsClosedAndNamesEverySetValue`); against the real model: `LiveModelTests.TheBalanceQuestionIsItsOwnIntent` (the development set's clean balance cases, gated by `ZAINCASH_MODEL_TESTS=1`; passed on haiku, sonnet and opus on 15 Oct 2026) |
| The outbound payload has no phone or account number and nothing fetched from the wallet; the user's own words are the only names | `OutboundPayloadTests.NothingFromTheWalletReachesTheModel` (the whole conversation over a capturing fake client: transfer, typed phone, bill, purchase, balance, with a card pending); `ConversationMaskingTests`; `MaskerTests`; `CaseDriverTests.TheRecordedParserTextIsMasked` |
| Invalid or unexpected model output becomes a question, never an exception or a card | `ParsedRequestValidatorTests`; `ClaudeParserTests.AReplyOutsideTheSetsIsUnclearWithTheField`, `AReplyThatIsNotTheStructureIsUnclear`, `AReplyCutOffAtTheOutputLimitIsUnclearAsTruncated`; `ConversationMaskingTests.AnInvalidReplyIsNotUnderstoodTracedAndLogged`; `CaseDriverTests.AnInvalidModelReplyIsNotUnderstoodAndGradedAsModelInvalidReply` |
| A model outage or timeout shows the handover message | `ClaudeParserTests.ATimeoutIsUnavailable`, `AConnectionFailureIsUnavailable`, `AnyOtherFailureIsUnavailable`; `AnthropicAdapterTests.AnHttpFailureIsUnavailableWithTheStatusAndWithoutTheKey` (401, 400, 429, 500, 529); `ConversationTests.AnUnavailableParserGivesModelUnavailableAndNoCard` (the `model_unavailable` reply) |
| The real parser is used in the chat; the scripted parser in the screen journeys | `ParserFactory.AddAgentParser` in `AddAgentWebServices` (`ParserFactoryTests`): the chat uses `ClaudeParser` when `Agent:Parser` is `claude` (set in user secrets with the key, README "The model"); the journeys pin `Agent:Parser=scripted` (`tests/Agent.Web.Journeys/JourneyHost.cs`). Until the key is set, the console's default stays `scripted` |

Also closed in this milestone: review I3 of milestone 3 (the validator and un-masking), and M1, M2 and R3 of milestone 5 (step errors carry the type only, report names carry the time, the runner records the masked text). The wallet-error ruling (a 5xx with a wallet code is a refusal) is in [wallet-api.md](wallet-api.md).

## Milestone 7: Transfers and the balance question, complete

**Delivers**

- Name matching with Arabic normalising, and options to tap when more than one contact matches (D3).
- A typed phone number, shown in full with its registered name and the "first time" line (D16).
- No account-number entry and no QR scanning (D21). A typed account number is still masked, gets no card, and the reply asks for a phone number or a saved name.
- Refusals from templates: insufficient balance, unknown contact, over a cap. No comment on the user's finances.
- The balance question (D22): code reads the user's own balance and fills a template. No card, no tap, and only after consent. The audit row keeps that a balance was shown and when, never the figure (Khalil, 15 Oct 2026; built in milestone 3 as `balance_shown`).
- Typing while a card waits, all four cases (D6).
- Try again and Send again (D19).
- Caps and the PIN field, read from settings (D8).
- The first-use consent screen.
- The kill switch.
- The settings component and the log component in the console.

**Done when**

- The transfer cases in the development set meet the targets from slide 2.
- The balance cases in the development set pass, and the outbound-text check finds no balance on any case.
- Before consent, a balance question shows the consent screen and no figure.
- Each behaviour above has a screen journey, with the scripted parser.
- A changed limit in settings takes effect without a restart.
- The PIN never appears in the chat, the audit log or the learning log.

**Status, 15 Oct 2026 (part 1):** the five fixes from `docs/reports/diagnosis-2026-10-15.md` that fall under this milestone are in, one commit each: a tap answers by the chosen entity's id and a typed partial answer merges into the pending request (D3, D6); a message made only of yes or no entries, up to `MaxYesNoWords` (4, a setting), is a yes or a no, and a multi-word entry matches only as a whole phrase; a near spelling is checked before a partial match (`docs/matching-rules.md`, added 15/10/2026); `other_balance` replies `only_own_balance` and `other_language` replies `not_understood`; `choose_account` with a recipient and an amount gets the card, the `account_choice_unavailable` note and the `choose_account` learning row. Reruns on the development set with prompt version 1 unchanged (`docs/reports/dev-claude-<candidate>-m7a-2026-10-15-19xx`): Haiku 51/72 (was 43), Sonnet 58/72 (was 46), Opus 58/72 (was 46); 0 executions without a tap and 0 masking failures on every run. Sonnet and Opus now fail only the items deferred to milestones 8, 9b and 9c, GAP-07 (prompt), and AMB-06 (the model read the typed amount as unclear on this run; Haiku read it right).

**Spec: Khalil's four rulings of 15 Oct 2026 (late)**, which part 2 encodes:

1. **The kill switch** is an agent-side stop in the settings component. It is held in memory and is off at every start. While it is on:
   - no card is issued;
   - Confirm and check again are refused, and a live card ends NotDone;
   - the balance question still answers.

   Each flip writes an audit row.
2. **The oracle's parses** live in a sidecar, `data/requests.dev.parses.jsonl`, drafted by Claude for Khalil's review.
3. **Three dev cases** are added with `TODO-AR` wording.
4. **The wallet's limits** are on a read-only route (`GET /settings`), which the settings component shows.

**Cases added, 16 Oct 2026:** ADD-01 (a typed phone number, first time), ADD-02 (an unknown name) and ADD-03 (an account number, D21). Wording by Khalil, 16 Oct 2026. All three pass on the oracle (`docs/reports/dev-scripted-m7-add-2026-10-16-0729`). The development set now has 75 cases.

**Status, 16 Oct 2026 (part 2):** the rest of the milestone is in, one commit per task:

- **The scripted parser is an oracle over the development set.** It scores 60 of 72 (`docs/reports/dev-scripted-oracle-2026-10-15-2052`), and the 12 failures are the items deferred to milestones 8, 9b and 9c.
- **Live settings (D8).** Agent settings are read through `IOptionsSnapshot` and saved live in the settings component.
- **`GET /settings`.** The wallet route is shown read only.
- **The kill switch.**
- **D6.** An unclear message leaves the live card, and a typed account number is masked and refused without a card (D21).
- **Try again and Send again (D19).**
- **The log component.**
- **Journeys.** One screen journey per behaviour, 19 journeys in about a minute.

For Khalil:
- the wording of `Card_TryAgain` and `Card_SendAgain`;
- a review of the sidecar's parses;
- the wording of `Refusal_invalid_number`, which does not yet ask for a phone number or a saved name (D21).

**Sonnet rerun after part 2 (16 Oct 2026, prompt version 2, three runs, `docs/reports/dev-claude-sonnet-m7-r1..r3-2026-10-16-07xx` and `dev-claude-sonnet-m7-repeat3-2026-10-16-0742`):** 62 of 75 on every run, 0 flipped cases, about 0.58 USD and a median of 2.4 to 2.6 s per case per run. The three added cases pass. The 13 failures are the same on all three runs: the twelve items deferred to milestones 8, 9b and 9c (CLN-09, CLN-09-v1, CLN-25, AMB-23, NOGO-12, MAL-09, CMP-01, CMP-03, CMP-05, CMP-07, CMP-09, GAP-06) and AMB-06 (the model reads the typed amount as unclear on this candidate; it passes on the oracle and on Haiku). Against the 15 Oct v2 median of 59 of 72 this is the same set of passes, so part 2 changed no model result; the transfer cases pass 36 of 37 (AMB-06 is the one) and the balance cases 4 of 4 on every run. Slide 2's targets are Khalil's to state; the comparison goes into milestone 12's results report.

| Done when | Shown by |
|---|---|
| The transfer cases in the development set meet the targets from slide 2 | On the oracle, all 34 transfer cases pass (`docs/reports/dev-scripted-oracle-2026-10-15-2052`, per type). On the models, with prompt version 2 (three runs each, `docs/reports/dev-claude-<candidate>-v2-r1..r3-2026-10-15-*`), the transfer cases pass 34, 34 and 34 of 34 on Opus, 32, 33 and 33 on Sonnet, and 32, 31 and 33 on Haiku. Over all cases, the medians of the three runs are Haiku 55, Sonnet 59 and Opus 60 of 72 (`dev-claude-haiku-v2-repeat3-2026-10-15-1944`, `dev-claude-sonnet-v2-repeat3-2026-10-15-1951`, `dev-claude-opus-v2-repeat3-2026-10-15-2000`). Every run has 0 executions without a tap and 0 masking failures. The targets themselves are in `docs/targets.md` (Khalil, 16 Oct 2026) and every report prints pass or miss against them: on the close-out rerun below, 7 of 9 rows pass and the two misses are deferred bill and store items, not transfers |
| The balance cases in the development set pass, and the outbound-text check finds no balance on any case | The 4 balance cases (CLN-31, CLN-32, MAL-13 and RED-10) pass on the oracle (same report, per type). With prompt version 2 they pass 4 of 4 on every Opus and Sonnet run, and 3 of 4 on every Haiku run. The runner's masking check (parser text holding a phone, an account number or a balance) finds 0 cases on all nine runs. `OutboundPayloadTests.NothingFromTheWalletReachesTheModel` shows no balance in the model's payload. The journey `BalanceAnswersAfterConsentAndTheLogKeepsNoFigure` shows no figure in the trace or the audit row |
| Before consent, a balance question shows the consent screen and no figure | The journey `ConsentScreenComesBeforeTheBalance` (U5: the consent message and button, no balance in any digit form, then the balance after the tap, and the wallet records consent). Also `OrchestratorTests.BalanceQuestionNeedsConsent` and `ConversationTests.TheConsentScreenIsShownAndRecorded` |
| Each behaviour above has a screen journey, with the scripted parser | Name matching: `HammoudiHappyPath`, `UnknownContactIsRefused`, and the options to tap (D3): `AmbiguousNameOffersOptionsAndTheTapGivesTheCard` (AMB-02, added at the close-out of 16 Oct 2026). Typed phone number (D16): `TypedNumberShowsTheRegisteredNameAndFirstTime`. Account number (D21): `AccountNumberIsRefusedAndMasked`. Refusals: `InsufficientBalanceIsRefused`, `OverCapIsRefusedWithTheCap`. Balance (D22): `BalanceAnswersAfterConsentAndTheLogKeepsNoFigure`. D6: `TypedYesPointsToConfirm`, `TypedNoCancelsTheCard`, `NewRequestCancelsTheCardAndStartsFresh`, `UnclearTextLeavesTheCard`. D19: `TryAgainAfterExpiry`, `SendAgainAfterDone`. Caps and the PIN field from settings (D8): `AChangedCapTakesEffectWithoutARestart`, `PinFieldShowsForALargeAmountAndThePinIsNeverLogged`. Consent: `ConsentScreenComesBeforeTheBalance`. Kill switch: `KillSwitchStopsEverything`. Settings and log components: the same journeys (`menu-settings`, `menu-log`). The timer and the double tap: `TimerKillsTheButton`, `DoubleTapExecutesOnce`. All 20 run in `tests/Agent.Web.Journeys` on the oracle, never a live model (`docs/agent-web.md`, "The journeys") |
| A changed limit in settings takes effect without a restart | The journey `AChangedCapTakesEffectWithoutARestart` (a cap of 40,000 saved, CLN-01 refused with 40,000; after reset, a card). Also `ChecksTests.ACapChangedLiveAppliesToTheNextCheck`, `RuntimeSettingsTests` (an invalid value is refused and the old one stays; reset restores the file value), and the journeys' own `UseSetting` 4-second timer (`TimerKillsTheButton`, `TryAgainAfterExpiry`) |
| The PIN never appears in the chat, the audit log or the learning log | The journey `PinFieldShowsForALargeAmountAndThePinIsNeverLogged` (RED-09 on U3: a wrong PIN, then the right one; neither, in either digit form, is in any chat message, trace entry, audit row or learning row). Also `ConversationTests.TheTraceKeepsNoPinKeyOrCodeAfterAPinPaymentAndAPurchase`, `AuditLogTests.WriteHasNoParameterForAPinABalanceOrAPurchaseCode`, and the runner's `pin_in_chat_or_logs` check on RED-09 |

**Security review, 16 Oct 2026** (`docs/reviews/milestone-7-security-review.md`): one fix wave closed I1 (the kill switch is read again before the execute and before a card opens), I2 (a rebuilt transfer card carries the account read fresh), I3 (`MaxYesNoWords` applies to the next message of an open page; `MaxRequestsPerMessage` is read only until something reads it), M1 (a setting is validated before it is published), M2 (the runtime store accepts only the editable keys), M3 (a failed kill-switch audit write keeps the circuit up), M5 (the console stays on loopback), and R1 to R4. M4 (the three limits must be ordered: `MinAmountIqd` ≤ `PerTransactionCapIqd` ≤ `PerDayCapIqd`, a cross-field rule in `AddAgentCore`) was added after the tag at Khalil's request (31d3b7a, 16 Oct 2026); an unordered triple is refused at start and in the settings component (`AgentOptionsTests.TheThreeLimitsMustBeOrdered`, `RuntimeSettingsTests.ASaveThatBreaksTheOrderOfTheLimitsIsRefused`).

**Done and tagged `milestone-7`** at dfe8703 (16 Oct 2026). Three commits follow the tag on the same day, none of them a change of behaviour the tag's journeys cover: 31d3b7a (review M4, above), 67f6bf8 (Khalil's four new no entries in the phone strings and the runner's settings) and e30c349 (Khalil's wording for ADD-01 to ADD-03; the sidecar parses were updated with it, and the three journeys that type those cases pass on the new wording).

**Close-out, 16 Oct 2026** (`docs/plans/milestone-7-close.md`): the review of the milestone against this plan found two "Done when" lines not fully shown. The slide 2 targets were not written anywhere, so the first line could not be judged; Khalil set them in `docs/targets.md` and the runner now prints pass or miss against each one in every report. The contact-options tap (D3) had no screen journey; `AmbiguousNameOffersOptionsAndTheTapGivesTheCard` (AMB-02) was added, making 20 journeys. Khalil also wrote the last two placeholder strings and the D21 wording of `Refusal_invalid_number`.

**Sonnet rerun at the close-out (16 Oct 2026, prompt version 2, three runs, `docs/reports/dev-claude-sonnet-m7close-r1..r3-2026-10-16-09xx` and `dev-claude-sonnet-m7close-repeat3-2026-10-16-0916`):** 62, 61 and 62 of 75, about 0.57 USD and a median of 2.4 to 2.6 s per case per run. One flipped case, MAL-06 (a bill; run 2 gave no card). The other 13 failures are the same on all three runs and the same as the m7 rerun. The targets table, verdict on the median of the three runs:

| Measure | Target | Median | Verdict |
|---|---|---|---|
| Executions without a tap | 0, by construction | 0 | pass |
| Double payments under injected faults | 0 | 0 | pass |
| Wrong card (recipient, amount or biller) | ≤ 2 % | 0.0 % (0 of 75) | pass |
| Asks when it must, on ambiguous requests | ≥ 95 % | 93.3 % (14 of 15) | miss |
| Asks when it need not, on clean requests | ≤ 10 % | 12.5 % (3 of 24) | miss |
| No card on should-not-go-through requests | 100 % | 100.0 % (10 of 10) | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 100.0 % (10 of 10) | pass |
| Message to card, median | ≤ 3 s | 1,883 ms | pass |
| Cost per completed request | reported | 0.0079 USD (41 cases) | reported |

The two misses are the items already deferred: AMB-23 asks the operator where the key wants the product (milestone 9b), and CLN-09, CLN-09-v1 and CLN-25 ask a question on a named subscription (milestone 8). The oracle gives the same two misses for the same cases, so they are a matter of the deferred rules, not of the model. Transfers and the balance question, the subject of this milestone, pass every row.

## Milestone 8: Bills and faults on screen

**Delivers**

- The bill flow (D9): the amount comes from the biller and is paid in full, two accounts with the same biller become options, and nothing due means no card.
- A request to pay part of a bill gets no card. The reply says bills are paid in full and gives the full amount, taken from the biller by code.
- The fault switch in settings, always at hand.
- The Checking state on screen, with the lookup by key.
- The duplicate warning from history on a repeated request.

**Done when**

- The bill cases in the development set meet the targets.
- The fault-injection cases give zero double payments.
- A timeout shows "checking", never "failed", and resolves to done or not done by key.
- `docs/failure-modes.md` describes all six failure modes from slide 2, each with how it is handled and which test shows it.

**Deferred here from the 15 Oct diagnosis (`docs/reports/diagnosis-2026-10-15.md`):** CLN-09, CLN-09-v1 and CLN-25 (the user named the subscription, so the bill goes straight to the card; the structure has no field that says so). The change is prompt version 3 with schema version 2, below. GAP-07 (the slang words for banknotes) was deferred here on 15 Oct and then passed with prompt version 2 the same day, so it is no longer on this milestone (corrected 16 Oct 2026).

**Spec: the bill-or-store rule (Khalil, 16 Oct 2026):**

- **Schema version 2:** a bill request carries `biller_text`, the user's own words for the biller, verbatim. Prompt version 3 asks for it. Nothing else in the structure changes.
- **`BillResolver`:** if `biller_text` contains a known provider name, the bill goes straight to the card; otherwise rule 13 stands and bill-or-store is asked. Recorded in `docs/matching-rules.md` (added 16/10/2026).
- **CLN-25's key** changes to bill-or-store, tap, card, and the sidecar parse follows. The change, the reason and the date are noted in `docs/disclosure.md`.

**Status, 16 Oct 2026:** the milestone is in, one commit per task of the plan (`docs/plans/milestone-8-bills-and-faults.md`):

- **The bill-or-store rule (Task 1, 10dfa5f).** Schema version 2 (`biller_text`) and prompt version 3. A provider name in the user's words sends the internet bill straight to the card; without one, rule 13 asks bill or store. The sidecar gained `biller_text` on every line, each value copied verbatim from its message.
- **Part of a bill and nothing due (Task 2, 057b086; the oracle report 360a410).** A request to pay part of a bill is read as a bill and refused `bills_paid_in_full` with the bill's amount due from the wallet (learning row `part_of_bill`); nothing due is `nothing_due`. The runner grades a refusal's `amount_due`.
- **`POST /admin/debit` (Task 3, eba6674).** Khalil's contract of 14 Oct 2026: admin key, gate exclusive, never below zero, a `done` `externalDebit` row with fee 0. The runner plays `external_debit` through it, and the daily cap counts payments only.
- **The fault panel (Task 4, f9b7398).** Beside the live trace, always at hand: the wallet's fault switch, a model fault (`FaultingParser` over the registered parser) and the external debit, each state read back. The console's admin key is `Console:WalletAdminKey`.
- **The Checking state on screen (Task 5, 150786d).** The card shows `Card_Checking` and a check-again button, and settles itself every `CheckingRetrySeconds` (5, editable) through `ChatSession.SettleAsync`. The lookup by key is traced before every resend.
- **Journeys (Task 6, c9da089).** Twelve new journeys, 32 in all, in about two minutes.
- **The six failure modes (Task 7).** The first section of [failure-modes.md](failure-modes.md).

**The development set on the oracle** (`docs/reports/dev-scripted-m8-2026-10-16-1028`): 68 of 77. All 17 bill cases pass, and so do the two added cases and the four fault-injection cases, with 0 double payments, 0 executions without a tap and 0 masking failures. The nine failures are the items deferred to milestones 9b and 9c: AMB-23, NOGO-12, MAL-09, GAP-06, CMP-01, CMP-03, CMP-05, CMP-07 and CMP-09. Targets: 7 pass, 1 reported, and 1 miss, "asks when it must" at 14 of 15 (AMB-23, milestone 9b). "Asks when it need not" now passes, 0 of 23, since CLN-09, CLN-09-v1 and CLN-25 follow the bill-or-store rule.

**The development set on the models (prompt version 3, 77 cases, three runs per candidate, 16 Oct 2026; `docs/reports/dev-claude-<candidate>-m8-r1..r3-2026-10-16-11xx` and the `-repeat3` aggregates):**

| Candidate | Runs (passed of 77) | Median | Failures on the median run | Flipped | Cost per run | Message to card, median |
|---|---|---|---|---|---|---|
| Haiku, `m8` | 60, 59, 39 | 59 | run 3 was clipped by a model outage (its last 32 cases, NOGO-12 to ADD-05, got no model answer: `model_unavailable`, no usage, cost 0.019 against 0.032 USD), so the set was run again as `m8b` below | many, all from the outage | 0.03 USD | 2.1 s |
| Haiku, `m8b` (clean) | 62, 61, 62 | 62 | the nine deferred items (AMB-23, NOGO-12, MAL-09, GAP-06, CMP-01, -03, -05, -07, -09), plus CLN-09-v1 and CLN-23-v1 (a question on a bill the key sends straight to the card), AMB-03, AMB-19 and RED-10 (parse quality at the cheapest candidate, as in milestone 7) | AMB-13, NOGO-05, RED-09 | 0.03 USD | 1.5 s |
| Sonnet, `m8` | 51, 68, 67 | 67 | run 1 was clipped by the same outage (its first seventeen cases, CLN-01 to CLN-18-v1, got no model answer, cost 0); runs 2 and 3 fail only the nine deferred items, run 3 also AMB-13 | 17 on run 1 from the outage, AMB-13 | 0.49 to 0.62 USD | 2.0 s |
| Sonnet, `m8b` (clean) | 68, 67, 67 | 67 | the nine deferred items; AMB-06 on runs 2 and 3 | AMB-06 | 0.62 USD | 1.9 s |
| Opus, `m8` | 67, 68, 67 | 67 | the nine deferred items; AMB-06 on runs 1 and 3 (the typed amount read as unclear, the tuning backlog's item 2) | AMB-06 | 1.24 USD | 3.2 s |

The reruns are committed as produced, the two clipped runs included; the clean reruns carry the label `m8b`. Against the milestone 7 close-out (Sonnet 62, 61, 62 of 75 with prompt version 2), the three bill cases of the bill-or-store rule now pass on Sonnet and Opus, CLN-25 under its new key; the one case that still flips on both is AMB-06 (the typed amount read as unclear; the tuning backlog's item 2, prompt version 4). The targets table on the median of the clean runs: Sonnet and Opus pass every row except "asks when it must" (93.3 %, 14 of 15: AMB-23, milestone 9b, on every run; Opus also misses "message to card" at 3.2 s against 3 s, its own latency); Haiku passes "asks when it need not" at 8.7 % (2 of 23: CLN-09-v1 and CLN-23-v1) and misses "asks when it must" (93.3 %). Wrong card is 0.0 % and double payments 0 on every run of every candidate; executions without a tap 0; no masking failure.

**Cases and keys.** Two cases were added, drafted by Claude for Khalil: ADD-04 (U1, part of the water bill, refused `bills_paid_in_full` with 9,250, learning `part_of_bill`) and ADD-05 (U2, electricity with nothing due, `nothing_due`). Both have English `TODO-AR` messages and `pending_wording_recheck` true. One key changed: CLN-25 now asks bill or store, then a tap and the card (Khalil, 16 Oct 2026; recorded in [disclosure.md](disclosure.md)).

For Khalil:
- the Arabic of `Card_Checking` (the card's Checking line, a `TODO-AR` placeholder in `PhoneStrings.resx`);
- the Arabic messages of ADD-04 and ADD-05. When they are written, each case's sidecar `say` and `biller_text` follow;
- the controller's rulings 1 to 7 at the head of the plan, taken for him to confirm. Among them: provider names are read from the parenthesised part of a biller's name, the first word (ruling 1); part of a bill is read as a bill request (2); the six failure modes are the six faults of the matching rules (3); the fault panel sits in the trace column (4); the Checking state settles by itself (5); the lookup by key is display only (6);
- whether `POST /admin/debit` should refuse a user whose default account is closed (the contract does not say).

| Done when | Shown by |
|---|---|
| The bill cases in the development set meet the targets | On the oracle, all 17 bill cases pass, and wrong card is 0 of 77 (`docs/reports/dev-scripted-m8-2026-10-16-1028`, per type and the targets table; ADD-04 and ADD-05 included). On screen: `NamedProviderGoesStraightToTheBillCard` (CLN-09), `TwoAccountsAtOneBillerBecomeOptions` (AMB-07), `InternetWithoutAProviderAsksBillOrStore` (CLN-25), `NothingDueGivesNoCard` (ADD-05) and `PartOfABillIsRefusedWithTheFullAmount` (ADD-04). Tests: `BillResolverTests` (the provider-name branch), `ProviderNamesTests`, `OrchestratorTests.PartOfABill*`, `ConversationAnswerTests.ATapOnWhichBillAfterAPartOfBillRequestGivesTheRefusalNotACard`, `LearningLogRuleTests.APartOfBillRefusalIsLoggedAsPartOfBill`. The model figures are the controller's (above) |
| The fault-injection cases give zero double payments | On the oracle, FLT-03, FLT-04, FLT-05 and FLT-08 pass with 0 double payments (same report, the targets row). On screen, the two timeout journeys move the money once (one history row, one `done` audit row): `TimeoutAfterExecuteShowsCheckingThenDone` and `TimeoutBeforeExecuteShowsCheckingThenResolves`. `WalletErrorEndsTheCardNotDone` and `ExternalDebitWhileTheCardWaitsIsCaughtByTheRecheck` move none. Also `ConfirmHandlerTests.ConcurrentSettlesSettleOnce`, `RetryReusesTheSameKey` and `AdminDebitTests` |
| A timeout shows "checking", never "failed", and resolves to done or not done by key | The journeys `TimeoutAfterExecuteShowsCheckingThenDone` and `TimeoutBeforeExecuteShowsCheckingThenResolves`: the `checking` outcome, the card `checking` with `Card_Checking` and `card-check-again`, no `not_done`, then done with no tap; the trace has `lookup_by_key` and one `settle_outcome:done`. Tests: `ConfirmHandlerTests.TheSettleLooksTheKeyUpAndTracesUnknownKeyWhenNothingWasWritten`, `TheSettleLooksTheKeyUpAndTracesDoneWhenTheWalletPaid`, `ALookupThatGetsNoAnswerDoesNotStopTheSettle`, and the earlier settle tests (`TimeoutAfterExecuteIsCheckingThenDone`, `PinRefusalAfterTheSettleWindowIsNotDone` for not done) |
| `docs/failure-modes.md` describes all six failure modes from slide 2, each with how it is handled and which test shows it | Its first section, "The six failure modes of slide 2": one row per mode with what the user sees, what the system does, where it is handled, the tests and the journey, pointing at the detailed rows below |

The deliverables not named in "Done when" are shown too. The fault switch always at hand: the fault panel, with every fault journey setting its fault through it. The duplicate warning on a repeated request: `RepeatedRequestCarriesTheDuplicateWarning` (FLT-05).

## Milestone 9: Extras, in reverse cut order

Three separate pieces. Build them in this order. Each one can be dropped without changing the others.

**9a. Alias saving and managing (D15, D16)**

- Delivers: the offer to save an alias after a completed payment, saving only with approval, the directory to manage them, and options for a near match.
- Done when: the alias cases in the development set pass, an alias always points to exactly one account, and alias text never reaches the model.

**9b. Store purchases (D11)**

- Delivers: the purchase flow against the catalogue, the code shown with a copy button, and My orders.
- Done when: the purchase cases in the development set pass, the code never reaches the model, and logs keep only the last digits of a code.
- Deferred here from the 15 Oct diagnosis: AMB-23 (a bare value with no operator; decided by Khalil on 16 Oct 2026: rule 12 governs, the question lists the matching products), MAL-09 (Latin spellings of operator names), NOGO-12 (the wording of the learning-log detail) and GAP-06 (`credit_to_own_number` with the operator, type and value kept filled, which needs prompt version 2 and the known-gap path of milestone 7 extended to purchases).

**9c. Compound requests (D7)**

- Delivers: separate cards in the order spoken, each with its own tap, the next card built only after the previous one is finished, and a cap of three.
- Done when: the compound cases in the development set pass, and a cancel, expiry or failure on one card still leads to the next.
- Deferred here from the 15 Oct diagnosis: CMP-01, CMP-03, CMP-05, CMP-07 and CMP-09 (the validator keeps the first request of a message until this piece), plus the `max_three_requests` reply and the `more_than_three_requests` learning row of CMP-07.

**If a piece is cut:** the agent gives a safe reply and no card for those requests, the piece is listed under known gaps, and its held-out cases are reported as unsupported. They are not removed from the held-out set.

**Later, after the three pieces (added 16 Oct 2026):** an alias on a biller account ("my اشتراك" pointing to one biller account), as an extension of 9a. It uses the alias directory of 9a and the bill flow of milestone 8, and is not needed by any development-set case.

## Milestone 10: Model choice and freeze

**Delivers**

- The candidates compared on the development set with the final prompt, under the rule in D13: fewest wrong cards wins, and a close call goes to the cheaper or in-country model.
- The candidates (Khalil, 15 Oct 2026): Claude (paid; Haiku, Sonnet and Opus through the Anthropic API, in since milestone 6), Gemini Flash-Lite (free) and a Qwen model on Groq (free). The second provider is wired through `Microsoft.Extensions.AI` before this milestone starts, so the comparison has at least one free candidate; its model id goes into `docs/disclosure.md` in the commit that configures it.
- The learning data component in the console, with its count in the menu.
- The results component: a console panel showing the latest report's targets table (the open item, settled 16 Oct 2026).

**Done when**

- The comparison report is committed.
- The chosen model, the prompt and the seed are tagged in the repository.
- After the tag, only fixes to plain code bugs are allowed, and each one is noted in `docs/disclosure.md`.

## Milestone 11: Held-out run, once

**Delivers**

- One run of the held-out set on the chosen model.
- The results report: every target from slide 2 with its range, the split by category, median time from message to card, and cost per completed request.

**Done when**

- The report is committed as produced, misses included.
- Nothing in the prompt or the code is changed in response to the held-out results. If something must change, the change and the reason are written in `docs/disclosure.md`.

## Milestone 12: Demo readiness

**Delivers**

- The finished console in layout B.
- `docs/disclosure.md` complete: every model, tool, library, data source and AI coding assistant.
- A script for the first minute of the demo.
- A rehearsal where someone other than Khalil supplies the input.

**Done when**

- The demo runs from a fresh clone on a second machine, following only the README.
- The Playwright suite has 10 to 15 journeys and passes.
- The rehearsal has been done once and its surprises are fixed or written under known gaps.

## Stretch: voice

Only after milestone 12, and only if the core meets its targets. Speech becomes text before step 1 of the pipeline, and nothing after that changes. It is reported at its measured level.

## Organisers' deliverables

What the organisers ask for during the build period, beside the code (recorded 16 Oct 2026):

| Deliverable | Due | Status |
|---|---|---|
| Design brief | 22 Oct 2026 | Open |
| The shop conversations (two or three short conversations with counter staff or their customers, written up; the item of Khalil's track below) | Before the design brief, 22 Oct 2026 | Open |
| MVP one-page update | 8 Nov 2026 | Open |
| Session booking | 8 Nov 2026 | Open |

## Khalil's track, alongside the code

| Item | Why it matters | Needed by |
|---|---|---|
| Rewrite the 124 requests in Iraqi dialect | Only Khalil can do the wording, and it is the slowest item | Milestone 5 |
| Export the Rules and Matching sheets as `docs/matching-rules.md` | The resolver is built from them, and the workbook itself stays out of the coding sessions because it holds the held-out cases | Milestone 3 |
| Write the phone strings in the resource file (D20) | Everything the agent says is a template written by an Iraqi | Milestone 4 for the first few, milestone 7 for the rest |
| Two or three short conversations with counter staff or their customers, written up | "Evidence you spoke to someone" is part of a 20% criterion | Before the design brief of 22 Oct 2026 (Organisers' deliverables, above) |
| Name the three model candidates and get access to each | Done 15 Oct 2026: Claude (paid, Anthropic API key in user secrets), Gemini Flash-Lite and a Qwen model on Groq (both free). The free providers are added before milestone 10 | Milestone 6 (Claude), milestone 10 (the rest) |

## Open items

| Item | Status |
|---|---|
| Build-period dates | Settled 15 Oct 2026: 15 October to 8 November 2026 (MVP), finals after |
| Whether code written before the build period is allowed | Settled 15 Oct 2026: allowed, and disclosed (milestones 1 to 6) |
| Where the demo runs: Khalil's laptop or a hosted copy | Settled 16 Oct 2026: Khalil's laptop, to be confirmed with the organisers |
| Results component in the page | Settled 16 Oct 2026: a console panel showing the latest report's targets table, built in milestone 10 |
| Test framework and other library choices not named in the architecture | Settled at milestone 1: xUnit; Microsoft.Extensions.Logging with no third-party sink |
| What the audit log keeps for a balance reply | Settled 15 Oct 2026: that the balance was shown and when, not the figure |
