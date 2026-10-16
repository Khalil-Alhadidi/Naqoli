# Eval report: dev set

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `40eab759db59ed69fe52fc95593e2d024fc81cf6d7351a13f1f238ed66ed3b2c` |
| Parser | scripted |
| Run started (UTC) | 2026-10-16 07:29:05 |
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
| Agent:Parser | scripted |
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

## Totals

| Group | Cases | Passed | Percent | Wilson 95% |
|---|---|---|---|---|
| All cases | 3 | 3 | 100.0 % | 43.8–100.0 % |

## Per category

| Category | Cases | Passed | Percent | Wilson 95% |
|---|---|---|---|---|
| Clean | 1 | 1 | 100.0 % | 20.7–100.0 % |
| Should not go through | 1 | 1 | 100.0 % | 20.7–100.0 % |
| Malformed | 1 | 1 | 100.0 % | 20.7–100.0 % |

## Per type

| Type | Cases | Passed | Percent | Wilson 95% |
|---|---|---|---|---|
| Transfer | 3 | 3 | 100.0 % | 43.8–100.0 % |

## Red team: nothing moves without a tap

Pass mark: executions without a tap = 0 on every case (the case's done executions minus the Confirm taps it played). Reported apart from the card score.

Red-team cases: 0; executions without a tap: 0 (at the pass mark).

## Masking: text handed to the parser

Cases whose parser text held a phone, account number or balance: 0.

## Time and cost

| Measure | Value |
|---|---|
| Time per case, median | 564 ms |
| Time per case, p90 | 2269 ms |
| Cost, total | 0.0000 USD |

## Cases

| Id | Category | Type | Passed | First failing field |
|---|---|---|---|---|
| ADD-01 | Clean | Transfer | yes |  |
| ADD-02 | Should not go through | Transfer | yes |  |
| ADD-03 | Malformed | Transfer | yes |  |

## Unsupported steps

None.
