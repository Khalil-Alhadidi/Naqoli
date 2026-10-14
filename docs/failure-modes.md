# Failure modes

How the system can fail and what it does. One row per mode. Added to whenever a new one is found. The six modes from slide 2 are completed in milestone 8.

| Mode | What the system does | Where handled | Test |
|---|---|---|---|
| Wallet times out after execute, status unknown | Pending moves to Checking, the user sees "checking", never "failed"; a lookup by idempotency key resolves it | Milestone 3 executor, milestone 8 screen | pending |
| Double tap on Confirm | A pending record leaves Pending once; the second tap does nothing | Milestone 3 pending store | pending |
| Card timer ends | Record becomes Expired; Confirm is disabled; "Try again" rebuilds a new record | Milestone 3 and 4 | pending |
| Balance changes between card and tap | Recheck at execute fails the record as NotDone with a template reply | Milestone 3 | pending |
| Wallet rejects the execute | NotDone, template reply, no retry without a new tap | Milestone 3 | pending |
| Model outage or timeout | Handover message from a template; no card | Milestone 6 | pending |
| Invalid or unexpected model output | Becomes a question to the user, never an exception or a card | Milestone 6 | pending |
| Connection string missing, or SQL Server unreachable, when `migrate` runs | One clear line to stderr and exit 1 | Milestone 1, src/Wallet.MockApi/Program.cs and Data/WalletConnection.cs | review only (returns before the host is built; unreachable-server path exercised by hand) |
| Windows path longer than about 100 characters | `dotnet build` fails with MSB3021 from the Playwright package; README tells the reader to clone to a short path or enable long paths | README, Prerequisites | reproduced by hand on 14 Oct 2026 |
| Seeding fails part way (SQL error during `migrate` or reset) | `SeedData.Apply` and `SeedReset.Reset` run in one database transaction, so a failure leaves no half seed that the "users exist" check would then treat as complete | Milestone 2, src/Wallet.MockApi/Data/SeedData.cs and SeedReset.cs | SeedApplyTests (opt-in SQL) |
| Reset on a table that never held a row | `DBCC CHECKIDENT ... RESEED, 0` would make the next identity 0, not 1; the reset reseeds only tables whose `last_value` is set, so ids always start at 1 | Milestone 2, src/Wallet.MockApi/Data/SeedReset.cs | SeedApplyTests.ResetRestoresTheSeedWithIdentitiesFromOne (opt-in SQL) |
