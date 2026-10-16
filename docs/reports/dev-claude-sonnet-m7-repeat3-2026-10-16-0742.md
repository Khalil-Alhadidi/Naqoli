# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `40eab759db59ed69fe52fc95593e2d024fc81cf6d7351a13f1f238ed66ed3b2c` |
| Parser | claude-sonnet |
| Runs | 3 |
| First run started (UTC) | 2026-10-16 07:42:47 |
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
| Model:CostUsd | 1.7279 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 0.5250 |
| Model:InputTokens | 239643 |
| Model:ModelId | claude-sonnet-5-5 |
| Model:OutputTokens | 9646 |
| Model:PromptVersion | 2 |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 75 | 62 | 62.0 | 62 |
| Clean | 24 | 21 | 21.0 | 21 |
| Ambiguous | 15 | 13 | 13.0 | 13 |
| Should not go through | 10 | 9 | 9.0 | 9 |
| Malformed | 8 | 7 | 7.0 | 7 |
| Compound | 5 | 0 | 0.0 | 0 |
| Red team | 5 | 5 | 5.0 | 5 |
| Fault injection | 4 | 4 | 4.0 | 4 |
| Known gap | 4 | 3 | 3.0 | 3 |

## Flipped cases

Cases that passed in some runs and failed in others: 0.

## Runs

- Run 1: 62/75 passed, cost 0.5757 USD, median time 2476.0 ms
- Run 2: 62/75 passed, cost 0.5768 USD, median time 2561.0 ms
- Run 3: 62/75 passed, cost 0.5753 USD, median time 2431.0 ms

## Run reports

- dev-claude-sonnet-m7-r1-2026-10-16-0742.md, dev-claude-sonnet-m7-r1-2026-10-16-0742.json
- dev-claude-sonnet-m7-r2-2026-10-16-0746.md, dev-claude-sonnet-m7-r2-2026-10-16-0746.json
- dev-claude-sonnet-m7-r3-2026-10-16-0749.md, dev-claude-sonnet-m7-r3-2026-10-16-0749.json
