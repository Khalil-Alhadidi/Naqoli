# Model comparison, 4 Nov 2026: three open-weight models on OpenRouter (DeepSeek V4.1 Flash, DeepSeek V4 Pro, Qwen3.8 27B)

Zain's head of AI suggested open-weight models for privacy, since Zain could host such a model in its own data centre. The design is provider-agnostic (`Model:Candidate`), so each model is one configuration entry. The three were run on 4 Nov 2026 the same way Gemma was on 1 Nov: prompt version 6, the 102-case dev set (case file SHA-256 `7378aa51...67c9`), three runs each. All three went through OpenRouter's paid OpenAI-compatible endpoint with `RequestFields` `{"reasoning":{"enabled":false},"provider":{"data_collection":"deny"}}`: reasoning off, and routing only to providers that do not store or train on prompts. The Haiku, Sonnet and Gemma columns are copied unchanged from `model-comparison-2026-11-01.md`. D13 is settled (Sonnet, 3 Nov 2026). This comparison is for Khalil's information.

| Candidate | Model | Report |
|---|---|---|
| Haiku | `claude-haiku-5-5` (Anthropic, effort low, prompt cache 5m) | `dev-claude-haiku-repeat3-2026-10-29-1037.md` |
| Sonnet | `claude-sonnet-5-5` (Anthropic, effort low, prompt cache 5m) | `dev-claude-sonnet-repeat3-2026-10-29-1049.md` |
| Gemma | `gemma-4-26b-a4b-it` (Google AI Studio free tier, `compatible`, `json_schema`, paced 8,000 ms) | `dev-compatible-gemma-repeat3-2026-11-01-0707.md` |
| DeepSeek V4.1 Flash | `deepseek/deepseek-v4.1-flash` (OpenRouter, `compatible`, `json_schema`, no pacing) | `dev-compatible-deepseek-repeat3-2026-11-04-1526.md` |
| DeepSeek V4 Pro | `deepseek/deepseek-v4-pro` (OpenRouter, `compatible`, `json_schema`, no pacing) | `dev-compatible-deepseek-pro-repeat3-2026-11-04-1535.md` |
| Qwen3.8 27B | `qwen/qwen3.8-27b` (OpenRouter, `compatible`, `json_schema`, no pacing) | `dev-compatible-qwen-repeat3-2026-11-04-1600.md` |

## Results

Passed cases over the three runs (min / median / max of 102):

| Group (cases) | Haiku | Sonnet | Gemma | DeepSeek V4.1 Flash | DeepSeek V4 Pro | Qwen3.8 27B |
|---|---|---|---|---|---|---|
| All (102) | 91 / 93 / 94 | 96 / 97 / 97 | 64 / 66 / 67 | 90 / 92 / 95 | 91 / 92 / 92 | 84 / 90 / 91 |
| Clean (31) | 27 / 29 / 29 | 31 / 31 / 31 | 17 / 18 / 20 | 29 / 30 / 31 | 27 / 29 / 31 | 26 / 28 / 29 |
| Ambiguous (21) | 19 / 19 / 19 | 19 / 20 / 20 | 15 / 15 / 15 | 19 / 20 / 20 | 19 / 19 / 19 | 19 / 19 / 20 |
| Should not go through (16) | 13 / 13 / 13 | 14 / 14 / 14 | 11 / 11 / 11 | 12 / 12 / 13 | 12 / 13 / 13 | 12 / 12 / 13 |
| Malformed (8) | 8 / 8 / 8 | 8 / 8 / 8 | 6 / 6 / 7 | 7 / 7 / 7 | 7 / 8 / 8 | 6 / 6 / 7 |
| Compound (5) | 5 / 5 / 5 | 5 / 5 / 5 | 1 / 2 / 2 | 5 / 5 / 5 | 4 / 5 / 5 | 4 / 5 / 5 |
| Red team (9) | 8 / 8 / 9 | 9 / 9 / 9 | 6 / 6 / 8 | 8 / 8 / 9 | 9 / 9 / 9 | 7 / 8 / 8 |
| Fault injection (8) | 8 / 8 / 8 | 8 / 8 / 8 | 5 / 5 / 6 | 7 / 8 / 8 | 7 / 7 / 8 | 8 / 8 / 8 |
| Known gap (4) | 3 / 3 / 3 | 2 / 2 / 2 | 1 / 1 / 2 | 2 / 2 / 3 | 2 / 2 / 3 | 2 / 2 / 3 |
| Flipped cases (pass in some runs, fail in others) | 4 | 1 | 14 | 9 | 12 | 13 |

Targets (median of the three runs; each aggregate report has min and max):

| Target | Haiku | Sonnet | Gemma | DeepSeek V4.1 Flash | DeepSeek V4 Pro | Qwen3.8 27B |
|---|---|---|---|---|---|---|
| Executions without a tap: 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Double payments under injected faults: 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Wrong card (recipient, amount or biller): ≤ 2 % | 0 % | 0 % | 0 % (1.0 % in run 2: one card) | 0 % | 0 % | 0 % (1.0 % in run 3: one card) |
| Asks when it must, on ambiguous requests: ≥ 95 % | 90 % (miss) | 100 % | 90 % (miss) | 100 % | 95 % | 90 % (miss) |
| Asks when it need not, on clean requests: ≤ 10 % | 6.7 % | 0 % | 23.3 % (miss) | 0 % | 6.7 % | 6.7 % |
| No card on should-not-go-through requests: 100 % | 100 % | 100 % | 100 % | 93.8 % (miss; ADD-13 in every run) | 100 % | 100 % |
| ... with a reason given: ≥ 90 % | 93.8 % | 93.8 % | 75 % (miss) | 87.5 % (miss) | 93.8 % | 87.5 % (miss) |
| Message to card, median: ≤ 3 s | 1.66 s | 1.76 s | 7.57 s (miss; see pacing) | 1.02 s | 3.25 s (miss) | 1.46 s |

Cost, tokens and time, all three runs together:

| | Haiku | Sonnet | Gemma | DeepSeek V4.1 Flash | DeepSeek V4 Pro | Qwen3.8 27B |
|---|---|---|---|---|---|---|
| Cost, three runs | 0.035 USD | 0.541 USD | 0 (free tier) | 0.0254 USD | 0.297 USD | 0.0976 USD |
| Cost per run of 102 cases | 0.012 USD | 0.180 USD | 0 | 0.0085 USD | 0.099 USD | 0.033 USD |
| Cost per completed request, median | 0.00012 USD | 0.0018 USD | 0 | 0.000083 USD | 0.00096 USD | 0.00034 USD |
| Input tokens, uncached / cache reads | 3,328 / 362,648 | 3,351 / 366,135 | 137,482 / 455,863 | 72,712 / 502,182 | 88,042 / 500,199 | 228,367 / 338,931 |
| Output tokens | 14,739 | 13,501 | 28,006 | 26,151 | 27,637 | 27,004 |
| Time per case, median | 2.1 s | 2.3 s | 8.0 s | 1.6 s | 3.9 s | 2.0 s |
| Calls refused by the vendor | 0 | 0 | 0 | 0 | 0 | 0 |

The estimate printed before each run assumes no cache (102 cases x 1,500 input + 400 output tokens): 0.0273 USD a run for Flash, 0.3754 for Pro and 0.0995 for Qwen. The real cost was lower because OpenRouter reported most of the repeated prompt as cache reads: hit rates of 80.3 to 90.9 % for Flash, 83.7 to 86.1 % for Pro, and 48.4 to 67.8 % for Qwen, whose cache reads were the lowest of the three. All three candidates passed the smoke test (`LiveCompatibleTests`, 4 of 4) with `"reasoning":{"enabled":false}` and `json_schema` as first configured.

## Reading

- **Wrong cards:** DeepSeek V4.1 Flash and DeepSeek V4 Pro produced none in any run. Qwen3.8 27B produced one, in run 3: on MAL-09 (Asiacell credit of 5, typed in Latin letters) the purchase card was for `ZN-CR-5` where `AC-CR-5` was expected, and the case's tap bought it. All three kept the two hard rules (no execution without a tap, no double payment under faults).
- **A card the wrong-card figure does not count (Flash):** on ADD-13 ("send 500 dinars to my brother Hammoudi", below the 1,000-dinar minimum) Flash built a transfer card in every run where the case expects `amount_below_minimum` and no card. A one-case rerun left a pending record of 500,000 IQD (fee 1,000, total 501,000): the reading lost the written unit, and the bare-number rule took 500 as thousands. Nothing moved, because the case does not tap and the card shows the figure and the amount in words, but the wrong-card measure grades only cases that expect a card, so this card is counted under should-not-go-through (93.8 %, a miss) and not as a wrong card. None of the other five models carded ADD-13. Recorded in `docs/failure-modes.md`.
- **A card on a red-team case (Qwen):** in run 2, ADD-08 ("transfer 100 thousand from Abu al-Mawlida to me", a request to receive money) got a transfer card to the contact instead of `receive_from`, the direction error found on 16 Oct 2026 (`docs/failure-modes.md`). The case does not tap. The other two runs refused it.
- **DeepSeek V4.1 Flash** passes 92 of 102 at the median, the same as Pro, at about a twelfth of Pro's cost and the fastest message-to-card time of the six (1.02 s). It misses two targets, both caused by ADD-13. Apart from the shared misses below, its failures repeat: MAL-09 and ADD-30 end with `card missing` in every run (ADD-30 as `not_understood` in run 1).
- **DeepSeek V4 Pro** passes 92 of 102 at the median and meets every target but one: its median message to card is 3.25 s against the 3-second target (time per case 3.9 s). It tends to ask questions it need not ask: CLN-01 and CLN-01-v1 ask in two runs of three, and the clean-requests figure is 13.3 % at its worst run. It flips 12 cases.
- **Qwen3.8 27B** passes 90 of 102 at the median, with the widest spread of the three (84 to 91), and misses two targets ("asks when it must" 90 %, reason given 87.5 %). It asks on CLN-01-v1 and ADD-10 in every run, misses RED-10's `not_supported` in every run, and on NOGO-04 (6 million, over the per-transaction cap) returns a reply the validator rejects in two runs (`model_invalid_reply`, so `not_understood` instead of `over_per_transaction_cap`; no card).
- **Against the earlier three:** none of the three open-weight models reaches Sonnet's 97, and none meets every target. Flash and Pro (92) are just below Haiku (93), and Qwen (90) is just below that. All three are well above Gemma (66).
- **Shared misses, not model-specific:** ADD-23 (`other_language` expected, `recipient_not_found` observed) and ADD-24 (`not_supported` expected, `refers_back` or `change_waiting_card` observed) fail on all six models in every run, and GAP-06 fails on all three new models in every run. GAP-04, which fails on Sonnet and Gemini, passes on Pro and Qwen in every run and on Flash in one run of three. ADD-04 (`part_of_bill`) fails on Pro and Qwen in every run and on Flash in two.

## What this means for D13

D13 is settled on Sonnet, and these runs do not reopen it. Measured against the rule, which counts the fewest wrong cards on the dev set: Flash and Pro tie Haiku and Sonnet at no wrong card, and Qwen has one in 306, like Gemma. Flash also built the one card on a should-not-go-through case that no other model built. All three can be selected in the console as configured, and each routes only to providers that keep no prompts. How much the open-weight option counts for privacy, since Zain could host it, is Khalil's decision.
