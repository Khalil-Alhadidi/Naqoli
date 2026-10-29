# Model comparison, 29 Oct 2026: Anthropic, Gemini, OpenRouter

The comparison D13 asks for (`docs/architecture.md`): the candidates play the development set through the real conversation and the mock wallet, three runs each, and the one with the fewest wrong cards wins; a close call goes to the cheaper model. Prompt version 6, dev set of 102 cases (the same case file for every run, SHA-256 in each report header). Run by `Agent.Eval dev --parser <provider>:<candidate> --repeat 3`; the reports are beside this file.

| Candidate | Model | Report |
|---|---|---|
| Haiku | `claude-haiku-5-5` (Anthropic, effort low, prompt cache 5m) | `dev-claude-haiku-repeat3-2026-10-29-1037.md` |
| Sonnet | `claude-sonnet-5-5` (Anthropic, effort low, prompt cache 5m) | `dev-claude-sonnet-repeat3-2026-10-29-1049.md` |
| Gemini | `gemini-3.5-flash-lite` (Google AI Studio free tier, `compatible`, `json_schema`, paced 6,500 ms) | `dev-compatible-gemini-repeat3-2026-10-29-1106.md` |
| OpenRouter | `recursive/flint-v1.0:free`, then `xingchen-agi/xing4.0-29b-a4b:free` | no run completed (see below) |

## Results

Passed cases over the three runs (min / median / max of 102):

| Group (cases) | Haiku | Sonnet | Gemini |
|---|---|---|---|
| All (102) | 91 / 93 / 94 | 96 / 97 / 97 | 80 / 81 / 86 |
| Clean (31) | 27 / 29 / 29 | 31 / 31 / 31 | 22 / 23 / 26 |
| Ambiguous (21) | 19 / 19 / 19 | 19 / 20 / 20 | 15 / 16 / 17 |
| Should not go through (16) | 13 / 13 / 13 | 14 / 14 / 14 | 13 / 14 / 14 |
| Malformed (8) | 8 / 8 / 8 | 8 / 8 / 8 | 7 / 8 / 8 |
| Compound (5) | 5 / 5 / 5 | 5 / 5 / 5 | 2 / 4 / 5 |
| Red team (9) | 8 / 8 / 9 | 9 / 9 / 9 | 8 / 8 / 9 |
| Fault injection (8) | 8 / 8 / 8 | 8 / 8 / 8 | 7 / 7 / 8 |
| Known gap (4) | 3 / 3 / 3 | 2 / 2 / 2 | 2 / 2 / 3 |
| Flipped cases (pass in some runs, fail in others) | 4 | 1 | 20 |

Targets (median of the three runs; the report has min and max):

| Target | Haiku | Sonnet | Gemini |
|---|---|---|---|
| Executions without a tap: 0 | 0 | 0 | 0 |
| Double payments under injected faults: 0 | 0 | 0 | 0 |
| Wrong card (recipient, amount or biller): ≤ 2 % | 0 % | 0 % | 0 % |
| Asks when it must, on ambiguous requests: ≥ 95 % | 90 % (miss) | 100 % | 85 % (miss) |
| Asks when it need not, on clean requests: ≤ 10 % | 6.7 % | 0 % | 13.3 % (miss) |
| No card on should-not-go-through requests: 100 % | 100 % | 100 % | 100 % |
| ... with a reason given: ≥ 90 % | 93.8 % | 93.8 % | 93.8 % |
| Message to card, median: ≤ 3 s | 1.66 s | 1.76 s | 5.97 s (miss; see pacing) |

Cost and tokens, all three runs together:

| | Haiku | Sonnet | Gemini |
|---|---|---|---|
| Cost, three runs | 0.035 USD | 0.541 USD | 0 (free tier) |
| Cost per run of 102 cases | 0.012 USD | 0.180 USD | 0 |
| Cost per completed request, median | 0.00012 USD | 0.0018 USD | 0 |
| Input tokens, uncached / cache reads | 3,328 / 362,648 | 3,351 / 366,135 | 192,021 / 0 |
| Output tokens | 14,739 | 13,501 | 11,137 |
| Time per case, median (p90) | 2.1 s (3.3 s) | 2.3 s (3.4 s) | 6.5 s (12 s) |

The Gemini times carry the pacing: the candidate is paced at 6,500 ms between calls because its free tier allows a few requests a minute, and the parser counts the wait inside the call (`docs/agent-model.md`). Its raw answer time was about 2 s in the smoke test. Its 192 k input tokens are the whole prompt on every call: the compatible provider sends no cache control.

## Reading

- **Wrong cards:** none, for all three. On D13's first measure the three tie, and every candidate kept the two hard rules (no execution without a tap, no double payment under faults).
- **Sonnet** is the only candidate that meets every target. It misses the same five cases in every run, four of which every model misses (below), and flips one case.
- **Haiku** misses the "asks when it must" target (90 % against ≥ 95 %: AMB-24 and ADD-11 never ask) and flips four cases between runs. It costs a fifteenth of Sonnet per request.
- **Gemini** (flash-lite, free tier) passes 81 of 102 at the median, misses three targets, and flips 20 cases, so a single run of it says little. Its failures are of two kinds: it asks a question on a clean request (CLN-02-v1, CLN-20, ADD-19, ADD-21, ADD-22 in every run: `asks.count: expected 0, observed 1`), and it returns no card on a clean transfer (CLN-18, CLN-23, ADD-14, ADD-26, ADD-27 in some runs: `card missing`). Five calls in the three runs were `model_unavailable (timeout)` at the 30 s limit, and the first attempt on `gemini-3.5-flash` could not run at all: that model's free tier is 5 requests a minute and 20 a day (the 429 body names `GenerateRequestsPerDayPerProjectPerModel-FreeTier`, value 20), spent by the smoke test alone.
- **Shared misses, not model-specific:** GAP-06 (known gap), ADD-23 (`other_language` expected, `recipient_not_found` observed) and ADD-24 (`not_supported` expected, `refers_back` or `change_waiting_card` observed) fail on every model in every run; GAP-04 fails on Sonnet and Gemini. These are prompt or rule questions for the next prompt pass, not a reason to prefer a model.

## OpenRouter

No run completed, for reasons that are the vendor's, recorded here so they are not repeated.

1. `recursive/flint-v1.0:free` (Khalil's pick) is a reasoning model whose thinking cannot be switched off (`reasoning.enabled=false` is refused: "Reasoning is mandatory for this endpoint"). On the real parse prompt it thinks for 4,400 to 12,000+ tokens whatever the control: `effort` `low` or `minimal` and `max_tokens` 400 or 1,000 all ran to the 4,000-token cap (`truncated`), 30 to 80 s a parse. On a two-line toy prompt the same controls brought it to 180 tokens in 2 s, which is why the `RequestFields` setting was built first (`docs/agent-model.md`); the control does not hold on a 1,700-token prompt. 25 cases of a run went through before it was stopped; nothing moved and no card was wrong, the parses were simply cut off.
2. `xingchen-agi/xing4.0-29b-a4b:free` answered the real prompt in 5 s with no reasoning tokens and the right intent, and supports strict schemas. Its upstream pool was saturated when the run was tried: six of seven calls three seconds apart were refused (`temporarily rate-limited upstream`, `limit_source: upstream_provider_shared_pool`), and one came back as HTTP 200 with no choices and the error inside, which the OpenAI adapter turned into `ArgumentOutOfRangeException` (now mapped to `model_unavailable (empty_reply)`, `docs/failure-modes.md`).
3. The key's own allowance then ran out: OpenRouter's free models allow 20 requests a minute and 50 a day on an account that has bought fewer than 10 credits, 1,000 a day from 10 credits (openrouter.ai/docs/api-reference/limits). A dev run is about 125 calls, so on the current account no free OpenRouter model can finish one run, whatever its quality. The shipped `openrouter` entry names `xing4.0` for the next attempt.

## What this means for D13

With zero wrong cards everywhere, the choice is between Sonnet (every target met, 97 of 102, one flipped case, 0.0018 USD a request) and Haiku (one target missed, 93 of 102, four flipped cases, 0.00012 USD a request). The free tiers of Gemini and OpenRouter, as measured today, are not production candidates: their quotas (20 to 1,000 requests a day, 5 to 20 a minute, shared pools) would not carry a demo day, and Gemini's output is below Haiku's on the same prompt. Whether a paid Gemini tier or a credited OpenRouter account changes that is a question of money and a new run, not of this report. The decision is Khalil's.
