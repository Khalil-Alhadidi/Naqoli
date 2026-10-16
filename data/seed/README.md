# Seed data

The mock wallet starts from one frozen, synthetic seed: the eight JSON files in this folder. Khalil exported them on 14 Oct 2026 from the test-set workbook v0.15, together with the request sets and `docs/matching-rules.md` (commit 462187f). [data/README.md](../README.md) is the export's own description of every file and field; this file says how the wallet uses them.

Every name, phone number, account number, purchase code and transaction here is invented. None belongs to a real person or company account.

## The files

| File | Rows | What it is |
|---|---|---|
| `users.json` | 5 | The mock users U1 to U5: number, registered name, balance, consent flag, PIN |
| `accounts.json` | 42 | The wallet's own account table: every number that exists, its registered name (or null) and status. 5 user accounts, 33 contact accounts, 4 other accounts (X01 to X04). A number that is not here has no wallet |
| `contacts.json` | 34 | Each user's saved contacts: saved name, optional alias (9 have one), number |
| `billers.json` | 4 | `electricity`, `water`, `internet`, `school_fees`, each with an English and an Arabic name |
| `bills.json` | 11 | What each user owes to which biller: label (or null), subscriber number, amount due |
| `store.json` | 15 | The products: code, provider, provider names, the other name the product is known by (or null), type, face value, price |
| `settings.json` | 1 object | Fees, caps, PIN threshold, timer, fault codes, learning-log reasons. Read with the others; the wallet itself takes its fees and PIN rules from `WalletOptions`, and `SeedSetTests.WalletDefaultsMatchTheSettingsFile` checks that the defaults agree with this file |
| `history.json` | 86 | Three months of transactions, dated as days before the seed is loaded: 57 transfers, 20 bills, 9 purchases |

Two accounts are closed: `07805550120` (behind contact S20 of U1) and `07805550193` (X04, which also has no registered name). U5 has not given consent and has no history.

## Source of truth

The JSON files are the source. The wallet invents no row: every name, number, code, balance and amount comes from them. Claude never edits them; a change is Khalil's, made in the workbook and exported again.

The Arabic text in the files is Khalil's wording. C# holds no Arabic seed text; tests read it from the files through `SeedSet` (`tests/Agent.Core.Tests/Wallet/SeedFacts.cs`).

## Frozen by hash

`seed-hash.txt` holds `SeedHash.Compute` over this folder: SHA-256, lower-case hex, over each file in `SeedFiles.All` order (users, accounts, contacts, billers, bills, store, settings, history), taking the file name, a `\n`, the file's bytes with every `\r` removed, and a `\n`. Removing `\r` makes a CRLF checkout and an LF checkout hash alike. The test `SeedHashTests.SeedHashMatchesCommittedValue` fails if any file changes.

To re-freeze after an intended change (a new export from Khalil):

1. Run `dotnet run --project src/Wallet.MockApi --no-launch-profile -- seed-hash`. It needs no database. The hash is the last line printed; without `--no-launch-profile`, `dotnet run` may print launch-profile and build lines first, so never redirect the whole output into the file.
2. Write that value, with a final newline, to `seed-hash.txt`.
3. Re-check every answer key against the new seed (build plan, milestone 2) and tag the new seed.

`seed-v1` stays in the history as the tag of the first seed, the C# literals of milestone 2, before the export. The seed from these files is tagged `seed-v2` after the milestone 2b review. The answer keys from milestone 5 depend on it, so any later change to the seed means re-checking every answer key.

`seed-v3` (16 Oct 2026) adds one value, `model_refusal`, to `settings.json`'s `learning_log.logged`, right after `model_invalid_reply`, at Khalil's instruction. No other file, no answer key and no figure changes, so no answer key needed re-checking.

`seed-v4` (16 Oct 2026) adds one value, `receive_from`, to `settings.json`'s `learning_log.logged`, at the end of the list after `other_language`, at Khalil's instruction. No other file, no answer key and no figure changes, so no answer key needed re-checking.

## How the wallet loads them

- `src/Wallet.MockApi/Wallet.MockApi.csproj` links `data/seed/*.json` into the build output as `seed/*.json`, so `migrate`, `seed-hash`, the running API and the tests read the same bytes as the repository.
- `Wallet:SeedDirectory` names the folder; the default is `seed` beside the built assembly (`SeedFiles.DefaultDirectory`).
- `SeedSet.Load` reads the eight files and validates them: unique ids; every reference resolves (a user's number is a user account with the same name and balance, contact and history numbers are accounts, bills and history name users, billers, bills and products that exist); allowed values (account status `active` or `closed`, kind `user`, `contact` or `other`, product type `credit` or `internet`, history type `transfer`, `bill` or `purchase`, status `done`, time `HH:mm`); one account per alias per user (D15); nothing negative; every required member present and not null; and no member the models do not know. A failure throws `SeedException`, whose message starts with the file name.
- The `SeedSet` is a singleton resolved at start-up, so a missing or invalid file stops the API before it serves anything. `migrate` prints the message and exits 1. A file saved with a UTF-8 byte order mark is refused by the JSON reader, with the file named.
- `SeedData.Build(seed, now)` maps the files to entities; `SeedData.Apply` inserts them one row at a time, in a fixed order, inside one database transaction, and only when the database has no user yet. `migrate` and `POST /admin/reset` both go through it.

### Insert order and ids

Identity values follow the insert order, so they are the same on every fresh database and after every reset.

| Order | Rows | Ids |
|---|---|---|
| 1 | Users U1 to U5, each with its own account (the default account, D10) | User ids 1 to 5; account ids 1 to 5 (account 1 is U1's) |
| 2 | The other 37 accounts, in `accounts.json` order | Account ids 6 to 42 (6 is X03, 15 is `07805550101` behind S01, 33 is the closed `07805550120`, 35 is the closed X04) |
| 3 | Billers, in file order | 1 `electricity` to 4 `school_fees` |
| 4 | Products, in `store.json` order | 1 `ZN-CR-5` to 15 `KR-NET-30` (6 is `ZN-NET-40`) |
| 5 | Contacts, in `contacts.json` order | 1 `S01` to 34 `N01` |
| 6 | Aliases, one per contact that has one, in contact order | 1 to 9 (1 is S01's) |
| 7 | Bill accounts, in `bills.json` order | 1 `BS1` to 11 `BZ3` |
| 8 | Transactions, oldest first: largest `days_ago` first, then the earlier time, then file order | 1 `H0038` to 86 `H0043` |
| 9 | Orders, one per purchase, in transaction order | 1 to 9 |

An alias points at the account behind its contact's number. A contact is joined to its account by phone; S12 and S13 of U1 are two saved names for one account.

### Codes

Each seeded row keeps its test-set id as a code, so the answer keys can name it: `UserSummary.code` (`U1`), `ContactItem.code` (`S01`), `BillItem.code` (`BS1`), `ProductItem.code` (`ZN-CR-5`), and the code of the four other accounts (`X01` to `X04`, stored but not sent). Seeded transactions carry the idempotency key `seed-<history id>` (`seed-H0001`) and the reference `ZC-` followed by the transaction id padded to 10 digits. Those keys cannot be replayed or looked up: the wallet refuses any caller's key that starts with `seed-` (execute 400 `invalid_idempotency_key`, by-key 404 `unknown_key`). Each seeded row stores the request fingerprint of its own fields and no balance after (see [docs/wallet-api.md](../../docs/wallet-api.md#idempotency)). Seeded purchase codes are derivable and for display only.

The purchase code of a seeded order is `SeedData.OrderCodeFor(history id)`: `9` followed by the digits of the history id, left-padded with zeros to 12 (`H0086` gives `9000000000086`). Purchases made at run time get 13 random digits.

## Dates

`days_ago` counts back from today in Iraq time (UTC+3, no daylight saving, `SeedData.IraqOffset`), and `time` is the clock time in Iraq. A row is stored with `CreatedAt` = `CompletedAt` = that day at that time, with offset `+03:00`; `days_ago` 0 is today. "Today" is the Iraq date of the clock when the seed is applied, so every reset re-dates the history to the current day.

Two consequences:

- Two rows have `days_ago` 0: H0045 at 12:07 and H0043 at 16:36 (both U3). In a run before those times they are later than "now".
- A run that crosses midnight Iraq time sees the seeded dates shift by one day between a reset before midnight and a reset after it.

## PINs and consent

Each user's PIN is in `users.json`. The wallet stores only its PBKDF2 hash; each distinct PIN is hashed once per process and the hash is reused by every reset. `ConsentGiven` comes from `users.json` (false for U5 only). `POST /users/{id}/consent` sets it; a reset restores the file's value.

## Fees in the history

Each history row carries its own fee (transfer 1,000, bill 500, purchase 0, the values in `settings.json`). The seed stores the file's fee, not the configured one, so changing `WalletOptions` never rewrites history; total = amount + fee.

## Phone numbers

The export's numbers follow `07x0555xxxx` (prefixes `0750555`, `0770555`, `0780555`, `0790555`) plus `07715550191` (X02). Khalil's export supersedes the decision of 14 Oct 2026 to keep seeded phones in `0770000xxxx`; no test asserts the old pattern.

## Reset

`POST /admin/reset` on the mock wallet (and `SeedReset.ResetAsync` behind it) deletes every row, restarts every identity at 1 and applies the seed again, dated from the current clock: ids are as in the table above, so the next transaction after a reset is id 87. PIN failure counts, locks and consent are back to the files' values. The endpoint is serialised with payments (`WalletGate`) and clears the fault switch. See [docs/wallet-api.md](../../docs/wallet-api.md#reset).

A database that still holds the milestone 2 seed is cleaned by the migration `AdoptTestSeed`, which deletes every row and restarts the identities before it changes the schema; `migrate` then adds this seed and prints `Database ready: 5 users, 42 accounts, 34 contacts, 86 transactions`.
