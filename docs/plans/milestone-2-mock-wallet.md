# Milestone 2: Mock Wallet and Seed Data Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every endpoint of architecture section 7, a fault switch and reset, and a frozen synthetic seed (users, contacts with planted duplicates, aliases, biller accounts, store catalogue, three months of history), proven by API tests: the same idempotency key executes once, a timeout injected after execute leaves a transaction that the by-key lookup finds, reset restores the seed exactly, and a balance never goes below zero.

**Architecture:** `Wallet.MockApi` stays a minimal API over EF Core 10 and SQL Server. Endpoints live in one static class per area under `Endpoints/`, each exposing `MapXxx(this IEndpointRouteBuilder)`. Domain rules (fees, PIN, balance floor, idempotency, faults) live in small services under `Services/`. The seed is code (`SeedData`) so `/admin/reset` reuses it. The agent (milestone 3) will consume the JSON contract; no shared assembly, the OpenAPI document and the `.http` file are the contract.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs with `TypedResults` and ProblemDetails, EF Core 10 SqlServer 10.0.13, options pattern with validation, `TimeProvider`, xunit.v3 4.0.2 with `WebApplicationFactory`, Microsoft.AspNetCore.Mvc.Testing 10.0.13.

**Spec:** `docs/architecture.md` v0.11 (sections 1, 5, 7; decisions D8, D9, D10, D11, D15, D16, D19, D21, D22) and `docs/build-plan.md` milestone 2. Project rules: `CLAUDE.md`. Repository root: `C:\Users\Tech Line\source\repos\Naqoli`.

## Global Constraints

- Six projects only. API tests live in `tests/Agent.Core.Tests/Wallet/`.
- No secrets. Admin endpoints: in Development with no `Wallet:AdminKey` configured they are open; otherwise the request must carry header `X-Wallet-Admin-Key` equal to the configured key, else 403. No key is committed.
- Synthetic data only, per Khalil's decisions of 14 Oct 2026: phones stay in the pattern `^0770000\d{4}$` (documented as deliberate in `data/seed/README.md`); Claude drafts the Arabic seed strings and lists every one in `data/seed/README.md` under "Arabic strings for Khalil to review". Store providers use the operator brand names Zain, Asiacell and Korek as catalogue labels (controller ruling; Khalil may swap). Biller names are synthetic. No real person, document or transaction.
- Everything in code, comments and docs is English. Arabic appears only as seed data values.
- Limits, fees and timers are configuration: `Wallet` options section in `appsettings.json`, bound with `AddOptions<WalletOptions>().BindConfiguration("Wallet").ValidateDataAnnotations().ValidateOnStart()`.
- Follow Microsoft's documented guidance for minimal APIs (`TypedResults`, `Results<T1,T2>`, route groups, ProblemDetails), EF Core (transactions, `ExecuteUpdate`, concurrency, migrations), options validation, `TimeProvider`, and integration tests with `WebApplicationFactory`. Check learn.microsoft.com when unsure.
- Every package added gets a `docs/disclosure.md` line in the same commit. Every new failure mode gets a `docs/failure-modes.md` row.
- Rule 1 groundwork: nothing in the mock wallet executes money except `POST /transactions`. Rule 6: the idempotency key is unique in the database.
- Database tests: a shared fixture probes the test connection string (`ConnectionStrings__WalletTest`, default `Server=localhost;Database=ZainCashWallet_Test;Trusted_Connection=True;TrustServerCertificate=True`, database name must end in `_Test`) with a 3-second timeout. If unreachable, every database test is skipped with the reason "SQL Server not reachable at ...; set ConnectionStrings__WalletTest or start SQL Server". If `ZAINCASH_TEST_SQL=1` is set and the server is unreachable, the tests fail instead of skipping. The existing `SeedApplyTests` moves onto the fixture.
- Startup still never touches the database; `/health` stays database-free.
- Commits: English, imperative, small; end with the two attribution trailer lines given in the dispatch.
- Shell: Windows; quote paths.

## Review Focus

- Two concurrent `POST /transactions` with the same key: exactly one row, one balance change, the second caller gets the stored result or 409 `in_progress`, never a second execution.
- A transfer that would take the balance below zero: 422 `insufficient_funds`, no row change, even under two concurrent transfers that are each individually affordable but not together.
- `TimeoutAfterExecute` fault: money moved and committed before the response hangs; `GET /transactions/by-key/{key}` returns Done while the original request is still hanging.
- Reset after any sequence of operations: every table's rows and identity values equal the fresh seed; the seed hash test proves the seed itself did not drift.
- PIN: three wrong PINs lock the user (`pin_locked`) until reset; a correct PIN never appears in any log line or response body.

---

### Task 1: Domain model, options, seed and reset core

**Files:**
- Create: `src/Wallet.MockApi/Data/Entities/Contact.cs`, `Alias.cs`, `Biller.cs`, `BillAccount.cs`, `Product.cs`, `Order.cs`, `Transaction.cs`; `src/Wallet.MockApi/WalletOptions.cs`; `src/Wallet.MockApi/Services/PinHasher.cs`; `src/Wallet.MockApi/Data/SeedData.cs` (rewrite), `src/Wallet.MockApi/Data/SeedReset.cs`; `src/Wallet.MockApi/Migrations/<timestamp>_WalletDomain.cs` (generated); `tests/Agent.Core.Tests/Wallet/SeedDataTests.cs` (extend), `tests/Agent.Core.Tests/Wallet/SeedHashTests.cs`, `tests/Agent.Core.Tests/Wallet/PinHasherTests.cs`; `data/seed/README.md` (rewrite), `data/seed/seed-hash.txt`.
- Modify: `src/Wallet.MockApi/Data/Entities/User.cs`, `Account.cs`, `src/Wallet.MockApi/Data/WalletDbContext.cs`, `src/Wallet.MockApi/Program.cs` (options registration, `TimeProvider`), `src/Wallet.MockApi/appsettings.json` (Wallet section).

**Interfaces:**
- Produces (namespace `Wallet.MockApi.Data.Entities`):
  - `User { int Id; string Phone; string DisplayName; string PinHash; int PinFailedAttempts; bool PinLocked; List<Account> Accounts; List<Contact> Contacts; List<Alias> Aliases; List<BillAccount> BillAccounts }`. `DisplayName` is the registered name in Arabic.
  - `Account { int Id; int UserId; bool IsDefault; long BalanceIqd }` (unchanged shape).
  - `Contact { int Id; int OwnerUserId; string SavedName; string Phone }` (Phone of another `User`; unique on (OwnerUserId, SavedName, Phone)).
  - `Alias { int Id; int OwnerUserId; string Text; int TargetAccountId }` (unique on (OwnerUserId, Text)).
  - `Biller { int Id; string Code; string Name }` (Code unique: `electricity`, `water`, `internet`).
  - `BillAccount { int Id; int UserId; int BillerId; string Label; string AccountNumber; long AmountDueIqd; DateOnly? DueDate }`.
  - `Product { int Id; string Provider; ProductType Type; string Name; long FaceValueIqd; long PriceIqd }`, `enum ProductType { Credit, Internet }`.
  - `Order { int Id; int UserId; int ProductId; int TransactionId; string Code; DateTimeOffset CreatedAt }`.
  - `Transaction { int Id; string IdempotencyKey; string Reference; int UserId; int FromAccountId; TransactionType Type; TransactionStatus Status; string? ErrorCode; long AmountIqd; long FeeIqd; long TotalIqd; int? ToAccountId; int? BillAccountId; int? ProductId; DateTimeOffset CreatedAt; DateTimeOffset? CompletedAt }`, `enum TransactionType { Transfer, Bill, Purchase }`, `enum TransactionStatus { Executing, Done, Failed }`. `IdempotencyKey` unique index, max length 64. `Reference` unique, format `ZC-` + 10 digits.
- Produces (namespace `Wallet.MockApi`): `WalletOptions { long TransferFeeIqd = 1000; long BillFeeIqd = 500; long PurchaseFeeIqd = 0; long PinRequiredAboveIqd = 1_000_000; int MaxPinAttempts = 3; int FaultDelayMs = 60_000; string? AdminKey }` with `[Range]` attributes; bound from `Wallet` section.
- Produces (namespace `Wallet.MockApi.Services`): `static class PinHasher { string Hash(string pin); bool Verify(string pin, string hash) }` using `Rfc2898DeriveBytes.Pbkdf2` (SHA-256, 100 000 iterations, 16-byte salt, output `pbkdf2$<iterations>$<saltBase64>$<hashBase64>`).
- Produces (namespace `Wallet.MockApi.Data`): `SeedData` with `const int DemoUserId = 1`, `static readonly DateTimeOffset Anchor = new(2026, 10, 14, 9, 0, 0, TimeSpan.FromHours(3))`, `IReadOnlyList<User> Users()`, `IReadOnlyList<Biller> Billers()`, `IReadOnlyList<Product> Products()`, `void Apply(WalletDbContext db)` (idempotent: returns if `db.Users.Any()`; inserts users+accounts, billers, products, then contacts, aliases, bill accounts, transactions, orders, in that order so identities are deterministic), `string ComputeHash()` (SHA-256 over a canonical JSON of all seed rows, hex). `SeedReset.Reset(WalletDbContext db)`: in one database transaction, deletes all rows (Orders, Transactions, BillAccounts, Aliases, Contacts, Accounts, Users, Products, Billers), runs `DBCC CHECKIDENT ('<table>', RESEED, 0)` for each table, then `SeedData.Apply`. Pure data; no HTTP.

Seed content (all synthetic; the demo user is Ahmed, id 1, the one the agent will use):

| Id | Phone | Registered name (Arabic) | PIN | Balance |
|---|---|---|---|---|
| 1 | 07700001001 | أحمد كريم جاسم | 1234 | 300000 |
| 2 | 07700001002 | سارة عبد الله حسن | 1234 | 1250000 |
| 3 | 07700001003 | عمر سعد محمود | 1234 | 0 |
| 4 | 07700001004 | محمود كريم جاسم (Hammoudi, Ahmed's brother) | 1234 | 80000 |
| 5 | 07700001005 | علي حسن كاظم | 1234 | 45000 |
| 6 | 07700001006 | علي كريم عبود | 1234 | 120000 |
| 7 | 07700001007 | حسن علي رشيد | 1234 | 10000 |
| 8 | 07700001008 | حسين علي رشيد | 1234 | 10000 |
| 9 | 07700001009 | فاطمة جاسم محمد (Ahmed's mother) | 1234 | 500000 |
| 10 | 07700001010 | نور صباح فاضل | 1234 | 25000 |
| 11 | 07700001011 | أم محمد (registered: زينب طالب حميد) | 1234 | 60000 |
| 12 | 07700001012 | أبو علي (registered: كاظم جبار علي) | 1234 | 15000 |

Contacts of user 1 (saved name → phone): `حمودي` → 07700001004; `علي` → 07700001005; `علي` → 07700001006 (planted duplicate); `حسن` → 07700001007; `حسين` → 07700001008 (planted similar); `ماما` → 07700001009; `نور` → 07700001010; `ام محمد` → 07700001011; `ابو علي` → 07700001012; `سارة` → 07700001002. User 2 has contacts `احمد` → 07700001001 and `عمر` → 07700001003. Aliases of user 1: `اخوي` → account of user 4; `البيت` → account of user 9; `الدكان` → account of user 12.

Billers: `electricity` `الكهرباء`, `water` `الماء`, `internet` `نور نت` (synthetic ISP). Bill accounts of user 1: electricity `البيت` 11-digit synthetic number `10000000001`, due 45000, due date 2026-10-25; electricity `الدكان` `10000000002`, due 120000, due date 2026-10-20; water `البيت` `20000000001`, due 15000; internet `البيت` `30000000001`, due 0 (nothing due). User 2: electricity `البيت` `10000000003`, due 60000.

Products (Provider, Type, Name, FaceValue, Price): Zain Credit `رصيد زين ٥٠٠٠` 5000/5000; Zain Credit `رصيد زين ١٠٠٠٠` 10000/10000; Zain Internet `انترنت زين ١٠ جيجا` 0/15000; Asiacell Credit `رصيد آسياسيل ٥٠٠٠` 5000/5000; Asiacell Credit `رصيد آسياسيل ١٠٠٠٠` 10000/10000; Asiacell Internet `انترنت آسياسيل ١٠ جيجا` 0/15000; Korek Credit `رصيد كورك ٥٠٠٠` 5000/5000; Korek Internet `انترنت كورك ١٠ جيجا` 0/15000.

History for user 1, three months back from the anchor, all Done, keys `seed-u1-001` onward, references `ZC-0000000001` onward, created at anchor minus the stated days: transfers to 4 (50000, 90 days; 50000, 60 days; 50000, 30 days; 25000, 7 days), to 5 (20000, 45 days), to 6 (30000, 20 days), to 9 (100000, 75 days; 100000, 40 days; 100000, 10 days), to 10 (15000, 3 days), to 11 (10000, 50 days); bills: electricity home 42000 (80 days), 44000 (50 days), 46000 (18 days), water 15000 (48 days), internet 25000 (35 days); purchases: Zain credit 5000 (28 days, code `1234567890001`), Asiacell internet 15000 (12 days, code `1234567890002`), each with an Order. Fees per the options defaults. Seeded balances are the balances after this history; do not recompute, they are frozen values. User 2 history: one transfer to 1 of 200000 (15 days). Users 3 to 12 have no history.

- [ ] **Step 1: Failing tests.** Extend `SeedDataTests`: `DemoUserHasTenContactsIncludingTwoNamedAli`, `EveryContactPhoneBelongsToASeededUser`, `EveryAliasTargetsExactlyOneDefaultAccount`, `DemoUserHasTwoElectricityAccountsAndOneWithNothingDue`, `CatalogueHasEightProductsAcrossThreeProviders`, `DemoUserHistorySpansThreeMonths` (min CreatedAt ≤ Anchor−85 days, max ≥ Anchor−5 days), `NoPinIsStoredInClear` (no `User.PinHash` equals "1234" and every hash starts with `pbkdf2$`). `PinHasherTests`: `VerifyAcceptsTheRightPin`, `VerifyRejectsTheWrongPin`, `HashIsSaltedSoTwoHashesDiffer`. `SeedHashTests`: `SeedHashMatchesCommittedValue` reading `data/seed/seed-hash.txt` (located by walking up from `AppContext.BaseDirectory` to the directory containing `ZainCashAgent.slnx`), with the failure message "The seed changed. If that is intended, update data/seed/seed-hash.txt and re-check every answer key (build plan, milestone 2)."
- [ ] **Step 2: Run** `dotnet test tests/Agent.Core.Tests` → Expected: compile failure, the new types do not exist.
- [ ] **Step 3: Implement** entities, `WalletDbContext` configuration (`OnModelCreating`: tables, unique indexes listed above, max lengths: names 100, phone 15, alias 50, key 64, reference 16, code 32; enums stored as strings via `HasConversion<string>()`; `DateOnly` as date), `WalletOptions`, `PinHasher`, `SeedData`, `SeedReset`, `ComputeHash`. Register in `Program.cs`: `builder.Services.AddSingleton(TimeProvider.System);` and the options. `appsettings.json` gains the `Wallet` section with the defaults above (no AdminKey).
- [ ] **Step 4: Migration.** `dotnet ef migrations add WalletDomain --project src/Wallet.MockApi`. Inspect the generated file: every table, index and string-enum column present; no data loss warnings. Then `dotnet run --project src/Wallet.MockApi -- migrate` against a fresh database: first `dotnet ef database drop --force --project src/Wallet.MockApi`, then migrate → Expected: `Database ready: 12 users, 12 accounts` (update the printed line to also include `, N contacts, N transactions`; the exact new line is `Database ready: 12 users, 12 accounts, 12 contacts, 20 transactions`, update README and CLAUDE.md proposal accordingly).
- [ ] **Step 5: Hash file.** Write `data/seed/seed-hash.txt` by running a one-off: `dotnet run --project src/Wallet.MockApi -- seed-hash` prints the hash (add this second command to `Program.cs`, three lines). Commit the printed value.
- [ ] **Step 6: Run** `dotnet test tests/Agent.Core.Tests` → Expected: all new tests pass; `SeedApplyTests` still skipped unless SQL is reachable.
- [ ] **Step 7:** Rewrite `data/seed/README.md`: the phone-pattern decision (Khalil, 14 Oct 2026), the table of users, contacts, aliases, billers, bill accounts, products and the history summary, and a section "Arabic strings for Khalil to review" listing every Arabic value with its English meaning. State that the seed is frozen by `seed-hash.txt` and tagged `seed-v1` at the end of milestone 2.
- [ ] **Step 8: Commit** `feat(wallet): domain model, synthetic seed, reset core and seed hash`.

### Task 2: Test fixture and read endpoints

**Files:**
- Create: `tests/Agent.Core.Tests/Wallet/WalletApiFixture.cs`, `tests/Agent.Core.Tests/Wallet/WalletApiCollection.cs`, `tests/Agent.Core.Tests/Wallet/ReadEndpointsTests.cs`; `src/Wallet.MockApi/Endpoints/ReadEndpoints.cs`; `src/Wallet.MockApi/Contracts/*.cs` (response records).
- Modify: `src/Wallet.MockApi/Program.cs` (`app.MapReadEndpoints();`), `tests/Agent.Core.Tests/Wallet/SeedApplyTests.cs` (use the fixture's connection string and probe).

**Interfaces:**
- Produces: `WalletApiFixture : IAsyncLifetime` holding `WebApplicationFactory<Program>` configured with `UseSetting("ConnectionStrings:Wallet", TestConnectionString)` and `UseSetting("Wallet:AdminKey", "")`; properties `HttpClient Client`, `bool DatabaseAvailable`, `string SkipReason`; `InitializeAsync` probes with `SqlConnection` (Connect Timeout=3), and if available runs `EnsureDeleted`, `Migrate`, `SeedData.Apply`; `Task ResetAsync()` posts `/admin/reset` (Task 4 adds the endpoint; until then calls `SeedReset.Reset` directly through a scoped `WalletDbContext` from the factory's services). `[CollectionDefinition("wallet-api")]` so all database tests share one database. Every test starts with `Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason)` unless `ZAINCASH_TEST_SQL=1`, in which case the fixture throws in `InitializeAsync` when the server is unreachable.
- Produces JSON contracts (camelCase, namespace `Wallet.MockApi.Contracts`): `UserSummary(int id, string phone, string registeredName)`; `ContactItem(int contactId, string savedName, string phone, string registeredName, int targetAccountId, DateTimeOffset? lastSentAt, long? lastSentAmountIqd)`; `AliasItem(int aliasId, string text, int targetAccountId, string phone, string registeredName)`; `ContactsResponse(IReadOnlyList<ContactItem> contacts, IReadOnlyList<AliasItem> aliases)`; `BillItem(int billAccountId, string billerCode, string billerName, string label, string accountNumber, long amountDueIqd, DateOnly? dueDate)`; `BalanceResponse(int accountId, long balanceIqd, DateTimeOffset asOf)`; `HistoryItem(int transactionId, string reference, string type, string status, long amountIqd, long feeIqd, long totalIqd, string? counterpartyPhone, string? counterpartyName, int? billAccountId, int? productId, DateTimeOffset createdAt)`; `ProductItem(int productId, string provider, string type, string name, long faceValueIqd, long priceIqd)`; `OrderItem(int orderId, int transactionId, int productId, string productName, string code, DateTimeOffset createdAt)`.
- Endpoints (all `TypedResults`, 404 ProblemDetails for unknown user): `GET /users/{id}` → `UserSummary`; `GET /users/by-phone/{phone}` → `UserSummary` (needed by D16 for a typed phone number; controller ruling); `GET /users/{id}/contacts?q=` (q optional, case-insensitive substring on SavedName or alias Text, raw text, no Arabic normalising: that is the agent's job) → `ContactsResponse` with `lastSentAt`/`lastSentAmountIqd` from the latest Done transfer to that phone's default account; `GET /users/{id}/bills` → `BillItem[]`; `GET /users/{id}/balance` → `BalanceResponse` of the default account; `GET /users/{id}/history?to=&since=&take=` (to = phone; since = ISO date-time; take default 50, max 200; newest first) → `HistoryItem[]`; `GET /store/products?provider=&type=&value=` (value = faceValueIqd) → `ProductItem[]`; `GET /users/{id}/orders` → `OrderItem[]` newest first.

- [ ] **Step 1: Failing tests** in `ReadEndpointsTests` (collection "wallet-api"): `UserByPhoneReturnsRegisteredName`, `UnknownUserIs404ProblemDetails`, `ContactsOfDemoUserIncludeTwoAliAndLastSentToHammoudi` (lastSentAmountIqd 25000, lastSentAt = Anchor−7 days), `ContactsQueryFiltersBySubstring` (q=`علي` returns 3 contacts: two `علي` and `ابو علي`; aliases none), `BillsOfDemoUserListFourWithOneNothingDue`, `BalanceOfDemoUserIs300000`, `HistoryToHammoudiHasFourTransfersNewestFirst`, `HistorySinceLimitsResults`, `ProductsFilterByProviderTypeAndValue` (provider=Zain&type=Credit → 2; value=5000 → 3), `OrdersOfDemoUserListTwoWithCodes`.
- [ ] **Step 2: Run** `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Wallet.ReadEndpointsTests"` → Expected: fail (404 or compile error).
- [ ] **Step 3: Implement** fixture, contracts, `ReadEndpoints` with a route group `app.MapGroup("")`? No: plain `MapGet` calls inside `MapReadEndpoints`, with `.WithName(...)` and `.Produces<T>()` metadata so OpenAPI documents them.
- [ ] **Step 4: Run** the class → Expected: all pass with SQL reachable; run once with the server name wrong via `ConnectionStrings__WalletTest=Server=localhost,1;Database=X_Test;...;Connect Timeout=1` → Expected: all database tests Skipped with the reason, `HealthTests` and seed tests still pass.
- [ ] **Step 5: Commit** `feat(wallet): read endpoints and the database test fixture`.

### Task 3: Quotes, transactions and idempotency

**Files:**
- Create: `src/Wallet.MockApi/Services/FeeCalculator.cs`, `src/Wallet.MockApi/Services/TransactionService.cs`, `src/Wallet.MockApi/Services/ReferenceGenerator.cs`, `src/Wallet.MockApi/Services/VoucherCodeGenerator.cs`, `src/Wallet.MockApi/Endpoints/TransactionEndpoints.cs`, `src/Wallet.MockApi/Contracts/QuoteRequest.cs` etc.; `tests/Agent.Core.Tests/Wallet/QuoteTests.cs`, `tests/Agent.Core.Tests/Wallet/TransactionTests.cs`, `tests/Agent.Core.Tests/Wallet/FeeCalculatorTests.cs`.
- Modify: `Program.cs` (`app.MapTransactionEndpoints();`, service registrations).

**Interfaces:**
- Request (both endpoints): `PaymentRequest(int userId, string type /* transfer|bill|purchase */, long? amountIqd, string? toPhone, int? toAccountId, int? billAccountId, int? productId, string? pin)`.
- `POST /quotes` → 200 `QuoteResponse(string type, long amountIqd, long feeIqd, long totalIqd, long balanceBeforeIqd, long balanceAfterIqd, bool pinRequired, string? recipientPhone, string? recipientName, int? billAccountId, int? productId)`; validation failures are 422 ProblemDetails with extension `code` in: `unknown_user`, `unknown_recipient`, `same_account`, `invalid_amount`, `nothing_due`, `bills_paid_in_full` (extension `amountDueIqd` carries the full amount), `unknown_product`, `insufficient_funds` (quotes report it but still return 200 with `balanceAfterIqd` negative? No: quotes return 200 with the figures and a boolean `sufficient`; add `bool sufficient` to `QuoteResponse`. The agent's checks step decides. Only execute refuses.)
- `POST /transactions` requires header `Idempotency-Key` (400 `missing_idempotency_key` if absent or longer than 64). Body `PaymentRequest`. Behaviour, in order: (1) validate as for quotes (422 with the same codes); (2) PIN: if `totalIqd > PinRequiredAboveIqd` and `pin` is null → 422 `pin_required`; if `pin` given: if user `PinLocked` → 423 `pin_locked`; if wrong → increment `PinFailedAttempts`, lock when it reaches `MaxPinAttempts`, 422 `pin_invalid` with extension `attemptsLeft`; if right → reset attempts; (3) insert `Transaction` with status `Executing` and the key; on unique-key violation (`DbUpdateException` wrapping SQL error 2601/2627) read the existing row: if `Done` or `Failed` return its stored result (200 with the same body it originally produced, including the original `code` for a failure, reconstructed from the row); if `Executing` → 409 `in_progress`; (4) execute inside a database transaction: debit `UPDATE Accounts SET BalanceIqd = BalanceIqd - @total WHERE Id = @from AND BalanceIqd >= @total` via `ExecuteUpdate` with the condition in `Where`; if 0 rows → mark the transaction `Failed` with `ErrorCode = insufficient_funds`, commit, return 422 `insufficient_funds`; otherwise credit the recipient account (transfer), set `AmountDueIqd = 0` on the bill account (bill), or create an `Order` with a 13-digit code from `VoucherCodeGenerator` (purchase, `RandomNumberGenerator`); set `Done`, `CompletedAt` from `TimeProvider`, commit; (5) return 201 `TransactionResponse(int transactionId, string reference, string type, string status, long amountIqd, long feeIqd, long totalIqd, long balanceAfterIqd, string? code, DateTimeOffset completedAt)`. The PIN never appears in any response or log; never log the request body.
- `GET /transactions/by-key/{key}` → 200 `TransactionResponse` (status Done, Failed or Executing; `balanceAfterIqd` is the current balance for a replay) or 404.
- `FeeCalculator.Fee(TransactionType type, long amount)` from `WalletOptions`. `ReferenceGenerator.Next()` → `ZC-` + 10 digits from the identity value, zero-padded (so references are deterministic after reset).

- [ ] **Step 1: Failing tests.** `FeeCalculatorTests` (three types). `QuoteTests`: `TransferQuoteToHammoudiMatchesTheSlideExample` (50000 → fee 1000, total 51000, balance after 249000, sufficient true, pinRequired false), `QuoteAboveThresholdRequiresPin` (1500000 from user 2 → pinRequired true), `BillQuoteUsesAmountDueAndRejectsPartial` (bill 1 → 45000; with amount 20000 → 422 `bills_paid_in_full`, `amountDueIqd` 45000), `BillWithNothingDueIs422` (internet account → `nothing_due`), `PurchaseQuoteUsesCataloguePrice`, `UnknownRecipientIs422`, `TransferToSelfIs422`. `TransactionTests` (each test calls `fixture.ResetAsync()` first): `MissingKeyIs400`, `TransferMovesMoneyAndCreditsRecipient`, `SameKeyTwiceExecutesOnce` (second call 200 with identical `transactionId`, balance moved once), `TwoConcurrentCallsWithTheSameKeyExecuteOnce` (`Task.WhenAll` of two posts; exactly one 201 and the other 200 or 409; balance moved once; one row), `InsufficientFundsIs422AndNothingMoves` (user 3), `TwoConcurrentAffordableTransfersNeverOverdraw` (user 7 with 10000: two transfers of 6000 each → one 201, one 422, balance 3000 after fee or whatever the fee makes it; compute with fee 1000: total 7000 each; first succeeds, balance 3000, second insufficient), `BillPaymentZeroesTheAmountDue`, `PurchaseReturnsACodeAndCreatesAnOrder`, `PinRequiredAboveThreshold`, `ThreeWrongPinsLockTheUser`, `ByKeyFindsADoneTransaction`, `ByKeyUnknownIs404`.
- [ ] **Step 2: Run** the three classes → Expected: fail.
- [ ] **Step 3: Implement** services and endpoints. Use `IDbContextTransaction` with `IsolationLevel.ReadCommitted`; rely on the conditional `ExecuteUpdate` for the balance floor and the unique index for idempotency. Map `DbUpdateException` carefully: inspect `InnerException is SqlException { Number: 2601 or 2627 }`.
- [ ] **Step 4: Run** the classes, then the whole suite → Expected: all pass, 0 warnings.
- [ ] **Step 5: Commit** `feat(wallet): quotes, transactions with idempotency, PIN and balance floor`.

### Task 4: Fault switch and admin reset

**Files:**
- Create: `src/Wallet.MockApi/Services/FaultSwitch.cs`, `src/Wallet.MockApi/Endpoints/AdminEndpoints.cs`, `src/Wallet.MockApi/AdminKeyFilter.cs`; `tests/Agent.Core.Tests/Wallet/FaultTests.cs`, `tests/Agent.Core.Tests/Wallet/ResetTests.cs`.
- Modify: `Program.cs`, `TransactionService.cs` (fault hooks), `WalletApiFixture.cs` (`ResetAsync` now posts `/admin/reset`).

**Interfaces:**
- `FaultSwitch` (singleton, thread-safe): `FaultMode Mode { None, Error, Slow, TimeoutBeforeExecute, TimeoutAfterExecute }`, `int DelayMs`, `bool Once`; `FaultState Get()`, `void Set(FaultMode mode, int? delayMs, bool once)`, `FaultState Take()` (returns the current state and clears it if `Once`). Applied only inside `POST /transactions`: `Error` → 500 ProblemDetails `injected_error` before any change; `Slow` → `Task.Delay(DelayMs)` before executing; `TimeoutBeforeExecute` → `Task.Delay(DelayMs)` then 504 `injected_timeout`, nothing executed; `TimeoutAfterExecute` → execute and commit, then `Task.Delay(DelayMs)` then return the normal 201 (the client will have timed out). `DelayMs` defaults to `WalletOptions.FaultDelayMs`.
- `POST /admin/faults` body `FaultRequest(string mode, int? delayMs, bool once = false)` → 200 `FaultState(string mode, int delayMs, bool once)`; `GET /admin/faults` → same; `POST /admin/reset` → 200 `ResetResponse(int users, int accounts, int contacts, int aliases, int billAccounts, int products, int transactions, int orders)` after `SeedReset.Reset` and `FaultSwitch.Set(None)`. All three under a route group `/admin` with an endpoint filter `AdminKeyFilter`: if `WalletOptions.AdminKey` is null or empty and the environment is Development, pass; else require header `X-Wallet-Admin-Key` equal to the key, else 403 ProblemDetails `admin_key_required`.

- [ ] **Step 1: Failing tests.** `FaultTests`: `ErrorFaultReturns500AndMovesNothing`, `SlowFaultDelaysButSucceeds` (delayMs 300, measure ≥ 300 ms), `TimeoutBeforeExecuteMovesNothingAndByKeyIs404`, `TimeoutAfterExecuteIsFoundByKeyWhileTheRequestStillHangs` (set mode with delayMs 5000 once; start the post with a client timeout of 1 s and expect `TaskCanceledException`; then `GET /transactions/by-key/{key}` → Done and the balance moved; finally clear faults), `OnceFaultClearsAfterOneRequest`, `AdminKeyRequiredWhenConfigured` (a second factory with `Wallet:AdminKey=test-key` and environment Production? Simpler: `UseSetting("Wallet:AdminKey","test-key")` on a second `WebApplicationFactory` and assert 403 without the header, 200 with it). `ResetTests`: `ResetRestoresTheSeedExactly` (take a snapshot of every table ordered by Id via EF as JSON, run a transfer, a bill payment, a purchase and three wrong PINs, reset, snapshot again → equal; also `SeedData.ComputeHash()` equals the committed hash), `ResetClearsFaults`.
- [ ] **Step 2: Run** → Expected: fail.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the whole suite → Expected: all pass, 0 warnings. Note the suite's wall time in the report.
- [ ] **Step 5: Commit** `feat(wallet): fault switch and admin reset`.

### Task 5: Docs, contract file and freeze

**Files:**
- Modify: `README.md` (mock wallet API section: endpoints table, error codes, faults, admin key, test database behaviour), `docs/failure-modes.md` (rows: injected error, slow, timeout before execute, timeout after execute → by-key; PIN locked; insufficient funds; duplicate idempotency key in flight → 409), `docs/disclosure.md` (any package added; otherwise unchanged), `src/Wallet.MockApi/Wallet.MockApi.http` (one request per endpoint with sample bodies), `data/seed/README.md` (confirm tag name), `CLAUDE.md` proposal file for the controller (`Database ready` line, reset command).
- No code changes except the `.http` file.

- [ ] **Step 1:** Write the docs. Every endpoint, code and header named must exist in the code (grep to confirm).
- [ ] **Step 2:** `dotnet build` and `dotnet test` once more → Expected: green.
- [ ] **Step 3: Commit** `docs: mock wallet API, failure modes and seed freeze notes`. The controller then tags `seed-v1` and `milestone-1`/`milestone-2` after the security review.
