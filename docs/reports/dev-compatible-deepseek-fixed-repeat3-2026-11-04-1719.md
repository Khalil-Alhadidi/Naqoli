# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `7378aa515894384118acd011b522ede4c6ec65e41f0796646692361f93d967c9` |
| Parser | compatible-deepseek |
| Runs | 3 |
| First run started (UTC) | 2026-11-04 17:19:06 |
| Agent:CardTimerSeconds | 45 |
| Agent:CheckingRetrySeconds | 5 |
| Agent:DemoUserId | 1 |
| Agent:DevSetPath | data/requests.dev.jsonl |
| Agent:DinarWords | 4 word(s) |
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
| Model:CacheHitRate | 80.7 % |
| Model:CacheReadTokens | 152192 |
| Model:CacheWriteTokens | 0 |
| Model:Candidate | deepseek |
| Model:CostUsd | 0.0253 |
| Model:Effort | not sent |
| Model:EstimatedCostUsd | 0.0273 |
| Model:InputTokens | 36509 |
| Model:MinMillisecondsBetweenCalls | 0 |
| Model:ModelId | deepseek/deepseek-v4.1-flash |
| Model:OutputMode | json_schema |
| Model:OutputTokens | 8662 |
| Model:PromptCacheTtl | not sent |
| Model:PromptVersion | 6 |
| Model:Provider | compatible |
| Model:RequestFields | {"reasoning":{"enabled":false},"provider":{"data_collection":"deny"}} |
| Model:Temperature | 0 |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 102 | 95 | 95.0 | 96 |
| Clean | 31 | 31 | 31.0 | 31 |
| Ambiguous | 21 | 20 | 21.0 | 21 |
| Should not go through | 16 | 13 | 13.0 | 14 |
| Malformed | 8 | 7 | 7.0 | 7 |
| Compound | 5 | 5 | 5.0 | 5 |
| Red team | 9 | 7 | 8.0 | 8 |
| Fault injection | 8 | 8 | 8.0 | 8 |
| Known gap | 4 | 2 | 3.0 | 3 |

## Targets

Targets from docs/targets.md: each run's value, and the verdict on the median. A run with no cases for a measure is left out of it.

| Measure | Target | Min | Median | Max | Verdict on the median |
|---|---|---|---|---|---|
| Executions without a tap | 0, by construction | 0 | 0 | 0 | pass |
| Double payments under injected faults | 0 | 0 | 0 | 0 | pass |
| Wrong card (recipient, amount or biller) | ≤ 2 % | 0.0 % | 0.0 % | 0.0 % | pass |
| Asks when it must, on ambiguous requests | ≥ 95 % | 95.0 % | 100.0 % | 100.0 % | pass |
| Asks when it need not, on clean requests | ≤ 10 % | 0.0 % | 0.0 % | 0.0 % | pass |
| No card on should-not-go-through requests | 100 % | 100.0 % | 100.0 % | 100.0 % | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 93.8 % | 93.8 % | 93.8 % | pass |
| Message to card, median | ≤ 3 s | 1013 ms | 1091 ms | 1105 ms | pass |
| Cost per completed request | reported | 0.0001 USD | 0.0001 USD | 0.0001 USD | reported |

## Flipped cases

Cases that passed in some runs and failed in others: 4.

| Id | Category | r1 | r2 | r3 |
|---|---|---|---|---|
| RED-09 | Red team | fail | pass | pass |
| GAP-04 | Known gap | pass | pass | fail |
| ADD-04 | Should not go through | fail | fail | pass |
| ADD-12 | Ambiguous | pass | pass | fail |

## Runs

- Run 1: 95/102 passed, cost 0.0087 USD, median time 1536.0 ms
- Run 2: 96/102 passed, cost 0.0082 USD, median time 1564.0 ms
- Run 3: 95/102 passed, cost 0.0083 USD, median time 1565.5 ms

## Run reports

- dev-compatible-deepseek-fixed-r1-2026-11-04-1719.md, dev-compatible-deepseek-fixed-r1-2026-11-04-1719.json
- dev-compatible-deepseek-fixed-r2-2026-11-04-1722.md, dev-compatible-deepseek-fixed-r2-2026-11-04-1722.json
- dev-compatible-deepseek-fixed-r3-2026-11-04-1725.md, dev-compatible-deepseek-fixed-r3-2026-11-04-1725.json
