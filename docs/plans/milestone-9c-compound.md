# Milestone 9c: Compound requests (D7)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task (one implementer agent for the whole milestone, per Khalil's rule of 14 Oct 2026; the controller reviews each task's diff itself). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every deliverable of milestone 9c in `docs/build-plan.md`: a message with several requests gets separate cards in the order spoken, each with its own tap; the next card is built only after the previous one is finished (done, not done, cancelled or expired); a cancel, expiry or failure on one still leads to the next; a new request cancels the rest; the cap is `Agent:MaxRequestsPerMessage` (3), `Refusal_max_three_requests` names the rest, and the learning row is `more_than_three_requests`. Each part's checks run when its turn comes, against the balance then (rule 21). Rules 21 and 22 go to `yes` in `docs/rules-coverage.md`. Done when CMP-01, CMP-03, CMP-05, CMP-07 and CMP-09 pass on the oracle and a cancel, expiry or failure on one card still leads to the next, each shown by a test and a screen journey.

**Architecture:** The model already returns a list (`ParsedRequestsDto`, prompt version 2 onwards: "one entry per request in the message, in the order spoken"); `ParsedRequestValidator` keeps the first and only notes the rest. This milestone carries the whole list through the parser seam, and gives the conversation a queue of the parts still to run. Nothing else moves: the orchestrator answers one request at a time as today, so rule 21 holds by construction (each part is quoted against the wallet when its turn comes); every card is its own pending record with its own key and timer (D7: one button bound to one transaction). No prompt change, no schema change, no migration.

**Tech Stack:** .NET 10, Blazor Server with MudBlazor 9, EF Core, xUnit, Playwright for .NET. No new library; nothing for `docs/disclosure.md`.

**Spec:** `docs/architecture.md` (D7, D6, D19), `docs/build-plan.md` (9c), `docs/matching-rules.md` (rules 21, 22), `docs/rules-coverage.md` (rows 21, 22), `docs/known-gaps.md` (rows 8, 17), `data/README.md` (the step keys `then_reply`, `no_card_N`; the reply key `not_prepared`), the brief of 19 Oct 2026.

## Controller rulings (for Khalil to confirm)

1. **When the "not prepared" reply is shown.** CMP-07 puts `then_reply: max_three_requests` on the third Confirm, so the reply comes when the last prepared part finishes (done, not done, cancelled or expired), not when the message is read. If the user abandons the prepared parts (a new message cancels the rest), the reply is never shown; nothing was promised for the fourth part.
2. **How the rest are named.** `Refusal_max_three_requests` is Khalil's line and is left as it is. A second line follows it, the new key `Refusal_not_prepared` (`{0}`: the names, joined by the new key `List_joiner`), both `TODO-AR`. Each part beyond the cap is named by the user's own words for it (a transfer's recipient, a bill's biller or label, a purchase's operator) only when the model's text occurs verbatim in the typed message (ruling 3 of milestone 9a, `parsedVerbatim`); otherwise by the request type's word (`Type_transfer`, `Type_bill`, `Type_purchase`; for any other intent the new key `Type_request`, `TODO-AR`). No model text ever reaches the phone (rules 3 and 4).
3. **The learning row** `more_than_three_requests` is written together with that reply, with the number of requests in the message as its detail (`4`), under the message's masked text.
4. **A structure that breaks a rule anywhere in the list** refuses the whole message, as a single broken request does today: `not_understood`, one `model_invalid_reply` row naming the field, nothing queued. The validator checks every entry.
5. **What drops the queue.** Only a message handled as a new request (one that reaches the orchestrator from the parse call, replacing or merging into the pending request) replaces the queue with its own parts (D7: "a new request cancels the rest"; trace `requests_dropped` with the count). A typed yes (points to Confirm), a typed no (cancels the card, which leads to the next), an unclear message while a card waits or a question is open, an answer read in code, a typed amount after an amount refusal, a tap, consent, a reissue and the Save of an alias all keep it.
6. **A message whose first part cannot be read** while a card waits (D6: the card is kept) or a question is open (the question is repeated) is handled as today, and its other parts are not queued.
7. **The amount retry** (D6 extension, 17 Oct 2026) opens only when the refused transfer is the last part: with a part still queued, the next part runs at once and the retry is not opened.
8. **Confirm and Cancel name their card.** `Conversation.ConfirmAsync(Guid pendingId, string? pin)` and `CancelAsync(Guid pendingId)` act only when `pendingId` is the card shown; otherwise they trace `tap_rejected` and add the outcome `already_handled`, and nothing moves. Rule 1 says the handler is "given one pending ID"; with cards that follow one another, a second tap meant for card 1 must never reach card 2. The phone's card panel captures its card's id when it renders; the runner passes the id of the card shown before the tap (a `twice` step uses the one id for both taps).
9. **A card still checking** is not finished: the next part waits until "check again" settles it (rule 6: unknown is never failed).
10. **The alias offer** under a Done transfer stays open while the next card is built (it is closed by the next typed message, tap, reissue or save, as today).
11. **`MaxRequestsPerMessage` becomes editable** in the settings component (range 1 to 20, as the attribute says), as every other limit is; a value saved applies to the next message. A new value never changes a queue already running.
12. **The runner grades the three keys** it only recorded until now: `then_reply` (after the step's last outcome message there is a reply with that code), `no_card_N` (the case issued fewer than N cards), `not_prepared` (the names of the reply, joined by `، ` for several, equal the key's text; rendered only as "names equal" or "names different"). A step's `card` and `card_outcome` are read on the card that was shown when the step began, so a step that finishes card 1 and brings card 2 grades card 1 (`StepObservation.ActedCardState`). The join form for several names is a guess until a key shows one.

## Global constraints

- Rules 1 to 9 of `CLAUDE.md`. In particular rule 1: the queue never executes anything; a queued part only ever becomes a card (or a question, a refusal, a balance reply) through the orchestrator, and that card waits for its own tap. `NoTapNoMoneyTests` stays green.
- No Arabic in C#, Razor or test code. New phone strings are English placeholders starting `TODO-AR`, listed in `PhoneStringsTests.AwaitingKhalil` and in the controller's summary: `Refusal_not_prepared`, `List_joiner`, `Type_request`.
- Khalil's answer keys, `docs/matching-rules.md` and the sidecar `data/requests.dev.parses.jsonl` are not changed. A mismatch is reported, not patched.
- The held-out set stays sealed (rule 7).
- Settings are configuration shown in the settings component.
- One commit per task, Khalil's name only (no Co-Authored-By line). Commit messages name the task's substance.
- Every doc section this milestone touches that describes a process gets or keeps a Mermaid diagram (the queue's states in `docs/agent-core.md`).

## Review focus

Inputs the spec implies that no case exercises; each gets a test in the task that owns it:

1. A list whose second entry breaks a rule (`intent` unknown): the whole message is `not_understood`, one `model_invalid_reply` row, no queue. (Task 1)
2. Part 1 refused (`recipient_not_found`), part 2 clean: the refusal, its learning row, then the card for part 2 in the same action. (Task 2)
3. A card that fails at Confirm (`insufficient_funds` by an external debit while it waits; or the wallet fault `error`): `not_done`, then the next card. (Task 2)
4. Two Confirms on card 1 at once (`Task.WhenAll`, the one id): exactly one execution, card 2 live and unpaid; then a Confirm with card 1's id after card 2 is live: `already_handled`, nothing moves. (Task 3)
5. The cap at 1 (`MaxRequestsPerMessage = 1`) on a two-part message: one card, the reply names the second part; and five parts at the cap of 3: three cards, two names in order. (Task 3)
6. A non-verbatim name beyond the cap (the parser returns a recipient the user did not type): the type word is used, never the model's text. (Task 3)
7. A compound message while a card waits: the card is cancelled, the old queue dropped (`requests_dropped`), the new parts queued. (Task 2)
8. The consent screen on part 1 of a two-part message: nothing queued is lost; after the button, part 1's card, then part 2. (Task 2)

---

### Task 1: The parser seam returns every request

**Files**
- Modify: `src/Agent.Core/Parsing/IParser.cs`, `src/Agent.Core/Parsing/ParsedRequestValidator.cs`, `src/Agent.Core/Parsing/ParsedRequestDto.cs` (the summary of `ParsedRequestsDto`), `src/Agent.Core/Parsing/ScriptedParser.cs`, `src/Agent.Core/Parsing/ClaudeParser.cs`, `src/Agent.Eval/Running/RecordingParser.cs`, `src/Agent.Web/Services/FaultingParser.cs`, `src/Agent.Core/Conversation/Conversation.cs` (reads `.Requests[0]` for now; the queue is Task 2), `docs/agent-core.md` ("The parser seam", "Masking and structure validation", "The Claude parser": the list form)
- Create: `src/Agent.Core/Parsing/ParsedMessage.cs`
- Test: `tests/Agent.Core.Tests/Parsing/ParsedRequestValidatorTests.cs`, `tests/Agent.Core.Tests/Parsing/ClaudeParserTests.cs`, `tests/Agent.Core.Tests/Parsing/ScriptedParserTests.cs` (if it exists; else the class that loads the oracle), `tests/Agent.Core.Tests/Conversation/CapturingParser.cs`, every test that builds a parser reply (mechanical: wrap in `ParsedMessage.One`)

**Interfaces**
- Produces: `ParsedMessage(IReadOnlyList<ParsedRequest> Requests)`: sealed record, at least one request (the constructor throws on an empty list); `ParsedMessage.One(ParsedRequest)`; `First => Requests[0]`.
- Produces: `IParser.ParseAsync` returns `Task<ParsedMessage>`. Anything unreadable is one `Unclear` request (as today), so every caller that only wants the first request still works.
- Produces: `ValidationResult(IReadOnlyList<ParsedRequest> Requests, string? Problem)`: with a problem, `Requests` is one `Unclear` request carrying it; `MoreThanOneRequest` is gone. `Validate(ParsedRequestsDto, MaskMap)` validates every entry in order and stops at the first violation (a null entry is the problem `requests`). `Validate(ParsedRequestDto, MaskMap)` keeps its shape for the single form.
- Consumes: nothing new.

- [ ] **Step 1: `ParsedRequestValidatorTests`.** Replace `AListUsesItsFirstRequestAndSaysWhenThereWereMore` by `AListKeepsEveryRequestInOrder` (two entries → `Requests.Count == 2`, recipients in order, `Problem` null) and add `AListWithABrokenSecondEntryIsOneUnclearRequest` (the second entry's `intent` is `"refund"` → `Problem` `"intent"`, `Requests` is one `Unclear` with that problem), `AListWithANullEntryIsTheProblemRequests`. Run: fail.
- [ ] **Step 2: Implement** `ParsedMessage`, the validator, the four parsers (`ScriptedParser.Load` keeps `result.Requests`; `ScriptedParser.ParseAsync` returns the stored message; the Claude parser keeps its `more_than_one_request` trace and returns the validated list; `RecordingParser` and `FaultingParser` pass the message through, an injected invalid reply is `ParsedMessage.One(Unclear)`), `CapturingParser` (its dictionary value becomes `ParsedMessage`; a helper overload keeps the single-request constructor for the existing tests), and the one line of `Conversation.SendAsync` that reads the first request. Run the Parsing classes: pass.
- [ ] **Step 3: `ClaudeParserTests`.** Rename `MoreThanOneRequestTakesTheFirstAndTracesTheCount` to `MoreThanOneRequestReturnsEveryRequestAndTracesTheCount` and assert both requests come back in order. Run: pass.
- [ ] **Step 4: Whole core suite** (`dotnet test tests/Agent.Core.Tests`): green, 0 warnings. `dotnet build`: 0 warnings (the web and the eval compile against the new seam).
- [ ] **Step 5: Docs** (`docs/agent-core.md`: the three sections named above; the sentence "until milestone 9 the others are dropped" goes).
- [ ] **Step 6: Commit**: `feat(core): the parse call returns every request of a message (ParsedMessage; the validator checks the whole list, D7), the conversation still runs the first until the queue lands`.

### Task 2: The queue: one part at a time, the next after the previous is finished

**Files**
- Modify: `src/Agent.Core/Conversation/Conversation.cs`, `src/Agent.Core/Conversation/ConversationState.cs` (nothing new unless a type is needed), `src/Agent.Core/Trace/TraceSteps.cs` (`RequestsSplit = "requests_split"`, detail `"<total> requests, <kept> kept"`; `NextRequest = "next_request"`, detail `"<n> of <kept>"`; `RequestsDropped = "requests_dropped"`, detail the count), `docs/agent-core.md` ("The conversation": the queue, its states as a Mermaid state diagram, what finishes a part, what drops the queue)
- Test: `tests/Agent.Core.Tests/Conversation/ConversationCompoundTests.cs` (new; the harness and `DevSetText` as `ConversationAnswerTests` uses them), `tests/Agent.Core.Tests/Trace/TraceSinkTests.cs` (the new step names in whatever list it walks)

**Interfaces**
- Produces: the private queue `Queue<(ParsedRequest Request, bool Verbatim)>` of the parts after the first, at most `MaxRequestsPerMessage - 1` long (the cap's remainder is Task 3; here the parts beyond the cap are simply not queued and nothing is said yet). `Conversation.QueuedRequests` (int, for tests and the trace panel): the parts still to run.
- Produces: the advance. A part is finished when its reply is a `Refusal`, a `BalanceReply` or an `Unsupported` (at once, inside `HandleCoreAsync`), or when its card reaches Done, NotDone, Expired or Cancelled (inside `ShowOutcome`, `CancelCardAsync`, `MarkExpired` and the `AlreadyHandled` branch when the state it reports is finished), or when its open question is cancelled (`DeclineQuestionAsync`'s cancel branch). A `Question`, a `ConsentScreen` and a live or checking card wait. Finishing a part with a non-empty queue dequeues the next and runs it through `HandleCoreAsync` under the same turn (trace `next_request`), with its own `recipientVerbatim`; a refused or read-only part therefore runs on to the next in the same action. Nothing is dequeued by a reissue (the queue is always empty by then; the method leaves it alone).
- Produces: the drop. In `SendAsync`, when the parsed message is handled as a new request (the `HandleCoreAsync` call at the end of the parse path, merged or not), the queue is replaced by the message's own parts after the first: first the old queue is cleared (`requests_dropped` when it held anything), then, if a card waits, `CancelCardAsync` (which now finds nothing to continue), then the new parts are queued (`requests_split` when the message had more than one), then part 1 runs. On every other path (`message_too_long`, yes, no, PIN in chat, the amount retry, an answer read in code, parser unavailable, the card kept, the question repeated) the queue is untouched (ruling 5, 6).
- Produces: `amountRetryOpen` is set only when the queue is empty after the refusal (ruling 7).

- [ ] **Step 1: `ConversationCompoundTests`** on the oracle over the dev set (`DevSetText`, user U1 unless the case says otherwise), each asserting the cards in order, the taps, the outcomes, the wallet's balance and the trace: `CMP01_TwoCardsInTheOrderSpokenOneTapEach` (say; card 1 transfer `S04` 25,000, confirm with card 1's id → Done; card 2 purchase `AC-CR-5` live; cancel → Cancelled; no third card; balance 724,000), `CMP03_AnExpiredCardStillLeadsToTheNext` (card 1 to `S18`; advance the host clock past `ExpiresAt`, run `PendingStore.ExpireDueAsync`, `RefreshAsync` → Expired; card 2 to `S19` with `BalanceAfterIqd` 724,000, i.e. from the full balance; confirm → Done), `CMP05_AQuestionIsAskedWhenItsPartComesAndTheSecondWaits` (`which_bill` with `BS1`, `BS2`; `QueuedRequests == 1`; tap `BS2` → bill card; confirm → Done; transfer card to `S01` with `BalanceAfterIqd` 571,000; confirm → Done), `CMP09_TheBalanceIsAnsweredThenTheCardIsBuilt` (a `Balance` message of 750,000 before the card; confirm → Done; balance 699,000). Add the inputs of the review focus: `APartRefusedRunsOnToTheNextInTheSameAction` (a `CapturingParser` message of a transfer to a name no contact has, then a clean transfer: `recipient_not_found`, its learning row, then the card, one `SendAsync`), `AFailedCardStillLeadsToTheNext` (two transfers; while card 1 waits, debit the wallet through the host's admin route so the recheck refuses; confirm → NotDone `insufficient_funds`; card 2 live, built against the balance then), `ACancelledCardStillLeadsToTheNext` (typed no on card 1 → Cancelled; card 2 live), `ACancelledQuestionStillLeadsToTheNext` (CMP-05's message; `DeclineQuestionAsync` on `which_bill` → `question_cancelled`; the transfer card live), `ANewMessageWhileACardWaitsDropsTheRest` (CMP-01's message; then CLN-01's message: card 1 Cancelled, `requests_dropped` `1`, the new card is CLN-01's, confirm → Done, no purchase card follows), `AYesAnUnclearMessageAndAnAnswerKeepTheQueue` (CMP-01's message; typed yes → `points_to_confirm`, `QueuedRequests` still 1; an unreadable message → card kept, still 1), `TheConsentScreenOnPartOneLosesNothing` (a user not consented: CMP-01's message → `ConsentScreen`, `QueuedRequests == 1`; `GiveConsentAsync` → card 1; confirm; card 2), `AnAmountRefusalOnAMiddlePartDoesNotOpenTheRetry` (part 1 a transfer over the per-transaction cap, part 2 clean: the refusal, then card 2; a typed amount afterwards is a fresh message, not a merge; trace has no `amount_retry`), `TheQueueNeverExecutes` (after CMP-01's message, before any tap: `QueuedRequests == 1`, one pending record Pending, the wallet unchanged). Run: fail.
- [ ] **Step 2: Implement** the queue, the advance, the drop, the trace steps, `QueuedRequests`. Keep every existing `ConversationTests` green: a single-request message behaves exactly as before.
- [ ] **Step 3: `TraceSinkTests`** (or whichever test pins the trace step names): the three new steps. Run: pass.
- [ ] **Step 4: Whole core suite**: green, 0 warnings. `NoTapNoMoneyTests` green.
- [ ] **Step 5: Docs** (`docs/agent-core.md`, with the Mermaid state diagram of a part: queued → running → waiting on a question, the consent screen or a card → finished → the next).
- [ ] **Step 6: Commit**: `feat(core): compound requests run one part at a time (D7, rule 21): the next card only after the previous is finished, a cancel, expiry or failure still leads to the next, a new request drops the rest`.

### Task 3: The cap, the reply that names the rest, and taps that name their card

**Files**
- Modify: `src/Agent.Core/Conversation/Conversation.cs`, `src/Agent.Core/Conversation/ConversationState.cs` (`ConversationMessage.NotPrepared`, `NotPreparedPart`), `src/Agent.Core/Trace/TraceSteps.cs` (`TapRejected = "tap_rejected"`, detail `stale_card` or `no_card`), `src/Agent.Core/AgentOptions.cs` (the `MaxRequestsPerMessage` summary: read by the conversation when a message is split), `src/Agent.Eval/Running/CaseDriver.cs` (passes the card's id; compiles), `src/Agent.Web/Services/ChatSession.cs` (passes the id; compiles; the rendering is Task 5), `docs/agent-core.md` ("The conversation": rule 22; "Messages as codes": the new fields; "Actions": the id on Confirm and Cancel; "The learning-log rule": `more_than_three_requests` is now built; "Settings": the row)
- Test: `tests/Agent.Core.Tests/Conversation/ConversationCompoundTests.cs`, `tests/Agent.Core.Tests/Conversation/ConversationMessageTests.cs`, `tests/Agent.Core.Tests/Architecture/NoTapNoMoneyTests.cs` (the signature), a test helper `ConversationExtensions.ConfirmShownAsync(this Conversation, string? pin = null)` / `CancelShownAsync` in the tests project so the existing call sites change by one name only

**Interfaces**
- Produces: `NotPreparedPart(RequestIntent Intent, string? Text)`: `Text` is the user's own words for the part (ruling 2) or null. `ConversationMessage.NotPrepared` (`IReadOnlyList<NotPreparedPart>?`), set on the `Info` message with code `max_three_requests`; `ToString()` prints the count only.
- Produces: when a message is split, the parts beyond `MaxRequestsPerMessage` are kept as `NotPreparedPart`s with the message (not queued). When the last queued part finishes with the queue empty and such parts exist, the conversation adds the `Info` message `max_three_requests` with them, writes the learning row `more_than_three_requests` with the total count as detail (`LastLearningEntry` set), traces `learning_logged`, and forgets them. A drop forgets them too.
- Produces: `ConfirmAsync(Guid pendingId, string? pin)`, `CancelAsync(Guid pendingId)` (ruling 8). `SettleAsync`, `ReissueAsync` unchanged.
- Consumes: `AgentOptions.MaxRequestsPerMessage` read at the split (`options.CurrentValue`).

- [ ] **Step 1: `ConversationCompoundTests`**: `CMP07_FourRequestsGiveThreeCardsAndTheReplyNamesTheFourth` (three cards in order to `S04`, `S19`, `S18`; after the third Done, an `Info` `max_three_requests` whose `NotPrepared` is one part, `Transfer`, with `Text` equal to the fourth recipient as the dev set's key says (`not_prepared` read from the case through `DevSetText` or a small helper, never a literal); `LastLearningEntry` `more_than_three_requests` with detail `4`; no fourth card; balance 717,000), `ACapOfOneNamesTheSecondPart` (the harness configured `MaxRequestsPerMessage = 1` on CMP-01's message: one card; after Done the reply names one part, `Purchase`, with the operator's text), `FivePartsAtTheCapOfThreeNameTwoInOrder` (a `CapturingParser` message of five transfers: three cards; two names in order), `ANameTheUserDidNotTypeIsReplacedByTheType` (a `CapturingParser` reply whose fourth recipient is not in the typed text: `Text` null), `TheReplyIsNotShownWhenANewMessageDropsTheRest` (CMP-07's message; a new message while card 1 waits: no `max_three_requests` ever, no such learning row), `TwoConfirmsOnCardOneExecuteOnceAndNeverReachCardTwo` (CMP-01's message; `Task.WhenAll` of two `ConfirmAsync(card1Id, null)`: one Done, one `already_handled`; exactly one wallet transaction; card 2 live), `AConfirmWithAStaleIdIsAlreadyHandledAndMovesNothing` (after card 2 is live, `ConfirmAsync(card1Id, null)` → null, outcome `already_handled`, trace `tap_rejected` `stale_card`, the wallet unchanged, card 2 still live; the same for `CancelAsync(card1Id)`), `AConfirmWithNoCardIsRejected` (`Guid.NewGuid()` on an empty conversation → null, `tap_rejected` `no_card`, no message). Run: fail.
- [ ] **Step 2: Implement.** Then `ConversationMessageTests`: the `ToString` of a message with three not-prepared parts holds no text of theirs. Run: pass.
- [ ] **Step 3: The two signatures** across `src` and `tests` (the helper keeps the existing tests short); `CaseDriver` passes `conversation.Card?.PendingId ?? Guid.Empty`, captured once before a `twice` pair; `ChatSession.ConfirmAsync(Guid, string?)` and `CancelAsync(Guid)` pass through (the panel changes in Task 5; for now the panel passes `Session.Card!.PendingId` so it compiles). `dotnet build`: 0 warnings. Whole core suite: green.
- [ ] **Step 4: Docs** (`docs/agent-core.md`).
- [ ] **Step 5: Commit**: `feat(core): the cap from Agent:MaxRequestsPerMessage (rule 22): the rest are named under the last prepared card and logged more_than_three_requests; Confirm and Cancel name their card, a tap for a card no longer shown is already_handled`.

### Task 4: The runner grades compound cases

**Files**
- Modify: `src/Agent.Eval/Running/CaseDriver.cs`, `src/Agent.Eval/Running/CaseObservation.cs` (`StepObservation.ActedCardState`), `src/Agent.Eval/Grading/CaseGrader.cs`, `docs/agent-eval.md` ("Not played yet" loses its two rows; the grade section names the three keys and the acted card)
- Test: `tests/Agent.Core.Tests/Eval/CaseGraderTests.cs`, `tests/Agent.Core.Tests/Eval/CaseDriverTests.cs`

**Interfaces**
- Produces: `StepObservation.ActedCardState`: the end-of-step state of the card that was shown when the step began (`cardBefore`), from the watcher; null when none was shown. `CaseGrader` reads `ActedCardState ?? CardState` for a step's `card` and `card_outcome` (ruling 12); everything else as today.
- Produces: `then_reply` graded: among the step's replies there is an outcome message, and after the last one a reply item (`ReplyItems`) with that code. `no_card_N` graded: `observation.Cards.Count < N` when true (false: `>= N`). `not_prepared` graded: the matching reply message's `NotPrepared` names, each `Text` (or, when null, the string `type:<intent>` so the mismatch is visible), joined by `، `, against the key's text, through `Name(...)` (rendered as "names equal"/"names different").

- [ ] **Step 1: `CaseGraderTests`**: `AStepThatFinishesOneCardAndBringsTheNextGradesTheFirst` (an observation whose step has `ActedCardState` `done`, `CardState` `live`, key `card_outcome: done` → ok), `ThenReplyPassesWhenTheCodeFollowsTheOutcome` (replies `[Outcome done, Info max_three_requests]` → ok; `[Info, Outcome]` → not ok; no outcome → not ok), `NoCardFourPassesWithThreeCards`, `NotPreparedComparesTheNamesWithoutPrintingThem` (equal and different; the verdict's observed value is never the text). `CaseDriverTests`: a two-card case through the driver (CMP-01 by id from the dev set, as the existing driver tests play a case) records `ActedCardState` `done` and `CardState` `live` on the confirm step, and `Cards.Count == 2`. Run: fail. Implement. Run: pass.
- [ ] **Step 2: Oracle run** with the wallet running (build the runner into a separate output so no build locks another, as in milestone 9b): `dotnet run --project src/Agent.Eval -- dev --label m9c`. Expected: 95 of 96; the one miss is GAP-06 (milestone 10); CMP-01, CMP-03, CMP-05, CMP-07 and CMP-09 pass with `then_reply`, `no_card_4` and `not_prepared` graded; no other case changes; 0 executions without a tap, 0 masking failures. If a CMP case fails, the defect is in Tasks 2 to 4, not the key: report it. Commit the two report files with the code.
- [ ] **Step 3: Docs** (`docs/agent-eval.md`).
- [ ] **Step 4: Commit**: `feat(eval): then_reply, no_card_N and not_prepared graded; a step's card outcome is read on the card the step acted on; oracle m9c report (95 of 96, GAP-06 only)`.

### Task 5: The phone

**Files**
- Modify: `src/Agent.Web/Components/Phone/CardPanel.razor` (captures `Session.Card.PendingId` in `OnInitialized` into a field, passes it to `Session.ConfirmAsync(cardId, pin)` and `Session.CancelAsync(cardId)`), `src/Agent.Web/Services/ChatSession.cs` (the `Info` `max_three_requests` message becomes two chat lines: `Refusal_max_three_requests`, then `Refusal_not_prepared` with `{0}` the names joined by `List_joiner`, each name the part's `Text` or its type word; `data-testid` `not-prepared` on the second line through `ChatMessage` if the panel needs a hook; the `QueuedRequests` count shown in the live trace is not needed), `src/Agent.Web/Services/RuntimeSettings.cs` (`EditableNames` gains `MaxRequestsPerMessage`), `src/Agent.Web/Components/Console/SettingsPanel.razor` (the row becomes editable like the others, if the panel lists editable keys by hand), `src/Agent.Web/Resources/PhoneStrings.resx` (`Refusal_not_prepared` = `TODO-AR Not prepared: {0}`, `List_joiner` = `TODO-AR ، `, `Type_request` = `TODO-AR request`), `docs/agent-web.md` ("The session flow", "Actions": the id; "How replies become messages": the two lines; "The phone strings": the three keys; "The settings component": the editable key; "The `data-testid` contract")
- Test: `tests/Agent.Core.Tests/Web/ChatSessionTests.cs`, `tests/Agent.Core.Tests/Web/PhoneStringsTests.cs` (`AwaitingKhalil` = the three keys), `tests/Agent.Core.Tests/Web/RuntimeSettingsTests.cs` (the key is editable, 0 refused, 21 refused)

- [ ] **Step 1: `ChatSessionTests`**: `TheMaxThreeReplyIsTwoLinesAndNamesTheRestWithTheUsersWords` (CMP-07 through the session on the oracle: after the third Confirm, the last two agent messages are `Refusal_max_three_requests` and `Refusal_not_prepared` filled with the fourth part's text), `ANotPreparedPartWithNoTextIsNamedByItsType` (a `CapturingParser` message whose fourth part's text is not verbatim: the second line holds `Type_transfer`), `ConfirmAndCancelPassTheCardId` (a stale id → `already_handled` line, nothing moved). Run: fail. Implement. Run: pass.
- [ ] **Step 2: Build** the panel and the settings change. `dotnet build`: 0 warnings. Run the console by hand once (wallet first): CMP-01's sentence gives the transfer card, Confirm, then the purchase card under it; the first card's panel gives way to its title line and outcome; the settings panel saves `MaxRequestsPerMessage`.
- [ ] **Step 3: Docs** (`docs/agent-web.md`).
- [ ] **Step 4: Commit**: `feat(web): the card panel confirms and cancels by its own card id; the "not prepared" lines name the rest (strings TODO-AR); MaxRequestsPerMessage editable in the settings component`.

### Task 6: The journeys

**Files**
- Create: `tests/Agent.Web.Journeys/CompoundJourneys.cs`
- Modify: `tests/Agent.Web.Journeys/DevSet.cs` (`NotPrepared(id)`: the `expect.replies[].not_prepared` text of a case), `docs/agent-web.md` ("The journeys": the eight)

Every sentence is a dev-set message by id (`DevSet.Say`); every name, code and figure is read from the wallet, the dev set or the resource file. One journey per behaviour, on the oracle:

1. `TwoCardsComeInTheOrderSpokenEachWithItsOwnTap` (CMP-01: the transfer card, Confirm → done; the purchase card appears with `data-state` live; Cancel → cancelled; exactly two cards were shown, by the count of card messages; balance 724,000).
2. `AnExpiredCardStillLeadsToTheNext` (CMP-03 on `Host.ShortTimerWebAddress`: card 1 expires on its own; card 2 shows live; Confirm → done; balance 724,000).
3. `AQuestionOnTheFirstPartIsAskedInItsTurnAndTheSecondWaits` (CMP-05: `which_bill` with the two option values; no card yet; tap `BS2`; the bill card; Confirm → done; the transfer card; Confirm → done; balance 571,000).
4. `FourRequestsGiveThreeCardsAndTheReplyNamesTheFourth` (CMP-07: three Confirms; the two lines, the second holding `DevSet.NotPrepared("CMP-07")`; no fourth card; the learning row `more_than_three_requests`; balance 717,000).
5. `TheBalanceIsAnsweredBeforeTheCardOfTheSecondPart` (CMP-09: the balance line with 750,000 above the card; Confirm → done; balance 699,000).
6. `ACancelledCardStillLeadsToTheNext` (CMP-01: Cancel on card 1 → cancelled; the purchase card live; balance unchanged).
7. `AFailedCardStillLeadsToTheNext` (CMP-03 with the wallet fault `error` once on Confirm through the fault panel, as `FaultJourneys` sets it: card 1 not_done; card 2 live; faults cleared).
8. `ANewMessageCancelsTheRest` (CMP-01's message, then CLN-01's while card 1 waits: card 1 cancelled, the new card is CLN-01's; Confirm → done; no further card).

- [ ] **Step 1: Write the eight**, run `dotnet test tests/Agent.Web.Journeys`: 66 journeys green (58 + 8).
- [ ] **Step 2: Docs** (`docs/agent-web.md`).
- [ ] **Step 3: Commit**: `test(journeys): eight compound-request journeys, one per behaviour (milestone 9c)`.

### Task 7: Coverage, failure modes, known gaps, status

**Files**
- Modify: `docs/rules-coverage.md` (rows 21 and 22 → `yes` with the conversation's members, the Task 2 and 3 tests and the journeys; the totals: 49 `yes`, 0 `partial`, 0 `no`; "Not yet built" goes; the header's survey note gains the date), `docs/failure-modes.md` (rows: a second tap on a card that has given way to the next is `already_handled` and moves nothing; a message with more requests than the cap names the rest and nothing is prepared for them; a new message while cards are queued drops them, traced), `docs/known-gaps.md` (row 8: "today" is the built behaviour; row 17 removed and the numbering or the cross-references kept consistent; the header date), `docs/user-messages.md` (the `Refusal_max_three_requests` row loses its `U` flag; rows for the three new keys), `docs/tuning-backlog.md` ("Not on this list": the compound cases are built), `docs/build-plan.md` (the 9c status: done, the date, the commits, the controller rulings, the oracle figures; the Sonnet figures come in the close-out commit), `docs/agent-core.md` ("Known limits" if it names 9c)

- [ ] **Step 1: Write the docs.** Whole suite: `dotnet test` green, 0 warnings.
- [ ] **Step 2: Commit**: `docs: rules 21 and 22 yes in rules-coverage; compound rows in failure-modes; known gaps 8 and 17; user messages; milestone 9c status`.

### After the tasks (the controller)

- Security review by the `dotnet-security-reviewer` agent over the milestone's diff; fixes in one commit; the review under `docs/reviews/milestone-9c-security-review.md`.
- The Sonnet rerun: `dotnet run --project src/Agent.Eval -- dev --parser claude:sonnet --repeat 3 --label m9c` (reports `dev-claude-sonnet-m9c-r1..r3` and `-repeat3`), the figures into the 9c status, the reports committed.
- `git tag milestone-9c`, push with tags.
