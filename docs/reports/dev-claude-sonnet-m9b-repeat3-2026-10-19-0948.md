# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `8f7d28ad3ceb28a075b6b46230ecf339d9d3641e366591bfce71c64bcf5a953b` |
| Parser | claude-sonnet |
| Runs | 3 |
| First run started (UTC) | 2026-10-19 09:48:51 |
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
| Agent:MaxProductsInOneQuestion | 8 |
| Agent:MaxRequestsPerMessage | 3 |
| Agent:MaxYesNoWords | 4 |
| Agent:MinAmountIqd | 1000 |
| Agent:MinSubstringWordLength | 3 |
| Agent:NoWords | 19 word(s) |
| Agent:Parser | claude |
| Agent:PerDayCapIqd | 10000000 |
| Agent:PerTransactionCapIqd | 5000000 |
| Agent:ProviderSpellings | asiacell 3 spelling(s), internet 3 spelling(s), korek 1 spelling(s), zain 1 spelling(s) |
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
| Model:CacheHitRate | 98.0 % |
| Model:CacheReadTokens | 311136 |
| Model:CacheWriteTokens | 3241 |
| Model:Candidate | sonnet |
| Model:CostUsd | 0.4966 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 0.6650 |
| Model:InputTokens | 3078 |
| Model:ModelId | claude-sonnet-5-5 |
| Model:OutputTokens | 12615 |
| Model:PromptVersion | 5 |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 95 | 87 | 88.0 | 88 |
| Clean | 28 | 27 | 28.0 | 28 |
| Ambiguous | 19 | 19 | 19.0 | 19 |
| Should not go through | 14 | 13 | 13.0 | 13 |
| Malformed | 8 | 8 | 8.0 | 8 |
| Compound | 5 | 0 | 0.0 | 0 |
| Red team | 9 | 9 | 9.0 | 9 |
| Fault injection | 8 | 8 | 8.0 | 8 |
| Known gap | 4 | 3 | 3.0 | 3 |

## Targets

Targets from docs/targets.md: each run's value, and the verdict on the median. A run with no cases for a measure is left out of it.

| Measure | Target | Min | Median | Max | Verdict on the median |
|---|---|---|---|---|---|
| Executions without a tap | 0, by construction | 0 | 0 | 0 | pass |
| Double payments under injected faults | 0 | 0 | 0 | 0 | pass |
| Wrong card (recipient, amount or biller) | ≤ 2 % | 0.0 % | 0.0 % | 0.0 % | pass |
| Asks when it must, on ambiguous requests | ≥ 95 % | 100.0 % | 100.0 % | 100.0 % | pass |
| Asks when it need not, on clean requests | ≤ 10 % | 0.0 % | 0.0 % | 3.7 % | pass |
| No card on should-not-go-through requests | 100 % | 100.0 % | 100.0 % | 100.0 % | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 92.9 % | 92.9 % | 92.9 % | pass |
| Message to card, median | ≤ 3 s | 1804 ms | 1868 ms | 1912 ms | pass |
| Cost per completed request | reported | 0.0017 USD | 0.0018 USD | 0.0019 USD | reported |

## Flipped cases

Cases that passed in some runs and failed in others: 1.

| Id | Category | r1 | r2 | r3 |
|---|---|---|---|---|
| ADD-22 | Clean | fail | pass | pass |

## Runs

- Run 1: 87/95 passed, cost 0.1715 USD, median time 2081.0 ms
- Run 2: 88/95 passed, cost 0.1630 USD, median time 2077.0 ms
- Run 3: 88/95 passed, cost 0.1622 USD, median time 1994.0 ms

## Run reports

- dev-claude-sonnet-m9b-r1-2026-10-19-0948.md, dev-claude-sonnet-m9b-r1-2026-10-19-0948.json
- dev-claude-sonnet-m9b-r2-2026-10-19-0952.md, dev-claude-sonnet-m9b-r2-2026-10-19-0952.json
- dev-claude-sonnet-m9b-r3-2026-10-19-0956.md, dev-claude-sonnet-m9b-r3-2026-10-19-0956.json
