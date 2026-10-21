# Milestone 2b: Adopt the Test-Set Seed Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The mock wallet loads its seed from the exported test-set files `data/seed/*.json` (commit 462187f), so that every answer key in `data/requests.dev.jsonl` is arithmetic on the wallet's real state; the seed is re-frozen by a hash over those files and tagged `seed-v2`.

**Architecture:** `Wallet.MockApi` keeps its endpoints, idempotency path, PIN lockout, fault switch and reset gate. What changes is where the rows come from: a `SeedSet` is read and validated from the JSON files once at startup (and by `migrate`, `seed-hash` and the tests), `SeedData.Build` turns it into entities with history dated relative to "now" in Iraq time, and `SeedData.Apply` inserts in a fixed order so identities are deterministic. The domain gains what the export has and the old seed lacked: an account table with registered names and a closed status, accounts that belong to no user, per-user PINs, a consent flag, codes (`U1`, `S01`, `BS1`, `ZN-CR-5`) that the eval runner will use, and a fourth biller. The old C# seed (`SeedStrings`, the literal rows in `SeedData`) is removed.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 10 SqlServer 10.0.13, `System.Text.Json` with `JsonNamingPolicy.SnakeCaseLower`, options pattern, `TimeProvider`, xunit.v3 4.0.2 with `WebApplicationFactory`.

**Spec:** `docs/architecture.md` v0.11 (sections 6 and 7; D8, D9, D10, D11, D15, D16, D22), `docs/build-plan.md` milestone 2 ("Done when": seed frozen and tagged; answer keys depend on it), `data/README.md` (the export's own description of its files), `docs/matching-rules.md` (fees, limits, fault codes). Project rules: `CLAUDE.md`. Repository root: the clone folder.

## Global Constraints

- Six projects only. API tests live in `tests/Agent.Core.Tests/Wallet/`. Rule 7: nothing in this milestone reads, opens, counts or copies `data/requests.heldout.jsonl`; `data/requests.dev.jsonl` is not read by code in this milestone either (milestone 5 does that).
- The seed files are Khalil's export and are not edited. If a file seems wrong, stop and report; do not fix the data. `data/seed/seed-hash.txt` is the only file under `data/seed/` this milestone writes.
- Arabic appears in C# nowhere after this milestone (controller updates the CLAUDE.md line). Tests take Arabic values from the loaded `SeedSet` by code (`seed.Contacts.Single(c => c.Id == "S01").SavedName`), never as literals.
- Synthetic data only. The export's phone pattern (`07x0555xxxx` and `07715550191`) replaces the 14 Oct 2026 decision to use `0770000xxxx`; the new pattern is documented as Khalil's, and no test asserts the old one.
- Limits, fees and timers stay configuration (`Wallet` options section). `settings.json` is the test set's record of the same values; a test pins the wallet's defaults to it (fees and PIN only; caps, timer and the rest belong to the agent, milestone 3 and later).
- Follow Microsoft's documented guidance: `System.Text.Json` deserialisation with explicit options, `IOptions` validation on start, EF Core migrations that carry data changes in `migrationBuilder.Sql`, `TimeProvider` for "now", `TypedResults`, integration tests with `WebApplicationFactory`. Check learn.microsoft.com when unsure.
- Every new failure mode gets a `docs/failure-modes.md` row. No new packages are expected; if one is added it gets a `docs/disclosure.md` line in the same commit.
- Rule 1 groundwork holds: nothing in the mock wallet moves money except `POST /transactions`. Rule 6 holds: the idempotency key stays unique in the database.
- Database tests: unchanged fixture rules (`ConnectionStrings__WalletTest`, name ends `_Test`, skip when unreachable, `ZAINCASH_TEST_SQL=1` to demand). Every test that touches the database starts with `await fixture.ResetAsync()` or lives in `SeedApplyTests` and resets in `finally`.
- Commits: English, imperative, small; end with the two attribution trailer lines given in the dispatch.
- Shell: Windows; quote paths. Build with `dotnet build`; run one class with `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Wallet.<Class>"`.

## Review Focus

- A seed file edited by hand (one byte) fails `SeedHashTests` with a message that names `data/seed/seed-hash.txt`, and a CRLF checkout (the repo has `* text=auto`) hashes the same as an LF checkout. Test in Task 1.
- Reset at a different time of day keeps "days ago" dates: every seeded row's `CreatedAt` falls on today's Iraq date minus `days_ago`, at the file's clock time. Test in Task 2.
- A transfer to a closed account (contact S20, phone `07805550120`) is refused with 422 `account_closed` at quote and at execute, nothing moves, and a retry with the same key gets the same refusal. Test in Task 3.
- A typed number with no account gives 404 `unknown_account` on the lookup and 422 `unknown_recipient` on a quote; the closed account `07805550193` with no registered name gives 200 with `registeredName` null and `status` `closed`. Test in Task 3.
- `migrate` on a database still holding the old twelve-user seed wipes it and loads the new seed once; `migrate` twice on the new seed does not duplicate rows. Test in Task 2.

---

### Task 1: Seed files as the source: models, loader, validation and hash

**Files:**
- Create: `src/Wallet.MockApi/Data/Seed/SeedModels.cs`, `src/Wallet.MockApi/Data/Seed/SeedSet.cs`, `src/Wallet.MockApi/Data/Seed/SeedHash.cs`; `tests/Agent.Core.Tests/Wallet/SeedSetTests.cs`.
- Modify: `src/Wallet.MockApi/Wallet.MockApi.csproj` (link the JSON files into the output), `src/Wallet.MockApi/WalletOptions.cs` (`SeedDirectory`), `src/Wallet.MockApi/Program.cs` (`seed-hash` command computes from files), `tests/Agent.Core.Tests/Wallet/SeedHashTests.cs` (rewrite), `data/seed/seed-hash.txt` (new value).
- Leave `SeedData.cs`, `SeedStrings.cs` and the entities untouched in this task; they are replaced in Task 2. The old `SeedData.ComputeHash` is no longer called by anything after this task.

**Interfaces:**
- Produces (namespace `Wallet.MockApi.Data.Seed`):
  - `SeedFiles`: `const string Users = "users.json", Accounts = "accounts.json", Contacts = "contacts.json", Billers = "billers.json", Bills = "bills.json", Store = "store.json", Settings = "settings.json", History = "history.json"`; `static readonly string[] All` in that order; `static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "seed")`.
  - Records (all `sealed record`, properties bound from snake_case JSON; every property in the files is modelled so that `JsonUnmappedMemberHandling.Disallow` catches export drift):
    - `SeedUser(string Id, string Name, string Number, long Balance, bool ConsentGiven, string Pin, string? RoleInTestSet)`
    - `SeedAccount(string Number, string? RegisteredName, string Status, long Balance, string Kind, string? Id)` (`Status` is `active` or `closed`; `Kind` is `user`, `contact` or `other`; `Id` is set for `other` only)
    - `SeedContact(string User, string Id, string SavedName, string? Alias, string Number, string? Why)`
    - `SeedBiller(string Code, string NameEn, string NameAr)`
    - `SeedBill(string User, string Id, string Biller, string? Label, string SubscriberNumber, long AmountDue, string? Note)`
    - `SeedProduct(string Id, string Provider, string ProviderNameEn, string ProviderNameAr, string? AlsoCalled, string Type, long FaceValue, long Price)`
    - `SeedHistoryRow(string Id, string User, int DaysAgo, string Time, string Type, long Amount, long Fee, string Status, string? ToNumber, string? Contact, string? Bill, string? Biller, string? Product)`
    - `SeedSettings(string Currency, SeedFees Fees, long MinAmount, SeedPin Pin, SeedCaps Caps, string[] CheckOrder, int CardTimerSeconds, int MaxRequestsPerMessage, int MaxProductsInOneQuestion, int RepeatWarningWindowMinutes, int HistoryDays, SeedFault[] Faults, SeedLearningLog LearningLog)`, `SeedFees(long Transfer, long Bill, long Purchase)`, `SeedPin(long RequiredAbove, int Attempts)`, `SeedCaps(long PerTransaction, long PerDay, string Counts)`, `SeedFault(string Code, string Description)`, `SeedLearningLog(string[] Logged, string[] NotLogged)`.
  - `sealed class SeedSet { IReadOnlyList<SeedUser> Users; IReadOnlyList<SeedAccount> Accounts; IReadOnlyList<SeedContact> Contacts; IReadOnlyList<SeedBiller> Billers; IReadOnlyList<SeedBill> Bills; IReadOnlyList<SeedProduct> Products; SeedSettings Settings; IReadOnlyList<SeedHistoryRow> History; string Directory; static SeedSet Load(string directory) }`. `Load` reads the eight files (throws `SeedException : Exception` naming the file and the problem when one is missing, is not valid JSON, has an unknown property, or fails a check below) and validates:
    1. User ids, account numbers, contact ids, biller codes, bill ids, product ids and history ids are each unique.
    2. Every user's number is an account of kind `user` with the same registered name and balance.
    3. Every contact's `User`, every bill's `User` and every history row's `User` is a user id; every contact's number is an account number.
    4. Every bill's `Biller` is a biller code; every history `Bill` is a bill id, every history `Product` is a product id, every history `ToNumber` is an account number, and a transfer row has `ToNumber`, a bill row has `Bill`, a purchase row has `Product`.
    5. Account `Status` is `active` or `closed`; `Kind` is `user`, `contact` or `other`; product `Type` is `credit` or `internet`; history `Type` is `transfer`, `bill` or `purchase` and `Status` is `done`; `Time` parses as `HH:mm`.
    6. No contact alias repeats within one user (D15: one alias, one account); no (user, saved name, number) repeats.
    7. Balances and amounts are not negative; `DaysAgo` is 0 or more.
  - `static class SeedHash { static string Compute(string directory) }`: SHA-256 (lower-case hex) over, for each file name in `SeedFiles.All` order, the UTF-8 bytes of the file name, a `\n`, and the file's bytes with every `\r` removed (so CRLF and LF checkouts hash alike), then a `\n`. Throws `FileNotFoundException` naming the file when one is missing.
- Produces (namespace `Wallet.MockApi`): `WalletOptions.SeedDirectory` (`string`, default `SeedFiles.DefaultDirectory`, `[Required]`).
- Consumes: nothing from other tasks.

JSON options (one static instance in `SeedSet`):

```csharp
private static readonly JsonSerializerOptions Json = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false,
};
```

Note: `JsonUnmappedMemberHandling.Disallow` is applied per type with `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` on each record, or globally through the options property `UnmappedMemberHandling`; use the options property. The records are positional; `System.Text.Json` binds the constructor by parameter name through the naming policy. Keep every property non-nullable where the files always carry a value, nullable where the files carry `null`.

csproj link (so the API's output has `seed/*.json` and `migrate`, `seed-hash`, the running app and the tests all read the same bytes as the repository):

```xml
<ItemGroup>
  <Content Include="..\..\data\seed\*.json" Link="seed\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

`Program.cs` command:

```csharp
// "seed-hash [directory]": print the hash of the seed files and exit. Never touches the database.
if (args is ["seed-hash", ..])
{
    var directory = args.Length > 1 ? args[1] : SeedFiles.DefaultDirectory;
    Console.WriteLine(SeedHash.Compute(directory));
    return 0;
}
```

- [ ] **Step 1: Write the failing tests** (`SeedSetTests`, no database; `SeedHashTests` rewritten)

```csharp
public class SeedSetTests
{
    internal static string SeedDirectory => Path.Combine(SeedHashTests.RepositoryRoot(), "data", "seed");
    internal static SeedSet Seed => SeedSet.Load(SeedDirectory);

    [Fact] public void LoadsTheEightFiles()
    {
        var seed = Seed;
        Assert.Equal(5, seed.Users.Count);
        Assert.Equal(42, seed.Accounts.Count);
        Assert.Equal(34, seed.Contacts.Count);
        Assert.Equal(4, seed.Billers.Count);
        Assert.Equal(11, seed.Bills.Count);
        Assert.Equal(15, seed.Products.Count);
        Assert.Equal(86, seed.History.Count);
        Assert.Equal("IQD", seed.Settings.Currency);
    }

    [Fact] public void EveryUserIsAnActiveAccountWithTheSameNameAndBalance() { /* assert through seed.Accounts by number, kind "user" */ }
    [Fact] public void EveryContactNumberIsAnAccount() { }
    [Fact] public void EveryHistoryRowPointsAtSeedRows() { /* ToNumber, Bill, Product, User */ }
    [Fact] public void TwoAccountsAreClosed() => Assert.Equal(["07805550120", "07805550193"], Seed.Accounts.Where(a => a.Status == "closed").Select(a => a.Number).Order());
    [Fact] public void NineContactsHaveAnAlias() => Assert.Equal(9, Seed.Contacts.Count(c => c.Alias is not null));
    [Fact] public void OnlyOneUserHasNotGivenConsent() => Assert.Equal("U5", Assert.Single(Seed.Users, u => !u.ConsentGiven).Id);
    [Fact] public void HistoryFeesMatchTheSettings()
    {
        var seed = Seed;
        Assert.All(seed.History, h => Assert.Equal(h.Type switch { "transfer" => seed.Settings.Fees.Transfer, "bill" => seed.Settings.Fees.Bill, _ => seed.Settings.Fees.Purchase }, h.Fee));
    }
    [Fact] public void WalletDefaultsMatchTheSettingsFile()
    {
        var options = new WalletOptions();
        var s = Seed.Settings;
        Assert.Equal(s.Fees.Transfer, options.TransferFeeIqd);
        Assert.Equal(s.Fees.Bill, options.BillFeeIqd);
        Assert.Equal(s.Fees.Purchase, options.PurchaseFeeIqd);
        Assert.Equal(s.Pin.RequiredAbove, options.PinRequiredAboveIqd);
        Assert.Equal(s.Pin.Attempts, options.MaxPinAttempts);
    }
    [Fact] public void MissingFileNamesTheFile()
    {
        var dir = Directory.CreateTempSubdirectory();
        try { var ex = Assert.Throws<SeedException>(() => SeedSet.Load(dir.FullName)); Assert.Contains("users.json", ex.Message); }
        finally { dir.Delete(true); }
    }
    [Fact] public void UnknownPropertyIsRejected()
    {
        // Copy the seed to a temp dir, add "extra": 1 to the first user object, expect SeedException naming users.json.
    }
    [Fact] public void DanglingContactNumberIsRejected()
    {
        // Copy the seed to a temp dir, change S01's number to 07805559999, expect SeedException naming contacts.json and S01.
    }
}

public class SeedHashTests
{
    [Fact] public void SeedHashMatchesCommittedValue()
    {
        var committed = File.ReadAllText(Path.Combine(RepositoryRoot(), "data", "seed", "seed-hash.txt")).Trim();
        Assert.True(string.Equals(committed, SeedHash.Compute(SeedSetTests.SeedDirectory), StringComparison.Ordinal),
            "The seed files changed. If that is intended, run `dotnet run --project src/Wallet.MockApi -- seed-hash`, write the value to data/seed/seed-hash.txt and re-check every answer key.");
    }
    [Fact] public void OutputCopyHashesTheSame()
        => Assert.Equal(SeedHash.Compute(SeedSetTests.SeedDirectory), SeedHash.Compute(SeedFiles.DefaultDirectory));
    [Fact] public void CrlfAndLfHashTheSame() { /* temp copy with \n -> \r\n in every file; same hash */ }
    [Fact] public void OneChangedByteChangesTheHash() { /* temp copy; change 750000 to 750001 in users.json; different hash */ }
    internal static string RepositoryRoot() { /* unchanged: walk up to ZainCashAgent.slnx */ }
}
```

Write the bodies in full (the comments above say what each asserts). Use `Directory.CreateTempSubdirectory()` and copy with `File.Copy` for the temp-directory tests; delete in `finally`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Wallet.SeedSetTests"`
Expected: build error (`SeedSet` not defined).

- [ ] **Step 3: Implement** `SeedModels.cs`, `SeedSet.cs` (`Load`, the seven validation groups, `SeedException`), `SeedHash.cs`, the csproj link, `WalletOptions.SeedDirectory`, the `seed-hash` command. Read each file with `File.ReadAllBytes` and `JsonSerializer.Deserialize<List<T>>(bytes, Json)` (settings: `Deserialize<SeedSettings>`); wrap `JsonException` in `SeedException($"{file}: {ex.Message}")`.

- [ ] **Step 4: Write the new hash**

Run: `dotnet run --project src/Wallet.MockApi -- seed-hash` and write the printed value to `data/seed/seed-hash.txt` (one line, no trailing spaces). Run `dotnet build` and confirm `src/Wallet.MockApi/bin/Debug/net10.0/seed/users.json` exists.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Wallet.SeedSetTests"` and the same for `SeedHashTests`. Expected: all pass. Then `dotnet build` with 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/Wallet.MockApi/Data/Seed src/Wallet.MockApi/Wallet.MockApi.csproj src/Wallet.MockApi/WalletOptions.cs src/Wallet.MockApi/Program.cs tests/Agent.Core.Tests/Wallet/SeedSetTests.cs tests/Agent.Core.Tests/Wallet/SeedHashTests.cs data/seed/seed-hash.txt
git commit -m "feat(wallet): load and validate the test-set seed files; hash over the files"
```

---

### Task 2: Domain, migration, seed build and reset on the new seed

**Files:**
- Modify: `src/Wallet.MockApi/Data/Entities/User.cs`, `Account.cs`, `Contact.cs`, `Biller.cs`, `BillAccount.cs`, `Product.cs`; `src/Wallet.MockApi/Data/WalletDbContext.cs`; `src/Wallet.MockApi/Data/SeedData.cs` (rewrite: build from `SeedSet`), `src/Wallet.MockApi/Data/SeedReset.cs`; `src/Wallet.MockApi/Program.cs` (register `SeedSet`, `migrate` output); `src/Wallet.MockApi/Services/PaymentValidator.cs`, `src/Wallet.MockApi/Endpoints/ReadEndpoints.cs` (compile-level: recipient and counterparty come from `Account`, product has no `Name`, bill has no `DueDate`); `src/Wallet.MockApi/Contracts/BillItem.cs`, `ProductItem.cs`, `OrderItem.cs`; `src/Wallet.MockApi/Endpoints/AdminEndpoints.cs` (reset takes the seed and the clock).
- Create: `src/Wallet.MockApi/Migrations/<timestamp>_AdoptTestSeed.cs` (generated, then edited to wipe rows first).
- Delete: `src/Wallet.MockApi/Data/SeedStrings.cs`, `tests/Agent.Core.Tests/Wallet/SeedDataTests.cs` (its checks now live in `SeedSetTests` and `SeedApplyTests`).
- Tests: `tests/Agent.Core.Tests/Wallet/WalletApiFixture.cs`, `SeedApplyTests.cs` (rewrite), `ResetTests.cs`, `TransactionTests.cs`, `QuoteTests.cs`, `ReadEndpointsTests.cs`, `FaultTests.cs` (facts updated per the table below; shapes of tests unchanged).

**Interfaces:**
- Consumes: `SeedSet`, `SeedFiles`, `WalletOptions.SeedDirectory` from Task 1.
- Produces (namespace `Wallet.MockApi.Data.Entities`):
  - `User { int Id; string Code; string Phone; string DisplayName; string PinHash; int PinFailedAttempts; bool PinLocked; bool ConsentGiven; List<Account> Accounts; List<Contact> Contacts; List<Alias> Aliases; List<BillAccount> BillAccounts }`. `Code` unique, max 8 (`U1`).
  - `Account { int Id; string? Code; string Phone; string? RegisteredName; AccountStatus Status; int? UserId; User? User; bool IsDefault; long BalanceIqd }`, `enum AccountStatus { Active, Closed }` stored as string. `Phone` unique. `UserId` null for accounts of kind `contact` and `other`; a user's account has `IsDefault = true`. `Code` is `X01`..`X04` for the `other` accounts, else null.
  - `Contact { int Id; string Code; int OwnerUserId; string SavedName; string Phone }`. `Code` unique (`S01`). `Phone` is an `Account.Phone`.
  - `Alias` unchanged (`Text` max length raised to 100).
  - `Biller { int Id; string Code; string Name; string NameEn }` (`Name` is the Arabic name).
  - `BillAccount { int Id; string Code; int UserId; int BillerId; string? Label; string AccountNumber; long AmountDueIqd }` (`DueDate` removed; `AccountNumber` holds the subscriber number, max 32).
  - `Product { int Id; string Code; string Provider; string ProviderNameEn; string ProviderNameAr; string? AlsoCalled; ProductType Type; long FaceValueIqd; long PriceIqd }` (`Name` removed; `Provider` is the lower-case code `zain`, `asiacell`, `korek`; `Code` unique, max 16).
  - `Order`, `Transaction`, `TransactionType`, `TransactionStatus` unchanged.
- Produces (namespace `Wallet.MockApi.Data`):
  - `static class SeedData`: `static readonly TimeSpan IraqOffset = TimeSpan.FromHours(3)`; `static DateOnly TodayIn(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(IraqOffset).DateTime)`; `static DateTimeOffset At(DateOnly today, int daysAgo, string time)` (the date `today.AddDays(-daysAgo)` at `time` (`HH:mm`), offset `IraqOffset`); `static SeedGraph Build(SeedSet seed, DateTimeOffset now)`; `static void Apply(WalletDbContext db, SeedSet seed, DateTimeOffset now)` (returns if `db.Users.Any()`; otherwise inserts in the order below inside a transaction unless one is open); `static string OrderCodeFor(string historyId)` = `"9" + digits of the id, left-padded with zeros to 12` (`H0086` → `9000000000086`).
  - `sealed record SeedGraph(List<User> Users, List<Account> Accounts, List<Biller> Billers, List<Product> Products, List<Contact> Contacts, List<Alias> Aliases, List<BillAccount> BillAccounts, List<Transaction> Transactions, List<Order> Orders)`.
  - `SeedReset.ResetAsync(WalletDbContext db, SeedSet seed, DateTimeOffset now, CancellationToken ct)`.
  - PIN hashes: cached per process in a `ConcurrentDictionary<string, string>` keyed by PIN value (five distinct PINs, hashed once each).
- Insert order (identities are deterministic because of it): users with their own account (users.json order, so user ids 1..5 are U1..U5 and account ids 1..5 are their accounts), then the other accounts in accounts.json order (ids 6..42), billers (billers.json order), products (store.json order: `ZN-CR-5` is 1, `ZN-NET-40` is 6, `KR-NET-30` is 15), contacts (contacts.json order: `S01` is 1, `N01` is 34), aliases (one per contact with a non-null alias, contacts order), bill accounts (bills.json order: `BS1` is 1, `BZ3` is 11), transactions in chronological order (largest `days_ago` first, then earlier `time` first, then file order; ids 1..86), orders (one per purchase row, in transaction order; 9 rows).
- Transaction rows from history: `IdempotencyKey = "seed-" + row.Id` (`seed-H0043`), `Reference = "ZC-" + id padded to 10`, `User`/`FromAccount` = the user and its account, `Type`, `Status = Done`, `AmountIqd = Amount`, `FeeIqd = Fee` (from the file; not from `WalletOptions`), `TotalIqd = Amount + Fee`, `ToAccount` by `ToNumber`, `BillAccount` by `Bill` code, `Product` by `Product` code, `CreatedAt = CompletedAt = At(today, DaysAgo, Time)`.
- `migrate` prints `Database ready: 5 users, 42 accounts, 34 contacts, 86 transactions`.
- `Program.cs` registers `builder.Services.AddSingleton(sp => SeedSet.Load(sp.GetRequiredService<IOptions<WalletOptions>>().Value.SeedDirectory))` and resolves it once at startup (after `Build()`, before `Run()`: `app.Services.GetRequiredService<SeedSet>()`) so a missing or invalid seed fails the start with the `SeedException` message, not the first reset.
- The migration `AdoptTestSeed` starts with `migrationBuilder.Sql` deletes in dependency order (`Orders`, `Transactions`, `BillAccounts`, `Aliases`, `Contacts`, `Accounts`, `Users`, `Products`, `Billers`) before any `AlterColumn`/`AddColumn`, so a database holding the old seed migrates cleanly and `migrate` then seeds it. Generate with `dotnet ef migrations add AdoptTestSeed --project src/Wallet.MockApi`, then edit the `Up` method to put the deletes first; leave `Down` as generated.

Test facts after this task (all tests use these; ids are the insert order above):

| Fact | Value |
|---|---|
| Demo user | U1, id 1, phone `07805550001`, account id 1, balance 750,000, PIN from `seed.Users` (`2580`) |
| Hammoudi | contact S01, phone `07805550101`, account id 6 or later (look it up by phone in tests, never hard-code), balance 100,000, registered name from `seed.Accounts` |
| Transfer 50,000 from U1 to S01 | fee 1,000, total 51,000, U1 after 699,000, recipient after 150,000, new transaction id 87, reference `ZC-0000000087` |
| Bill BS1 (bill account id 1) | electricity, 43,750 due; total 44,250; U1 after 705,750 |
| Nothing due | U2 (id 2) bill BZ1 (bill account id 5), 0 due |
| Purchase product 1 (`ZN-CR-5`) | 5,000; U1 after 745,000 |
| PIN above threshold | U3 (id 3) balance 12,500,000, PIN `9036`; transfer 1,100,000 to account 1 → `pin_required` without PIN; with PIN U3 after 11,399,000 and U1 after 1,850,000 |
| Wrong PIN | any PIN that is not U3's (`0000`) |
| Insufficient funds | U2 (id 2) balance 18,500; transfer 18,000 to account 1 → total 19,000 → 422, balance unchanged |
| Concurrent overdraw | U2 sends 9,000 twice to account 1 (10,000 each): one succeeds, U2 ends at 8,500, U1 at 759,000 |
| Two concurrent payments of one bill | BS1 as above; loser gets `nothing_due`; U1 ends at 705,750 |
| Contact search `q` = S04's saved name's first word (`علي` from `seed.Contacts`) | 4 contacts (S04, S05, S06, S07) |
| Last sent to S01 | `At(today, 9, "19:34")`, 50,000 |
| History `to=07805550101` | 1 row, 50,000, created `At(today, 9, "19:34")` |
| History `since` = `At(today, 10, "00:00")` for U1 | rows with `days_ago` ≤ 10: H0022 (2), H0014 (3), H0017 (5), H0026 (6), H0023 (6), H0027 (8), H0001 (9) = 7 rows |
| Products | 15; `provider=zain` 6; `type=internet&value=40000` one, `ZN-NET-40`, id 6 |
| U1 orders | 4, newest first: H0078 (`ZN-NET-25`, 12 days), H0081 (`AC-CR-5`, 25), H0080 (`ZN-NET-25`, 73), H0079 (`ZN-CR-10`, 82); codes `9000000000078` and so on |
| Reset counts | users 5, accounts 42, contacts 34, aliases 9, bill accounts 11, products 15, transactions 86, orders 9 |
| Fault tests | U1 before 750,000, after one 50,000 transfer 699,000 |
| Unknown recipient | `07805559999` |
| Same account | U1 to `07805550001` |

- [ ] **Step 1: Update the entities, the DbContext and generate the migration.** Entities as in Interfaces. DbContext: `Account.Phone` unique index, `Account.Code` max 8, `Account.RegisteredName` max 100 nullable, `Account.Status` string max 16, `Account.UserId` optional with `OnDelete(DeleteBehavior.Cascade)` kept for the user's own account (an optional FK with cascade is fine); `User.Code` unique max 8; `Contact.Code` unique max 8; `BillAccount.Code` unique max 8, `Label` nullable; `Product.Code` unique max 16, `Provider` max 32, `ProviderNameEn`/`ProviderNameAr` max 100, `AlsoCalled` max 100 nullable; `Biller.NameEn` max 100; `Alias.Text` max 100. Run `dotnet ef migrations add AdoptTestSeed --project src/Wallet.MockApi`, then edit `Up` to begin with:

```csharp
migrationBuilder.Sql("DELETE FROM [Orders]; DELETE FROM [Transactions]; DELETE FROM [BillAccounts]; DELETE FROM [Aliases]; DELETE FROM [Contacts]; DELETE FROM [Accounts]; DELETE FROM [Users]; DELETE FROM [Products]; DELETE FROM [Billers];");
```

Then `dotnet build` (expect errors in `SeedData`, `SeedStrings` users, validator, endpoints: fix in the next steps).

- [ ] **Step 2: Rewrite `SeedData` and `SeedReset`.** Delete `SeedStrings.cs`. `Build` maps the `SeedSet` as in Interfaces; `Apply(db, seed, now)` inserts one row at a time in the fixed order (as the old `InsertInOrder` did); `SeedReset.ResetAsync(db, seed, now, ct)` deletes, reseeds identities, clears the tracker and calls `Apply`. Update `Program.cs` (`migrate` passes `SeedSet` and `TimeProvider.System.GetUtcNow()`), `AdminEndpoints.ResetAsync` (inject `SeedSet seed, TimeProvider clock`), `WalletApiFixture` (`SeedData.Apply(db, seed, clock.GetUtcNow())` with both from the factory's services).

- [ ] **Step 3: Compile-level changes in validator and endpoints.** `PaymentValidator.TransferAsync`: recipients are `db.Accounts` filtered by `Id` or by `Phone` (no join to `User`); `RecipientName = a.RegisteredName` (make `PaymentPlan.RecipientName` and `QuoteResponse.RecipientName` `string?`); keep `same_account` and `unknown_recipient`; (closed accounts come in Task 3). `ReadEndpoints`: contacts join `db.Accounts` on `Phone` (`RegisteredName` from the account, nullable in `ContactItem`), aliases read `a.TargetAccount!.Phone` and `RegisteredName`; history counterparty from `t.ToAccount!.Phone` / `RegisteredName`; `GetUserByPhoneAsync` stays for now (users only); bills: `BillItem(BillAccountId, BillerCode, BillerName, Label?, AccountNumber, AmountDueIqd)`; products: `ProductItem(ProductId, Provider, Type, FaceValueIqd, PriceIqd)` for now (Task 3 adds the rest); orders: `OrderItem(OrderId, TransactionId, ProductId, Code, CreatedAt)`. `dotnet build` with 0 warnings.

- [ ] **Step 4: Rewrite `SeedApplyTests`** (database):

```csharp
[Fact] public async Task ApplyTwiceDoesNotDuplicateRows() { /* Apply twice; counts equal seed counts (5 users, 42 accounts, 86 transactions) */ }
[Fact] public async Task ResetRestoresIdentitiesFromOne()
{
    // Change U1's balance to 1, delete U1's contacts, ResetAsync, then:
    // users ids 1..5 are U1..U5 by Code; account 1 has phone 07805550001 and balance 750000;
    // contact with Code S01 has Id 1; product ZN-CR-5 has Id 1; bill BS1 has Id 1;
    // transaction 1 has IdempotencyKey seed-H0038 (90 days ago) and reference ZC-0000000001; transaction 86 is seed-H0043;
    // orders min id 1, count 9; aliases 9.
}
[Fact] public async Task HistoryIsDatedRelativeToToday()
{
    // For every transaction: CreatedAt.Offset == SeedData.IraqOffset and
    // DateOnly.FromDateTime(CreatedAt.DateTime) == SeedData.TodayIn(now).AddDays(-daysAgo) for the row's history entry (match by key),
    // and CreatedAt.TimeOfDay == the row's Time. Use the fixture's TimeProvider for now.
}
[Fact] public async Task MigrateOnTheOldSeedWipesAndReseeds()
{
    // Not a migration replay (the old schema is gone); instead: insert one extra user row by hand, run SeedData.Apply (no-op because users exist),
    // then SeedReset.ResetAsync and assert the extra row is gone and counts equal the seed. (The migration's wipe is proven by the fixture's
    // EnsureDeleted + Migrate + Apply path producing exactly the seed counts.)
}
```

Keep `finally { await fixture.ResetAsync(); }` on every test that changes data.

- [ ] **Step 5: Update the facts in `ResetTests`, `TransactionTests`, `QuoteTests`, `ReadEndpointsTests`, `FaultTests`** per the table. Replace `SeedData.DemoUserId` with a `const int DemoUserId = 1` in a new `tests/Agent.Core.Tests/Wallet/SeedFacts.cs` (`internal static class SeedFacts { const int DemoUserId = 1; const int DemoAccountId = 1; const string DemoPhone = "07805550001"; const string HammoudiPhone = "07805550101"; const int TraderUserId = 3; const int TightUserId = 2; const long DemoBalance = 750_000; static SeedSet Seed => SeedSetTests.Seed; static string PinOf(string userCode) => Seed.Users.Single(u => u.Id == userCode).Pin; static SeedContact Contact(string code) => Seed.Contacts.Single(c => c.Id == code); static SeedAccount Account(string phone) => Seed.Accounts.Single(a => a.Number == phone); }`). Arabic values come from `SeedFacts.Contact("S01").SavedName` and `SeedFacts.Account(phone).RegisteredName`. Delete `SeedDataTests.cs`. Remove every `SeedStrings` reference. Dates: compare with `SeedData.At(SeedData.TodayIn(now), 9, "19:34")` where `now` comes from the fixture's `TimeProvider`.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test` from the root. Expected: all pass, 0 skipped when SQL Server is up, 0 build warnings. Fix anything the facts table got wrong by checking the seed files, not by changing the files.

- [ ] **Step 7: Commit**

```bash
git add -A src/Wallet.MockApi tests/Agent.Core.Tests
git commit -m "feat(wallet): build the seed from the test-set files; accounts table, codes, consent, relative dates"
```

---

### Task 3: Contract additions the test set needs

**Files:**
- Modify: `src/Wallet.MockApi/Contracts/UserSummary.cs`, `ContactItem.cs`, `AliasItem.cs`, `BillItem.cs`, `ProductItem.cs`, `OrderItem.cs`, `HistoryItem.cs`; create `src/Wallet.MockApi/Contracts/AccountSummary.cs`; `src/Wallet.MockApi/Endpoints/ReadEndpoints.cs`, `WalletProblems.cs`; `src/Wallet.MockApi/Services/PaymentValidator.cs`; `src/Wallet.MockApi/Wallet.MockApi.http`.
- Tests: `tests/Agent.Core.Tests/Wallet/ReadEndpointsTests.cs`, `QuoteTests.cs`, `TransactionTests.cs` (new tests added; existing ones adjusted to the new record shapes).

**Interfaces:**
- Consumes: entities and `SeedFacts` from Task 2.
- Produces (namespace `Wallet.MockApi.Contracts`):
  - `UserSummary(int Id, string Code, string Phone, string RegisteredName, bool ConsentGiven)`.
  - `AccountSummary(int AccountId, string Phone, string? RegisteredName, string Status)` (`active` or `closed`).
  - `ContactItem(int ContactId, string Code, string SavedName, string Phone, string? RegisteredName, string AccountStatus, int TargetAccountId, DateTimeOffset? LastSentAt, long? LastSentAmountIqd)`.
  - `AliasItem(int AliasId, string Text, int TargetAccountId, string Phone, string? RegisteredName, string AccountStatus)`.
  - `BillItem(int BillAccountId, string Code, string BillerCode, string BillerName, string BillerNameEn, string? Label, string AccountNumber, long AmountDueIqd)`.
  - `ProductItem(int ProductId, string Code, string Provider, string ProviderNameEn, string ProviderNameAr, string? AlsoCalled, string Type, long FaceValueIqd, long PriceIqd)`.
  - `OrderItem(int OrderId, int TransactionId, int ProductId, string ProductCode, string Code, DateTimeOffset CreatedAt)`.
  - `HistoryItem` adds `int? CounterpartyAccountId` after `CounterpartyName`.
- Routes: `GET /users/{id}` returns the new `UserSummary`; `GET /users/by-phone/{phone}` is removed; `GET /accounts/by-phone/{phone}` returns `AccountSummary`, 404 `unknown_account` (new code in `WalletProblems`); `POST /users/{id}/consent` sets `ConsentGiven = true` and returns 204 (404 `unknown_user` if no such user; idempotent); `GET /store/products?provider=` matches the lower-case provider code exactly (any case).
- `PaymentErrors.AccountClosed = "account_closed"` (422, quote and execute): a transfer whose recipient account has `Status == Closed`. Checked after `unknown_recipient` and before `same_account`.
- A transfer *from* a closed account cannot happen (users' accounts are all active) and is not checked.

- [ ] **Step 1: Write the failing tests**

```csharp
// ReadEndpointsTests
[Fact] public async Task UserSummaryCarriesCodeAndConsent()
  // /users/1 -> Code "U1", ConsentGiven true; /users/5 -> "U5", false
[Fact] public async Task AccountByPhoneFindsContactsOthersAndClosed()
  // /accounts/by-phone/07805550101 -> registered name of S01's account, status active
  // /accounts/by-phone/07805550193 -> RegisteredName null, status closed
  // /accounts/by-phone/07805559999 -> 404 unknown_account
  // /users/by-phone/07805550001 -> 404 (route gone)
[Fact] public async Task ConsentCanBeGivenOnce()
  // POST /users/5/consent -> 204; GET /users/5 -> true; POST again -> 204; POST /users/99/consent -> 404 unknown_user; reset -> false again
[Fact] public async Task ContactsCarryCodesAndAccountStatus()
  // S20 -> AccountStatus "closed"; S01 -> Code "S01", "active"; aliases of U1 count 4, each with Phone and RegisteredName
[Fact] public async Task BillsCarryCodesAndEnglishNames()
  // U1 bills: BS1..BS4 with BillerNameEn "Electricity", "Electricity", "Water", "Internet (Earthlink subscription)"; BS3 Label null
[Fact] public async Task ProductsCarryCodesAndProviderNames()
  // ZN-NET-40: Provider "zain", ProviderNameEn "Zain", AlsoCalled from seed, Type "internet"; KR-CR-5 AlsoCalled null; provider=ZAIN matches 6
[Fact] public async Task OrdersCarryProductCodes()
  // U1 orders[0].ProductCode == "ZN-NET-25"
[Fact] public async Task HistoryCarriesCounterpartyAccountId()
  // to=07805550101 -> CounterpartyAccountId == account id of that phone (from /accounts/by-phone)

// QuoteTests
[Fact] public async Task QuoteToClosedAccountIsRefused()
  // 07805550120 -> 422 account_closed; 07805550193 -> 422 account_closed

// TransactionTests
[Fact] public async Task TransferToClosedAccountIsRefusedAndNothingMoves()
  // key "t-closed": 422 account_closed; U1 balance 750000; by-key 404 unknown_key (no row); same key again -> 422 account_closed
```

- [ ] **Step 2: Run to verify they fail** (`ReadEndpointsTests`, `QuoteTests`, `TransactionTests` classes). Expected: build errors on the new record members and route.

- [ ] **Step 3: Implement** the contract records, the routes, the consent update (`ExecuteUpdateAsync` on `db.Users.Where(u => u.Id == id)` setting `ConsentGiven` true; 0 rows → 404), the `account_closed` check in `PaymentValidator.TransferAsync` (select `a.Status` with the recipient), `unknown_account` in `WalletProblems`, and the `.http` samples (`GET /accounts/by-phone/07805550101`, `POST /users/5/consent`).

- [ ] **Step 4: Run the whole suite.** `dotnet test`: all pass, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add -A src/Wallet.MockApi tests/Agent.Core.Tests
git commit -m "feat(wallet): account lookup by phone, closed accounts, consent, codes on every item"
```

---

### Task 4: Docs, contract file and freeze

**Files:**
- Modify: `data/seed/README.md` (rewrite), `docs/wallet-api.md`, `docs/failure-modes.md`, `README.md` (migrate line), `docs/build-plan.md` (one note under milestone 2), `docs/disclosure.md` (no new package; the test-set line already exists; add nothing unless a package was added).
- Controller only: `CLAUDE.md` (Arabic-in-code line, migrate output line, seed-hash line, new route in the reset/contract lines if needed).

**Interfaces:** consumes everything above; produces documentation only.

- [ ] **Step 1: Rewrite `data/seed/README.md`.** Sections: what the files are (table: file, rows, what it is, from `data/README.md`), source of truth (the JSON files, exported by Khalil from the test-set workbook v0.15, never edited by Claude; the hash in `seed-hash.txt` is `SeedHash.Compute` over the files with `\r` stripped; how to re-freeze), how the wallet loads them (link copy into the output, `Wallet:SeedDirectory`, validated at start, insert order and the resulting ids, codes), dates (`days_ago` relative to today in Iraq time, `SeedData.IraqOffset`, clock time from the file; reset re-dates; a run that crosses midnight Iraq time sees a one-day shift between resets), PINs (per user, from `users.json`, stored hashed), order codes for seeded purchases (`OrderCodeFor`), the phone pattern note (Khalil's export supersedes the 14 Oct `0770000` decision), and the frozen tag `seed-v2` (`seed-v1` stays in history as the pre-export seed). Remove the "Arabic strings for Khalil to review" section: the strings are Khalil's.
- [ ] **Step 2: Update `docs/wallet-api.md`.** Contract tables for every changed record and route (`AccountSummary`, `/accounts/by-phone`, `/users/{id}/consent`, codes, product and bill fields, `account_closed`, `unknown_account`, removed `/users/by-phone` and `dueDate`); examples use U1 and S01 figures (750,000 → 699,000); the migrate output; and a Mermaid flowchart of the seed load: files → `SeedSet.Load` (validate) → `SeedData.Build(now)` → `Apply` in insert order → identities; plus the hash path. Update the reset sequence diagram's `SeedData.Apply` note to name the `SeedSet` and the clock.
- [ ] **Step 3: `docs/failure-modes.md` rows:** seed files missing or invalid at start (app fails to start with the `SeedException` message; `migrate` and `seed-hash` print it and exit 1); a seed file changed by hand (hash test fails; re-freeze procedure); transfer to a closed account (422 `account_closed`, nothing moves, no row); the run crossing midnight Iraq time (seeded dates shift one day at the next reset; the eval runner resets before every case so a case never straddles it).
- [ ] **Step 4: `README.md`** migrate line → `Database ready: 5 users, 42 accounts, 34 contacts, 86 transactions`; one line that the seed comes from `data/seed/*.json`. **`docs/build-plan.md`**: under milestone 2 "Done when", append a note: "14 Oct 2026: the seed was replaced by Khalil's test-set export (commit 462187f) in milestone 2b and re-frozen as `seed-v2`; the milestone 2 'Done when' lines were re-run on it."
- [ ] **Step 5: Run `dotnet test` once more and commit**

```bash
git add data/seed/README.md docs/wallet-api.md docs/failure-modes.md README.md docs/build-plan.md
git commit -m "docs: seed from the test-set files, contract changes, seed-load diagram"
```

- [ ] **Step 6 (controller):** update `CLAUDE.md`, run the final Fable review and the security review, then tag `seed-v2` and `milestone-2b`.

---

## Self-review notes

- Spec coverage: section 7 routes all remain; the one removed (`/users/by-phone`, added by me in milestone 2 for D16) is replaced by the account lookup that D16 actually needs (a typed number may belong to a non-user account). D10 (one default account per user) holds: `IsDefault` is true on exactly the five user accounts. D15: aliases still point at one account. D22: `ConsentGiven` is stored where the test set puts it; the consent route is the smallest way to persist the consent screen's outcome (milestone 7) and is flagged to Khalil as a ruling.
- Not in scope (suggest to Khalil): an admin endpoint for the `external_debit` fault (milestone 8); caps and the card timer in the wallet (they are agent settings).
- Type consistency: `RecipientName` becomes `string?` in `PaymentPlan`, `QuoteResponse`; `RegisteredName` is `string?` on `AccountSummary`, `ContactItem`, `AliasItem`, `HistoryItem.CounterpartyName` (already nullable).
