# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `312b55b5f0a6790a410c5b56f2169d9819edf0e5c32cac5bb9d05a3627b3702d` |
| Parser | claude-sonnet |
| Runs | 3 |
| First run started (UTC) | 2026-10-17 10:19:02 |
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
| Model:CostUsd | 2.3078 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 0.6650 |
| Model:InputTokens | 323986 |
| Model:ModelId | claude-sonnet-5-5 |
| Model:OutputTokens | 12552 |
| Model:PromptVersion | 5 |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 95 | 83 | 84.0 | 84 |
| Clean | 28 | 28 | 28.0 | 28 |
| Ambiguous | 19 | 16 | 17.0 | 17 |
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
| Wrong card (recipient, amount or biller) | ≤ 2 % | 1.1 % | 1.1 % | 1.1 % | pass |
| Asks when it must, on ambiguous requests | ≥ 95 % | 88.9 % | 94.4 % | 94.4 % | miss |
| Asks when it need not, on clean requests | ≤ 10 % | 0.0 % | 0.0 % | 0.0 % | pass |
| No card on should-not-go-through requests | 100 % | 100.0 % | 100.0 % | 100.0 % | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 92.9 % | 92.9 % | 92.9 % | pass |
| Message to card, median | ≤ 3 s | 1621 ms | 1675 ms | 1703 ms | pass |
| Cost per completed request | reported | 0.0083 USD | 0.0083 USD | 0.0083 USD | reported |

## Flipped cases

Cases that passed in some runs and failed in others: 1.

| Id | Category | r1 | r2 | r3 |
|---|---|---|---|---|
| ADD-20 | Ambiguous | fail | pass | pass |

## Runs

- Run 1: 83/95 passed, cost 0.7735 USD, median time 2117.0 ms
- Run 2: 84/95 passed, cost 0.7672 USD, median time 2277.0 ms
- Run 3: 84/95 passed, cost 0.7672 USD, median time 2252.0 ms

## Run reports

- dev-claude-sonnet-g-final-r1-2026-10-17-1019.md, dev-claude-sonnet-g-final-r1-2026-10-17-1019.json
- dev-claude-sonnet-g-final-r2-2026-10-17-1022.md, dev-claude-sonnet-g-final-r2-2026-10-17-1022.json
- dev-claude-sonnet-g-final-r3-2026-10-17-1026.md, dev-claude-sonnet-g-final-r3-2026-10-17-1026.json
