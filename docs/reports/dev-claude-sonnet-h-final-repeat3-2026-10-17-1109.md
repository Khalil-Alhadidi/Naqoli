# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `8f7d28ad3ceb28a075b6b46230ecf339d9d3641e366591bfce71c64bcf5a953b` |
| Parser | claude-sonnet |
| Runs | 3 |
| First run started (UTC) | 2026-10-17 11:09:48 |
| Agent:CardTimerSeconds | 45 |
| Agent:CheckingRetrySeconds | 5 |
| Agent:DemoUserId | 1 |
| Agent:DevSetPath | data/requests.dev.jsonl |
| Agent:Endearments | 8 word(s) |
| Agent:FillerWords | 8 word(s) |
| Agent:HistoryDays | 90 |
| Agent:HistoryPageSize | 200 |
| Agent:LogPageSize | 100 |
| Agent:MaxProductsInOneQuestion | 8 |
| Agent:MaxRequestsPerMessage | 3 |
| Agent:MaxYesNoWords | 4 |
| Agent:MinAmountIqd | 1000 |
| Agent:MinSubstringWordLength | 3 |
| Agent:NoWords | 19 word(s) |
| Agent:Parser | claude |
| Agent:PerDayCapIqd | 10000000 |
| Agent:PerTransactionCapIqd | 5000000 |
| Agent:ProviderSpellings | internet 3 spelling(s) |
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
| Model:Candidate | sonnet |
| Model:CostUsd | 2.2783 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 0.6650 |
| Model:InputTokens | 317455 |
| Model:ModelId | claude-sonnet-5-5 |
| Model:OutputTokens | 12451 |
| Model:PromptVersion | 5 |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 95 | 85 | 85.0 | 85 |
| Clean | 28 | 28 | 28.0 | 28 |
| Ambiguous | 19 | 18 | 18.0 | 18 |
| Should not go through | 14 | 12 | 12.0 | 12 |
| Malformed | 8 | 7 | 7.0 | 7 |
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
| Asks when it must, on ambiguous requests | ≥ 95 % | 94.4 % | 94.4 % | 94.4 % | miss |
| Asks when it need not, on clean requests | ≤ 10 % | 0.0 % | 0.0 % | 0.0 % | pass |
| No card on should-not-go-through requests | 100 % | 100.0 % | 100.0 % | 100.0 % | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 92.9 % | 92.9 % | 92.9 % | pass |
| Message to card, median | ≤ 3 s | 1646 ms | 1656 ms | 1689 ms | pass |
| Cost per completed request | reported | 0.0083 USD | 0.0083 USD | 0.0083 USD | reported |

## Flipped cases

Cases that passed in some runs and failed in others: 0.

## Runs

- Run 1: 85/95 passed, cost 0.7594 USD, median time 2258.0 ms
- Run 2: 85/95 passed, cost 0.7595 USD, median time 2196.0 ms
- Run 3: 85/95 passed, cost 0.7594 USD, median time 2347.0 ms

## Run reports

- dev-claude-sonnet-h-final-r1-2026-10-17-1109.md, dev-claude-sonnet-h-final-r1-2026-10-17-1109.json
- dev-claude-sonnet-h-final-r2-2026-10-17-1113.md, dev-claude-sonnet-h-final-r2-2026-10-17-1113.json
- dev-claude-sonnet-h-final-r3-2026-10-17-1117.md, dev-claude-sonnet-h-final-r3-2026-10-17-1117.json
