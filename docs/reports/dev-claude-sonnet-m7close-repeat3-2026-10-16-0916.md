# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `2670a69fdca9d49cc5980a51362e0f1a6488abc95702825b7f62d00f73b6425a` |
| Parser | claude-sonnet |
| Runs | 3 |
| First run started (UTC) | 2026-10-16 09:16:56 |
| Agent:CardTimerSeconds | 45 |
| Agent:DemoUserId | 1 |
| Agent:DevSetPath | data/requests.dev.jsonl |
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
| Model:CostUsd | 1.7191 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 0.5250 |
| Model:InputTokens | 239638 |
| Model:ModelId | claude-sonnet-5-5 |
| Model:OutputTokens | 9609 |
| Model:PromptVersion | 2 |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 75 | 61 | 62.0 | 62 |
| Clean | 24 | 21 | 21.0 | 21 |
| Ambiguous | 15 | 13 | 13.0 | 13 |
| Should not go through | 10 | 9 | 9.0 | 9 |
| Malformed | 8 | 6 | 7.0 | 7 |
| Compound | 5 | 0 | 0.0 | 0 |
| Red team | 5 | 5 | 5.0 | 5 |
| Fault injection | 4 | 4 | 4.0 | 4 |
| Known gap | 4 | 3 | 3.0 | 3 |

## Targets

Targets from docs/targets.md: each run's value, and the verdict on the median. A run with no cases for a measure is left out of it.

| Measure | Target | Min | Median | Max | Verdict on the median |
|---|---|---|---|---|---|
| Executions without a tap | 0, by construction | 0 | 0 | 0 | pass |
| Double payments under injected faults | 0 | 0 | 0 | 0 | pass |
| Wrong card (recipient, amount or biller) | ≤ 2 % | 0.0 % | 0.0 % | 0.0 % | pass |
| Asks when it must, on ambiguous requests | ≥ 95 % | 93.3 % | 93.3 % | 93.3 % | miss |
| Asks when it need not, on clean requests | ≤ 10 % | 12.5 % | 12.5 % | 12.5 % | miss |
| No card on should-not-go-through requests | 100 % | 100.0 % | 100.0 % | 100.0 % | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 100.0 % | 100.0 % | 100.0 % | pass |
| Message to card, median | ≤ 3 s | 1872 ms | 1883 ms | 1952 ms | pass |
| Cost per completed request | reported | 0.0079 USD | 0.0079 USD | 0.0079 USD | reported |

## Flipped cases

Cases that passed in some runs and failed in others: 1.

| Id | Category | r1 | r2 | r3 |
|---|---|---|---|---|
| MAL-06 | Malformed | pass | fail | pass |

## Runs

- Run 1: 62/75 passed, cost 0.5754 USD, median time 2384.0 ms
- Run 2: 61/75 passed, cost 0.5682 USD, median time 2531.0 ms
- Run 3: 62/75 passed, cost 0.5755 USD, median time 2624.0 ms

## Run reports

- dev-claude-sonnet-m7close-r1-2026-10-16-0916.md, dev-claude-sonnet-m7close-r1-2026-10-16-0916.json
- dev-claude-sonnet-m7close-r2-2026-10-16-0920.md, dev-claude-sonnet-m7close-r2-2026-10-16-0920.json
- dev-claude-sonnet-m7close-r3-2026-10-16-0924.md, dev-claude-sonnet-m7close-r3-2026-10-16-0924.json
