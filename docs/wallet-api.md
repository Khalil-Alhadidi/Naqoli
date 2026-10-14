# Mock wallet API

The mock wallet stands in for the ZainCash wallet. The agent talks to it over HTTP only. It is a minimal API in `src/Wallet.MockApi`, backed by SQL Server through EF Core, and it starts from the frozen synthetic seed described in [data/seed/README.md](../data/seed/README.md). The first draft of the endpoint list is section 7 of [architecture.md](architecture.md); this file is the contract as built in milestone 2.

Every request below is in [src/Wallet.MockApi/Wallet.MockApi.http](../src/Wallet.MockApi/Wallet.MockApi.http), ready to send from Visual Studio or VS Code with the REST Client extension.

## Running it

```powershell
dotnet run --project src/Wallet.MockApi -- migrate   # create and seed the database, then exit
dotnet run --project src/Wallet.MockApi              # serve on http://localhost:5276
```

The API serves plain HTTP on loopback and does not redirect to HTTPS. `GET /health` returns `ok`. In Development the OpenAPI document is at `/openapi/v1.json`. The connection string and the database routes are in [README.md](../README.md#database).

## Conventions

- JSON in both directions, camelCase property names (the ASP.NET Core web defaults).
- Amounts are whole Iraqi dinars, as integers, in fields ending `Iqd`.
- Times are `DateTimeOffset` in ISO 8601. Bill due dates are dates only (`2026-10-25`) or `null`.
- Every user has exactly one default account (D10). Payments are paid from it, and a phone number reaches its holder's default account. In the seed, account ids equal user ids.
- Errors are RFC 7807 ProblemDetails (`application/problem+json`). Every error the wallet raises itself carries a machine-readable `code` extension and a fixed English `title`; nothing from the request is echoed back. The agent branches on `code`, never on `title`. The two exceptions are the user 404 of the read endpoints (no `code`, see below) and errors raised by ASP.NET Core itself, such as a body that is not valid JSON (400 without `code`).

## Endpoints

| Method and route | Purpose | Success | Errors |
|---|---|---|---|
| `GET /health` | Liveness | 200 `ok` (text) | none |
| `GET /users/{id}` | One user | 200 `UserSummary` | 404 user |
| `GET /users/by-phone/{phone}` | The user behind a phone number (exact match) | 200 `UserSummary` | 404 user |
| `GET /users/{id}/contacts?q=` | Contacts and aliases, optionally filtered | 200 `ContactsResponse` | 404 user |
| `GET /users/{id}/bills` | Bill accounts with the amount due | 200 `BillItem[]` | 404 user |
| `GET /users/{id}/balance` | Balance of the default account (D22) | 200 `BalanceResponse` | 404 user |
| `GET /users/{id}/history?to=&since=&take=` | Transactions the user paid, newest first | 200 `HistoryItem[]` | 404 user |
| `GET /store/products?provider=&type=&value=` | Store catalogue (D11) | 200 `ProductItem[]` | none |
| `GET /users/{id}/orders` | Past purchases and their purchase codes, newest first | 200 `OrderItem[]` | 404 user |
| `POST /quotes` | Fee, total and balance after. No money moves | 200 `QuoteResponse` | 422 |
| `POST /transactions` | Execute a transfer, bill payment or purchase. Needs `Idempotency-Key` | 201 `TransactionResponse` (new), 200 (replay) | 400, 409, 422, 423, 500, 504 |
| `GET /transactions/by-key/{key}` | Outcome of a key, for the status check after a timeout | 200 `TransactionResponse` | 404 `unknown_key` |
| `GET /admin/faults` | Current fault switch | 200 `FaultState` | 403 |
| `POST /admin/faults` | Set the fault switch | 200 `FaultState` | 403, 422 |
| `POST /admin/reset` | Restore the seed, clear faults | 200 `ResetResponse` | 403 |

`{id}` is an integer; a path with a non-integer id matches no route and returns a plain 404 (no `code`).

### The user 404

Every `/users/...` read route answers a user that does not exist with the same ProblemDetails: status 404, title `User not found`, detail `No wallet user matches the request.` It has no `code` extension. `GET /users/{id}/balance` treats "no default account" as "no such user".

### Read endpoints

`GET /users/{id}` and `GET /users/by-phone/{phone}` return `UserSummary`:

| Field | Type | Notes |
|---|---|---|
| `id` | int | |
| `phone` | string | `07700001001` form |
| `registeredName` | string | The name the wallet holds, in Arabic |

`GET /users/{id}/contacts?q=` returns `ContactsResponse` = `{ contacts: ContactItem[], aliases: AliasItem[] }`. `q` is a raw, case-insensitive substring match on the contact's saved name and on the alias text. The wallet does no Arabic normalising; that is the agent's job. Without `q`, everything is returned. Both lists are ordered by id.

`ContactItem`:

| Field | Type | Notes |
|---|---|---|
| `contactId` | int | |
| `savedName` | string | The name the user saved |
| `phone` | string | |
| `registeredName` | string | The wallet holder behind the phone |
| `targetAccountId` | int | That holder's default account |
| `lastSentAt` | DateTimeOffset or null | Latest `done` transfer from this user to that account (D3) |
| `lastSentAmountIqd` | long or null | Its amount |

`AliasItem` (one alias, one account, D15):

| Field | Type |
|---|---|
| `aliasId` | int |
| `text` | string |
| `targetAccountId` | int |
| `phone` | string |
| `registeredName` | string |

`GET /users/{id}/bills` returns `BillItem[]`, ordered by id:

| Field | Type | Notes |
|---|---|---|
| `billAccountId` | int | Use as `billAccountId` in a payment |
| `billerCode` | string | `electricity`, `water`, `internet` |
| `billerName` | string | Arabic |
| `label` | string | The user's label for the account, Arabic |
| `accountNumber` | string | |
| `amountDueIqd` | long | Zero means nothing is due |
| `dueDate` | date or null | |

`GET /users/{id}/balance` returns `BalanceResponse`: `accountId` (int), `balanceIqd` (long), `asOf` (DateTimeOffset, the server's clock). Code reads it for checks and the balance reply; it never reaches the model (CLAUDE.md, rule 5).

`GET /users/{id}/history` returns `HistoryItem[]`, the transactions the user paid, of every status, newest first (`createdAt`, then id).

| Query | Effect |
|---|---|
| `to` | Phone number. Only transfers to that phone holder's default account |
| `since` | DateTimeOffset. Only transactions created at or after it |
| `take` | Number of items. Default 50, clamped to 1..200 |

| Field | Type | Notes |
|---|---|---|
| `transactionId` | int | |
| `reference` | string | `ZC-` and the id padded to 10 digits |
| `type` | string | `Transfer`, `Bill`, `Purchase` (enum names, PascalCase) |
| `status` | string | `Executing`, `Done`, `Failed` (enum names, PascalCase) |
| `amountIqd`, `feeIqd`, `totalIqd` | long | |
| `counterpartyPhone`, `counterpartyName` | string or null | Transfers only: the recipient |
| `billAccountId` | int or null | Bills only |
| `productId` | int or null | Purchases only |
| `createdAt` | DateTimeOffset | |

Note the casing: history and products use the C# enum names (`Transfer`, `Done`, `Credit`); quotes and transactions use lower case (`transfer`, `done`).

`GET /store/products` returns `ProductItem[]`, ordered by id. All filters are optional and combine:

| Query | Effect |
|---|---|
| `provider` | Case-insensitive exact match: `Zain`, `Asiacell`, `Korek` |
| `type` | `Credit` or `Internet`, any case. An unknown name or a number returns an empty list |
| `value` | Exact face value in IQD. Internet cards have face value 0 |

Fields: `productId` (int), `provider`, `type`, `name` (Arabic), `faceValueIqd` (long), `priceIqd` (long).

`GET /users/{id}/orders` returns `OrderItem[]`, newest first: `orderId`, `transactionId`, `productId`, `productName`, `code` (the purchase code), `createdAt`. Purchase codes are for code and the card only, never for the model (rule 5).

### Payment request

`POST /quotes` and `POST /transactions` take the same body, `PaymentRequest`:

| Field | Type | Used by |
|---|---|---|
| `userId` | int | All. The payer |
| `type` | string | All. Exactly `transfer`, `bill` or `purchase` (lower case) |
| `amountIqd` | long or null | Transfer: required, above 0. Bill: optional, must equal the amount due (D9). Purchase: optional, must equal the price (D11) |
| `toPhone` | string or null | Transfer: recipient by phone (their default account) |
| `toAccountId` | int or null | Transfer: recipient by account. If both are sent, both must name the same account |
| `billAccountId` | int or null | Bill: one of the payer's own bill accounts |
| `productId` | int or null | Purchase |
| `pin` | string or null | Execute only; see PIN rules. Ignored by quotes |

Examples, all for the demo user 1:

```json
{ "userId": 1, "type": "transfer", "amountIqd": 50000, "toPhone": "07700001004" }
{ "userId": 1, "type": "bill", "billAccountId": 1 }
{ "userId": 1, "type": "purchase", "productId": 1 }
```

Fees are flat per type and come from configuration: `Wallet:TransferFeeIqd` (1,000), `Wallet:BillFeeIqd` (500), `Wallet:PurchaseFeeIqd` (0). Total = amount + fee. The amount of a bill and of a purchase always comes from the wallet's data, never from the request.

Checks, in order (the same for quote and execute, in `PaymentValidator`):

1. `type` is one of the three names, else `invalid_type`.
2. The payer exists, else `unknown_user`.
3. Transfer: `amountIqd` above 0, else `invalid_amount`; a recipient is given and exists, else `unknown_recipient`; the recipient is not the payer's own account, else `same_account`.
4. Bill: the bill account exists and belongs to the payer, else `unknown_recipient`; something is due, else `nothing_due`; an `amountIqd` other than the amount due gives `bills_paid_in_full` with `amountDueIqd`.
5. Purchase: the product exists, else `unknown_product`; an `amountIqd` other than the price gives `invalid_amount`.
6. Amount plus fee would overflow: `invalid_amount`.

Funds are not checked here. A quote never refuses for funds; only execute does, at the moment of the debit.

### `POST /quotes`

Returns 200 `QuoteResponse`. No money moves and nothing is written.

| Field | Type | Notes |
|---|---|---|
| `type` | string | `transfer`, `bill`, `purchase` |
| `amountIqd`, `feeIqd`, `totalIqd` | long | |
| `balanceBeforeIqd` | long | Payer's balance now |
| `balanceAfterIqd` | long | Before minus total. Negative when funds are short |
| `sufficient` | bool | Balance covers the total |
| `pinRequired` | bool | Total above `Wallet:PinRequiredAboveIqd` |
| `recipientPhone`, `recipientName` | string or null | Transfers only; the registered name |
| `billAccountId` | int or null | Bills only |
| `productId` | int or null | Purchases only |

Example: user 1 sends 50,000 to Hammoudi (07700001004): fee 1,000, total 51,000, balance 300,000 before and 249,000 after, `sufficient` true, `pinRequired` false (`QuoteTests.TransferQuoteToHammoudiMatchesTheSlideExample`).

### `POST /transactions`

The only endpoint that moves money. Header `Idempotency-Key` is required: not empty or whitespace, at most 64 characters. A missing or longer key is 400 `missing_idempotency_key`, checked before anything else (before the fault switch, so a 400 does not use up a `once` fault).

| Status | When | Body |
|---|---|---|
| 201 Created | The payment ran now. `Location: /transactions/by-key/{key}` | `TransactionResponse`, `status` `done` |
| 200 OK | The key was used before and that payment is `done` (replay) | The stored `TransactionResponse` |
| 400 | `missing_idempotency_key` | ProblemDetails |
| 409 | `in_progress`: the key's payment is still executing | ProblemDetails |
| 422 | Validation codes above, `insufficient_funds`, `pin_required`, `pin_invalid` | ProblemDetails |
| 423 | `pin_locked` | ProblemDetails |
| 500 | `injected_error` (fault switch) | ProblemDetails |
| 504 | `injected_timeout` (fault switch) | ProblemDetails |

`TransactionResponse`:

| Field | Type | Notes |
|---|---|---|
| `transactionId` | int | |
| `reference` | string | `ZC-` and the id padded to 10 digits |
| `type` | string | `transfer`, `bill`, `purchase` |
| `status` | string | `executing`, `done`, `failed` |
| `amountIqd`, `feeIqd`, `totalIqd` | long | |
| `balanceAfterIqd` | long | Payer's balance when this response was made (for a replay or lookup, the balance now) |
| `code` | string or null | Purchase code, purchases only |
| `completedAt` | DateTimeOffset | Completion time, or creation time while executing |

What execute does, in order (`TransactionService.ExecuteAsync`):

1. Applies the fault switch (see Faults).
2. Validates as above. A refusal first looks up the key: if the key was used before, the stored outcome is returned instead (see Idempotency).
3. Checks the PIN if one was sent or one is required.
4. In one database transaction (read committed): inserts the transaction row as `executing` under the key; debits the payer with one conditional update (`balance >= total`); then credits the recipient (transfer), clears the amount due only if it still equals the quoted amount (bill), or creates the order with a new purchase code (purchase); marks the row `done` and commits.
5. If the debit finds the balance short, the row is stored as `failed` with `insufficient_funds` and committed, and 422 is returned. If the bill was already cleared by another payment, the debit is refunded in the same transaction and the row is stored as `failed` with `nothing_due`.

### `GET /transactions/by-key/{key}`

Returns 200 with the `TransactionResponse` stored under the key, whatever its status (`executing`, `done` or `failed`; a failed one carries the amounts and no code). An unknown key, or a key longer than 64 characters, is 404 `unknown_key`. This is how the agent resolves a timeout: the record moves to Checking, never Failed, and the lookup says what happened (CLAUDE.md, rule 6).

## Error codes

Every code is the `code` extension of a ProblemDetails. Source: `PaymentErrors` and `PaymentValidator` (`Services/PaymentValidator.cs`), `FaultErrors` (`Services/FaultSwitch.cs`), `AdminErrors` (`Endpoints/AdminEndpoints.cs`); titles in `Endpoints/WalletProblems.cs`.

| Code | Status | Raised by | Meaning | Extensions |
|---|---|---|---|---|
| `missing_idempotency_key` | 400 | execute | Header missing, blank or over 64 characters | |
| `invalid_type` | 422 | quote, execute | `type` is not `transfer`, `bill` or `purchase` | |
| `unknown_user` | 422 | quote, execute | The payer does not exist | |
| `unknown_recipient` | 422 | quote, execute | Transfer recipient missing or unknown; bill account missing or not the payer's | |
| `same_account` | 422 | quote, execute | Transfer to the payer's own account | |
| `invalid_amount` | 422 | quote, execute | Transfer amount not above 0; purchase amount not the price; total would overflow | |
| `nothing_due` | 422 | quote, execute | The bill has nothing due (also stored when a concurrent payment cleared it first) | |
| `bills_paid_in_full` | 422 | quote, execute | An amount other than the amount due was sent for a bill (D9) | `amountDueIqd` |
| `unknown_product` | 422 | quote, execute | Product missing or unknown | |
| `insufficient_funds` | 422 | execute | The balance does not cover the total at the moment of the debit. Stored against the key | |
| `pin_required` | 422 | execute | Total above the PIN threshold and no PIN sent | |
| `pin_invalid` | 422 | execute | Wrong PIN | `attemptsLeft` |
| `pin_locked` | 423 | execute | The user's PIN is locked | |
| `in_progress` | 409 | execute | The key's payment is still executing | |
| `unknown_key` | 404 | by-key | No transaction has this key | |
| `injected_error` | 500 | execute | Fault switch, mode `error` | |
| `injected_timeout` | 504 | execute | Fault switch, mode `timeoutBeforeExecute` | |
| `admin_key_required` | 403 | `/admin/*` | Admin key missing or wrong, or no key configured outside Development | |
| `invalid_fault_mode` | 422 | `POST /admin/faults` | Unknown mode name | |
| `invalid_fault_delay` | 422 | `POST /admin/faults` | `delayMs` below 0 or above 600,000 | |

The user 404 of the read endpoints has no code (see above).

## Idempotency

- The key is the `Idempotency-Key` header, at most 64 characters, and it is unique in the database (a unique index on `Transactions.IdempotencyKey`). The outcome is keyed by the header only: a retry with the same key and a different body gets the first outcome, not a new payment.
- A key executes at most once. A second call with a used key returns the stored outcome without running again:
  - stored `done`: 200 with the stored `TransactionResponse` (`balanceAfterIqd` is the balance now);
  - stored `failed`: the original 422 with its stored code (`insufficient_funds` or `nothing_due`);
  - still `executing`: 409 `in_progress`.
- The lookup happens even when the retry would now fail validation or the PIN check (for example, a paid bill now has nothing due), so a retry never depends on data the first call changed. A wrong PIN on a known key costs no attempt.
- Two calls with the same key at the same time: one insert wins; the other waits on the unique index until the first transaction ends, gets SQL error 2601/2627, and replays the stored outcome. Money moves once (`TransactionTests.SameKeyTwiceExecutesOnce`, `TwoConcurrentCallsWithTheSameKeyExecuteOnce`).
- Only an attempt that reached the database insert is recorded. Refusals by validation or the PIN rules, and the `error` and `timeoutBeforeExecute` faults, write nothing: the key stays unknown (`by-key` is 404) and a retry with the same key runs normally.
- The agent sends one key per pending record and reuses it on every retry (CLAUDE.md, rule 6).

## PIN rules (D8)

- A PIN is required when the total is above `Wallet:PinRequiredAboveIqd` (1,000,000). The quote reports this as `pinRequired`.
- The PIN is checked on execute only, and only when one is sent or one is required. A PIN sent on a small payment is still verified.
- Every seeded PIN is `1234`, stored only as a salted PBKDF2 hash.
- A wrong PIN is 422 `pin_invalid` with `attemptsLeft`. After `Wallet:MaxPinAttempts` (3) wrong entries the PIN is locked: the call that uses the last attempt still answers `pin_invalid` with `attemptsLeft: 0`, and every later call that checks the PIN, even with the right PIN, is 423 `pin_locked`. A right PIN resets the count while not locked. Only `POST /admin/reset` unlocks.
- The PIN is never echoed in any response and never logged (`PaymentRequest.ToString()` leaves it out; the log lines carry the user id and attempt count only).

## Balance floor

A balance never goes below zero. The debit is one conditional update whose condition is `BalanceIqd >= total`, so two payments racing for the same money cannot both pass; the loser is stored as `failed` with `insufficient_funds` (`TransactionTests.InsufficientFundsIs422AndNothingMoves`, `TwoConcurrentAffordableTransfersNeverOverdraw`). The quote reports `sufficient` and a possibly negative `balanceAfterIqd`, but never refuses.

## Faults

A judge can inject failures into `POST /transactions`, and only there. Quotes, reads, by-key and admin routes are never affected. The switch is process-wide, held in memory, and starts as `none`.

`POST /admin/faults` takes `FaultRequest` and returns the new `FaultState`. `GET /admin/faults` returns the current `FaultState`.

| Field | Type | Notes |
|---|---|---|
| `mode` | string | `none`, `error`, `slow`, `timeoutBeforeExecute`, `timeoutAfterExecute`. Any case on input; reported in camelCase. Anything else is 422 `invalid_fault_mode` |
| `delayMs` | int or null | 0 to 600,000. Null or absent takes `Wallet:FaultDelayMs` (60,000). Out of range is 422 `invalid_fault_delay` |
| `once` | bool | Default false. True clears the fault after one `POST /transactions` takes it. Always false for `none` |

| Mode | What `POST /transactions` does |
|---|---|
| `none` | Runs normally |
| `error` | 500 `injected_error` at once. Nothing is written; by-key is 404 |
| `slow` | Waits `delayMs`, then runs normally (201) |
| `timeoutBeforeExecute` | Waits `delayMs`, then 504 `injected_timeout`. Nothing is written; by-key is 404 `unknown_key` |
| `timeoutAfterExecute` | Runs and commits first, then waits `delayMs` before answering, so a caller with a shorter timeout gives up not knowing the outcome. `GET /transactions/by-key/{key}` already finds the `done` row while the request is still hanging. If the caller disconnects, the wait ends quietly and the committed result stands |

The fault is taken (and, with `once`, cleared under the same lock) before validation, so it applies to any execute call with a valid key header, whatever the body. Delays honour the request's cancellation and sit outside the reset gate, so a hanging request never blocks a reset.

Tests: `FaultTests.ErrorFaultReturns500AndMovesNothing`, `SlowFaultDelaysButSucceeds`, `TimeoutBeforeExecuteMovesNothingAndByKeyIs404`, `TimeoutAfterExecuteIsFoundByKeyWhileTheRequestStillHangs`, `OnceFaultClearsAfterOneRequest`.

## Reset

`POST /admin/reset` (no body) returns the wallet to the frozen seed exactly, and clears the fault switch to `none`.

- In one database transaction it deletes every row, reseeds each identity column so ids start where the seed leaves them (the next transaction after a reset is id 20), and applies the seed. PIN failure counts and locks are back to the seed (unlocked).
- It is serialised with payments: it waits for payments in progress to finish, holds new ones back until it is done, and never runs alongside another reset (`WalletGate`). Fault delays are outside the gate.
- It returns `ResetResponse`, the row counts read back from the database. For the seed these are:

```json
{ "users": 12, "accounts": 12, "contacts": 12, "aliases": 3, "billAccounts": 5, "products": 8, "transactions": 19, "orders": 2 }
```

Tests: `ResetTests.ResetRestoresTheSeedExactly` (compares every table before and after, and the next transaction id), `ResetTests.ResetClearsFaults`.

## Admin key

The `/admin` routes are guarded by `AdminKeyFilter`:

- With no `Wallet:AdminKey` configured, they are open in the Development environment (what `dotnet run` uses) and closed with 403 `admin_key_required` in any other environment.
- With a key configured, every `/admin` request must carry it in the `X-Wallet-Admin-Key` header; a missing or wrong key is 403 `admin_key_required`. The comparison is constant-time.
- The key is never committed. Set it in user secrets or the environment:

```powershell
dotnet user-secrets set "Wallet:AdminKey" "<choose-a-key>" --project src/Wallet.MockApi
# or
$env:Wallet__AdminKey = '<choose-a-key>'
```

## Configuration

The `Wallet` section of `src/Wallet.MockApi/appsettings.json`, bound to `WalletOptions` and validated at start-up:

| Setting | Default | Range |
|---|---|---|
| `TransferFeeIqd` | 1,000 | 0 to 1,000,000 |
| `BillFeeIqd` | 500 | 0 to 1,000,000 |
| `PurchaseFeeIqd` | 0 | 0 to 1,000,000 |
| `PinRequiredAboveIqd` | 1,000,000 | 0 to 100,000,000 |
| `MaxPinAttempts` | 3 | 1 to 10 |
| `FaultDelayMs` | 60,000 | 0 to 600,000 |
| `AdminKey` | not set | user secrets or environment only |

## Tests

The API tests are in `tests/Agent.Core.Tests/Wallet/` and run the API in process (`WebApplicationFactory`) against a real SQL Server test database. They probe the server once (3-second timeout) and report Skipped when it does not answer, unless `ZAINCASH_TEST_SQL=1` is set, in which case they fail. They use `ConnectionStrings__WalletTest` (default: the local default instance, database `ZainCashWallet_Test`), refuse any database name that does not end in `_Test`, and never read the `Wallet` connection string. Details in [README.md](../README.md#commands).
