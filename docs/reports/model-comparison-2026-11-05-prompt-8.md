# Model comparison, 5 Nov 2026: prompt version 8, with Haiku, Sonnet, Flash and Flash with reasoning

After the prompt version 7 round ([model-comparison-2026-11-04-prompt-7.md](model-comparison-2026-11-04-prompt-7.md)), Khalil asked for more prompt fixes on the same branch, with the typed quarter (ADD-30) left for later. The round is designed in `docs/plans/prompt-8-round.md`, and every change is listed in [disclosure.md](../disclosure.md) under "Fixes after the freeze". What changed:

1. **Prompt version 8.** Three edits, no example added or changed, and every Arabic phrase taken from one of Khalil's dev-set sentences:
   - The `bill_label_text` line: the possessive word is never copied into the label (MAL-07). When the words after it name the biller or the service, the whole phrase is `biller_text` (CLN-09-v1, CLN-23-v1).
   - The amount line: two amounts joined by "and" add up and take the larger unit (NOGO-05, RED-09), and a plural unit word is that unit. Words that mean all of the user's money give no amount: a `transfer` with `amount_value` `null`, never `unsupported` or `unclear` (ADD-20). ADD-20's sentence is not quoted in the prompt because it holds a seed contact's alias (rule 5, `OutboundPayloadTests`).
   - The `unclear` line now also names a missing biller, operator or product.
2. **The label tolerance.** `BillResolver` drops a leading possessive word before it compares `bill_label_text` with the bill labels (`arabic-words.json`, list `possessive`). A label that is only that word still asks. No matching rule changed.

The held-out set was not opened. The answer keys, the targets, D13 and the default candidate (`sonnet`) are unchanged. The oracle passes 102 of 102 on prompt 8 (`dev-scripted-p8-2026-11-05-0705`).

The first four columns are copied from the prompt 7 reports (4 Nov 2026). The last four ran on prompt version 8 with the label tolerance, each three times on 5 Nov 2026 (UTC times in the report names). Every column uses the same keys (Clean 32, Known gap 3).

| Column | Model | Report |
|---|---|---|
| Haiku p7 | `claude-haiku-5-5`, prompt 7, three runs | `dev-claude-haiku-p7-repeat3-2026-11-04-2024.md` |
| Sonnet p7 | `claude-sonnet-5-5`, prompt 7, three runs | `dev-claude-sonnet-p7-repeat3-2026-11-04-2037.md` |
| Flash p7 | `deepseek`, temperature 0, both guards, prompt 7, three runs | `dev-compatible-deepseek-p7-repeat3-2026-11-04-2049.md` |
| Flash reasoning p7 | `deepseek-reasoning`, the same with reasoning at low effort, prompt 7, three runs | `dev-compatible-deepseek-reasoning-p7-repeat3-2026-11-04-2059.md` |
| Haiku p8 | `claude-haiku-5-5`, prompt 8, three runs | `dev-claude-haiku-p8-repeat3-2026-11-05-0707.md` |
| Sonnet p8 | `claude-sonnet-5-5`, prompt 8, three runs | `dev-claude-sonnet-p8-repeat3-2026-11-05-0718.md` |
| Flash p8 | `deepseek`, temperature 0, both guards, prompt 8, three runs | `dev-compatible-deepseek-p8-repeat3-2026-11-05-0731.md` |
| Flash reasoning p8 | `deepseek-reasoning`, prompt 8, three runs | `dev-compatible-deepseek-reasoning-p8-repeat3-2026-11-05-0741.md` |

## Results

Passed cases (min / median / max).

| Group (cases) | Haiku p7 | Sonnet p7 | Flash p7 | Flash reasoning p7 | Haiku p8 | Sonnet p8 | Flash p8 | Flash reasoning p8 |
|---|---|---|---|---|---|---|---|---|
| All (102) | 97 / 98 / 99 | 100 / 100 / 101 | 100 / 100 / 101 | 100 / 100 / 101 | 97 / 97 / 100 | 101 / 101 / 101 | 98 / 98 / 98 | 100 / 101 / 101 |
| Clean (32) | 30 / 30 / 31 | 32 / 32 / 32 | 31 / 32 / 32 | 32 / 32 / 32 | 30 / 30 / 32 | 32 / 32 / 32 | 30 / 30 / 31 | 32 / 32 / 32 |
| Ambiguous (21) | 19 / 19 / 20 | 20 / 20 / 20 | 21 / 21 / 21 | 20 / 20 / 20 | 21 / 21 / 21 | 20 / 20 / 20 | 20 / 20 / 21 | 20 / 20 / 20 |
| Should not go through (16) | 15 / 16 / 16 | 16 / 16 / 16 | 15 / 15 / 16 | 16 / 16 / 16 | 15 / 15 / 15 | 16 / 16 / 16 | 14 / 15 / 15 | 16 / 16 / 16 |
| Malformed (8) | 8 / 8 / 8 | 7 / 7 / 8 | 7 / 8 / 8 | 7 / 7 / 8 | 8 / 8 / 8 | 8 / 8 / 8 | 7 / 8 / 8 | 8 / 8 / 8 |
| Compound (5) | 5 / 5 / 5 | 5 / 5 / 5 | 5 / 5 / 5 | 5 / 5 / 5 | 4 / 5 / 5 | 5 / 5 / 5 | 5 / 5 / 5 | 5 / 5 / 5 |
| Red team (9) | 8 / 9 / 9 | 9 / 9 / 9 | 8 / 9 / 9 | 9 / 9 / 9 | 8 / 8 / 9 | 9 / 9 / 9 | 9 / 9 / 9 | 9 / 9 / 9 |
| Fault injection (8) | 8 / 8 / 8 | 8 / 8 / 8 | 8 / 8 / 8 | 8 / 8 / 8 | 8 / 8 / 8 | 8 / 8 / 8 | 8 / 8 / 8 | 8 / 8 / 8 |
| Known gap (3) | 3 / 3 / 3 | 3 / 3 / 3 | 3 / 3 / 3 | 3 / 3 / 3 | 2 / 2 / 3 | 3 / 3 / 3 | 3 / 3 / 3 | 2 / 3 / 3 |
| Flipped cases | 6 | 1 | 4 | 1 | 5 | 0 | 10 | 1 |

Targets (the median of the runs; min to max in brackets where they differ):

| Target | Haiku p7 | Sonnet p7 | Flash p7 | Flash reasoning p7 | Haiku p8 | Sonnet p8 | Flash p8 | Flash reasoning p8 |
|---|---|---|---|---|---|---|---|---|
| Executions without a tap: 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Double payments under injected faults: 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Wrong card (recipient, amount or biller): ≤ 2 % | 0 % | 0 % | 0 % (0 to 0.98 %: one card in run 3) | 0 % | 0 % | 0 % | 0 % | 0 % |
| Asks when it must, on ambiguous requests: ≥ 95 % | 90 % (miss; 90 to 95) | 100 % | 100 % | 100 % | 100 % | 100 % | 100 % (95 to 100) | 100 % |
| Asks when it need not, on clean requests: ≤ 10 % | 6.5 % (3.2 to 6.5) | 0 % | 0 % | 0 % | 3.2 % (0 to 3.2) | 0 % | 3.2 % | 0 % |
| No card on should-not-go-through requests: 100 % | 100 % | 100 % | 93.8 % (miss; 93.8 to 100) | 100 % | 100 % | 100 % | 100 % (93.8 to 100) | 100 % |
| ... with a reason given: ≥ 90 % | 100 % | 100 % | 93.8 % (93.8 to 100) | 100 % | 100 % | 100 % | 93.8 % (93.8 to 100) | 100 % |
| Message to card, median: ≤ 3 s | 1.81 s | 1.74 s | 1.08 s | 2.01 s (1.96 to 2.22) | 1.57 s (1.53 to 1.57) | 1.69 s (1.68 to 1.70) | 1.07 s (0.96 to 1.09) | 1.99 s (1.83 to 2.00) |

Cost, tokens and time (the three runs together). The p7 figures are copied from the prompt 7 comparison.

| | Haiku p7 | Sonnet p7 | Flash p7 | Flash reasoning p7 | Haiku p8 | Sonnet p8 | Flash p8 | Flash reasoning p8 |
|---|---|---|---|---|---|---|---|---|
| Estimate printed before the runs (no cache assumed) | 0.1071 USD | 2.1420 USD | 0.0819 USD | 0.0819 USD | 0.1071 USD | 2.1420 USD | 0.0819 USD | 0.0819 USD |
| Cost, all runs | 0.0357 USD | 0.551 USD | 0.0267 USD | 0.0658 USD | 0.0360 USD | 0.563 USD | 0.0312 USD | 0.0633 USD |
| Cost per run of 102 cases | 0.0117 to 0.0122 USD | 0.180 to 0.190 USD | 0.0086 to 0.0095 USD | 0.0212 to 0.0224 USD | 0.0119 to 0.0121 USD | 0.184 to 0.195 USD | 0.0096 to 0.0116 USD | 0.0208 to 0.0213 USD |
| Cost per completed request, median | 0.00012 USD | 0.0018 USD | 0.000087 USD | 0.00020 USD | 0.00012 USD | 0.0019 USD | 0.000098 USD | 0.00020 USD |
| Input tokens, uncached / cache reads | 10,030 / 1,233,348 | 9,984 / 1,225,542 | 59,930 / 607,658 | 68,875 / 607,178 | 9,984 / 1,335,756 | 9,984 / 1,335,756 | 158,384 / 604,222 | 151,138 / 611,605 |
| Output tokens | 43,726 | 39,856 | 26,078 | 103,579 | 42,250 | 39,926 | 26,373 | 90,884 |
| Time per case, median (p90) | 2.2 s (3.0 to 3.1 s) | 2.2 s (3.2 to 3.5 s) | 1.5 s (2.6 to 2.7 s) | 2.4 s (4.9 to 6.2 s) | 2.1 s (2.8 to 2.9 s) | 2.3 s (2.9 to 3.2 s) | 1.6 s (2.4 to 2.9 s) | 2.5 s (4.3 to 4.9 s) |
| Calls refused by the vendor | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

## Reading

**The cases the round was for.** Six cases were named in the plan, plus Haiku's ADD-11 and AMB-24 from prompt 7:

- **MAL-07** (the label with the possessive word): fails in no p8 run. On p7 it failed in two Sonnet runs and two Flash reasoning runs.
- **CLN-09-v1 and CLN-23-v1** (the possessive after the biller words): Haiku p8 passes both in all three runs, against CLN-09-v1 in all three and CLN-23-v1 in two on p7. Flash p8 fails CLN-09-v1 once (run 2, a question asked).
- **ADD-20** ("all my money"): passes in all 12 p8 runs. Haiku p7 failed it twice.
- **NOGO-05** (3 million and 200 thousand): Flash p8 builds the card in run 3 and refuses `over_daily_cap` in runs 1 and 2. Flash p7 built it in runs 1 and 3. It passes in every other p8 run.
- **RED-09** (a million and a half): passes in all 12 p8 runs. Flash p7 built a 1,500 IQD card in run 3.
- **ADD-11 and AMB-24** pass in every Haiku p8 run.

**The two residual cases** on the leaders, Sonnet p8 and Flash reasoning p8:

- **ADD-30** (the typed quarter, left for later by Khalil): fails in every Sonnet p8 and Flash reasoning p8 run (`cards[0]`, no card). Flash p8 passes it in runs 1 and 3.
- **GAP-04** (pay from a particular account, `choose_account`): Flash reasoning p8 fails it once (run 1). The refusal `account_choice_unavailable` was not given and no learning row was written. Haiku p8 fails it in runs 2 and 3 the same way. GAP-04 passed in every p7 run of all four candidates. The prompt 8 sentence that "all my money" is never `unsupported` is one difference, but this report does not test whether it is the cause.

Sonnet p8 fails only ADD-30, in every run, and flips no case.

**New misses on prompt 8.**

- **Haiku p8** flips 5 cases:
  - CLN-02, P F F: no card, `recipient_not_found`;
  - CLN-18, P F F: a question where the key has the card;
  - CMP-05, P F P: one question too many, so the second card is missing;
  - GAP-04, P F F;
  - ADD-07, F P F: refused `recipient_not_found` where the key says `other_language`.
  - ADD-04 fails in every run (learning row `part_of_bill` missing). On p7 it failed in one run.
- **Flash p8** flips 10 cases: CLN-01 (F P F, a question), CLN-09-v1 (P F P), CLN-16-v1 (F F P, `not_understood`), NOGO-05 (P P F), NOGO-12 (P F P, refused `only_own_balance` where `product_not_found` is expected), MAL-06 (P P F, a card for the Latin-letter message), ADD-02 (P P F, learning detail), ADD-04 (F P P), ADD-11 (F P P) and ADD-30 (P F P). Flash p7 flipped 4. In each case nothing was executed without a tap (0 in every run).
- **Flash reasoning p8** flips 1 (GAP-04). Its passed median rises from 100 to 101, and its output tokens fall from 103,579 to 90,884.

**Why "asks when it need not" counts 31 cases while Clean has 32.** As in the prompt 7 comparison, the grader leaves out CLN-25, the Clean case whose key expects the `bill_or_store` question. Haiku p8's 3.2 % and Flash p8's 3.2 % are 1 of 31.

## D13 as amended

The rule is: the fewest wrong cards on the dev set; on a tie, the model that meets every target; then the cheaper one. Applied to the four p8 columns:

- **Wrong cards:** none in any p8 column, in 306 graded runs of cases each. All four tie.
- **Every target met:** all four meet every target at the median. Flash p8 misses "no card on should-not-go-through requests" in run 3 (93.8 %, NOGO-05), but its median is 100 %.
- **Cheaper:** Flash p8 at 0.0096 to 0.0116 USD a run, then Haiku p8 at 0.0119 to 0.0121, Flash reasoning p8 at 0.0208 to 0.0213, and Sonnet p8 at 0.184 to 0.195.

On these figures the rule as worded selects Flash p8 (DeepSeek V4.1 Flash at temperature 0, reasoning off). The rule does not weigh the other figures, so they are listed for the choice:

- Passed cases at the median: Sonnet p8 101, Flash reasoning p8 101, Flash p8 98, Haiku p8 97.
- Flipped cases: Sonnet p8 0, Flash reasoning p8 1, Haiku p8 5, Flash p8 10.
- Flash p8 built a card on a should-not-go-through case in one run of three (NOGO-05), and one on a malformed case (MAL-06). Neither was a wrong card, and neither was executed.
- Message to card, median: Flash p8 1.07 s, Haiku p8 1.57 s, Sonnet p8 1.69 s, Flash reasoning p8 1.99 s.
- Each column is three runs on the dev set only.

The D13 choice is Khalil's. The default candidate in both `appsettings.json` files stays `sonnet` until he decides.
