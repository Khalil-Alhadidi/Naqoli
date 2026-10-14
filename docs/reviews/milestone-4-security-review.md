# Security review: milestone 4 (first slice on screen), range 2cd2744..6e59ad6

Written by the controller (Claude Fable 5.1) on 15 Oct 2026 from the diffs, under Khalil's rule of 14 Oct 2026 to use one agent per milestone (no security agent was dispatched). Read-only pass over `src/Agent.Web`, the `Agent.Core` changes of this milestone and the journeys. Verdict: pass. Critical 0, Important 0, Minor 3 (all fixed in 6e59ad6), Recommendations 4.

## Approved
- **Rule 1 on the screen.** The IL test now scans `Agent.Web`: the only callers of `ConfirmHandler.ConfirmAsync` and `SettleCheckingAsync` are `ChatSession.ConfirmAsync` (the card's Confirm button) and `ChatSession.SettleAsync` (the "check again" button); Razor components call the session, never the handler. A typed yes word writes `points_to_confirm` and never calls Confirm (`ChatSession.SendAsync`; `ChatSessionTests`).
- **PIN handling.** The PIN input is `type="password"`, `inputmode="numeric"`, `autocomplete="off"` (`PinField.razor`); the value lives in the card component's field, is copied and cleared before the call (`CardPanel.razor`), passed through `ChatSession.ConfirmAsync` to the handler and kept nowhere; no `ILogger` exists in `Agent.Web`; trace entries carry only step names, codes, states, amounts and the typed message's length.
- **Card and reply contents.** Components receive `CardView` only (no idempotency key, quoted balance, row version or user id); every name from the wallet renders through Razor encoding inside `<bdi>`; no `MarkupString` in `Agent.Web`; every phone string is a resource value (`TODO-AR` placeholders for Khalil); no Arabic in `.cs`/`.razor`.
- **Scopes.** Every session action opens its own DI scope and resolves `Orchestrator`, `ConfirmHandler`, `PendingStore`, `WalletClient` from it; a `SemaphoreSlim` runs one action at a time; no `AgentDbContext` is injected into a component; the scoped `ITraceSink` resolves the circuit's `TraceLog` through an `AsyncLocal` set per action.
- **Option taps** are validated against the options offered in the session; an unknown value is ignored and traced (`option_rejected`).
- **Sweeper.** `PendingSweeper` (BackgroundService, `PeriodicTimer`, scope per sweep) expires cards and moves stale Executing records (`StaleExecutingAfterSeconds` 40, validated to exceed three wallet timeouts) to Checking, never NotDone; it calls no wallet route.
- **Journeys** host the wallet and the console on Kestrel with test keys (`journeys-client-key-0001`) and `_Test` databases guarded by name; they reset the wallet before each case and skip cleanly without SQL Server or Chromium.
- **Configuration.** No secrets added; `Agent:WalletClientKey` still comes from user secrets outside Development; `System.Net.Http.HttpClient` and EF command logging at Warning; the new settings (`DemoUserId`, `SweepIntervalSeconds`, `StaleExecutingAfterSeconds`, `MinSubstringWordLength`) are bound and validated and shown read-only in the settings placeholder.
- **Rules 7, 8, 9.** Nothing reads the jsonl files; the scripted sentences are two English placeholders plus one alias copied from the seed; no real data; no keys in the repository.

## Minor (fixed in 6e59ad6)
- **M1.** `ChatSession.Dispose` disposed the action semaphore, so a circuit dropped mid-action raised an unobserved `ObjectDisposedException`. Now only the lifetime token source is disposed; actions end quietly on the session's own cancellation (`ChatSessionTests.DisposingTheSessionMidActionEndsItQuietly`).
- **M2.** `Agent.Web` `AllowedHosts` was `*`; now `localhost;127.0.0.1;[::1]` like the wallet (host-header filtering is not access control; the console is meant for loopback until the organisers say where the demo runs).
- **M3.** The sweeper logged a Warning every five seconds while the database was unreachable; now once per outage, Debug meanwhile, Information on recovery.

## Recommendations
- R1 `UseHttpsRedirection` with only an HTTP launch profile logs a warning at start and does nothing; keep it for a hosted demo, or drop it in Development.
- R2 A Checking record left by a dead circuit can only be settled by the sweeper's path once milestone 8 adds a settle timer; documented in `docs/agent-web.md` known gaps.
- R3 Option taps re-send a filled `ParsedRequest`; two contacts with the same display text or an unlabelled bill sharing its biller get the same question again until the orchestrator gains an answer entry point (milestone 7).
- R4 Amount in words on the card needs Khalil's Iraqi number words; the card shows digits only (known gap).

## Forward-looking for milestone 5 (test set and runner)
The runner drives `Orchestrator`/`ConfirmHandler` directly through scopes, as `ChatSessionTests` do, never the screen; the per-case masking check reads the outbound text sent to the parser (today the scripted parser sees the raw text, so the check is a no-op until milestone 6); the runner's wallet reset must also call `PendingStore.ExpireAllOpenAsync("wallet_reset")`.
