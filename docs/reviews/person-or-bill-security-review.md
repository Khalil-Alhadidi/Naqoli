# Security review: person or bill (Khalil, 20 Oct 2026), range 8aca616..50bc5ed

Written by the security reviewer agent (`dotnet-security-reviewer`, run on Opus) on 20 Oct 2026, saved verbatim by the controller. Sources: `CLAUDE.md`, the plan `docs/plans/person-or-bill.md`, and the current files under `src/` and `tests/` at 50bc5ed, read directly (no shell in this run, so no `git diff`). The working tree's uncommitted `PhoneStrings.resx` change and the untracked `dev-scripted-person-or-bill-*` reports were not reviewed. The reviewer did not open, search or hash the held-out file or any workbook (rule 7). No file was modified.

**Verdict: pass with changes.** Critical 0, Important 0, Minor 2, Recommendations 3.

## Summary

The nine rules hold on the new path.

- **Rule 1 (no tap, no money):** the person-or-bill path only ever returns a `Question` (`Orchestrator.PersonOrBillAsync`, line 393) or hands the lists it read to `TransferAsync`/`BillAsync`, which end in `CheckAndIssueAsync`, a card that waits for Confirm. Nothing new reaches the Confirm handler. `ConversationPersonOrBillTests.Cln10AsksPersonOrBillAndTheBillTapGivesTheBs2Card` confirms explicitly, and the person journey asserts that no money moved (`TappingThePersonGivesTheTransferPath`: balance and the amount due unchanged).
- **Rules 2 and 5 (model read only; masking):** the parser sees the masked typed text and `PendingNote()` = `open_question person_or_bill` (`Conversation.cs` line 1298). No prompt or schema change. `ThePendingNoteNamesOnlyTheQuestionAndNoNameOrCodeReachesTheParser` asserts that no contact code, bill code, saved name, phone or registered name reaches any parse call. The new data (the saved name in `Display`, `RegisteredName`, `BillerName`, the amount due) travels only in `Option`, to the phone. Trace: `question:person_or_bill` with `options N` only (line 735). Audit: `question_asked` with the kind as its reason (line 734). Learning log: `LearningLogRule.EntryFor` has no case for a `Question`; the test asserts `LearningRowsAsync()` is empty. Eval: the runner keeps `ConversationQuestion(kind, answer, option values)`, codes only. No `ILogger` call logs a reply or an option. The person-to-transfer conversion uses `BillLabelText`/`BillerText`, the model's reading of the user's words, not wallet data.
- **Rule 3 (card by code):** after a tap, the card's subject comes from the wallet's own contacts or bills read in the same call (`TransferAsync` lines 456-488, `BillAsync` lines 536-545); nothing comes from the option.
- **Rule 4 (templates):** the question is `Question_person_or_bill` from the resource file (`ChatSession.Localise` line 495); option text is built from wallet values plus `PhoneFormat.Amount` only.
- **Rules 6-9:** untouched. No Arabic in any changed `.cs`/`.razor`. Test names come from `SeedFacts`/`SeedWallet.LabelOf`, the sentence by case id. No configuration, key or connection string was added.

## Focus questions, answered

1. **Can a tap produce a card for anything but the option's own contact or bill?** `Conversation.TapOptionAsync` (line 522) accepts only a value among the open question's own options (`option_rejected` otherwise), so values from another question, or forged values, never reach the orchestrator. `PersonOrBillAnswer` (lines 1794-1824) branches on the stored option's `Side`, not on the value. Bill side: the code is matched only against the user's own fresh bills read (`BillAsync` line 536-538), so another user's bill cannot be picked. Person side: when the name resolves to several accounts, the tap is checked against the candidates the same resolution offers (lines 456-469); when it resolves to one account, the tap is not compared at all (M1).
2. **Rule 5:** holds (above).
3. **Code-side question, no loop:** the check runs at step 4 whatever the intent (a transfer, a bill, `choose_account` once converted to a transfer, line 163, and `part_of_bill` once converted to a bill, line 170). After a tap, `PersonOrBill = true` skips it (line 208), kept through the `which_amount` tap and typed answers (lines 1634, 1749). A typed message while the question is open has no code-side answer; an unclear message repeats the question (line 457); a new request replaces it with `SlotAnswers.None`. Compound requests: queued parts run with `SlotAnswers.None` (line 1374), and a `Question` reply does not set `partFinished` (line 1344), so that part waits. Gap: a recipient merged into the pending transfer keeps the flag (M2).
4. **Availability:** see below.
5. **Encoding:** `QuestionPanel.razor` renders `@Label(option)` and `@option.RegisteredName` inside `<bdi>`; the tapped option's text becomes a chat body rendered by Razor; no `MarkupString`, `Html.Raw` or `AddMarkupContent` anywhere in `Agent.Web`; `data-value="@option.Value"` is attribute-encoded (learn.microsoft.com/en-us/aspnet/core/blazor/components/#raw-html, learn.microsoft.com/en-us/aspnet/core/security/cross-site-scripting).

## Judgement on the extra read (focus 4)

On a transfer whose recipient exactly matches a contact, the agent now reads the user's bills too; if that read fails, the reply is `not_done_nothing_taken` with the `wallet_` detail (lines 358-360), and the mirror case refuses a labelled bill when the contacts read fails (lines 372-374). **This is the right failure.** The rule is "never choose when the name is shared"; with the second list unknown, going on with the model's intent would be the choice the rule forbids. Fail-closed is what the agent does for every other unanswered read, nothing has been taken, and the mock wallet's fault switch acts only on execute, so a failed second read is a transient error and the user can send again. No test covers either failed second read (R2).

## Findings

### Minor

**M1. On the person side, the tapped contact is not checked when the typed name now resolves to a single account: the card goes to whichever account the name resolves to at tap time.**
- Where: `src/Agent.Core/Pipeline/Orchestrator.cs` lines 456-469. `answers.ContactOption` is read only when `resolution is Resolution.Options`; for `Resolution.Card` (line 473) the tapped value is ignored. D3 and `SlotAnswers`' own doc say a tap picks only among what the orchestrator resolves.
- Scenario: the question offers S04 (alias X = BS2's label) and BS2. Before tapping, the user moves alias X to S05 in the directory (or from a second U1 console). The tap on S04's option builds the transfer card for S05.
- Severity: the card is built by code and shows S05's name and registered name, and Confirm is still needed, so no rule breaks; but the tapped option and the card disagree, unrecorded.
- Fix: in `TransferAsync`, when `answers.ContactOption` is set (and is not `NoneOfThese`) and the resolution is a `Card` whose `ContactOptionValue` differs, treat the tap as picking nothing; for a person-or-bill tap, clear `ContactOption` and `PersonOrBill` and run step 4 again. One orchestrator test.

**M2. A recipient typed into the pending transfer after a person tap keeps `PersonOrBill = true` and the tapped `ContactOption`, so the new name skips the person-or-bill check and can be matched with the stale tap.**
- Where: `src/Agent.Core/Conversation/Conversation.cs` lines 473-483 (`MergePartial` replaces `RecipientText` but `kept = answers` keeps `PersonOrBill: true, ContactOption`); the same at line 1651 (a typed contact answer clears `ContactOption` but keeps `PersonOrBill`).
- Scenario: CLN-10, a tap on the person, `which_amount` opens; the user types only a different name, which the model reads as a recipient with no amount, so the merge applies. (a) If that name is itself both a contact and a bill label, step 4 is skipped and the intent decides, which Khalil ruled out. (b) If it resolves to options that include the earlier tap, the stale value picks it silently.
- Severity: Confirm is still needed and the card shows the recipient, so Minor.
- Fix: reset the answers when the recipient changes (`kept = answers with { ContactOption = null, PersonOrBill = false }` in the merge branch when `parsed.RecipientText` is set; `PersonOrBill = false` at line 1651). One conversation test: a person tap, then a recipient-only message naming another clashing name asks `person_or_bill` again.

## Recommendations

- **R1.** A bill tap whose code is no longer among the user's bills is resolved as if untapped (`BillAsync` lines 536-545). For a person-or-bill tap on a request the model read as a transfer, the request is `ParsedRequest.Bill(null, null, amount, unit)`, which falls into rule 14: with exactly one bill due, an unrelated bill's card. Not reachable today (values are restricted to the offered ones; bill codes survive a reset; no route removes a bill). Refusing `bill_not_found` (or asking again) when `answers.PersonOrBill && answers.BillCode` is not found would make the bill side as strict as M1's fix makes the person side.
- **R2.** Tests to add: an exact-name transfer whose bills read fails and a labelled bill whose contacts read fails (each `not_done_nothing_taken` with a `wallet_` detail, no question, no pending record); a compound message whose first part asks `person_or_bill`, the second part running only after the tap; a new full request while the question is open replaces it with no answer carried; a tap with a value from an earlier question gives `option_rejected`.
- **R3.** The tap and its echo look an option up by `Value` alone (`Conversation.TapOptionAsync` line 522, `ChatSession.OptionText` line 595). This question is the first to mix contact values with bill codes in one list. They are disjoint today (seed codes `S..`/`BS..`); if the wallet ever generates codes, prefix the bill side's value or match on `(Value, Side)`.

## Checked and found sound

- `PersonOrBillRule`: pure; exact key only; `TypedText` excludes phone numbers and blank text; an empty key gives no bills. `ContactResolver.ExactMatches` reuses `ExactHits` and `OnePerAccount`, so the person list equals the accounts the exact step would give, endearment retry included; near, partial and shared-word steps never enter it.
- `PersonOrBillAsync`: reads the path's own list first and the second only on a hit (no extra read without a clash); the lists read are passed on, so no double read and no gap between check and path; the person option is shown by the account's saved name, not the matched alias.
- `QuestionPanel.razor`: the person option keeps the contact option's last-sent line (by `Side`); the decline button is `cancel_question`; `DeclineQuestionAsync` writes the `question_cancelled` audit row and clears the request (`CancelClearsTheRequest`).
- Journeys: the three new journeys type CLN-10 by id and read every name and figure from the host or the resource file.

## The controller's decisions (20 Oct 2026)

M1, M2 and R1 are fixed in the commit after this file, with the R2 tests. R3 stays a note: today's codes cannot collide, and prefixing the bill value would change every bill option's value the runner grades.
