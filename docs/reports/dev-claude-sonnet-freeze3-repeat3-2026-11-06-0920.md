# Eval repeat report: dev set, 3 runs

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `7df4552c9de0fb5c6513750d83bab9cdd9fbea983663360b1eac2a48d1c37d91` |
| Parser | claude-sonnet |
| Runs | 3 |
| First run started (UTC) | 2026-11-06 09:20:13 |
| Agent:CardTimerSeconds | 45 |
| Agent:CheckingRetrySeconds | 5 |
| Agent:DemoUserId | 1 |
| Agent:DevSetPath | data/requests.dev.jsonl |
| Agent:DinarWords | 4 word(s) |
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
| Agent:Parser | claude |
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
| Model:BaseUrl | not sent |
| Model:CacheHitRate | 98.3 % |
| Model:CacheReadTokens | 438162 |
| Model:CacheWriteTokens | 4254 |
| Model:Candidate | sonnet |
| Model:CostUsd | 0.5585 |
| Model:Effort | low |
| Model:EstimatedCostUsd | 0.7140 |
| Model:InputTokens | 3298 |
| Model:MinMillisecondsBetweenCalls | 0 |
| Model:ModelId | claude-sonnet-5-5 |
| Model:OutputMode | not sent |
| Model:OutputTokens | 13194 |
| Model:PromptCacheTtl | 5m |
| Model:PromptVersion | 8 |
| Model:Provider | anthropic |
| Model:RequestFields | not sent |
| Model:Temperature | not sent |

## Passed across the runs

| Group | Cases | Min | Median | Max |
|---|---|---|---|---|
| All cases | 102 | 102 | 102.0 | 102 |
| Clean | 32 | 32 | 32.0 | 32 |
| Ambiguous | 21 | 21 | 21.0 | 21 |
| Should not go through | 16 | 16 | 16.0 | 16 |
| Malformed | 8 | 8 | 8.0 | 8 |
| Compound | 5 | 5 | 5.0 | 5 |
| Red team | 9 | 9 | 9.0 | 9 |
| Fault injection | 8 | 8 | 8.0 | 8 |
| Known gap | 3 | 3 | 3.0 | 3 |

## Targets

Targets from docs/targets.md: each run's value, and the verdict on the median. A run with no cases for a measure is left out of it.

| Measure | Target | Min | Median | Max | Verdict on the median |
|---|---|---|---|---|---|
| Executions without a tap | 0, by construction | 0 | 0 | 0 | pass |
| Double payments under injected faults | 0 | 0 | 0 | 0 | pass |
| Wrong card (recipient, amount or biller) | ≤ 2 % | 0.0 % | 0.0 % | 0.0 % | pass |
| Asks when it must, on ambiguous requests | ≥ 95 % | 100.0 % | 100.0 % | 100.0 % | pass |
| Asks when it need not, on clean requests | ≤ 10 % | 0.0 % | 0.0 % | 0.0 % | pass |
| No card on should-not-go-through requests | 100 % | 100.0 % | 100.0 % | 100.0 % | pass |
| No card on should-not-go-through requests: reason given | ≥ 90 % | 100.0 % | 100.0 % | 100.0 % | pass |
| Message to card, median | ≤ 3 s | 1679 ms | 1717 ms | 1759 ms | pass |
| Cost per completed request | reported | 0.0019 USD | 0.0019 USD | 0.0020 USD | reported |

## Flipped cases

Cases that passed in some runs and failed in others: 0.

## Runs

- Run 1: 102/102 passed, cost 0.1930 USD, median time 2247.0 ms
- Run 2: 102/102 passed, cost 0.1828 USD, median time 2334.0 ms
- Run 3: 102/102 passed, cost 0.1828 USD, median time 2293.0 ms

## Run reports

- dev-claude-sonnet-freeze3-r1-2026-11-06-0920.md, dev-claude-sonnet-freeze3-r1-2026-11-06-0920.json
- dev-claude-sonnet-freeze3-r2-2026-11-06-0924.md, dev-claude-sonnet-freeze3-r2-2026-11-06-0924.json
- dev-claude-sonnet-freeze3-r3-2026-11-06-0928.md, dev-claude-sonnet-freeze3-r3-2026-11-06-0928.json
