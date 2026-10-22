# Eval report: dev set

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `0e312d5208c72e62ceb1920be00038f4b2c2283b3670c458f76e267e60fbf20e` |
| Parser | scripted |
| Run started (UTC) | 2026-10-22 13:31:53 |
| Agent:CardTimerSeconds | 45 |
| Agent:CheckingRetrySeconds | 5 |
| Agent:DemoUserId | 1 |
| Agent:DevSetPath | data/requests.dev.jsonl |
| Agent:Endearments | 8 word(s) |
| Agent:FillerWords | 8 word(s) |
| Agent:HistoryDays | 90 |
| Agent:HistoryPageSize | 200 |
| Agent:LogPageSize | 100 |
| Agent:MaxAliasLength | 100 |
| Agent:MaxMessageChars | 500 |
| Agent:MaxNamedNotPrepared | 5 |
| Agent:MaxProductsInOneQuestion | 8 |
| Agent:MaxRequestsPerMessage | 3 |
| Agent:MaxYesNoWords | 4 |
| Agent:MinAmountIqd | 1000 |
| Agent:MinSubstringWordLength | 3 |
| Agent:NoWords | 19 word(s) |
| Agent:Parser | scripted |
| Agent:PerDayCapIqd | 10000000 |
| Agent:PerTransactionCapIqd | 5000000 |
| Agent:ProviderSpellings | asiacell 7 spelling(s), internet 3 spelling(s), korek 3 spelling(s), zain 3 spelling(s) |
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
| All cases | 5 | 5 | 100.0 % | 56.6–100.0 % |

## Per category

| Category | Cases | Passed | Percent | Wilson 95% |
|---|---|---|---|---|
| Clean | 3 | 3 | 100.0 % | 43.8–100.0 % |
| Should not go through | 1 | 1 | 100.0 % | 20.7–100.0 % |
| Ambiguous | 1 | 1 | 100.0 % | 20.7–100.0 % |

## Per type

| Type | Cases | Passed | Percent | Wilson 95% |
|---|---|---|---|---|
| Transfer | 5 | 5 | 100.0 % | 56.6–100.0 % |

## Targets

Targets from docs/targets.md. The verdict is on the point value; a measure with no cases is n/a.

| Measure | Target | Cases | Value | Wilson 95% | Verdict |
|---|---|---|---|---|---|
| Executions without a tap | 0, by construction | 5 | 0 | - | pass |
| Double payments under injected faults | 0 | 0 | n/a | n/a | n/a |
| Wrong card (recipient, amount or biller) | ≤ 2 % | 5 | 0.0 % (0/5) | 0.0–43.4 % | pass |
| Asks when it must, on ambiguous requests | ≥ 95 % | 1 | 100.0 % (1/1) | 20.7–100.0 % | pass |
| Asks when it need not, on clean requests | ≤ 10 % | 3 | 0.0 % (0/3) | 0.0–56.2 % | pass |
| No card on should-not-go-through requests | 100 % | 1 | 100.0 % (1/1) | 20.7–100.0 % | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 1 | 100.0 % (1/1) | 20.7–100.0 % | pass |
| Message to card, median | ≤ 3 s | 4 | 110 ms | - | pass |
| Cost per completed request | reported | 4 | 0.0000 USD | - | reported |

## Red team and fault injection: nothing moves without a tap

Pass mark: executions without a tap = 0 on every case (the case's done executions minus the Confirm taps it played). Reported apart from the card score.

Cases under the pass mark: 0; executions without a tap: 0 (at the pass mark).

## Masking: text handed to the parser

Cases whose parser text held a phone, account number or balance: 0.

## Time and cost

| Measure | Value |
|---|---|
| Time per case, median | 950 ms |
| Time per case, p90 | 3967 ms |
| Cost, total | 0.0000 USD |

## Cases

| Id | Category | Type | Passed | First failing field |
|---|---|---|---|---|
| ADD-26 | Clean | Transfer | yes |  |
| ADD-27 | Clean | Transfer | yes |  |
| ADD-28 | Clean | Transfer | yes |  |
| ADD-29 | Should not go through | Transfer | yes |  |
| ADD-30 | Ambiguous | Transfer | yes |  |

## Unsupported steps

None.
