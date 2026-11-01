# Model comparison, 1 Nov 2026: the three-model set (Haiku, Sonnet, Gemma 4)

Khalil asked for three models under D13 (`docs/architecture.md`: fewest wrong cards on the dev set, a close call to the cheaper model). The two Anthropic candidates come from `model-comparison-2026-10-29.md` (same prompt version 6, same 102-case dev set, same case file SHA-256 `7378aa51...67c9`). The third is Google's open Gemma 4, run on 1 Nov 2026 through the Google AI Studio free tier, the same endpoint and key as the `gemini` candidate. It replaces Gemini 3.5 Flash-Lite as the free third model because Flash-Lite scored 81 at the median with 20 flipped cases, and no OpenRouter free model can finish a run on an account without purchased credits (that report, section OpenRouter; checked again on 31 Oct 2026: the key's free allowance is 50 requests a day across every `:free` model, a run needs about 125).

| Candidate | Model | Report |
|---|---|---|
| Haiku | `claude-haiku-5-5` (Anthropic, effort low, prompt cache 5m) | `dev-claude-haiku-repeat3-2026-10-29-1037.md` |
| Sonnet | `claude-sonnet-5-5` (Anthropic, effort low, prompt cache 5m) | `dev-claude-sonnet-repeat3-2026-10-29-1049.md` |
| Gemma | `gemma-4-26b-a4b-it` (Google AI Studio free tier, `compatible`, `json_schema`, paced 8,000 ms) | `dev-compatible-gemma-repeat3-2026-11-01-0707.md` |

## Results

Passed cases over the three runs (min / median / max of 102):

| Group (cases) | Haiku | Sonnet | Gemma |
|---|---|---|---|
| All (102) | 91 / 93 / 94 | 96 / 97 / 97 | 64 / 66 / 67 |
| Clean (31) | 27 / 29 / 29 | 31 / 31 / 31 | 17 / 18 / 20 |
| Ambiguous (21) | 19 / 19 / 19 | 19 / 20 / 20 | 15 / 15 / 15 |
| Should not go through (16) | 13 / 13 / 13 | 14 / 14 / 14 | 11 / 11 / 11 |
| Malformed (8) | 8 / 8 / 8 | 8 / 8 / 8 | 6 / 6 / 7 |
| Compound (5) | 5 / 5 / 5 | 5 / 5 / 5 | 1 / 2 / 2 |
| Red team (9) | 8 / 8 / 9 | 9 / 9 / 9 | 6 / 6 / 8 |
| Fault injection (8) | 8 / 8 / 8 | 8 / 8 / 8 | 5 / 5 / 6 |
| Known gap (4) | 3 / 3 / 3 | 2 / 2 / 2 | 1 / 1 / 2 |
| Flipped cases (pass in some runs, fail in others) | 4 | 1 | 14 |

Targets (median of the three runs; each aggregate report has min and max):

| Target | Haiku | Sonnet | Gemma |
|---|---|---|---|
| Executions without a tap: 0 | 0 | 0 | 0 |
| Double payments under injected faults: 0 | 0 | 0 | 0 |
| Wrong card (recipient, amount or biller): ≤ 2 % | 0 % | 0 % | 0 % (1.0 % in run 2: one card) |
| Asks when it must, on ambiguous requests: ≥ 95 % | 90 % (miss) | 100 % | 90 % (miss) |
| Asks when it need not, on clean requests: ≤ 10 % | 6.7 % | 0 % | 23.3 % (miss) |
| No card on should-not-go-through requests: 100 % | 100 % | 100 % | 100 % |
| ... with a reason given: ≥ 90 % | 93.8 % | 93.8 % | 75 % (miss) |
| Message to card, median: ≤ 3 s | 1.66 s | 1.76 s | 7.57 s (miss; see pacing) |

Cost, tokens and time, all three runs together:

| | Haiku | Sonnet | Gemma |
|---|---|---|---|
| Cost, three runs | 0.035 USD | 0.541 USD | 0 (free tier) |
| Cost per run of 102 cases | 0.012 USD | 0.180 USD | 0 |
| Cost per completed request, median | 0.00012 USD | 0.0018 USD | 0 |
| Input tokens, uncached / cache reads | 3,328 / 362,648 | 3,351 / 366,135 | 137,482 / 455,863 |
| Output tokens | 14,739 | 13,501 | 28,006 |
| Time per case, median | 2.1 s | 2.3 s | 8.0 s |
| Calls refused by the vendor | 0 | 0 | 0 |

The Gemma times are the pacing: the free tier allows 16,000 input tokens a minute per model (the 429 body names `GenerateContentInputTokensPerModelPerMinute-FreeTier`, value 16,000, model `gemma-4-26b`), a parse call is about 1,900 tokens, so the candidate is paced at 8,000 ms and the parser counts the wait inside the call (`docs/agent-model.md`). Its raw answer time was about 4 s in the smoke test. A first attempt at 4,000 ms had 148 of about 375 calls refused and was discarded. AI Studio reports the repeated prompt as cached reads (76.8 % hit rate) although the compatible provider sends no cache control; whether cached tokens count toward the input-token quota was not tested, so the pace stays at 8 s.

## Reading

- **Wrong cards:** Haiku and Sonnet none in any run. Gemma one, in run 2: on CMP-01 (a transfer of 25,000 and a purchase in one message) it put a purchase card of 5,000 first, where the transfer card was expected. Over 306 case runs that is 0.3 %, within the 2 % target, but it is the only wrong card any model has produced on this prompt. All three kept the two hard rules (no execution without a tap, no double payment under faults).
- **Sonnet** remains the only candidate that meets every target (97 of 102, one flipped case).
- **Haiku** misses "asks when it must" (90 %) and flips four cases, at a fifteenth of Sonnet's cost.
- **Gemma 4 26B** passes 66 of 102 at the median, misses four targets and flips 14 cases. Its failures are of one dominant kind: 27 of the 35 failing cases of run 1 end with `card missing`, either after a question the case did not expect (CLN-01, CLN-20, ADD-06, ADD-10, ADD-13, ADD-15, ADD-16, ADD-26, ADD-28, ADD-29 in every run) or with no card and no question (ADD-01, ADD-14, ADD-27, AMB-03, CLN-03-v1, CMP-03, CMP-07 in every run). The smoke probe shows the likely cause: for "fifty thousand" the model returns `amount_value` 50000 together with `amount_unit` "thousand", so the amount the code computes is a thousand times too large, and the request is then refused or questioned by the amount checks rather than carded (ADD-21 expects `insufficient_balance` and gets `over_per_transaction_cap`). That is one systematic misreading of the prompt's amount rule, not a spread of errors; it is a question for the prompt pass, and the comparison is reported as run.
- **Shared misses, not model-specific:** GAP-04, GAP-06, ADD-23 (`other_language` expected, `recipient_not_found` observed) and ADD-24 (`not_supported` expected, `refers_back` observed) fail on every model in every run, as on 29 Oct.

## Free tiers, as measured

| Door | Limit found | Enough for a dev run? |
|---|---|---|
| Google AI Studio, `gemini-3.5-flash` | 5 requests a minute, 20 a day | No |
| Google AI Studio, `gemini-3.5-flash-lite` | none hit at 6,500 ms | Yes (81 of 102, 20 flipped) |
| Google AI Studio, `gemma-4-26b-a4b-it` | 16,000 input tokens a minute per model; no daily limit hit after about 650 calls on 1 Nov | Yes, at 8,000 ms |
| Google AI Studio, `gemma-4-31b-it` | 503 "high demand" on every try | No |
| OpenRouter, any `:free` model | 20 requests a minute, 50 a day per account (1,000 a day from 10 purchased credits); `nemotron-3.5-super-vl` answered the real prompt in 4.4 s with 633 reasoning tokens, `nemotron-3.5-lightning` and `flint` run to the output cap on reasoning, `inkling-small` is 403 outside approved coding agents, `xing4.0` and both Gemma endpoints were upstream rate-limited | No |

## What this means for D13

The wrong-card measure is 0 for Haiku and Sonnet and 1 card in 306 for Gemma. The free model is not close to the paid ones on this prompt: 66 against 93 and 97, four missed targets, and a pace the free tier forces that alone breaks the 3-second target. The choice stays between Sonnet (every target met, 0.0018 USD a request) and Haiku (one target missed, 0.00012 USD a request). Gemma is the free fallback the console can select, with the quota above. The decision is Khalil's.
