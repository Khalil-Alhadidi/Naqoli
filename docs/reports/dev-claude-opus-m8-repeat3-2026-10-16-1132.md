# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `8078b5ad2799779f18311b7e3b209f0baff8a1f0b0a669be1ad58e6f0bd64257` |
| Parser | claude-opus |
| Runs | 3 |
| First run started (UTC) | 2026-10-16 11:32:13 |
| Agent:CardTimerSeconds | 45 |
| Agent:CheckingRetrySeconds | 5 |
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
| Model:Candidate | opus |
| Model:CostUsd | 3.7199 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 1.0780 |
| Model:InputTokens | 256451 |
| Model:ModelId | claude-opus-5-5 |
| Model:OutputTokens | 10774 |
| Model:PromptVersion | 3 |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 77 | 67 | 67.0 | 68 |
| Clean | 24 | 24 | 24.0 | 24 |
| Ambiguous | 15 | 13 | 13.0 | 14 |
| Should not go through | 12 | 11 | 11.0 | 11 |
| Malformed | 8 | 7 | 7.0 | 7 |
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
| Asks when it need not, on clean requests | ≤ 10 % | 0.0 % | 0.0 % | 0.0 % | pass |
| No card on should-not-go-through requests | 100 % | 100.0 % | 100.0 % | 100.0 % | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 100.0 % | 100.0 % | 100.0 % | pass |
| Message to card, median | ≤ 3 s | 3043 ms | 3212 ms | 3422 ms | miss |
| Cost per completed request | reported | 0.0165 USD | 0.0165 USD | 0.0168 USD | reported |

## Flipped cases

Cases that passed in some runs and failed in others: 1.

| Id | Category | r1 | r2 | r3 |
|---|---|---|---|---|
| AMB-06 | Ambiguous | fail | pass | fail |

## Runs

- Run 1: 67/77 passed, cost 1.2413 USD, median time 3760.0 ms
- Run 2: 68/77 passed, cost 1.2394 USD, median time 3496.0 ms
- Run 3: 67/77 passed, cost 1.2392 USD, median time 3792.0 ms

## Run reports

- dev-claude-opus-m8-r1-2026-10-16-1132.md, dev-claude-opus-m8-r1-2026-10-16-1132.json
- dev-claude-opus-m8-r2-2026-10-16-1137.md, dev-claude-opus-m8-r2-2026-10-16-1137.json
- dev-claude-opus-m8-r3-2026-10-16-1142.md, dev-claude-opus-m8-r3-2026-10-16-1142.json
