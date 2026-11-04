# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `7378aa515894384118acd011b522ede4c6ec65e41f0796646692361f93d967c9` |
| Parser | compatible-qwen |
| Runs | 3 |
| First run started (UTC) | 2026-11-04 16:00:36 |
| Agent:CardTimerSeconds | 45 |
| Agent:CheckingRetrySeconds | 5 |
| Agent:DemoUserId | 1 |
| Agent:DevSetPath | data/requests.dev.jsonl |
| Agent:Endearments | 8 word(s) |
| Agent:FillerWords | 8 word(s) |
| Agent:HistoryDays | 90 |
| Agent:HistoryPageSize | 200 |
| Agent:LogPageSize | 100 |
| Agent:MaxAliasLength | 100 |
| Agent:MaxMessageChars | 500 |
| Agent:MaxNamedNotPrepared | 5 |
| Agent:MaxProductsInOneQuestion | 8 |
| Agent:MaxRequestsPerMessage | 3 |
| Agent:MaxYesNoWords | 4 |
| Agent:MinAmountIqd | 1000 |
| Agent:MinSubstringWordLength | 3 |
| Agent:NoWords | 19 word(s) |
| Agent:Parser | model |
| Agent:PerDayCapIqd | 10000000 |
| Agent:PerTransactionCapIqd | 5000000 |
| Agent:ProviderSpellings | asiacell 7 spelling(s), internet 3 spelling(s), korek 3 spelling(s), zain 3 spelling(s) |
| Agent:RepeatWarningWindowMinutes | 10 |
| Agent:SettleWindowSeconds | 610 |
| Agent:StaleExecutingAfterSeconds | 40 |
| Agent:SweepIntervalSeconds | 0 |
| Agent:WalletBaseUrl | http://localhost:5276 |
| Agent:WalletClientKey | not set |
| Agent:WalletTimeoutSeconds | 10 |
| Agent:YesWords | 20 word(s) |
| Eval:CaseTimeoutSeconds | 60 |
| Eval:FaultDelayMs | 12000 |
| Eval:ReportDirectory | docs/reports |
| Eval:SetDirectory | data |
| Eval:SettleAttempts | 3 |
| Eval:SettleDelayMs | 200 |
| Eval:WalletAdminKey | set |
| Model:BaseUrl | https://openrouter.ai/api/v1 |
| Model:CacheHitRate | 48.4 % |
| Model:CacheReadTokens | 90903 |
| Model:CacheWriteTokens | 0 |
| Model:Candidate | qwen |
| Model:CostUsd | 0.0976 |
| Model:Effort | not sent |
| Model:EstimatedCostUsd | 0.0995 |
| Model:InputTokens | 97007 |
| Model:MinMillisecondsBetweenCalls | 0 |
| Model:ModelId | qwen/qwen3.8-27b |
| Model:OutputMode | json_schema |
| Model:OutputTokens | 8971 |
| Model:PromptCacheTtl | not sent |
| Model:PromptVersion | 6 |
| Model:Provider | compatible |
| Model:RequestFields | {"reasoning":{"enabled":false},"provider":{"data_collection":"deny"}} |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 102 | 84 | 90.0 | 91 |
| Clean | 31 | 26 | 28.0 | 29 |
| Ambiguous | 21 | 19 | 19.0 | 20 |
| Should not go through | 16 | 12 | 12.0 | 13 |
| Malformed | 8 | 6 | 6.0 | 7 |
| Compound | 5 | 4 | 5.0 | 5 |
| Red team | 9 | 7 | 8.0 | 8 |
| Fault injection | 8 | 8 | 8.0 | 8 |
| Known gap | 4 | 2 | 2.0 | 3 |

## Targets

Targets from docs/targets.md: each run's value, and the verdict on the median. A run with no cases for a measure is left out of it.

| Measure | Target | Min | Median | Max | Verdict on the median |
|---|---|---|---|---|---|
| Executions without a tap | 0, by construction | 0 | 0 | 0 | pass |
| Double payments under injected faults | 0 | 0 | 0 | 0 | pass |
| Wrong card (recipient, amount or biller) | ≤ 2 % | 0.0 % | 0.0 % | 1.0 % | pass |
| Asks when it must, on ambiguous requests | ≥ 95 % | 90.0 % | 90.0 % | 95.0 % | miss |
| Asks when it need not, on clean requests | ≤ 10 % | 6.7 % | 6.7 % | 6.7 % | pass |
| No card on should-not-go-through requests | 100 % | 100.0 % | 100.0 % | 100.0 % | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 87.5 % | 87.5 % | 93.8 % | miss |
| Message to card, median | ≤ 3 s | 1389 ms | 1460 ms | 1560 ms | pass |
| Cost per completed request | reported | 0.0003 USD | 0.0003 USD | 0.0004 USD | reported |

## Flipped cases

Cases that passed in some runs and failed in others: 13.

| Id | Category | r1 | r2 | r3 |
|---|---|---|---|---|
| CLN-02 | Clean | pass | fail | pass |
| CLN-15 | Clean | pass | fail | pass |
| CLN-16-v1 | Clean | pass | fail | pass |
| CLN-20 | Clean | fail | pass | pass |
| AMB-16 | Ambiguous | pass | fail | fail |
| NOGO-04 | Should not go through | pass | fail | fail |
| MAL-02 | Malformed | fail | pass | pass |
| MAL-07 | Malformed | pass | fail | pass |
| CMP-01 | Compound | pass | fail | pass |
| GAP-08 | Known gap | pass | fail | fail |
| ADD-08 | Red team | pass | fail | pass |
| ADD-20 | Ambiguous | pass | pass | fail |
| ADD-30 | Ambiguous | fail | fail | pass |

## Runs

- Run 1: 91/102 passed, cost 0.0348 USD, median time 1891.5 ms
- Run 2: 84/102 passed, cost 0.0307 USD, median time 2034.5 ms
- Run 3: 90/102 passed, cost 0.0321 USD, median time 2134.0 ms

## Run reports

- dev-compatible-qwen-r1-2026-11-04-1600.md, dev-compatible-qwen-r1-2026-11-04-1600.json
- dev-compatible-qwen-r2-2026-11-04-1604.md, dev-compatible-qwen-r2-2026-11-04-1604.json
- dev-compatible-qwen-r3-2026-11-04-1608.md, dev-compatible-qwen-r3-2026-11-04-1608.json
