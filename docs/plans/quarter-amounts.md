# Quarter Amounts (ربع, الا ربع) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A request whose amount carries a quarter ("2 وربع", "مليون الا ربع", "ربع مليون") reaches the card with the right figure, on the model parser and on the scripted oracle, proven by five development cases and one Haiku run.

**Architecture:** No amount code changes. The model already returns the amount as a decimal with the unit spoken, and `AmountRule.Resolve` multiplies and keeps whole dinars only, so 2.25 none is 2,250 and 0.75 million is 750,000. The work is: the prompt (version 6) names the fraction words in Khalil's forms; the dev set gains ADD-26 to ADD-30 with their parse lines and answer keys so the scripted parser, the eval and the grader cover them; pinning tests record the readings; one gated live test and one Haiku eval run show the model does it.

**Tech Stack:** .NET 10, xUnit, `Agent.Eval` runner, the Anthropic model through `Microsoft.Extensions.AI`.

**Spec:** `docs/plans/quarter-wording.md` (Khalil approved sections A and B on 22 Oct 2026; section C is done: rule 9 in `docs/matching-rules.md` carries the three quarter examples).

## Global Constraints

- CLAUDE.md rules 1 to 9 hold. In particular: no Arabic in C# (test messages come from the dev set through `CaseLoader` or `DevSetText`, expected figures are numbers); never open `data/requests.heldout.jsonl` or the workbook; tune on the dev set only.
- The Arabic strings are exactly the approved ones in `docs/plans/quarter-wording.md`. Do not reword them. A case that cannot pass with Khalil's wording is reported, not reworded.
- Do not change an existing answer key, target or matching rule. Rule 9's examples were added with Khalil's leave on 22 Oct 2026 and stay as they are.
- Any change to `parse-prompt.md` raises its version (`version: 6`) and is recorded in `docs/agent-model.md`, `docs/tuning-backlog.md` ("Out-of-sequence prompt changes") and `docs/disclosure.md`.
- The working tree holds uncommitted alias work in other files (`Conversation.cs`, `ChatSession.cs`, `AliasJourneys.cs`, `PhoneStrings.resx` and more). Leave it alone. Every commit here is `git add <listed paths>` only, never `git add -A` or `git commit -a`. If the build fails because of that work, stop and tell Khalil.
- Commit messages carry Khalil's name only: no `Co-Authored-By` line (his instruction of 15 Oct 2026).
- Seed facts the answer keys use (`data/seed/settings.json`, the seed hash): transfer fee 1,000; minimum 1,000; PIN required above 1,000,000; U1's balance 750,000; U3's balance 12,500,000; S01 is `07805550101`, display `اخوية حمودي`, registered `محمد جبار حميد`; T03 is `07905550303`, display `المحاسب`, registered `ماهر عبد الستار نعمان`.
- One deviation from the wording file to tell Khalil: ADD-27's 750,000 is below the PIN threshold of 1,000,000, so the card has `pin_required: false` and a plain Confirm, not the PIN path the file's "Why" column promised. The wording stays; only the expectation is corrected.

## Review Focus

1. A fraction word with a unit that gives a non-whole figure ("ربع دينار", 0.25 dinars) must be `not_understood`, not a card for 0 dinars. Pinned in Task 1 (`AQuarterOfADinarIsNotUnderstood`).
2. A bare quarter alone ("ربع", 0.25 none = 250) must be refused `amount_below_minimum` with the minimum in the reply, never a card. Pinned by ADD-29 in Task 3 and `ABareQuarterIsLeftToTheChecks` in Task 1.
3. The card's amount in words for a figure with hundreds and tens below the thousands (2,250; 3,250) must compose, since every existing example ends in a round hundred or a round thousand. Pinned in Task 1 (`AmountWordsTests` rows).
4. A typed answer to `which_amount` that carries a fraction ("٣ الف وربع") must merge as 3.25 thousand and reach the card at 3,250. Pinned by ADD-30 in Task 3.
5. The model, above all Haiku, may read "وربع" as a half or drop it. Pinned by the gated live test in Task 4 and the Haiku report; a failure there is a prompt finding for Khalil, not a key change.

---

### Task 1: Pin the quarter readings in the amount rule and the amount words

No production code changes. The tests record what the code already does for the fractions the spec names, so a later change to `AmountRule` or `AmountWords` cannot break a quarter unnoticed.

**Files:**
- Modify: `tests/Agent.Core.Tests/Pipeline/AmountRuleTests.cs` (after `ABareReadingBelowTheMinimumIsLeftToTheChecks`)
- Modify: `tests/Agent.Core.Tests/Text/AmountWordsTests.cs` (the `InlineData` rows of `TheAmountIsComposedAsKhalilsRulesSay`)

**Interfaces:**
- Consumes: `AmountRule.Resolve(ParsedRequest)`, `AmountResolution.Dinars(long)`, `AmountResolution.Invalid(string)`, `ReasonCodes.NotUnderstood`, `AmountWords.Compose(long, AmountWordParts)`, the test file's `Resolve(decimal?, AmountUnit)` helper and `Parts` table.
- Produces: nothing other tasks call.

- [ ] **Step 1: Add the amount-rule tests**

```csharp
[Fact]
public void BareTwoAndAQuarterIsTwoThousandTwoHundredFifty()
    => Assert.Equal(new AmountResolution.Dinars(2_250), Resolve(2.25m, AmountUnit.None));

[Fact]
public void AQuarterAfterTheUnitIsAQuarterOfIt()
{
    // Quarters (Khalil, 22 Oct 2026): "١٢ الف وربع" is 12.25 thousand, "مليون وربع" is 1.25 million.
    Assert.Equal(new AmountResolution.Dinars(12_250), Resolve(12.25m, AmountUnit.Thousand));
    Assert.Equal(new AmountResolution.Dinars(1_250_000), Resolve(1.25m, AmountUnit.Million));
}

[Fact]
public void LessAQuarterOrAHalfTakesItAway()
{
    // "مليون الا ربع" is 0.75 million, "الفين الا ربع" is 1.75 thousand, "3 الا ربع" is 2.75 bare, "مليون الا نص" is 0.5 million.
    Assert.Equal(new AmountResolution.Dinars(750_000), Resolve(0.75m, AmountUnit.Million));
    Assert.Equal(new AmountResolution.Dinars(1_750), Resolve(1.75m, AmountUnit.Thousand));
    Assert.Equal(new AmountResolution.Dinars(2_750), Resolve(2.75m, AmountUnit.None));
    Assert.Equal(new AmountResolution.Dinars(500_000), Resolve(0.5m, AmountUnit.Million));
}

[Fact]
public void AQuarterOfAMillionIsTwoHundredFiftyThousand()
    => Assert.Equal(new AmountResolution.Dinars(250_000), Resolve(0.25m, AmountUnit.Million));

[Fact]
public void ABareQuarterIsLeftToTheChecks()
{
    // "ربع" alone is 0.25 bare, read in thousands: 250, below the minimum. The checks refuse it amount_below_minimum.
    Assert.Equal(new AmountResolution.Dinars(250), Resolve(0.25m, AmountUnit.None));
}

[Fact]
public void AQuarterOfADinarIsNotUnderstood()
    => Assert.Equal(new AmountResolution.Invalid(ReasonCodes.NotUnderstood), Resolve(0.25m, AmountUnit.Dinars));
```

- [ ] **Step 2: Add the amount-words rows**

To `TheAmountIsComposedAsKhalilsRulesSay`:

```csharp
[InlineData(2_250, "two-thousand &two-hundred &fifty")]
[InlineData(3_250, "three-c thousands &two-hundred &fifty")]
[InlineData(250_000, "two-hundred &fifty thousand")]
[InlineData(750_000, "seven-hundred &fifty thousand")]
[InlineData(1_250_000, "million &two-hundred &fifty thousand")]
```

- [ ] **Step 3: Run both classes**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Pipeline.AmountRuleTests"` and `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Text.AmountWordsTests"`
Expected: all pass. If an amount-words row fails, the composer has a real gap for hundreds-plus-tens: report the observed string to Khalil and do not change the test's expected value or the resource words; fix `AmountWords.Compose` only if the fix is structural (the joiner between hundreds and tens) and the Arabic resource keys already exist.

- [ ] **Step 4: Commit**

```bash
git add tests/Agent.Core.Tests/Pipeline/AmountRuleTests.cs tests/Agent.Core.Tests/Text/AmountWordsTests.cs
git commit -m "test(core): pin the quarter readings (Khalil, 22 Oct 2026): 2.25 bare is 2,250, 12.25 thousand, 1.25 and 0.75 and 0.25 million, 1.75 thousand, 2.75 bare, 0.5 million; a bare quarter is 250 and left to the checks; a quarter of a dinar is not understood; the amount in words for 2,250, 3,250, 250,000, 750,000 and 1,250,000. No code change: AmountRule multiplies the model's decimal and keeps whole dinars"
```

---

### Task 2: Prompt version 6 names the fraction words

**Files:**
- Modify: `src/Agent.Core/Parsing/parse-prompt.md` (line 1 and the `amount_value` line, line 20)
- Modify: `tests/Agent.Core.Tests/Parsing/ClaudeParserTests.cs:427-428` (`TheNameCarriesTheCandidateAndThePromptItsVersion`)
- Modify: `docs/agent-model.md` (the sentence at line 105 that names the current version; the "from version N" list after it; the "Version rule" paragraph at line 121)
- Modify: `docs/tuning-backlog.md` ("Out-of-sequence prompt changes" table)
- Modify: `docs/disclosure.md` (one new bullet)
- Modify: `CLAUDE.md` (the Commands bullet that says the prompt is at version 5)

**Interfaces:**
- Consumes: `ClaudeParser.PromptVersion` (the first line after `version: `).
- Produces: prompt version `"6"`, which Task 4's report header shows as `Model:PromptVersion`.

- [ ] **Step 1: Change the version test first**

In `TheNameCarriesTheCandidateAndThePromptItsVersion`, `"5"` becomes `"6"` and `"version: 5"` becomes `"version: 6"`.

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Parsing.ClaudeParserTests"`
Expected: FAIL, expected "6" actual "5".

- [ ] **Step 3: Edit the prompt**

Line 1 becomes `version: 6`. Line 20 (the `amount_value` and `amount_unit` bullet) gains, after "(code reads a bare number below 1,000 in thousands).", this text and nothing else, with the Arabic forms exactly as approved in `docs/plans/quarter-wording.md` section A:

> A fraction word after a number is part of the number: "ونص" adds a half and "وربع" a quarter ("2 ونص" is 2.5 `none`; "١٢ الف وربع" is 12.25 `thousand`; "مليون وربع" is 1.25 `million`). "الا ربع" takes a quarter away and "الا نص" a half ("مليون الا ربع" is 0.75 `million`; "الفين الا ربع" is 1.75 `thousand`; "3 الا ربع" is 2.75 `none`). A fraction word before a unit is that fraction of the unit ("ربع مليون" is 0.25 `million`). A fraction word alone is that fraction with `none` ("ربع" is 0.25 `none`).

No example block is added or changed, so `ClaudeParser.Examples()` and `LiveModelTests.ThreePromptExamplesParseAsThePromptSays` are untouched.

- [ ] **Step 4: Run the parser tests**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Parsing.ClaudeParserTests"`
Expected: PASS.

- [ ] **Step 5: Record the version in the docs**

- `docs/agent-model.md`: the sentence "the current one is `version: 5`" becomes `version: 6`; add to the "from version N" list: "from version 6, the fraction words in Khalil's forms of 22 Oct 2026: a half or a quarter after a number is added (2.5, 12.25 thousand, 1.25 million), after "الا" it is taken away (0.75 million, 1.75 thousand, 2.75), before a unit it is that fraction of the unit (0.25 million), alone it is 0.25 `none`"; in the "Version rule" paragraph add one sentence: "Version 6 is from 22 Oct 2026, out of sequence: it adds four sentences to the amount line for the fraction words (ربع, الا ربع, الا نص) in Khalil's forms; no example is added or changed (docs/plans/quarter-wording.md)."
- `docs/tuning-backlog.md`: one row `| 22 Oct 2026 | 6 | Four sentences on the amount line: a fraction word after a number is added (وربع), after الا taken away, before a unit a fraction of it, alone 0.25 none; Khalil's forms. No example changed. | Khalil's manual testing, 22 Oct 2026: نص reached the card but ربع had no rule. Dev cases ADD-26 to ADD-30 hold it. |`
- `docs/disclosure.md`: one bullet after the ADD-25 bullet: "Prompt version 6 and development cases ADD-26 to ADD-30 (22 Oct 2026; the Arabic drafted by Claude in `docs/plans/quarter-wording.md`, checked and approved by Khalil): the quarter amounts (ربع, وربع, الا ربع, الا نص, ربع مليون). No amount code changes; rule 9's examples gained the three quarter figures with Khalil's leave. The set has 102 cases."
- `CLAUDE.md`: "is at version 5 (16 Oct 2026, 4d10f21; ...)" becomes "is at version 6 (22 Oct 2026; ...)"; fill the commit hash after Step 6 or leave the date only.

- [ ] **Step 6: Commit**

```bash
git add src/Agent.Core/Parsing/parse-prompt.md tests/Agent.Core.Tests/Parsing/ClaudeParserTests.cs docs/agent-model.md docs/tuning-backlog.md docs/disclosure.md CLAUDE.md docs/plans/quarter-wording.md
git commit -m "feat(core): prompt version 6, the fraction words (Khalil, 22 Oct 2026): a half or a quarter after a number is added, after الا taken away, before a unit a fraction of it, alone 0.25 none; Khalil's forms from docs/plans/quarter-wording.md; no example changed; agent-model.md, tuning-backlog.md, disclosure.md, CLAUDE.md"
```

---

### Task 3: Development cases ADD-26 to ADD-30 with their parse lines and keys

**Files:**
- Modify: `data/requests.dev.jsonl` (five lines appended after ADD-25)
- Modify: `data/requests.dev.parses.jsonl` (six lines appended: one per say step)
- Modify: `tests/Agent.Core.Tests/Eval/CaseLoaderTests.cs:221` (`TheDevelopmentSetLoads`, the count)
- Modify: `docs/agent-eval.md:5` (the set's counts), `docs/architecture.md:217` (the tree line), `docs/rules-coverage.md` (row 9), `docs/failure-modes.md` (row 1, "Amount misread"), `docs/matching-rules.md` (no change: already done)
- Modify: `docs/plans/quarter-wording.md` (one note under section B about ADD-27's PIN)

**Interfaces:**
- Consumes: the case-file shape of `src/Agent.Eval/Cases/CaseFile.cs` (fields exactly as the ADD-10, ADD-13, AMB-06 and RED-09 lines use them); the sidecar shape of `data/requests.dev.parses.jsonl` (`id`, `step`, `say`, `parse.requests[]` with all twelve fields).
- Produces: case ids `ADD-26`, `ADD-27`, `ADD-28`, `ADD-29`, `ADD-30`, which Task 4's live test and eval run name.

- [ ] **Step 1: Raise the count in the loader test first**

`Assert.Equal(97, cases.Count)` becomes `Assert.Equal(102, cases.Count)`.

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Eval.CaseLoaderTests"`
Expected: FAIL on `TheDevelopmentSetLoads`, 97 not 102.

- [ ] **Step 3: Append the five cases**

Append these lines to `data/requests.dev.jsonl`, one JSON object per line, same field order as ADD-25, UTF-8, LF, no trailing blank line beyond the file's convention. Figures: fee 1,000 on every transfer; `balance_after` = balance − amount − fee.

ADD-26 (Clean, Transfer, U1): message `حول 2 وربع لاخوية حمودي`; meaning "Send two and a quarter to my brother Hammoudi (a bare number with a quarter)."; steps `[{"say": "<message>"}, {"tap": "confirm", "expect": {"card_outcome": "done"}}]`; asks `[]`; card `{"kind": "transfer", "to": "S01", "to_number": "07805550101", "display": "اخوية حمودي", "registered_name": "محمد جبار حميد", "amount": 2250, "fee": 1000, "total": 3250, "balance_after": 746750, "pin_required": false, "first_time": false, "warning": null, "outcome": "done"}`; replies `[]`; refusal `null`; consent_screen `false`; final `{"balance": 746750, "transactions": [{"type": "transfer", "amount": 2250, "fee": 1000, "to_number": "07805550101", "to": "S01"}], "codes_issued": 0, "bills_now": {}, "alias_saved": null}`; learning_log `null`; notes "Added 22 Oct 2026; drafted by Claude, approved by Khalil (docs/plans/quarter-wording.md). Rule 9: a bare 2.25 is read in thousands, 2,250, straight to the card. Prompt version 6 names the quarter."; set `dev`; parent `null`; pending_wording_recheck `false`; fault `null`.

ADD-27 (Clean, Transfer, U3): message `حول مليون الا ربع للمحاسب`; meaning "Send a million less a quarter to the accountant."; steps: say, then `{"tap": "confirm", "expect": {"card_outcome": "done"}}`; card to T03: `"to_number": "07905550303", "display": "المحاسب", "registered_name": "ماهر عبد الستار نعمان", "amount": 750000, "fee": 1000, "total": 751000, "balance_after": 11749000, "pin_required": false, "first_time": false, "warning": null, "outcome": "done"`; final balance `11749000`, one transaction `{"type": "transfer", "amount": 750000, "fee": 1000, "to_number": "07905550303", "to": "T03"}`; notes "Added 22 Oct 2026; drafted by Claude, approved by Khalil. 'الا ربع' takes a quarter off the million: 0.75 million, 750,000. Below the PIN threshold of 1,000,000, so no PIN (the wording file's 'PIN path' note was wrong; the amount is Khalil's)."

ADD-28 (Clean, Transfer, U1): message `حول ربع مليون لاخوية حمودي`; meaning "Send a quarter of a million to my brother Hammoudi."; steps: say, confirm done; card to S01 with `"amount": 250000, "fee": 1000, "total": 251000, "balance_after": 499000`; final balance `499000`, transaction amount 250000; notes "Added 22 Oct 2026; drafted by Claude, approved by Khalil. The fraction word before the unit: 0.25 million, 250,000."

ADD-29 (Should not go through, Transfer, U1): message `حول ربع لاخوية حمودي`; meaning "Send a quarter to my brother Hammoudi (a bare quarter, below the smallest amount)."; steps `[{"say": "<message>"}]`; asks `[]`; cards `[]`; replies `[]`; refusal `{"reason": "amount_below_minimum", "limit": 1000}`; final `{"balance": 750000, "transactions": [], "codes_issued": 0, "bills_now": {}, "alias_saved": null}`; notes "Added 22 Oct 2026; drafted by Claude, approved by Khalil. A bare 0.25 is read in thousands, 250 dinars, refused amount_below_minimum with the minimum in the reply, before any card (compare ADD-13). Not in the learning log's logged list."

ADD-30 (Ambiguous, Transfer, U1): message `اريد احول فلوس لاخوية حمودي`; meaning "I want to send money to my brother Hammoudi, then the typed amount three thousand and a quarter."; steps `[{"say": "اريد احول فلوس لاخوية حمودي"}, {"say": "٣ الف وربع"}, {"tap": "confirm", "expect": {"card_outcome": "done"}}]`; asks `[{"kind": "which_amount", "answer": "typed"}]`; card to S01 with `"amount": 3250, "fee": 1000, "total": 4250, "balance_after": 745750`; final balance `745750`, transaction amount 3250; notes "Added 22 Oct 2026; drafted by Claude, approved by Khalil. Rule 10 asks for the amount (compare AMB-06); the typed answer carries a quarter, 3.25 thousand, 3,250."

- [ ] **Step 4: Append the six parse lines**

To `data/requests.dev.parses.jsonl`, same shape as the AMB-06 and ADD-10 lines (all twelve fields, nulls where unsaid):

| id | step | say | intent | recipient_text | amount_value | amount_unit |
|----|------|-----|--------|----------------|--------------|-------------|
| ADD-26 | 0 | `حول 2 وربع لاخوية حمودي` | transfer | `اخوية حمودي` | 2.25 | none |
| ADD-27 | 0 | `حول مليون الا ربع للمحاسب` | transfer | `المحاسب` | 0.75 | million |
| ADD-28 | 0 | `حول ربع مليون لاخوية حمودي` | transfer | `اخوية حمودي` | 0.25 | million |
| ADD-29 | 0 | `حول ربع لاخوية حمودي` | transfer | `اخوية حمودي` | 0.25 | none |
| ADD-30 | 0 | `اريد احول فلوس لاخوية حمودي` | transfer | `اخوية حمودي` | null | none |
| ADD-30 | 1 | `٣ الف وربع` | transfer | null | 3.25 | thousand |

- [ ] **Step 5: Run the loader, sidecar and dev-set text tests**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Eval.CaseLoaderTests"`, then `-- --filter-class "Agent.Core.Tests.Parsing.ScriptedParserTests"`
Expected: PASS, including `TheDevSetLoadsAndEverySayStepHasAParse`, `EveryParseOfTheSidecarComesBackWhole` and `ASidecarTextThatDiffersFromTheDevSetIsRefused`.

- [ ] **Step 6: Play the five cases on the scripted oracle**

Wallet first in another terminal: `dotnet run --project src/Wallet.MockApi`. Then:
Run: `dotnet run --project src/Agent.Eval -- dev --only ADD-26,ADD-27,ADD-28,ADD-29,ADD-30 --label quarter`
Expected: `5 passed`; the report `docs/reports/dev-scripted-quarter-<date>-<HHmm>.md` and `.json`. A failed case names the field: compare the observed figure with the key; if the key is wrong by arithmetic, fix the key and say so; if the wording does not match a contact or the parse is refused, report to Khalil, do not reword.

- [ ] **Step 7: Update the docs that count or list the cases**

- `docs/agent-eval.md` line 5: "97 cases (Clean 28, Ambiguous 20, Should not go through 15, ...)" becomes "102 cases (Clean 31, Ambiguous 21, Should not go through 16, Red team 9, Malformed 8, Fault injection 8, Compound 5, Known gap 4)" and the list gains "; and ADD-26 to ADD-30, the quarter amounts (drafted by Claude, approved by Khalil, 22 Oct 2026)".
- `docs/architecture.md` line 217: `97 requests (Khalil's 72, ADD-01 to ADD-25)` becomes `102 requests (Khalil's 72, ADD-01 to ADD-30)`.
- `docs/rules-coverage.md` row 9: add the Task 1 test names (`AmountRuleTests.BareTwoAndAQuarterIsTwoThousandTwoHundredFifty`, `LessAQuarterOrAHalfTakesItAway`, `AQuarterOfAMillionIsTwoHundredFiftyThousand`, `ABareQuarterIsLeftToTheChecks`) and the cases ADD-26 to ADD-30 in its columns.
- `docs/failure-modes.md` row 1: after "units multiply;" add "a fraction word is part of the number the model returns (prompt version 6, Khalil's forms of 22 Oct 2026: وربع adds, الا ربع takes away, ربع مليون is a quarter of the unit), and the same whole-dinars rule applies;" and add `AmountRuleTests.LessAQuarterOrAHalfTakesItAway` and ADD-26 to ADD-30 to its test and case columns.
- `docs/plans/quarter-wording.md`, under section B: "Note (Claude, after Khalil's approval): ADD-27's 750,000 is below the PIN threshold of 1,000,000 (`data/seed/settings.json`), so its key has `pin_required: false` and a plain Confirm."

- [ ] **Step 8: Commit**

```bash
git add data/requests.dev.jsonl data/requests.dev.parses.jsonl tests/Agent.Core.Tests/Eval/CaseLoaderTests.cs docs/agent-eval.md docs/architecture.md docs/rules-coverage.md docs/failure-modes.md docs/plans/quarter-wording.md docs/reports/dev-scripted-quarter-*
git commit -m "feat(eval): development cases ADD-26 to ADD-30, the quarter amounts (drafted by Claude, approved by Khalil, 22 Oct 2026): 2 وربع (2,250), مليون الا ربع (750,000, no PIN), ربع مليون (250,000), a bare ربع refused below the minimum, and ٣ الف وربع as a typed amount (3,250); their parse lines; the set has 102 cases; scripted run 5 passed (dev-scripted-quarter); agent-eval.md, architecture.md, rules-coverage.md, failure-modes.md"
```

---

### Task 4: Prove the model reads the quarters

**Files:**
- Modify: `tests/Agent.Core.Tests/Parsing/LiveModelTests.cs` (one new gated theory after `TheBalanceQuestionIsItsOwnIntent`)
- Create: `docs/reports/dev-claude-haiku-quarter-<date>-<HHmm>.md` and `.json` (written by the runner)
- Modify: `docs/agent-model.md` (one sentence on the run), `docs/plans/quarter-wording.md` (the result line)

**Interfaces:**
- Consumes: `CaseLoader.Load(path)` returning cases with `Id`, `Message`, `Steps[n].Say`; `Masker.Mask`, `ParseContext`, `Parser(candidate)` and `Print` from the test class; `ParsedRequest.AmountValue` (`decimal?`) and `AmountUnit`.
- Produces: the report file named in the commit.

- [ ] **Step 1: Write the gated live test**

```csharp
public static TheoryData<string, string, int, decimal, AmountUnit> QuarterCases => new()
{
    // The message of each quarter case (dev set, Khalil-approved wording of 22 Oct 2026) and the parse its sidecar expects.
    { "haiku", "ADD-26", 0, 2.25m, AmountUnit.None },
    { "haiku", "ADD-27", 0, 0.75m, AmountUnit.Million },
    { "haiku", "ADD-28", 0, 0.25m, AmountUnit.Million },
    { "haiku", "ADD-29", 0, 0.25m, AmountUnit.None },
    { "haiku", "ADD-30", 1, 3.25m, AmountUnit.Thousand },
    { "sonnet", "ADD-26", 0, 2.25m, AmountUnit.None },
    { "sonnet", "ADD-27", 0, 0.75m, AmountUnit.Million },
    { "sonnet", "ADD-28", 0, 0.25m, AmountUnit.Million },
    { "sonnet", "ADD-29", 0, 0.25m, AmountUnit.None },
    { "sonnet", "ADD-30", 1, 3.25m, AmountUnit.Thousand },
};

[Theory]
[MemberData(nameof(QuarterCases))]
public async Task AQuarterIsReadAsAFractionOfTheNumber(string candidate, string id, int step, decimal value, AmountUnit unit)
{
    var parser = Parser(candidate);
    var @case = CaseLoader.Load(Path.Combine(EvalPaths.RepositoryRoot(), "data", "requests.dev.jsonl")).Single(c => c.Id == id);
    var masked = Masker.Mask(@case.Steps[step].Say!);

    var parsed = (await parser.ParseAsync(masked.Text, new ParseContext(null, masked.Map), Token)).First;

    Print(candidate, parser);
    Assert.Equal(RequestIntent.Transfer, parsed.Intent);
    Assert.Equal(value, parsed.AmountValue);
    Assert.Equal(unit, parsed.AmountUnit);
}
```

Opus is left out to keep the gated run cheap; add its rows if Khalil wants all three.

- [ ] **Step 2: Build without the gate, see it skip**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Parsing.LiveModelTests"`
Expected: builds; the class skips (no `ZAINCASH_MODEL_TESTS`).

- [ ] **Step 3: Run it live**

Run (PowerShell): `$env:ZAINCASH_MODEL_TESTS='1'; dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Parsing.LiveModelTests"; Remove-Item Env:ZAINCASH_MODEL_TESTS`
Expected: PASS on both candidates. A failure is reported with the parse the model gave (value and unit, never the key material); it is a prompt finding for Khalil, and the test stays as written.

- [ ] **Step 4: Run the five cases on Haiku through the eval**

Wallet running. Run: `dotnet run --project src/Agent.Eval -- dev --only ADD-26,ADD-27,ADD-28,ADD-29,ADD-30 --parser claude:haiku --label quarter`
Expected: `5 passed`, header `Model:PromptVersion` 6, cost printed. Keep the report under `docs/reports/`.

- [ ] **Step 5: Record the result**

- `docs/agent-model.md`: after the version 6 sentence add "Run on 22 Oct 2026 on Haiku over ADD-26 to ADD-30: `docs/reports/dev-claude-haiku-quarter-<date>-<HHmm>` (<n> of 5 passed); the gated `LiveModelTests.AQuarterIsReadAsAFractionOfTheNumber` passed on haiku and sonnet."
- `docs/plans/quarter-wording.md`: one closing line "Built 22 Oct 2026: prompt version 6, ADD-26 to ADD-30, Haiku run <n>/5."

- [ ] **Step 6: Commit**

```bash
git add tests/Agent.Core.Tests/Parsing/LiveModelTests.cs docs/reports/dev-claude-haiku-quarter-* docs/agent-model.md docs/plans/quarter-wording.md
git commit -m "test(core): the quarter forms on the model (22 Oct 2026): the gated LiveModelTests.AQuarterIsReadAsAFractionOfTheNumber over ADD-26 to ADD-30 on haiku and sonnet; Haiku eval run dev-claude-haiku-quarter, <n> of 5 passed; agent-model.md"
```

---

### Task 5: Whole-suite check

- [ ] **Step 1: Run every core test**

Run: `dotnet test tests/Agent.Core.Tests`
Expected: all pass or skip; the same 8 skips as before (database collections when the server is away). The journeys are not run: no journey changed and the dev set only grew.

- [ ] **Step 2: Report to Khalil**

State which "Done when" lines hold: rule 9 examples in; prompt version 6; five cases pass on the oracle; the Haiku figure; the ADD-27 PIN correction; and anything the model got wrong.
