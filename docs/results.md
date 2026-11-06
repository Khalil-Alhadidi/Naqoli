# Results

What the agent scores on the development set, on which model, and how the choice was made. Every figure below is copied from a committed report in [reports/](reports/) and links to it; nothing is re-graded here. The held-out result is added after milestone 11 (last section).

## What was measured and how

- **The set.** The development set, `data/requests.dev.jsonl`: 102 cases in eight groups (Clean 32, Ambiguous 21, Should not go through 16, Malformed 8, Compound 5, Red team 9, Fault injection 8, Known gap 3), each with an answer key. `Agent.Eval` plays every case through the same conversation code as the phone, against the mock wallet, and grades it against its key ([agent-eval.md](agent-eval.md)).
- **Three runs, medians.** Each model ran the set three times (`--repeat 3`). A model's figure is the median of the three runs; each aggregate report has the minimum and maximum too. A case that passes in some runs and fails in others is a flipped case.
- **The targets.** Every report grades the targets from the brief: no execution without a tap, no double payment under injected faults, wrong cards at most 2 %, asks when it must at least 95 %, asks when it need not at most 10 %, no card on should-not-go-through requests 100 % with a reason at least 90 %, message to card at most 3 s, cost per completed request reported ([targets.md](targets.md), with how each is computed).
- **The oracle.** The scripted parser replays the answer keys' own parses, so it checks the code without a model: 102 of 102 on the frozen code ([dev-scripted-freeze3-2026-11-06-0853](reports/dev-scripted-freeze3-2026-11-06-0853.md)).

## The models

One row per model, the newest three-run measurement of each. "Every target met" is at the median. "Cost per completed request" is the median of the three runs. Rows on an older prompt version are as measured then: the prompt, the answer keys of five cases (re-keyed on 4 Nov 2026) and the guards changed since, so those rows are not comparable with the first four beyond showing where each model stood.

| Model | Passed of 102 (median) | Wrong cards (three runs) | Every target met | Cost per completed request | Open weights | Prompt version of the run | Report |
|---|---|---|---|---|---|---|---|
| DeepSeek V4.1 Flash with reasoning (`deepseek-reasoning`, the frozen model) | 102 | 0 | Yes | 0.00035 USD | Yes (MIT) | 8, the frozen code (three guards, fraction words read in code, provider pinned with Venice first) | [freeze, 6 Nov 2026](reports/dev-compatible-deepseek-reasoning-freeze4-repeat3-2026-11-06-0959.md) |
| Claude Sonnet 5.5 (`sonnet`) | 102 | 0 | Yes | 0.0019 USD | No | 8, the frozen code | [freeze, 6 Nov 2026](reports/dev-claude-sonnet-freeze3-repeat3-2026-11-06-0920.md) |
| DeepSeek V4.1 Flash, reasoning off (`deepseek`) | 98 | 0 | Yes | 0.000098 USD | Yes (MIT) | 8, before the reason guard, provider not pinned | [prompt 8](reports/dev-compatible-deepseek-p8-repeat3-2026-11-05-0731.md) |
| Claude Haiku 5.5 (`haiku`) | 97 | 0 | Yes | 0.00012 USD | No | 8, before the reason guard | [prompt 8](reports/dev-claude-haiku-p8-repeat3-2026-11-05-0707.md) |
| DeepSeek V4 Pro (`deepseek-pro`) | 92 | 0 | No: message to card 3.25 s | 0.00096 USD | Yes (MIT) | 6 (older; not comparable) | [open-weight comparison](reports/model-comparison-2026-11-04.md) |
| Qwen3.8 27B (`qwen`) | 90 | 1 of 306 | No: asks when it must 90 %, reason given 87.5 % | 0.00034 USD | Yes (Apache 2.0) | 6 (older; not comparable) | [open-weight comparison](reports/model-comparison-2026-11-04.md) |
| Gemini 3.5 Flash-Lite (`gemini`, free tier) | 81 | 0 | No: asks when it must 85 %, asks when it need not 13.3 %, message to card 5.97 s | 0 (free tier) | No | 6 (older; not comparable) | [29 Oct comparison](reports/model-comparison-2026-10-29.md) |
| Gemma 4 26B (`gemma`, free tier) | 66 | 1 of 306 | No: four missed | 0 (free tier) | Yes (Gemma Terms of Use) | 6 (older; not comparable) | [1 Nov comparison](reports/model-comparison-2026-11-01.md) |

Prompt 8 figures for Flash and Haiku are from [model-comparison-2026-11-05-prompt-8.md](reports/model-comparison-2026-11-05-prompt-8.md), whose cost figures are priced at the routing of that day; the frozen row is priced at Venice's fp8 prices (0.30 / 1.20 USD per million input / output tokens).

## D13 and the choice

D13 ([architecture.md](architecture.md)), as amended on 3 Nov 2026: the fewest wrong cards on the development set wins; on a tie, the model that meets every target; then the cheaper one.

- **3 Nov 2026, milestone 10:** Sonnet, on prompt version 6: Haiku and Sonnet had no wrong card, and Sonnet met every target where Haiku missed "asks when it must" ([model-comparison-2026-11-01.md](reports/model-comparison-2026-11-01.md)).
- **5 Nov 2026, before the held-out run:** Khalil reopened the choice (he had not submitted, his rulings of 4 Nov 2026 had changed the prompt, and an open-weight model can be hosted by the wallet operator itself) and chose DeepSeek V4.1 Flash with reasoning. On prompt 8 the four leading candidates tie on wrong cards (none) and meet every target at the median, so the rule as worded selects plain Flash on price. Khalil took the reasoning variant for one flipped case against ten and no card on a should-not-go-through case in any run, with open weights Zain can host itself and the same result as Sonnet at a fraction of the cost ([model-comparison-2026-11-05-prompt-8.md](reports/model-comparison-2026-11-05-prompt-8.md); [disclosure.md](disclosure.md), "Freeze before the held-out run").

## What each round fixed

- **1 Nov 2026, the prompt 6 baseline:** Sonnet 97, Haiku 93, Gemma 66 of 102 at the median, no wrong card on the Anthropic models ([model-comparison-2026-11-01.md](reports/model-comparison-2026-11-01.md)).
- **4 Nov 2026, the open-weight comparison:** DeepSeek V4.1 Flash 92, DeepSeek V4 Pro 92, Qwen3.8 27B 90; Flash built a 500,000 IQD card on ADD-13 ("500 dinars") in every run ([model-comparison-2026-11-04.md](reports/model-comparison-2026-11-04.md)).
- **4 Nov 2026, the Flash round:** temperature 0 and the dinar-unit guard; Flash 95, ADD-13 refused in every run, every target met; Sonnet 97 in one rerun ([model-comparison-2026-11-04-flash-round.md](reports/model-comparison-2026-11-04-flash-round.md)).
- **4 Nov 2026, prompt 7:** Khalil's rulings (English and Latin-letter Arabic refused, `undo_payment`, own-number credit a purchase), five cases re-keyed, the other-balance guard, the reasoning variant added; Haiku 98, Sonnet 100, Flash 100, Flash with reasoning 100 ([model-comparison-2026-11-04-prompt-7.md](reports/model-comparison-2026-11-04-prompt-7.md)).
- **5 Nov 2026, prompt 8:** the possessive word, compound amounts, "all my money", the bill-label tolerance; Haiku 97, Sonnet 101, Flash 98, Flash with reasoning 101, no wrong card in any run ([model-comparison-2026-11-05-prompt-8.md](reports/model-comparison-2026-11-05-prompt-8.md)).
- **5 Nov 2026, the freeze runs:** the reason guard and the provider pinned to fp8 (DeepInfra first); the oracle 102 of 102, Flash with reasoning 101 in every run with no flipped case, Sonnet 101 in every run with no flipped case; both fail only ADD-30 ([Flash with reasoning](reports/dev-compatible-deepseek-reasoning-freeze-repeat3-2026-11-05-0953.md), [Sonnet](reports/dev-claude-sonnet-freeze-repeat3-2026-11-05-1010.md), [oracle](reports/dev-scripted-freeze-2026-11-05-0952.md)).
- **6 Nov 2026, the fraction words in code:** the reader of a typed amount (`AmountText`) reads Khalil's words for a quarter, a half and "less", so the typed answer of ADD-30 no longer goes to the model; the prompt is unchanged. The oracle 102 of 102; Flash with reasoning 102 in every run and Sonnet 102 in every run, no flipped case and no wrong card on either. Message to card at the median: Flash with reasoning 2.58 s (1.90 s on 5 Nov; the change does not touch the path from a first message to its card, so the difference is taken to be the endpoint's speed on the day; the runner does not record it per provider), Sonnet 1.80 s; three runs cost 0.0407 USD and 0.5585 USD ([Flash with reasoning](reports/dev-compatible-deepseek-reasoning-freeze2-repeat3-2026-11-06-0746.md), [Sonnet](reports/dev-claude-sonnet-freeze2-repeat3-2026-11-06-0805.md), [oracle](reports/dev-scripted-freeze2-2026-11-06-0744.md)).
- **6 Nov 2026, the review before the freeze:** the security review found two things, both fixed ([the review](history/reviews/milestone-11-prefreeze-review.md)): three typed amounts that can be read two ways are no longer read in code, and the reason guard can no longer turn a request that was not a transfer into a transfer card. The oracle 102 of 102; Flash with reasoning 102 in every run and Sonnet 102 in every run, no flipped case, no wrong card. Flash with reasoning missed the time target in these runs: message to card 3.33 s at the median on DeepInfra's endpoint, where it was 1.90 s the day before and 2.58 s that morning; Sonnet 1.72 s ([Flash with reasoning](reports/dev-compatible-deepseek-reasoning-freeze3-repeat3-2026-11-06-0855.md), [Sonnet](reports/dev-claude-sonnet-freeze3-repeat3-2026-11-06-0920.md), [oracle](reports/dev-scripted-freeze3-2026-11-06-0853.md)).
- **6 Nov 2026, the provider order:** ten timed parse calls on each fp8 endpoint gave a median of 1.7 s on Venice, 2.7 s on DeepInfra (2.6 s on its priority tier), 2.6 s on InferenceNet and 3.8 s on Morph, so the frozen candidate is served by Venice's endpoint first and DeepInfra's second: the same model at the same precision, data collection denied, at Venice's higher price. Flash with reasoning on that order: 102 in every run, no flipped case, no wrong card, every target met, message to card 1.95 s, 0.00035 USD per completed request, 0.1145 USD for the three runs ([Flash with reasoning](reports/dev-compatible-deepseek-reasoning-freeze4-repeat3-2026-11-06-0959.md)). These are the frozen model's figures at the tag.

## The residual cases

- **ADD-30, the typed quarter:** failed on both leaders in every run of 5 Nov 2026 (`cards[0]`, no card), because the typed answer went to the model and the model called it unclear. Fixed in code on 6 Nov 2026 (the fraction words in `AmountText`); it passes on both in every run since. No case fails on either leader now.
- **GAP-04, choosing the account to pay from:** on prompt 8 Haiku failed it in two runs and Flash with reasoning in one, with a plain card and no note. The reason guard (5 Nov 2026) now turns a request that carries `choose_account` into the noted card whatever intent the model gave ([agent-model.md](agent-model.md), "The reason guard"); it passes in every freeze run.
- **ADD-04, part of a bill:** on prompt 8 Haiku failed it in every run and plain Flash in one: the learning row `part_of_bill` is missing when the model returns a plain bill with an amount. The code still refuses `bills_paid_in_full` and builds no card. Not the frozen model; not changed.

## Measurement notes

These are written in the comparison reports and apply to every table here:

- **The wrong-card measure counts only cases that expect a card.** A card issued where the key expects none is not a wrong card; it shows as a miss of "no card on should-not-go-through requests" or as a failed case (`card unexpected`). Read the two together ([failure-modes.md](failure-modes.md); [targets.md](targets.md)).
- **Flipped cases are not counted by D13.** The rule weighs wrong cards, targets and cost; a model that flips many cases between runs can still win it, so the flipped count is listed beside it.
- **"Asks when it need not" counts 31 cases while Clean has 32:** the grader leaves out CLN-25, whose key expects the `bill_or_store` question.
- **Message to card depends on who serves the model.** For a hosted open-weight model the time is mostly the provider's: the same code and model measured 1.90 s, 2.58 s and 3.33 s on DeepInfra within a day, and 1.95 s on Venice. A wallet operator hosting the weights itself would set this by its own hardware. The eval runner does not record which provider served each call; the candidate's `RequestFields` in each report's header give the order asked for.
- **Three runs on the development set only.** No figure here comes from the held-out set.

## Held-out set

The held-out set (62 cases, sealed on 15 Oct 2026, [HELDOUT_SEAL.md](HELDOUT_SEAL.md)) runs once, on the frozen model, in milestone 11. Its figures will be added here unchanged.
