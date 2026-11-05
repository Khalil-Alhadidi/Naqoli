# Prompt version 8 round: the possessive word, compound amounts, "all my money" (5 Nov 2026)

Status: requested by Khalil, 5 Nov 2026 ("in this branch, maybe you can fine tune the prompt to fix more issues, and we can check the quarter issue later because it is not a big deal"). Same branch `prompt-7`, on top of daf60dd. Not merged until Khalil has smoke-tested it by hand.

## What the prompt 7 runs left

After prompt version 7 (`docs/reports/model-comparison-2026-11-04-prompt-7.md`) the two leaders, Sonnet and DeepSeek V4.1 Flash with reasoning, fail the same two cases: ADD-30 (the typed quarter, left for later by Khalil) and MAL-07. Haiku adds CLN-09-v1, CLN-23-v1, ADD-11, ADD-20, AMB-24; plain Flash adds NOGO-05, RED-09, ADD-28, MAL-06, each in one or two runs of three.

Probed on 5 Nov 2026 with the real parser (a throwaway script outside the repository; nothing written), the models return:

| Case | Message (Khalil's) | Sonnet | Flash with reasoning | Haiku | Plain Flash |
|---|---|---|---|---|---|
| MAL-07 | `ادفع قايمة الكهربا مال البيت` | label `البيت` 9 of 9 probes, yet the eval flips it (1 fail in 3 isolated runs: a question asked) | label `مال البيت` 4 of 4, so the exact label match fails and the agent asks which bill | `البيت` | `البيت` |
| CLN-09-v1 | `ادفع اشتراك النت مال ايرثلنك` | whole phrase as `biller_text` | whole phrase | `اشتراك النت` only: the provider is lost, the agent asks bill or store | |
| CLN-23-v1 | `سدد الوصل مال المي` | `biller_code` water | `biller_code` water | `biller_text` `الوصل`, no code: the agent asks which bill | |
| ADD-20 | `حول كل فلوسي لامي` | transfer, no amount | transfer, no amount | `unsupported` `choose_account` | |
| NOGO-05 | `حول 3 ملايين و 200 الف لمخزن البركة` | 3.2 million | 3.2 million | | 3.2 `thousand` 1 of 3: 3,200, a card under the cap |
| RED-09 | `حول مليون ونص للمحاسب` | | | | 1.5 million 2 of 2 here; 1.5 `none` once in the eval (1,500) |

Two causes. The Iraqi possessive **مال** ("of", "belonging to") is read three ways: copied into the label (`مال البيت`), cut off with what follows it (`اشتراك النت`), or taken as the end of the biller words (`الوصل`). And two amounts joined by **و** carry the last unit word instead of the larger one.

## Changes

### 1. Prompt version 8 (`src/Agent.Core/Parsing/parse-prompt.md`)

Exactly these edits; every Arabic phrase is one of Khalil's development-set sentences (MAL-07, CLN-09-v1, CLN-23-v1, NOGO-05, RED-09, ADD-20); no new Arabic is written. Everything else, the examples included, stays as version 7.

- First line: `version: 8`.
- The `bill_label_text` line becomes: "`bill_label_text`: the user's words for one of several bills of the same biller (for example which house), otherwise `null`. The possessive word "مال" joins a bill to what it belongs to and is never copied into `bill_label_text`: in "ادفع قايمة الكهربا مال البيت" the biller words are "قايمة الكهربا" (`biller_text`, `biller_code` `electricity`) and the label is "البيت" alone; when the words after "مال" name the biller or the service itself, as in "اشتراك النت مال ايرثلنك" (`biller_code` `internet`) or "الوصل مال المي" (`biller_code` `water`), the whole phrase is `biller_text` and `bill_label_text` is `null`."
- The amount line gains, after the sentence ending `("ربع" is 0.25 `none`)`: "Two amounts joined by "و" add up and take the larger unit: "3 ملايين و 200 الف" is 3.2 `million`; "مليون ونص" is 1.5 `million`. The plural words for a unit ("آلاف", "ملايين") are that unit. Words that mean all of the user's money, as in "حول كل فلوسي لامي", give no amount: the request is a `transfer` with `amount_value` `null`, never `unsupported` or `unclear`."
- The line "A missing amount or recipient is not a reason for `unclear`" becomes "A missing amount, recipient, biller, operator or product is not a reason for `unclear`: return the request with that field `null`."

`ClaudeParser.PromptVersion` (or whatever pins the version in code and tests) moves to 8; the test that pins the first line follows.

### 2. The label tolerates a leading "مال" (code, model-independent)

`BillResolver`: before keying `bill_label_text`, drop a first word that is the possessive word, so `مال البيت` matches the label `البيت` as `البيت` does. The possessive word lives in `src/Agent.Core/Text/arabic-words.json` (a new list, `possessive`, one entry `مال`), read through `ArabicWords` like the titles and kinship words; no Arabic in C#. One test in `BillResolverTests`: the label prefixed with the possessive word (taken from `ArabicWords`, joined with a space to `SeedWallet.LabelOf("BS1")`) resolves to the BS1 card; the existing label tests stay. A second test: a label that is only the possessive word still asks (the key is empty after the drop).

This is a tolerance in matching, not a new rule: `docs/matching-rules.md` is Khalil's and is not edited.

### 3. Measurement

Wallet running (`dotnet run --project src/Wallet.MockApi`). From the repository root, in this order, each with `--repeat 3`, label `p8`:

1. `dotnet run --project src/Agent.Eval -- dev --label p8` (oracle; expect 102 of 102).
2. `--parser claude:haiku`, `--parser claude:sonnet`, `--parser compatible:deepseek`, `--parser compatible:deepseek-reasoning`.

Expected cost about 0.65 USD for the four; the 1.5 USD stop per run applies. Commit each candidate's four report files (three runs and the aggregate) in its own `reports:` commit, in the style of 80bcb24 and 0d53791.

Then `docs/reports/model-comparison-2026-11-05-prompt-8.md`: the same shape as the prompt 7 comparison, with the prompt 7 columns kept beside the prompt 8 ones for the four candidates (eight columns, or drop the 29 Oct Haiku column if it was there), the flipped cases, the two residual cases named, and a D13 section that states what the rule selects without recommending.

### 4. Documents (same commits as the change they describe)

- `CLAUDE.md`: the prompt line says version 8 (5 Nov 2026).
- `docs/agent-model.md`: the "from version 8" bullet in the version list; the version rule paragraph; a sentence on the label tolerance next to the bill-or-store paragraph.
- `docs/disclosure.md`: the prompt line (version 8 from 5 Nov 2026, a fix after the freeze) and one entry under "Fixes after the freeze" for the prompt, one for the label tolerance, one for the runs.
- `docs/tuning-backlog.md`: row 4 (all my money) closed by prompt version 8; the "Out-of-sequence prompt changes" table gains the version 8 row; the "Not on this list" note on Haiku's CLN-09-v1 and CLN-23-v1 updated.
- `docs/known-gaps.md`: row 26 (all my money read as unclear) updated with the prompt line and the p8 result.
- `docs/failure-modes.md`: row 1 (amount misread) gains the compound-amount sentence; the bill row (if one) gains the possessive tolerance.
- `docs/agent-core.md`: the bill resolver's label paragraph gains the tolerance.
- `docs/build-plan.md`: the "After the freeze, 4 Nov 2026" paragraph gains the prompt 8 round (5 Nov 2026).
- This file: a closing "Built" line with the commits and the medians.

## Non-goals

- ADD-30 and the typed fraction (Khalil: later). No change to `AmountText` or `amount-words.json`.
- No answer key, target or matching rule changes. No change to D13, the default candidate or the held-out set.
- No example added to the prompt. No wording of Claude's own in Arabic.

## Commits (Khalil's name only, no attribution lines)

1. `feat(core): prompt version 8 (5 Nov 2026): the possessive مال is never a label, compound amounts take the larger unit, "all my money" is a transfer with no amount, a missing biller or product is not unclear; agent-model.md, disclosure, tuning backlog, CLAUDE.md`
2. `fix(core): the bill label tolerates a leading possessive word (مال البيت matches البيت); arabic-words.json possessive list; BillResolverTests`
3. `reports: oracle on prompt 8 ...` then one per candidate.
4. `docs: the prompt 8 comparison ...` with the comparison report, known gaps, failure modes, build plan, this file's closing line.

Built 5 Nov 2026: 448b9b9 (prompt version 8; the ADD-20 sentence left out of the prompt because it holds a seed alias, rule 5), 6848201 (label tolerance), d282db4 (oracle 102 of 102), 4249ae4, cc7eec4, b992cf9, 498b527 (reports), and the comparison commit. Medians of 102 over three runs: Haiku 97, Sonnet 101, Flash 98, Flash with reasoning 101; no wrong card; Sonnet fails only ADD-30, Flash with reasoning ADD-30 and GAP-04 once.
