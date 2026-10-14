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
| Database missing at startup of the migrate command | One clear line and exit 1 | Milestone 1, src/Wallet.MockApi/Program.cs and Data/WalletConnection.cs | review only |
