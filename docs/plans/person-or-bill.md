# Person or bill: one name, two meanings (Khalil, 20 Oct 2026)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (one implementer, Opus; the controller, Opus from 20 Oct 2026, reviews each task's diff). Steps use checkbox (`- [ ]`) syntax.

**Decision (Khalil, 20 Oct 2026):** when the name the user typed is both a person (a saved name or an alias of one of their contacts) and one of their bills (the label of a bill), the agent never chooses. It asks one question, `person_or_bill`, with the person and the bill as options, whatever intent the model read ("option 1 (a): always ask"). His wording for the question: `Question_person_or_bill` = قصدك فاتورة لو شخص معين ؟

**Why:** the model reads intent from the sentence. "Pay the shop 50 thousand" can be a transfer or a bill, and with a contact and a bill of the same name either reading gives a card; the card shows its type and waits for Confirm, but nothing stopped the wrong card from being shown. The intent no longer decides when the name is shared.

**Spec:** this file; `docs/architecture.md` D3 (the pending request and its answers), D9 (a bill is paid in full); `docs/matching-rules.md` (the new rule, recorded in Task 3); `docs/known-gaps.md` row 18 (the biller alias stays a later item: this work does not build it).

## Definitions

- **Same name:** exact only. The typed text's `ArabicText.Key` equals the `ArabicText.Key` of a contact's saved name or alias (the contact resolver's exact step, after the endearment and kinship handling it already does) and the key of the label of one of the user's bills. Near spellings, partial matches and shared words do not count (a shared word like "the house" and "the landlord" is not the same name).
- **The typed text:** for a transfer, `RecipientText` (not a phone number or a mask token); for a bill, `BillLabelText`, else `BillerText`. Only the user's own bills and contacts are read, as every resolver does.
- **Today's seed has no such pair** (checked 20 Oct 2026: U1's BS1 "the house" and S15 "the landlord" share a word only; U1's and U3's "the shop" bills have no contact of that name), so no answer key of the development set changes. Tests create the pair at run time by saving an alias equal to a bill's label through the wallet.

## Behaviour

1. In `Orchestrator.HandleAsync`, after the consent gate and before `TransferAsync` or `BillAsync`, for a transfer or a bill whose typed text is set and when `SlotAnswers.PersonOrBill` is not yet answered: read the user's contacts and bills (the reads those paths already make; a read with no answer is the usual `not_done_nothing_taken` with the `wallet_` detail). If the typed text is the same name as exactly one contact account (hits on one account count as one, rule 7) and as one or more bills, return `Question(person_or_bill, Tap, options)`: first the contact option (built exactly as a `which_contact` option is, value the contact code), then one option per matching bill (built as a `which_bill` option, value the bill code). Trace `question:person_or_bill`. Audit as every question is.
2. If the name matches several contact accounts exactly (a name saved twice), the person side is one option per account, as `which_contact` would list them.
3. A tap on the contact option: the request becomes a transfer (`Intent = Transfer`, `RecipientText` = the typed text, the amount and its unit kept from the request), `SlotAnswers` with `ContactOption` = the value and `PersonOrBill` answered. The transfer path then runs as today: a missing amount asks `which_amount`; a card waits for Confirm.
4. A tap on a bill option: the request becomes a bill (`Intent = Bill`, the amount kept so D9 refuses a named amount that is not the amount due), `SlotAnswers` with `BillCode` = the value and `PersonOrBill` answered. The bill path then runs as today.
5. The question's last option is cancel (`Option_CancelQuestion`, `question_cancelled`), as on every question that is not a contact question.
6. Typed text while the question is open is handled as on every open question (an unreadable message repeats it; a new request replaces it). No code-side typed answer for this kind.
7. Not logged in the learning log (it is a question, not a refusal). Compound requests: a part that asks this question waits like any question (D7).
8. The phone: `Question_person_or_bill` (Khalil's text above); the contact option is labelled as a contact option is, the bill option as a bill option is (the panel decides the label by the option, not only by the question kind). The model never sees any of it (rule 5): the options are code's, the pending note says only `open_question person_or_bill`.

## Tasks

### Task 1: The rule in the orchestrator and the conversation

**Files:** `src/Agent.Core/Pipeline/QuestionKinds.cs` (`PersonOrBill = "person_or_bill"`), `src/Agent.Core/Pipeline/SlotAnswers.cs` (`PersonOrBill`, a bool or the chosen side), `src/Agent.Core/Pipeline/Orchestrator.cs`, a small pure helper for the same-name test (for example `Resolver/PersonOrBillRule.cs`, `Find(string typed, IReadOnlyList<ContactCandidate>, IReadOnlyList<WalletBill>, AgentOptions)`), `src/Agent.Core/Conversation/Conversation.cs` (`AnswerForOption` gains the case), `docs/agent-core.md` (the pipeline, the question kinds table, a Mermaid flowchart of the check).

**Tests** (no Arabic in C#: every name from `SeedFacts`, the label from the seed's bill): `PersonOrBillRuleTests` (exact key match on a saved name and on an alias; a near spelling, a shared word and a partial do not match; no bills or no contacts, no match; two accounts with the name give two person options); `OrchestratorTests` (a transfer to the label's text when an alias equal to BS2's label is saved on S04 asks `person_or_bill` with S04's code and `BS2`; a bill with that label asks the same; with the answer given it does not ask again; with no such alias CLN-10's parse gives the BS2 card as today); `ConversationPersonOrBillTests` (CLN-10 on the oracle with the alias saved: the question; tap `BS2` → the bill card for BS2's amount due, confirm → done; again, tap S04 → `which_amount` typed question (CLN-10 names no amount), type an amount the code reads → transfer card to S04; cancel → `question_cancelled`; the trace has `question:person_or_bill`; `OutboundPayloadTests`-style check that the pending note is `open_question person_or_bill` and no name or code reaches the parser). Save the alias in each test through `AliasDirectory.SaveAsync` with the text taken from the seed's BS2 label.

- [ ] Tests first, see them fail; implement; core suite green, `dotnet build` 0 warnings; docs; commit `feat(core): person_or_bill: a name that is both a contact and a bill label is asked about, never chosen by the intent (Khalil, 20 Oct 2026)`.

### Task 2: The phone and the runner

**Files:** `src/Agent.Web/Resources/PhoneStrings.resx` (`Question_person_or_bill` = Khalil's text above, verbatim), `src/Agent.Web/Components/Phone/QuestionPanel.razor` and whatever labels options (the contact option and the bill option each keep their usual label inside one question), `src/Agent.Eval` only if a question kind list must name it, `docs/agent-web.md`, `docs/user-messages.md` (the row), `tests/Agent.Web.Journeys/PersonOrBillJourneys.cs` (new).

**Tests:** `ChatSessionTests.ThePersonOrBillQuestionShowsKhalilsLineAndBothOptions` (option texts: the contact's saved name, the bill's biller and label); `PhoneStringsTests` green. Journeys on the oracle, the alias saved first through the phone's directory screen or the host, the sentence CLN-10 by id: `ANameThatIsAContactAndABillAsksPersonOrBill` (the question with two options and cancel; no card), `TappingTheBillGivesTheBillCard` (BS2's card, confirm, done, the bill now 0), `TappingThePersonGivesTheTransferPath` (the amount question; no bill paid). Expected: 69 journeys green.

- [ ] Tests first; implement; journeys green; docs; commit `feat(web): the person_or_bill question on the phone, Khalil's wording; three journeys`.

### Task 3: Records

**Files:** `docs/matching-rules.md` (a row under the rules, "Added 20/10/2026 (Khalil)": a name that is exactly both a contact's saved name or alias and a bill's label is asked about (`person_or_bill`), never chosen; shared words and near spellings do not count; this is Khalil's decision recorded verbatim in substance, not a change to an agreed rule), `docs/rules-coverage.md` (a row for it, `yes`, with code and tests; totals), `docs/failure-modes.md` (a row: one name for a person and a bill; the question; the tests and journeys), `docs/known-gaps.md` (row 18 notes that the clash between a person and a bill label is now asked about; the biller alias itself stays a later item), `docs/build-plan.md` (a post-9c entry: "Person or bill, 20 Oct 2026" with the commits and figures), oracle rerun `--label person-or-bill` (expected 95 of 96, GAP-06 only, no case changes since the seed has no such pair), the security review by `dotnet-security-reviewer` over the range, the review file saved by the controller, fixes if any.

- [ ] Commit the docs and the report; push.

## Global constraints

Rules 1 to 9 of `CLAUDE.md`. No Arabic in C#, Razor or tests; the one new string is Khalil's, verbatim. Answer keys, the seed and the sidecar are not changed. One commit per task; commits end with no Co-Authored-By line (Khalil's rule of 15 Oct 2026).
