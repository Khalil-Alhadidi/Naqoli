# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `084a67f6352f1b7c0bc1938dccfda78f3f6d364c8db969a68ef2b130df76bbb0` |
| Parser | claude-sonnet |
| Runs | 3 |
| First run started (UTC) | 2026-10-16 17:01:14 |
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
| Model:CostUsd | 2.2512 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 0.6300 |
| Model:InputTokens | 314185 |
| Model:ModelId | claude-sonnet-5-5 |
| Model:OutputTokens | 12198 |
| Model:PromptVersion | 5 |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 90 | 78 | 78.0 | 78 |
| Clean | 26 | 26 | 26.0 | 26 |
| Ambiguous | 17 | 15 | 15.0 | 15 |
| Should not go through | 13 | 11 | 11.0 | 11 |
| Malformed | 8 | 7 | 7.0 | 7 |
| Compound | 5 | 0 | 0.0 | 0 |
| Red team | 9 | 9 | 9.0 | 9 |
| Fault injection | 8 | 7 | 7.0 | 7 |
| Known gap | 4 | 3 | 3.0 | 3 |

## Targets

Targets from docs/targets.md: each run's value, and the verdict on the median. A run with no cases for a measure is left out of it.

| Measure | Target | Min | Median | Max | Verdict on the median |
|---|---|---|---|---|---|
| Executions without a tap | 0, by construction | 0 | 0 | 0 | pass |
| Double payments under injected faults | 0 | 1 | 1 | 1 | miss |
| Wrong card (recipient, amount or biller) | ≤ 2 % | 0.0 % | 0.0 % | 0.0 % | pass |
| Asks when it must, on ambiguous requests | ≥ 95 % | 94.1 % | 94.1 % | 94.1 % | miss |
| Asks when it need not, on clean requests | ≤ 10 % | 0.0 % | 0.0 % | 0.0 % | pass |
| No card on should-not-go-through requests | 100 % | 100.0 % | 100.0 % | 100.0 % | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 92.3 % | 92.3 % | 92.3 % | pass |
| Message to card, median | ≤ 3 s | 1774 ms | 1958 ms | 2064 ms | pass |
| Cost per completed request | reported | 0.0088 USD | 0.0088 USD | 0.0088 USD | reported |

## Flipped cases

Cases that passed in some runs and failed in others: 0.

## Runs

- Run 1: 78/90 passed, cost 0.7504 USD, median time 2257.0 ms
- Run 2: 78/90 passed, cost 0.7504 USD, median time 2482.0 ms
- Run 3: 78/90 passed, cost 0.7504 USD, median time 2523.0 ms

## Run reports

- dev-claude-sonnet-v5-r1-2026-10-16-1701.md, dev-claude-sonnet-v5-r1-2026-10-16-1701.json
- dev-claude-sonnet-v5-r2-2026-10-16-1705.md, dev-claude-sonnet-v5-r2-2026-10-16-1705.json
- dev-claude-sonnet-v5-r3-2026-10-16-1709.md, dev-claude-sonnet-v5-r3-2026-10-16-1709.json
