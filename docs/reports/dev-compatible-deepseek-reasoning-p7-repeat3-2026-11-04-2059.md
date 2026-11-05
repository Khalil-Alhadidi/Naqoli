# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `7df4552c9de0fb5c6513750d83bab9cdd9fbea983663360b1eac2a48d1c37d91` |
| Parser | compatible-deepseek-reasoning |
| Runs | 3 |
| First run started (UTC) | 2026-11-04 20:59:53 |
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
| Model:CacheHitRate | 80.0 % |
| Model:CacheReadTokens | 180912 |
| Model:CacheWriteTokens | 0 |
| Model:Candidate | deepseek-reasoning |
| Model:CostUsd | 0.0658 |
| Model:Effort | not sent |
| Model:EstimatedCostUsd | 0.0273 |
| Model:InputTokens | 45108 |
| Model:MinMillisecondsBetweenCalls | 0 |
| Model:ModelId | deepseek/deepseek-v4.1-flash |
| Model:OutputMode | json_schema |
| Model:OutputTokens | 31813 |
| Model:PromptCacheTtl | not sent |
| Model:PromptVersion | 7 |
| Model:Provider | compatible |
| Model:RequestFields | {"reasoning":{"effort":"low"},"provider":{"data_collection":"deny"}} |
| Model:Temperature | 0 |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 102 | 100 | 100.0 | 101 |
| Clean | 32 | 32 | 32.0 | 32 |
| Ambiguous | 21 | 20 | 20.0 | 20 |
| Should not go through | 16 | 16 | 16.0 | 16 |
| Malformed | 8 | 7 | 7.0 | 8 |
| Compound | 5 | 5 | 5.0 | 5 |
| Red team | 9 | 9 | 9.0 | 9 |
| Fault injection | 8 | 8 | 8.0 | 8 |
| Known gap | 3 | 3 | 3.0 | 3 |

## Targets

Targets from docs/targets.md: each run's value, and the verdict on the median. A run with no cases for a measure is left out of it.

| Measure | Target | Min | Median | Max | Verdict on the median |
|---|---|---|---|---|---|
| Executions without a tap | 0, by construction | 0 | 0 | 0 | pass |
| Double payments under injected faults | 0 | 0 | 0 | 0 | pass |
| Wrong card (recipient, amount or biller) | ≤ 2 % | 0.0 % | 0.0 % | 0.0 % | pass |
| Asks when it must, on ambiguous requests | ≥ 95 % | 100.0 % | 100.0 % | 100.0 % | pass |
| Asks when it need not, on clean requests | ≤ 10 % | 0.0 % | 0.0 % | 0.0 % | pass |
| No card on should-not-go-through requests | 100 % | 100.0 % | 100.0 % | 100.0 % | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 100.0 % | 100.0 % | 100.0 % | pass |
| Message to card, median | ≤ 3 s | 1961 ms | 2008 ms | 2221 ms | pass |
| Cost per completed request | reported | 0.0002 USD | 0.0002 USD | 0.0002 USD | reported |

## Flipped cases

Cases that passed in some runs and failed in others: 1.

| Id | Category | r1 | r2 | r3 |
|---|---|---|---|---|
| MAL-07 | Malformed | fail | fail | pass |

## Runs

- Run 1: 100/102 passed, cost 0.0212 USD, median time 2732.0 ms
- Run 2: 100/102 passed, cost 0.0224 USD, median time 2440.5 ms
- Run 3: 101/102 passed, cost 0.0222 USD, median time 2377.0 ms

## Run reports

- dev-compatible-deepseek-reasoning-p7-r1-2026-11-04-2059.md, dev-compatible-deepseek-reasoning-p7-r1-2026-11-04-2059.json
- dev-compatible-deepseek-reasoning-p7-r2-2026-11-04-2105.md, dev-compatible-deepseek-reasoning-p7-r2-2026-11-04-2105.json
- dev-compatible-deepseek-reasoning-p7-r3-2026-11-04-2111.md, dev-compatible-deepseek-reasoning-p7-r3-2026-11-04-2111.json
