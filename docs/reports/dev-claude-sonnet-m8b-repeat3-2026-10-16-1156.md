# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `8078b5ad2799779f18311b7e3b209f0baff8a1f0b0a669be1ad58e6f0bd64257` |
| Parser | claude-sonnet |
| Runs | 3 |
| First run started (UTC) | 2026-10-16 11:56:50 |
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
| Model:Candidate | sonnet |
| Model:CostUsd | 1.8553 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 0.5390 |
| Model:InputTokens | 256451 |
| Model:ModelId | claude-sonnet-5-5 |
| Model:OutputTokens | 10550 |
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
| Message to card, median | ≤ 3 s | 1909 ms | 1944 ms | 2057 ms | pass |
| Cost per completed request | reported | 0.0082 USD | 0.0082 USD | 0.0084 USD | reported |

## Flipped cases

Cases that passed in some runs and failed in others: 1.

| Id | Category | r1 | r2 | r3 |
|---|---|---|---|---|
| AMB-06 | Ambiguous | pass | fail | fail |

## Runs

- Run 1: 68/77 passed, cost 0.6184 USD, median time 2417.0 ms
- Run 2: 67/77 passed, cost 0.6185 USD, median time 2368.0 ms
- Run 3: 67/77 passed, cost 0.6184 USD, median time 2426.0 ms

## Run reports

- dev-claude-sonnet-m8b-r1-2026-10-16-1156.md, dev-claude-sonnet-m8b-r1-2026-10-16-1156.json
- dev-claude-sonnet-m8b-r2-2026-10-16-1200.md, dev-claude-sonnet-m8b-r2-2026-10-16-1200.json
- dev-claude-sonnet-m8b-r3-2026-10-16-1204.md, dev-claude-sonnet-m8b-r3-2026-10-16-1204.json
