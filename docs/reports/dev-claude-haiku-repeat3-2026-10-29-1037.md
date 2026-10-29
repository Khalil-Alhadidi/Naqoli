# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `7378aa515894384118acd011b522ede4c6ec65e41f0796646692361f93d967c9` |
| Parser | claude-haiku |
| Runs | 3 |
| First run started (UTC) | 2026-10-29 10:37:09 |
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
| Agent:Parser | claude |
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
| Model:BaseUrl | not sent |
| Model:CacheHitRate | 98.2 % |
| Model:CacheReadTokens | 362648 |
| Model:CacheWriteTokens | 3487 |
| Model:Candidate | haiku |
| Model:CostUsd | 0.0347 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 0.0357 |
| Model:InputTokens | 3328 |
| Model:MinMillisecondsBetweenCalls | 0 |
| Model:ModelId | claude-haiku-5-5 |
| Model:OutputMode | not sent |
| Model:OutputTokens | 14739 |
| Model:PromptCacheTtl | 5m |
| Model:PromptVersion | 6 |
| Model:Provider | anthropic |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 102 | 91 | 93.0 | 94 |
| Clean | 31 | 27 | 29.0 | 29 |
| Ambiguous | 21 | 19 | 19.0 | 19 |
| Should not go through | 16 | 13 | 13.0 | 13 |
| Malformed | 8 | 8 | 8.0 | 8 |
| Compound | 5 | 5 | 5.0 | 5 |
| Red team | 9 | 8 | 8.0 | 9 |
| Fault injection | 8 | 8 | 8.0 | 8 |
| Known gap | 4 | 3 | 3.0 | 3 |

## Targets

Targets from docs/targets.md: each run's value, and the verdict on the median. A run with no cases for a measure is left out of it.

| Measure | Target | Min | Median | Max | Verdict on the median |
|---|---|---|---|---|---|
| Executions without a tap | 0, by construction | 0 | 0 | 0 | pass |
| Double payments under injected faults | 0 | 0 | 0 | 0 | pass |
| Wrong card (recipient, amount or biller) | ≤ 2 % | 0.0 % | 0.0 % | 0.0 % | pass |
| Asks when it must, on ambiguous requests | ≥ 95 % | 90.0 % | 90.0 % | 90.0 % | miss |
| Asks when it need not, on clean requests | ≤ 10 % | 6.7 % | 6.7 % | 13.3 % | pass |
| No card on should-not-go-through requests | 100 % | 100.0 % | 100.0 % | 100.0 % | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 93.8 % | 93.8 % | 93.8 % | pass |
| Message to card, median | ≤ 3 s | 1605 ms | 1664 ms | 1674 ms | pass |
| Cost per completed request | reported | 0.0001 USD | 0.0001 USD | 0.0001 USD | reported |

## Flipped cases

Cases that passed in some runs and failed in others: 4.

| Id | Category | r1 | r2 | r3 |
|---|---|---|---|---|
| CLN-18 | Clean | pass | pass | fail |
| CLN-23-v1 | Clean | pass | fail | fail |
| RED-10 | Red team | fail | pass | fail |
| ADD-22 | Clean | fail | pass | fail |

## Runs

- Run 1: 93/102 passed, cost 0.0118 USD, median time 2134.5 ms
- Run 2: 94/102 passed, cost 0.0116 USD, median time 2189.5 ms
- Run 3: 91/102 passed, cost 0.0113 USD, median time 2150.0 ms

## Run reports

- dev-claude-haiku-r1-2026-10-29-1037.md, dev-claude-haiku-r1-2026-10-29-1037.json
- dev-claude-haiku-r2-2026-10-29-1041.md, dev-claude-haiku-r2-2026-10-29-1041.json
- dev-claude-haiku-r3-2026-10-29-1045.md, dev-claude-haiku-r3-2026-10-29-1045.json
