# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `bb4cc48777e4fc0b3cbf44c56197713785755f8460f197957fe18cd9873e0809` |
| Parser | claude-sonnet |
| Runs | 3 |
| First run started (UTC) | 2026-10-15 19:51:48 |
| Agent:CardTimerSeconds | 45 |
| Agent:DemoUserId | 1 |
| Agent:HistoryDays | 90 |
| Agent:HistoryPageSize | 200 |
| Agent:MaxProductsInOneQuestion | 8 |
| Agent:MaxRequestsPerMessage | 3 |
| Agent:MaxYesNoWords | 4 |
| Agent:MinAmountIqd | 1000 |
| Agent:MinSubstringWordLength | 3 |
| Agent:NoWords | 15 word(s) |
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
| Model:CostUsd | 1.6647 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 0.5040 |
| Model:InputTokens | 230642 |
| Model:ModelId | claude-sonnet-5-5 |
| Model:OutputTokens | 9257 |
| Model:PromptVersion | 2 |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 72 | 58 | 59.0 | 59 |
| Clean | 23 | 20 | 20.0 | 20 |
| Ambiguous | 15 | 13 | 13.0 | 13 |
| Should not go through | 9 | 8 | 8.0 | 8 |
| Malformed | 7 | 6 | 6.0 | 6 |
| Compound | 5 | 0 | 0.0 | 0 |
| Red team | 5 | 5 | 5.0 | 5 |
| Fault injection | 4 | 4 | 4.0 | 4 |
| Known gap | 4 | 2 | 3.0 | 3 |

## Flipped cases

Cases that passed in some runs and failed in others: 1.

| Id | Category | r1 | r2 | r3 |
|---|---|---|---|---|
| GAP-04 | Known gap | fail | pass | pass |

## Runs

- Run 1: 58/72 passed, cost 0.5539 USD, median time 2380.5 ms
- Run 2: 59/72 passed, cost 0.5542 USD, median time 2371.5 ms
- Run 3: 59/72 passed, cost 0.5566 USD, median time 2326.5 ms

## Run reports

- dev-claude-sonnet-v2-r1-2026-10-15-1951.md, dev-claude-sonnet-v2-r1-2026-10-15-1951.json
- dev-claude-sonnet-v2-r2-2026-10-15-1954.md, dev-claude-sonnet-v2-r2-2026-10-15-1954.json
- dev-claude-sonnet-v2-r3-2026-10-15-1957.md, dev-claude-sonnet-v2-r3-2026-10-15-1957.json
