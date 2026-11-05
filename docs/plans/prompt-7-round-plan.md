# Prompt version 7 round: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** apply Khalil's four rulings of 4 Nov 2026 (Latin script and English refused; the undo refusal; own-number credit as a purchase; someone else's balance refused by code), re-key the five dev cases they touch, add the Flash reasoning candidate, and re-measure Haiku, Sonnet and Flash three times each on prompt version 7.

**Architecture:** the prompt and the validator's reason list change together (the schema enumerates the validator's list); a refusal reason flows prompt → validator → orchestrator (`Unsupported_<reason>`) → learning-log rule (seed lists) → phone string. Guards are static classes applied after validation in both parsers through `ParseContext`. Keys and oracle parses change in the dev set files; the oracle run proves the keys and the code agree.

**Tech Stack:** .NET 10, xUnit v3, the eval runner, OpenRouter through the `compatible` provider.

**Spec:** `docs/plans/prompt-7-round.md` (approved 4 Nov 2026). The plan argues from it; a conflict resolves against the spec.

## Global Constraints

- CLAUDE.md rules 1 to 9. Never open, read, grep, list, hash or copy `data/requests.heldout.jsonl` or the `.xlsx` workbook. No secrets in any file, log, report or commit message; never print user-secrets values.
- No Arabic in C#. The one new phone string is Khalil's, verbatim: `اعتذر ما اكدر اتراجع عن عملية التحويل او الشراء`. Arabic test text comes from the dev set by id (`DevSet`, `DevSetText`) or the loaded settings, never as a literal.
- `src/Agent.Web/appsettings.json` and `src/Agent.Eval/appsettings.json` keep identical `Model` and `Agent` sections.
- Commits in Khalil's name only; never a Co-Authored-By or Claude-Session line. Small commits, messages in the style of the branch's history. Never merge, tag or push.
- A disclosure line in the same commit as any change to the prompt, the parsing code, the seed, or a model candidate, under "### Fixes after the freeze" (one entry per commit: date, what, why).
- The prompt is version 7 after this round; no edit beyond the four named in the spec (other-language definition, `undo_payment` added, `credit_to_own_number` removed, `recipient_text` definition extended).
- `Model:Candidate` in the console's user secrets is `sonnet` before and after the round; restore it after any smoke test.
- The runner is built to its own output (`src/Agent.Eval/bin/run-20261105`) so no running process locks the DLLs; reports are committed as produced, never edited; stop before any run whose printed estimate is above 1.5 USD.

## Review Focus

1. A mixed message (Arabic with one Latin operator word) must still resolve to a product, not be refused: pinned by the resolver-level test in Task 2 and stated in the prompt line.
2. The other-balance guard must never turn a transfer with a name into a refusal, nor a balance question with no name: `OtherBalanceGuardTests` in Task 3.
3. Removing `credit_to_own_number` must leave no dead reference (resx key, Help list, user-messages row, validator, learning lists, docs): `PhoneStringsTests` and `LearningLogRuleTests` plus a grep in Task 1's self-review.
4. The seed hash must be re-frozen only after the oracle passes 102 of 102 (Task 2), never before.
5. The reasoning candidate's output budget (4000) must be the only difference from `deepseek` besides `RequestFields`, `Temperature` and the timeout: `EvalSettingsTests` in Task 4.

---

### Task 1: prompt version 7 and the `undo_payment` reason end to end

**Files:**
- Modify: `src/Agent.Core/Parsing/parse-prompt.md` (first line → version 7, 4 Nov 2026; the four edits of the spec)
- Modify: `src/Agent.Core/Parsing/ParsedRequestValidator.cs` (`UnsupportedReasons`: add `"undo_payment"`, remove `"credit_to_own_number"`)
- Modify: `src/Agent.Core/Parsing/ParsedRequestDto.cs`, `src/Agent.Core/Requests/ParsedRequest.cs` (doc comments: schema version 4, the eleven reasons)
- Modify: `src/Agent.Core/Learning/LearningLogRule.cs` (`UndoPayment` constant in `Logged` at the seed's position; `CreditToOwnNumber` removed)
- Modify: `data/seed/settings.json` (`learning_log.logged`: `undo_payment` appended, `credit_to_own_number` removed), `data/seed/README.md` (the list and the seed version note, seed-v5)
- Modify: `src/Agent.Web/Resources/PhoneStrings.resx` (`Unsupported_undo_payment` = Khalil's string; `Unsupported_credit_to_own_number` removed), `src/Agent.Web/Components/Phone/HelpPanel.razor` (the "does not do" list: the new key in, the old out), `docs/user-messages.md` (row swapped)
- Modify: `docs/agent-model.md` (prompt version history entry 7; the reasons list), `docs/agent-core.md` (the learning lists), `docs/disclosure.md` ("Fixes after the freeze" entry; the prompt line says version 7), `CLAUDE.md` (the prompt version sentence)
- Test: `tests/Agent.Core.Tests/Learning/LearningLogRuleTests.cs`, `tests/Agent.Core.Tests/Parsing/` (the validator's reason tests), `tests/Agent.Core.Tests/Web/PhoneStringsTests.cs`

**Interfaces:**
- Produces: `LearningLogRule.UndoPayment == "undo_payment"`; `ParsedRequestValidator.UnsupportedReasons` without `credit_to_own_number`; resx key `Unsupported_undo_payment`.
- Note: the seed hash is NOT re-frozen in this task; `SeedHashTests` is expected to fail until Task 2 re-freezes it after the oracle passes.

- [ ] **Step 1: Write the failing tests.** `LearningLogRuleTests.TheListsAreTheSettingsLists` already compares code and seed: it fails once either side changes alone. Add `ParsedRequestValidatorTests.UndoPaymentIsAReasonAndCreditToOwnNumberIsNot` asserting `UnsupportedReasons` contains `"undo_payment"` and not `"credit_to_own_number"`. Add to `PhoneStringsTests` (where keys are enumerated) that `Unsupported_undo_payment` exists with a non-empty value and `Unsupported_credit_to_own_number` does not exist.
- [ ] **Step 2: Run them.** `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Parsing.ParsedRequestValidatorTests"` (and the two others): expect failures naming the missing reason and key.
- [ ] **Step 3: Implement.** The prompt edits (meaning per the spec; English wording the builder's); the validator list; the constants; the seed list and README; the resx key with Khalil's string; the Help list; the user-messages row; the docs and the disclosure entry.
- [ ] **Step 4: Run.** The three test classes pass; `dotnet build` has no new warning; `git grep -n credit_to_own_number -- src tests data docs/*.md CLAUDE.md` shows only history sentences in docs (the known-gaps row, the model doc's version history), nothing live.
- [ ] **Step 5: Commit.** `git add` the files by name. Message: `feat(core): prompt version 7 (Khalil, 4 Nov 2026): other_language covers English and Latin-script Arabic, undo_payment refusal with his wording, credit_to_own_number removed (own-number credit is a purchase), recipient_text names the person whose balance is asked; schema v4; learning lists (seed-v5, hash re-frozen in the next commit); Unsupported_undo_payment; Help list; disclosure`.

### Task 2: the five keys, the oracle parses, the two tests, the oracle run, the seed hash

**Files:**
- Modify: `data/requests.dev.jsonl` (MAL-06, MAL-09, ADD-07, ADD-23 notes, ADD-24, GAP-06 as the spec says), `data/requests.dev.parses.jsonl` (the four parse lines), `data/README.md` (category counts: Clean 32, Known gap 3; the rulings)
- Modify: `tests/Agent.Core.Tests/Conversation/ConversationTests.cs` and `tests/Agent.Web.Journeys/PurchaseJourneys.cs` (`ALatinOperatorSpellingGoesStraightToTheCard` → `ALatinScriptMessageIsRefusedAsOtherLanguage` on MAL-09: no card, reply `Unsupported_other_language`, learning row `other_language`); any test that cites GAP-06 as a known gap, ADD-07's key, or MAL-06's card (grep the ids)
- Test: a resolver-level test pinning that the Latin spelling `asia` resolves to the Asiacell product (if `ProviderSpellingsTests` or the product resolver's tests do not already; the spelling is the only literal)
- Modify: `data/seed/seed-hash.txt` (re-frozen after the oracle passes), `docs/disclosure.md` (the seed line's hash and date), `docs/known-gaps.md` (row 12 closed), `docs/rules-coverage.md` (rows citing MAL-06, MAL-09, ADD-07, ADD-24, GAP-06)
- Create: `docs/reports/dev-scripted-p7-<date>-<HHmm>.md/.json` (the oracle run, as produced)

**Interfaces:**
- Consumes: Task 1's reason and string.
- Produces: the dev set as the models are measured against from Task 5 on; the seed hash the disclosure names.

- [ ] **Step 1: Re-key and re-parse** the cases exactly as the spec's "The three keys and the oracle" section says (MAL-06 and ADD-07 in the same shape as MAL-09). GAP-06's card fields are copied from a passing Zain credit 5,000 purchase case of the Clean group (find it by `product_value` 5000 and the Zain operator in `expect.cards`).
- [ ] **Step 2: Change the two tests** and add the resolver-level one; run `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Conversation.ConversationTests"` and the new class: pass.
- [ ] **Step 3: The oracle run.** Wallet running (`dotnet run --project src/Wallet.MockApi`, wait for `/health`); `dotnet build src/Agent.Eval -o src/Agent.Eval/bin/run-20261105`; `dotnet src/Agent.Eval/bin/run-20261105/Agent.Eval.dll dev --label p7`. Expected: 102 passed of 102. If not, fix the key or the parse line (never the code) and run again; the committed report is the passing one.
- [ ] **Step 4: Re-freeze the seed hash.** `dotnet run --project src/Wallet.MockApi --no-launch-profile -- seed-hash`; the last line goes into `data/seed/seed-hash.txt`; the disclosure's seed line gets the new hash and "seed-v5, 4 Nov 2026: learning lists". `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Wallet.SeedHashTests"`: pass.
- [ ] **Step 5: Full core suite** `dotnet test tests/Agent.Core.Tests`: 0 failed. Then the journeys `dotnet test tests/Agent.Web.Journeys`: 0 failed (the renamed journey passes).
- [ ] **Step 6: Commit** in two commits: (a) the keys, parses, tests, docs and the oracle report: `test(dev-set): MAL-06, MAL-09 and ADD-07 refused as other_language, ADD-24 as undo_payment, GAP-06 a plain purchase (Khalil, 4 Nov 2026); parses; the two Latin-operator tests become refusal tests; oracle p7 102 of 102; known gap 12 closed`; (b) the seed hash: `data(seed): seed-v5 hash re-frozen after the oracle p7 run (learning lists); disclosure`.

### Task 3: the other-balance guard and the RED-10 probe

**Files:**
- Create: `src/Agent.Core/Parsing/OtherBalanceGuard.cs`, `src/Agent.Core/Parsing/ParseGuards.cs` (`Apply(IReadOnlyList<ParsedRequest> requests, string maskedText, IReadOnlyCollection<string> dinarWords)` = dinar guard then other-balance guard)
- Modify: `src/Agent.Core/Parsing/ClaudeParser.cs`, `src/Agent.Core/Parsing/ScriptedParser.cs` (call `ParseGuards.Apply` where `DinarUnitGuard.Apply` is called today)
- Modify: `docs/agent-core.md` or `docs/agent-model.md` (the guards paragraph), `docs/failure-modes.md` (the other-balance row), `docs/disclosure.md` (entry)
- Test: `tests/Agent.Core.Tests/Parsing/OtherBalanceGuardTests.cs`

**Interfaces:**
- Produces: `OtherBalanceGuard.Apply(IReadOnlyList<ParsedRequest>) → IReadOnlyList<ParsedRequest>`: a request with `Intent == RequestIntent.Balance` (use the enum as named in `ParsedRequest`) and non-blank `RecipientText` becomes `Intent` unsupported with `UnsupportedReason == "other_balance"`, all other fields kept; same list instance when nothing changes.

- [ ] **Step 1: Failing tests** `OtherBalanceGuardTests`: `ABalanceQuestionWithANameBecomesOtherBalance`, `ABalanceQuestionWithoutANameStays`, `ATransferWithANameStays`, `AnUnsupportedRequestStays`, `TheSameListIsReturnedWhenNothingChanges`. Names from `SeedFacts` (`Contact("S01").SavedName`).
- [ ] **Step 2: Run:** fail with the type missing.
- [ ] **Step 3: Implement** the guard and `ParseGuards`; wire both parsers; move the dinar call inside.
- [ ] **Step 4: Run** the new class, `DinarUnitGuardTests`, the scripted-parser tests and the full core suite: 0 failed. The oracle run is unchanged by construction (the oracle's RED-10 parse is unsupported already); no rerun needed.
- [ ] **Step 5: The probe.** `Model:Candidate` untouched (the runner takes `--parser`). Wallet running; `dotnet src/Agent.Eval/bin/run-20261105/Agent.Eval.dll dev --parser compatible:deepseek --only RED-10 --label probe`. Read the case's parse in the probe's trace (the report's case row or the eval database's trace rows for that case): if `recipient_text` holds the name and the case passes, go on and delete the probe's two report files (do not commit them); if the name is absent, stop and report BLOCKED with the parse as observed.
- [ ] **Step 6: Commit:** `fix(core): the other-balance guard (Khalil, 4 Nov 2026): a balance question that names a person is refused only_own_balance whatever the model read; ParseGuards applies both guards in both parsers; disclosure`.

### Task 4: the `deepseek-reasoning` candidate

**Files:**
- Modify: `src/Agent.Web/appsettings.json`, `src/Agent.Eval/appsettings.json` (the entry after `qwen`), `tests/Agent.Core.Tests/Eval/EvalSettingsTests.cs` (pin: a copy of `deepseek` except `RequestFields` `{"reasoning":{"effort":"low"},"provider":{"data_collection":"deny"}}`, `TimeoutSeconds` 90, `MaxOutputTokens` 4000), `docs/agent-model.md` (candidates table), `docs/disclosure.md` (one line), `CLAUDE.md` (candidate list)

- [ ] **Step 1: Failing test** in `EvalSettingsTests`: the entry exists in both files with exactly those differences.
- [ ] **Step 2: Add the entry**; test passes; both Model sections identical (assert in the test as the existing tests do).
- [ ] **Step 3: Smoke test**, gated: `dotnet user-secrets set "Model:Candidate" "deepseek-reasoning" --project src/Agent.Web`, `ZAINCASH_MODEL_TESTS=1 dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Parsing.LiveCompatibleTests"`: 4 of 4; then **restore** `dotnet user-secrets set "Model:Candidate" "sonnet" --project src/Agent.Web`.
- [ ] **Step 4: Commit:** `config: deepseek-reasoning (Flash with reasoning at low effort, temperature 0, 4000 output tokens) for the stability experiment of the prompt 7 round; EvalSettingsTests; disclosure`.

### Task 5: the runs, the comparison, the closing docs

**Files:**
- Create: `docs/reports/dev-claude-haiku-p7-r1..r3-*`, `-p7-repeat3-*`; the same for `dev-claude-sonnet-p7-*`, `dev-compatible-deepseek-p7-*`, `dev-compatible-deepseek-reasoning-p7-*`; `docs/reports/model-comparison-2026-11-05.md` (named for the runs' date)
- Modify: `docs/agent-model.md` (pointer and one paragraph), `docs/known-gaps.md` (row 25: Flash accepts `Temperature` 0 since 4 Nov 2026, flips 9 → 4 → the reasoning figure; the held-out open item), `docs/build-plan.md` (an "after the freeze, 4 Nov 2026" entry under milestone 10 and the held-out open item under milestone 11), `docs/failure-modes.md` (only if a new failure appeared)

- [ ] **Step 1: Runs**, wallet running, in this order, each committed as produced before the next starts: `dev --parser claude:haiku --repeat 3 --label p7`; `dev --parser claude:sonnet --repeat 3 --label p7`; `dev --parser compatible:deepseek --repeat 3 --label p7`; `dev --parser compatible:deepseek-reasoning --repeat 3 --label p7`. Stop before any run whose estimate is above 1.5 USD.
- [ ] **Step 2: The report** per the spec's "Runs and the report": the three tables with columns Haiku (29 Oct), Sonnet (29 Oct), Flash fixed (4 Nov), Haiku p7, Sonnet p7, Flash p7, Flash reasoning p7 (drop the 29 Oct Haiku column and cite it if seven is too wide); the group table says Clean 32 / Known gap 3 from this report on; the Reading section covers MAL-06, MAL-09, ADD-07, ADD-23, ADD-24, GAP-06 and RED-10 per column, flips with and without reasoning, time to card with reasoning, and what the D13 rule as amended selects, without recommending.
- [ ] **Step 3: The docs** listed above.
- [ ] **Step 4: Full core suite** once more: 0 failed.
- [ ] **Step 5: Commit** the reports per run (`reports: Haiku on prompt 7, three runs of <date> ...` and so on) and the comparison and docs: `docs: the prompt 7 comparison (<figures>); known gap 25 corrected; the held-out keys recorded as Khalil's open item`.

### Controller steps after Task 5

1. Review every diff against the spec; rulings in the ledger.
2. Tell Khalil the branch is ready for his hand smoke test, with the four sentences to type and the user-secrets switch for Flash.
3. After his test: the `dotnet-security-reviewer` agent on `main..prompt-7`, one fix wave, the review in `docs/reviews/prompt-7-security-review.md`.
4. Merge `--no-ff` to main, push main and the branch. No tag.
