# Seed data

The mock wallet starts from one frozen, synthetic seed. The rows live in `src/Wallet.MockApi/Data/SeedData.cs`, so that `migrate` and the reset core (`SeedReset.Reset`) load exactly the same data. This file describes the seed in words; the code is the source of truth.

Every name, phone number, account number, purchase code and transaction here is invented. None belongs to a real person or company account.

## Frozen by hash

`seed-hash.txt` holds the SHA-256 of a canonical JSON form of every seed row (`SeedData.ComputeHash()`). The test `SeedHashTests.SeedHashMatchesCommittedValue` fails if the seed changes. If a change is intended, run `dotnet run --project src/Wallet.MockApi -- seed-hash`, write the printed value to `seed-hash.txt`, and re-check every answer key (build plan, milestone 2). PIN hashes are salted, so the hash covers a `***` placeholder instead of them.

The seed is tagged `seed-v1` at the end of milestone 2.

## Phone numbers

Decision by Khalil, 14 Oct 2026: seeded phone numbers stay in the pattern `^0770000\d{4}$` (`07700001001` to `07700001012`). The pattern is clearly synthetic and is checked by `SeedDataTests.PhonesAreSyntheticAndUnique`.

## The anchor

History dates are counted back from a fixed anchor, `SeedData.Anchor` = 14 Oct 2026 09:00 (UTC+3). Balances are frozen values, the balances after the history; they are not recomputed from it.

## Users

Every user has one default account (D10). Every seeded PIN is `1234`, stored only as a PBKDF2 hash. The demo user, the one the agent acts for, is user 1 (`SeedData.DemoUserId`).

| Id | Phone | Registered name | Note | Balance (IQD) |
|---|---|---|---|---|
| 1 | 07700001001 | أحمد كريم جاسم | Demo user, Ahmed | 300,000 |
| 2 | 07700001002 | سارة عبد الله حسن | Sara | 1,250,000 |
| 3 | 07700001003 | عمر سعد محمود | Omar, empty balance | 0 |
| 4 | 07700001004 | محمود كريم جاسم | Hammoudi, Ahmed's brother | 80,000 |
| 5 | 07700001005 | علي حسن كاظم | First Ali | 45,000 |
| 6 | 07700001006 | علي كريم عبود | Second Ali | 120,000 |
| 7 | 07700001007 | حسن علي رشيد | Hasan | 10,000 |
| 8 | 07700001008 | حسين علي رشيد | Husain, near name of Hasan | 10,000 |
| 9 | 07700001009 | فاطمة جاسم محمد | Ahmed's mother | 500,000 |
| 10 | 07700001010 | نور صباح فاضل | Noor | 25,000 |
| 11 | 07700001011 | زينب طالب حميد | Known as Umm Muhammad | 60,000 |
| 12 | 07700001012 | كاظم جبار علي | Known as Abu Ali | 15,000 |

Account ids equal user ids (account 1 belongs to user 1, and so on).

## Contacts

Contacts are names a user saved against another user's phone. Unique on (owner, saved name, phone).

| Owner | Saved name | Phone | Points to user |
|---|---|---|---|
| 1 | حمودي | 07700001004 | 4 |
| 1 | علي | 07700001005 | 5 |
| 1 | علي | 07700001006 | 6 (planted duplicate) |
| 1 | حسن | 07700001007 | 7 |
| 1 | حسين | 07700001008 | 8 (planted near name) |
| 1 | ماما | 07700001009 | 9 |
| 1 | نور | 07700001010 | 10 |
| 1 | ام محمد | 07700001011 | 11 |
| 1 | ابو علي | 07700001012 | 12 |
| 1 | سارة | 07700001002 | 2 |
| 2 | احمد | 07700001001 | 1 |
| 2 | عمر | 07700001003 | 3 |

## Aliases

One alias, one account (D15). Unique on (owner, text).

| Owner | Alias | Target account |
|---|---|---|
| 1 | اخوي | Account of user 4 |
| 1 | البيت | Account of user 9 |
| 1 | الدكان | Account of user 12 |

## Billers and bill accounts

| Code | Name |
|---|---|
| electricity | الكهرباء |
| water | الماء |
| internet | نور نت (synthetic internet provider) |

| Owner | Biller | Label | Account number | Due (IQD) | Due date |
|---|---|---|---|---|---|
| 1 | electricity | البيت | 10000000001 | 45,000 | 2026-10-25 |
| 1 | electricity | الدكان | 10000000002 | 120,000 | 2026-10-20 |
| 1 | water | البيت | 20000000001 | 15,000 | none |
| 1 | internet | البيت | 30000000001 | 0 (nothing due) | none |
| 2 | electricity | البيت | 10000000003 | 60,000 | none |

## Store catalogue

| Id | Provider | Type | Name | Face value (IQD) | Price (IQD) |
|---|---|---|---|---|---|
| 1 | Zain | Credit | رصيد زين ٥٠٠٠ | 5,000 | 5,000 |
| 2 | Zain | Credit | رصيد زين ١٠٠٠٠ | 10,000 | 10,000 |
| 3 | Zain | Internet | انترنت زين ١٠ جيجا | 0 | 15,000 |
| 4 | Asiacell | Credit | رصيد آسياسيل ٥٠٠٠ | 5,000 | 5,000 |
| 5 | Asiacell | Credit | رصيد آسياسيل ١٠٠٠٠ | 10,000 | 10,000 |
| 6 | Asiacell | Internet | انترنت آسياسيل ١٠ جيجا | 0 | 15,000 |
| 7 | Korek | Credit | رصيد كورك ٥٠٠٠ | 5,000 | 5,000 |
| 8 | Korek | Internet | انترنت كورك ١٠ جيجا | 0 | 15,000 |

## History

19 transactions, all `Done`. Fees are the `WalletOptions` defaults (transfer 1,000, bill 500, purchase 0); total = amount + fee. `CompletedAt` equals `CreatedAt`. Keys are `seed-u<user>-<nnn>`, references `ZC-` followed by the transaction id padded to 10 digits. Users 3 to 12 have no history.

| Id | Key | Payer | Type | To | Amount (IQD) | Days before anchor |
|---|---|---|---|---|---|---|
| 1 | seed-u1-001 | 1 | Transfer | user 4 | 50,000 | 90 |
| 2 | seed-u1-002 | 1 | Transfer | user 4 | 50,000 | 60 |
| 3 | seed-u1-003 | 1 | Transfer | user 4 | 50,000 | 30 |
| 4 | seed-u1-004 | 1 | Transfer | user 4 | 25,000 | 7 |
| 5 | seed-u1-005 | 1 | Transfer | user 5 | 20,000 | 45 |
| 6 | seed-u1-006 | 1 | Transfer | user 6 | 30,000 | 20 |
| 7 | seed-u1-007 | 1 | Transfer | user 9 | 100,000 | 75 |
| 8 | seed-u1-008 | 1 | Transfer | user 9 | 100,000 | 40 |
| 9 | seed-u1-009 | 1 | Transfer | user 9 | 100,000 | 10 |
| 10 | seed-u1-010 | 1 | Transfer | user 10 | 15,000 | 3 |
| 11 | seed-u1-011 | 1 | Transfer | user 11 | 10,000 | 50 |
| 12 | seed-u1-012 | 1 | Bill | electricity, home (10000000001) | 42,000 | 80 |
| 13 | seed-u1-013 | 1 | Bill | electricity, home (10000000001) | 44,000 | 50 |
| 14 | seed-u1-014 | 1 | Bill | electricity, home (10000000001) | 46,000 | 18 |
| 15 | seed-u1-015 | 1 | Bill | water, home (20000000001) | 15,000 | 48 |
| 16 | seed-u1-016 | 1 | Bill | internet, home (30000000001) | 25,000 | 35 |
| 17 | seed-u1-017 | 1 | Purchase | Zain credit 5,000 (product 1) | 5,000 | 28 |
| 18 | seed-u1-018 | 1 | Purchase | Asiacell internet (product 6) | 15,000 | 12 |
| 19 | seed-u2-001 | 2 | Transfer | user 1 | 200,000 | 15 |

Orders: transaction 17 has purchase code `1234567890001`, transaction 18 has `1234567890002`. Both codes are synthetic.

## Arabic strings for Khalil to review

Every Arabic value in the seed, with its English meaning. Claude drafted these; Khalil reviews them.

Registered names:

| Arabic | English |
|---|---|
| أحمد كريم جاسم | Ahmed Karim Jasim |
| سارة عبد الله حسن | Sara Abdullah Hasan |
| عمر سعد محمود | Omar Saad Mahmoud |
| محمود كريم جاسم | Mahmoud Karim Jasim |
| علي حسن كاظم | Ali Hasan Kadhim |
| علي كريم عبود | Ali Karim Abboud |
| حسن علي رشيد | Hasan Ali Rashid |
| حسين علي رشيد | Husain Ali Rashid |
| فاطمة جاسم محمد | Fatima Jasim Muhammad |
| نور صباح فاضل | Noor Sabah Fadhil |
| زينب طالب حميد | Zainab Talib Hameed |
| كاظم جبار علي | Kadhim Jabbar Ali |

Contact saved names:

| Arabic | English |
|---|---|
| حمودي | Hammoudi, a pet form of Mahmoud |
| علي | Ali |
| حسن | Hasan |
| حسين | Husain |
| ماما | Mum |
| نور | Noor |
| ام محمد | Umm Muhammad, "mother of Muhammad" (written without hamza, as typed on a phone) |
| ابو علي | Abu Ali, "father of Ali" (written without hamza) |
| سارة | Sara |
| احمد | Ahmed (written without hamza) |
| عمر | Omar |

Aliases and bill account labels:

| Arabic | English |
|---|---|
| اخوي | My brother (Iraqi) |
| البيت | The house, home |
| الدكان | The shop |

Billers:

| Arabic | English |
|---|---|
| الكهرباء | Electricity |
| الماء | Water |
| نور نت | Noor Net, a synthetic internet provider |

Products:

| Arabic | English |
|---|---|
| رصيد زين ٥٠٠٠ | Zain credit 5,000 |
| رصيد زين ١٠٠٠٠ | Zain credit 10,000 |
| انترنت زين ١٠ جيجا | Zain internet 10 GB |
| رصيد آسياسيل ٥٠٠٠ | Asiacell credit 5,000 |
| رصيد آسياسيل ١٠٠٠٠ | Asiacell credit 10,000 |
| انترنت آسياسيل ١٠ جيجا | Asiacell internet 10 GB |
| رصيد كورك ٥٠٠٠ | Korek credit 5,000 |
| انترنت كورك ١٠ جيجا | Korek internet 10 GB |
