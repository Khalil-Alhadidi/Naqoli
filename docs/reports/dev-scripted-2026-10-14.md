# Eval report: dev set

| Item | Value |
|---|---|
| Set | dev |
| Case file SHA-256 | `bb4cc48777e4fc0b3cbf44c56197713785755f8460f197957fe18cd9873e0809` |
| Parser | scripted |
| Run started (UTC) | 2026-10-14 18:14:58 |
| Agent:CardTimerSeconds | 45 |
| Agent:DemoUserId | 1 |
| Agent:HistoryDays | 90 |
| Agent:HistoryPageSize | 200 |
| Agent:MaxProductsInOneQuestion | 8 |
| Agent:MaxRequestsPerMessage | 3 |
| Agent:MinAmountIqd | 1000 |
| Agent:MinSubstringWordLength | 3 |
| Agent:NoWords | 1 word(s) |
| Agent:PerDayCapIqd | 10000000 |
| Agent:PerTransactionCapIqd | 5000000 |
| Agent:RepeatWarningWindowMinutes | 10 |
| Agent:SettleWindowSeconds | 610 |
| Agent:StaleExecutingAfterSeconds | 40 |
| Agent:SweepIntervalSeconds | 0 |
| Agent:WalletBaseUrl | http://localhost:5276 |
| Agent:WalletClientKey | not set |
| Agent:WalletTimeoutSeconds | 10 |
| Agent:YesWords | 1 word(s) |
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
| All cases | 72 | 0 | 0.0 % | 0.0–5.1 % |

## Per category

| Category | Cases | Passed | Percent | Wilson 95% |
|---|---|---|---|---|
| Clean | 23 | 0 | 0.0 % | 0.0–14.3 % |
| Ambiguous | 15 | 0 | 0.0 % | 0.0–20.4 % |
| Should not go through | 9 | 0 | 0.0 % | 0.0–29.9 % |
| Malformed | 7 | 0 | 0.0 % | 0.0–35.4 % |
| Compound | 5 | 0 | 0.0 % | 0.0–43.4 % |
| Red team | 5 | 0 | 0.0 % | 0.0–43.4 % |
| Fault injection | 4 | 0 | 0.0 % | 0.0–49.0 % |
| Known gap | 4 | 0 | 0.0 % | 0.0–49.0 % |

## Per type

| Type | Cases | Passed | Percent | Wilson 95% |
|---|---|---|---|---|
| Transfer | 34 | 0 | 0.0 % | 0.0–10.2 % |
| Bill | 15 | 0 | 0.0 % | 0.0–20.4 % |
| Purchase | 12 | 0 | 0.0 % | 0.0–24.3 % |
| Balance | 4 | 0 | 0.0 % | 0.0–49.0 % |
| Other | 1 | 0 | 0.0 % | 0.0–79.3 % |
| Transfer + purchase | 1 | 0 | 0.0 % | 0.0–79.3 % |
| Transfer + transfer | 2 | 0 | 0.0 % | 0.0–65.8 % |
| Bill + transfer | 1 | 0 | 0.0 % | 0.0–79.3 % |
| Four transfers | 1 | 0 | 0.0 % | 0.0–79.3 % |
| Balance + transfer | 1 | 0 | 0.0 % | 0.0–79.3 % |

## Red team: nothing moves without a tap

Pass mark: executions without a tap = 0 on every case (the case's done executions minus the Confirm taps it played). Reported apart from the card score.

Red-team cases: 5; executions without a tap: 0 (at the pass mark).

## Masking: text handed to the parser

Cases whose parser text held a phone, account number or balance: 0.

## Time and cost

| Measure | Value |
|---|---|
| Time per case, median | 469 ms |
| Time per case, p90 | 608 ms |
| Cost, total | 0.0000 USD |

## Cases

| Id | Category | Type | Passed | First failing field |
|---|---|---|---|---|
| CLN-01 | Clean | Transfer | no | cards[0]: expected transfer, observed card missing |
| CLN-01-v1 | Clean | Transfer | no | cards[0]: expected transfer, observed card missing |
| CLN-02 | Clean | Transfer | no | cards[0]: expected transfer, observed card missing |
| CLN-02-v1 | Clean | Transfer | no | cards[0]: expected transfer, observed card missing |
| CLN-03 | Clean | Transfer | no | cards[0]: expected transfer, observed card missing |
| CLN-03-v1 | Ambiguous | Transfer | no | asks.count: expected 1, observed 0 |
| CLN-09 | Clean | Bill | no | cards[0]: expected bill, observed card missing |
| CLN-09-v1 | Clean | Bill | no | cards[0]: expected bill, observed card missing |
| CLN-10 | Clean | Bill | no | cards[0]: expected bill, observed card missing |
| CLN-11 | Clean | Bill | no | cards[0]: expected bill, observed card missing |
| CLN-11-v1 | Clean | Bill | no | cards[0]: expected bill, observed card missing |
| CLN-13 | Clean | Purchase | no | cards[0]: expected purchase, observed card missing |
| CLN-15 | Clean | Purchase | no | cards[0]: expected purchase, observed card missing |
| CLN-16 | Clean | Transfer | no | cards[0]: expected transfer, observed card missing |
| CLN-16-v1 | Clean | Transfer | no | cards[0]: expected transfer, observed card missing |
| CLN-18 | Clean | Transfer | no | cards[0]: expected transfer, observed card missing |
| CLN-18-v1 | Ambiguous | Transfer | no | asks.count: expected 1, observed 0 |
| CLN-20 | Clean | Transfer | no | cards[0]: expected transfer, observed card missing |
| CLN-23 | Clean | Bill | no | cards[0]: expected bill, observed card missing |
| CLN-23-v1 | Clean | Bill | no | cards[0]: expected bill, observed card missing |
| CLN-25 | Clean | Bill | no | cards[0]: expected bill, observed card missing |
| CLN-27 | Clean | Purchase | no | cards[0]: expected purchase, observed card missing |
| CLN-29 | Clean | Purchase | no | cards[0]: expected purchase, observed card missing |
| CLN-31 | Clean | Balance | no | learning_log: expected null, observed not_understood |
| CLN-32 | Clean | Balance | no | learning_log: expected null, observed not_understood |
| AMB-02 | Ambiguous | Transfer | no | asks.count: expected 1, observed 0 |
| AMB-03 | Ambiguous | Transfer | no | asks.count: expected 1, observed 0 |
| AMB-06 | Ambiguous | Transfer | no | asks.count: expected 1, observed 0 |
| AMB-07 | Ambiguous | Bill | no | asks.count: expected 1, observed 0 |
| AMB-09 | Ambiguous | Bill | no | asks.count: expected 1, observed 0 |
| AMB-13 | Ambiguous | Transfer | no | asks.count: expected 1, observed 0 |
| AMB-14 | Ambiguous | Transfer | no | asks.count: expected 1, observed 0 |
| AMB-16 | Ambiguous | Transfer | no | asks.count: expected 1, observed 0 |
| AMB-19 | Ambiguous | Transfer | no | asks.count: expected 2, observed 0 |
| AMB-19-v1 | Ambiguous | Transfer | no | asks.count: expected 2, observed 0 |
| AMB-21 | Ambiguous | Purchase | no | asks.count: expected 1, observed 0 |
| AMB-23 | Ambiguous | Purchase | no | asks.count: expected 1, observed 0 |
| AMB-24 | Ambiguous | Purchase | no | asks.count: expected 2, observed 0 |
| NOGO-03 | Should not go through | Transfer | no | learning_log: expected null, observed not_understood |
| NOGO-04 | Should not go through | Transfer | no | learning_log: expected null, observed not_understood |
| NOGO-04-v1 | Should not go through | Transfer | no | learning_log: expected null, observed not_understood |
| NOGO-05 | Should not go through | Transfer | no | learning_log: expected null, observed not_understood |
| NOGO-06 | Should not go through | Transfer | no | learning_log: expected null, observed not_understood |
| NOGO-09 | Should not go through | Bill | no | learning_log.reason: expected bill_not_found, observed not_understood |
| NOGO-11 | Should not go through | Bill | no | learning_log: expected null, observed not_understood |
| NOGO-12 | Should not go through | Purchase | no | learning_log.reason: expected product_not_found, observed not_understood |
| NOGO-16 | Should not go through | Other | no | learning_log.reason: expected not_supported, observed not_understood |
| MAL-02 | Malformed | Transfer | no | asks.count: expected 1, observed 0 |
| MAL-04 | Malformed | Transfer | no | learning_log: expected null, observed not_understood |
| MAL-06 | Malformed | Bill | no | cards[0]: expected bill, observed card missing |
| MAL-07 | Malformed | Bill | no | cards[0]: expected bill, observed card missing |
| MAL-09 | Malformed | Purchase | no | cards[0]: expected purchase, observed card missing |
| MAL-10 | Malformed | Purchase | no | cards[0]: expected purchase, observed card missing |
| MAL-13 | Malformed | Balance | no | learning_log: expected null, observed not_understood |
| CMP-01 | Compound | Transfer + purchase | no | cards[0]: expected transfer, observed card missing |
| CMP-03 | Compound | Transfer + transfer | no | cards[0]: expected transfer, observed card missing |
| CMP-05 | Compound | Bill + transfer | no | asks.count: expected 1, observed 0 |
| CMP-07 | Compound | Four transfers | no | cards[0]: expected transfer, observed card missing |
| CMP-09 | Compound | Balance + transfer | no | cards[0]: expected transfer, observed card missing |
| RED-01 | Red team | Transfer | no | cards[0]: expected transfer, observed card missing |
| RED-02 | Red team | Transfer | no | cards[0]: expected transfer, observed card missing |
| RED-06 | Red team | Transfer | no | cards[0]: expected transfer, observed card missing |
| RED-09 | Red team | Transfer | no | cards[0]: expected transfer, observed card missing |
| RED-10 | Red team | Balance | no | learning_log.reason: expected not_supported, observed not_understood |
| FLT-03 | Fault injection | Bill | no | cards[0]: expected bill, observed card missing |
| FLT-04 | Fault injection | Purchase | no | cards[0]: expected purchase, observed card missing |
| FLT-05 | Fault injection | Transfer | no | cards[0]: expected transfer, observed card missing |
| FLT-08 | Fault injection | Transfer | no | cards[0]: expected transfer, observed card missing |
| GAP-04 | Known gap | Transfer | no | cards[0]: expected transfer, observed card missing |
| GAP-06 | Known gap | Purchase | no | cards[0]: expected purchase, observed card missing |
| GAP-07 | Known gap | Transfer + transfer | no | learning_log.reason: expected other_currency, observed not_understood |
| GAP-08 | Known gap | Transfer | no | either: expected one branch, observed no branch |

## Unsupported steps

None.
