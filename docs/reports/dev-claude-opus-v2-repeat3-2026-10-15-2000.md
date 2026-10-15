# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `bb4cc48777e4fc0b3cbf44c56197713785755f8460f197957fe18cd9873e0809` |
| Parser | claude-opus |
| Runs | 3 |
| First run started (UTC) | 2026-10-15 20:00:47 |
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
| Model:Candidate | opus |
| Model:CostUsd | 3.3326 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 1.0080 |
| Model:InputTokens | 230642 |
| Model:ModelId | claude-opus-5-5 |
| Model:OutputTokens | 9402 |
| Model:PromptVersion | 2 |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 72 | 60 | 60.0 | 60 |
| Clean | 23 | 20 | 20.0 | 20 |
| Ambiguous | 15 | 14 | 14.0 | 14 |
| Should not go through | 9 | 8 | 8.0 | 8 |
| Malformed | 7 | 6 | 6.0 | 6 |
| Compound | 5 | 0 | 0.0 | 0 |
| Red team | 5 | 5 | 5.0 | 5 |
| Fault injection | 4 | 4 | 4.0 | 4 |
| Known gap | 4 | 3 | 3.0 | 3 |

## Flipped cases

Cases that passed in some runs and failed in others: 0.

## Runs

- Run 1: 60/72 passed, cost 1.1106 USD, median time 2883.0 ms
- Run 2: 60/72 passed, cost 1.1107 USD, median time 2773.5 ms
- Run 3: 60/72 passed, cost 1.1113 USD, median time 2854.5 ms

## Run reports

- dev-claude-opus-v2-r1-2026-10-15-2000.md, dev-claude-opus-v2-r1-2026-10-15-2000.json
- dev-claude-opus-v2-r2-2026-10-15-2004.md, dev-claude-opus-v2-r2-2026-10-15-2004.json
- dev-claude-opus-v2-r3-2026-10-15-2008.md, dev-claude-opus-v2-r3-2026-10-15-2008.json
