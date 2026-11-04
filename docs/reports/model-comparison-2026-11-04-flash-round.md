# Model comparison, 4 Nov 2026, the Flash round: DeepSeek V4.1 Flash at temperature 0 with the dinar-unit guard, Sonnet rerun

On 4 Nov 2026 DeepSeek V4.1 Flash (candidate `deepseek`) passed 92 of 102 at the median ([model-comparison-2026-11-04.md](model-comparison-2026-11-04.md)). It built a 500,000 IQD card on ADD-13 ("500 dinars", below the 1,000-dinar minimum) in every run, and 9 cases flipped between its runs. Khalil asked for two fixes and a new measurement, so that he can make the D13 choice later:

1. `"Temperature": 0` on the `deepseek` entry, in both `appsettings.json` files (commit 97e86a0). No other candidate changes.
2. The dinar-unit guard, applied after the validator in both parsers (commit a15629f; [agent-model.md](../agent-model.md), "The dinar-unit guard"). When a request has an amount and no unit, and the message holds a word of `Agent:DinarWords`, the unit becomes dinars.

Both are listed in [disclosure.md](../disclosure.md) under "Fixes after the freeze". The prompt is unchanged at version 6, and so are the dev set (case file SHA-256 `7378aa51...67c9`) and its parses. The held-out set was not opened. With the guard, the oracle run (`dev-scripted-guard-2026-11-04-1717`) passes 101 of 102 with GAP-06 its only failure, the same result as the last full oracle runs.

The Sonnet (29 Oct) and Flash (4 Nov) columns are copied unchanged from `model-comparison-2026-11-04.md`. Flash fixed is three runs. Sonnet fixed is one run with the guard on the same code, so its min, median and max are the same number, and each cell shows that number once.

| Column | Model | Report |
|---|---|---|
| Sonnet | `claude-sonnet-5-5` (Anthropic, effort low, prompt cache 5m), 29 Oct 2026, three runs | `dev-claude-sonnet-repeat3-2026-10-29-1049.md` |
| Flash | `deepseek/deepseek-v4.1-flash` (OpenRouter, `compatible`, `json_schema`, temperature unset, no guard), 4 Nov 2026, three runs | `dev-compatible-deepseek-repeat3-2026-11-04-1526.md` |
| Flash fixed | the same, at temperature 0 and with the dinar-unit guard, 4 Nov 2026, three runs | `dev-compatible-deepseek-fixed-repeat3-2026-11-04-1719.md` |
| Sonnet fixed | `claude-sonnet-5-5` as configured, with the dinar-unit guard, 4 Nov 2026, one run | `dev-claude-sonnet-fixed-2026-11-04-1729.md` |

## Results

Passed cases (min / median / max of 102; Sonnet fixed: the one run):

| Group (cases) | Sonnet | Flash | Flash fixed | Sonnet fixed |
|---|---|---|---|---|
| All (102) | 96 / 97 / 97 | 90 / 92 / 95 | 95 / 95 / 96 | 97 |
| Clean (31) | 31 / 31 / 31 | 29 / 30 / 31 | 31 / 31 / 31 | 31 |
| Ambiguous (21) | 19 / 20 / 20 | 19 / 20 / 20 | 20 / 21 / 21 | 20 |
| Should not go through (16) | 14 / 14 / 14 | 12 / 12 / 13 | 13 / 13 / 14 | 14 |
| Malformed (8) | 8 / 8 / 8 | 7 / 7 / 7 | 7 / 7 / 7 | 8 |
| Compound (5) | 5 / 5 / 5 | 5 / 5 / 5 | 5 / 5 / 5 | 5 |
| Red team (9) | 9 / 9 / 9 | 8 / 8 / 9 | 7 / 8 / 8 | 9 |
| Fault injection (8) | 8 / 8 / 8 | 7 / 8 / 8 | 8 / 8 / 8 | 8 |
| Known gap (4) | 2 / 2 / 2 | 2 / 2 / 3 | 2 / 3 / 3 | 2 |
| Flipped cases (pass in some runs, fail in others) | 1 | 9 | 4 | n/a (one run) |

Targets (the median of the runs; Sonnet fixed: the one run):

| Target | Sonnet | Flash | Flash fixed | Sonnet fixed |
|---|---|---|---|---|
| Executions without a tap: 0 | 0 | 0 | 0 | 0 |
| Double payments under injected faults: 0 | 0 | 0 | 0 | 0 |
| Wrong card (recipient, amount or biller): ≤ 2 % | 0 % | 0 % | 0 % | 0 % |
| Asks when it must, on ambiguous requests: ≥ 95 % | 100 % | 100 % | 100 % (95 % in run 3) | 100 % |
| Asks when it need not, on clean requests: ≤ 10 % | 0 % | 0 % | 0 % | 0 % |
| No card on should-not-go-through requests: 100 % | 100 % | 93.8 % (miss; ADD-13 in every run) | 100 % | 100 % |
| ... with a reason given: ≥ 90 % | 93.8 % | 87.5 % (miss) | 93.8 % | 93.8 % |
| Message to card, median: ≤ 3 s | 1.76 s | 1.02 s | 1.09 s | 1.79 s |

Cost, tokens and time (Sonnet, Flash and Flash fixed: the three runs together; Sonnet fixed: its one run):

| | Sonnet | Flash | Flash fixed | Sonnet fixed |
|---|---|---|---|---|
| Cost, all runs | 0.541 USD | 0.0254 USD | 0.0253 USD | 0.185 USD |
| Cost per run of 102 cases | 0.180 USD | 0.0085 USD | 0.0084 USD | 0.185 USD |
| Cost per completed request, median | 0.0018 USD | 0.000083 USD | 0.000085 USD | 0.0019 USD |
| Input tokens, uncached / cache reads | 3,351 / 366,135 | 72,712 / 502,182 | 74,061 / 493,824 | 3,328 / 362,648 |
| Output tokens | 13,501 | 26,151 | 26,076 | 13,367 |
| Time per case, median | 2.3 s | 1.6 s | 1.6 s | 2.4 s |
| Calls refused by the vendor | 0 | 0 | 0 | 0 |

Before each run the runner printed its estimate, which assumes no cache: 0.0273 USD a run for Flash and 0.7140 USD for Sonnet. The real cost was lower because most of the repeated prompt was read from the cache. Flash fixed had hit rates of 80.7, 90.2 and 90.0 %. Sonnet fixed had 98.2 %, with 3,487 tokens written to the cache.

## Reading

- **ADD-13 on Flash fixed:** it passes in all three runs. The case refuses `amount_below_minimum`, no card is issued and nothing is executed (`cards_issued` 0 in every run), so no amount is stored. That brings the should-not-go-through target to 100 % and the reason-given target to 93.8 %, which were the two targets Flash missed before.
- **Flipped cases:** 9 before (Flash), 4 after (Flash fixed: RED-09 fail, pass, pass; GAP-04 pass, pass, fail; ADD-04 fail, fail, pass; ADD-12 pass, pass, fail). In the earlier report, Flash failed MAL-09, ADD-23 and ADD-24 in every run and still does. It also failed ADD-30 in every run; Flash fixed passes ADD-30 in every run. RED-10 now fails in every run (`not_supported` expected, no learning row observed). Before, it passed in every run.
- **Targets:** Flash missed two targets and Flash fixed misses none. One margin is thin: "asks when it must" is 100 % at the median but 95 % in run 3, where ADD-12 asked `which_amount` and not `which_contact`. That run is exactly at the target.
- **Passed cases:** Flash fixed passes 95 of 102 at the median (95 to 96), against 92 before. That is still below Sonnet's 97. It is also below Sonnet on red team (8 against 9) and malformed (7 against 8).
- **Wrong cards:** none in Flash fixed's 306 graded runs of cases, and none on any should-not-go-through case. On GAP-04 and GAP-06, a card was issued and tapped in some runs. These are known-gap cases (`choose_account`, `credit_to_own_number`), as on the other models.
- **Sonnet fixed against its 29 Oct runs:** no regression. It passes 97 of 102, and the failed set (GAP-04, GAP-06, ADD-23, ADD-24, ADD-30) is the same as in its 29 Oct runs 2 and 3; run 1 also failed ADD-20. It meets every target. Message to card is 1.79 s against 1.76 s, and the cost is 0.185 USD for the run against 0.177 to 0.187. The guard changed nothing on Sonnet that the run can show, because Sonnet already returned the written unit on ADD-13.
- **Cost and time:** temperature 0 and the guard did not change Flash's cost (0.0084 against 0.0085 USD a run) or its speed (1.09 against 1.02 s message to card, 1.6 s time per case). Flash fixed costs about one twenty-second of Sonnet per run. The three Flash fixed runs took about ten minutes in all (17:19 to 17:29 UTC) and the Sonnet run about four and a half (17:29 to 17:34 UTC).

## D13 as amended

The rule is the fewest wrong cards on the dev set; on a tie, the model that meets every target; then the cheaper one. Applied to the four columns:

- **Wrong cards:** Sonnet 0 in 306 runs of cases (29 Oct) and 0 in 102 (Sonnet fixed); Flash 0 in 306 under the measure, though it built the ADD-13 card, which the measure does not count; Flash fixed 0 in 306. Sonnet, Sonnet fixed and Flash fixed tie at none.
- **Every target met:** Sonnet, Sonnet fixed and Flash fixed all meet every target at the median. Flash does not (two misses), and it would drop out here even without the tie.
- **Cheaper:** Flash fixed, at 0.0084 USD a run against 0.180 to 0.185 USD for Sonnet.

On these figures, the rule as worded selects Flash fixed (DeepSeek V4.1 Flash at temperature 0 with the dinar-unit guard). The rule does not weigh other figures, and they are listed here so the choice can take them into account:

- Sonnet passes more cases: 97 against Flash fixed's 95 at the median.
- Sonnet flips fewer cases: 1 against 4.
- Flash fixed meets "asks when it must" by the smallest margin in one run (95 %).
- The Sonnet fixed column is a single run.
- Flash runs through OpenRouter with `data_collection` `deny`, and its open-weight release could be hosted by Zain.

The D13 choice is Khalil's. The default candidate in both `appsettings.json` files stays `sonnet` until he decides.
