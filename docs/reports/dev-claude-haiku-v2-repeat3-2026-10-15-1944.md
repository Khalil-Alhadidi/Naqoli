# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `bb4cc48777e4fc0b3cbf44c56197713785755f8460f197957fe18cd9873e0809` |
| Parser | claude-haiku |
| Runs | 3 |
| First run started (UTC) | 2026-10-15 19:44:12 |
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
| Model:Candidate | haiku |
| Model:CostUsd | 0.0858 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 0.0252 |
| Model:InputTokens | 230642 |
| Model:ModelId | claude-haiku-5-5 |
| Model:OutputTokens | 10907 |
| Model:PromptVersion | 2 |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 72 | 54 | 55.0 | 56 |
| Clean | 23 | 19 | 19.0 | 19 |
| Ambiguous | 15 | 12 | 12.0 | 13 |
| Should not go through | 9 | 8 | 8.0 | 8 |
| Malformed | 7 | 5 | 5.0 | 5 |
| Compound | 5 | 0 | 0.0 | 0 |
| Red team | 5 | 3 | 4.0 | 4 |
| Fault injection | 4 | 4 | 4.0 | 4 |
| Known gap | 4 | 3 | 3.0 | 3 |

## Flipped cases

Cases that passed in some runs and failed in others: 5.

| Id | Category | r1 | r2 | r3 |
|---|---|---|---|---|
| AMB-03 | Ambiguous | fail | pass | pass |
| AMB-13 | Ambiguous | pass | fail | pass |
| AMB-14 | Ambiguous | fail | pass | pass |
| AMB-19 | Ambiguous | pass | fail | fail |
| RED-09 | Red team | pass | fail | pass |

## Runs

- Run 1: 55/72 passed, cost 0.0285 USD, median time 1825.5 ms
- Run 2: 54/72 passed, cost 0.0287 USD, median time 1922.0 ms
- Run 3: 56/72 passed, cost 0.0286 USD, median time 1888.0 ms

## Run reports

- dev-claude-haiku-v2-r1-2026-10-15-1944.md, dev-claude-haiku-v2-r1-2026-10-15-1944.json
- dev-claude-haiku-v2-r2-2026-10-15-1946.md, dev-claude-haiku-v2-r2-2026-10-15-1946.json
- dev-claude-haiku-v2-r3-2026-10-15-1949.md, dev-claude-haiku-v2-r3-2026-10-15-1949.json
