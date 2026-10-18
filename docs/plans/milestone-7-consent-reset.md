# Consent and reset (milestone 7, post-tag)

**18 Oct 2026.** Khalil's instruction of 18 Oct 2026, after the milestone 9a close-out: report where consent is stored and what the wallet's reset and `migrate` do with it; make both put every seed user back to not consented; a Reset control in the console next to the fault panel, behind the admin key; the consent screen on opening the chat, before any typing, with the first-request gate kept as the fallback; two journeys; the record under milestone 7's post-tag notes in `docs/build-plan.md`.

## Findings (the report)

- Consent is stored in the **wallet** database only: `User.ConsentGiven` (`src/Wallet.MockApi/Data/Entities/User.cs`), read by the agent through `GET /users/{id}` and set through `POST /users/{id}/consent`. The agent database holds no consent; `Conversation.ConsentShown` is per conversation and in memory.
- Neither `POST /admin/reset` nor `migrate` puts consent back to not consented for every user. Both apply the seed as it is, and the seed's `data/seed/users.json` says `consent_given` true for U1 to U4 and false for U5 ("First use: has not given consent yet"). After either, only U5 sees the consent screen.
- The seed is Khalil's and is never edited (its hash is frozen). The development set (95 cases, 75 on U1, none on U5) and the sealed held-out set were keyed for the seed's roles: U1 to U4 consented, U5 not. The runner resets the wallet before every case, so a reset that cleared consent for every user would put the consent screen in front of every case and break every key.

## Ruling A (controller, for Khalil to confirm or overturn)

The wallet's reset and a fresh `migrate` clear consent for every user, as instructed, and this is the default. The seed's `consent_given` values stay the test set's roles and can be asked for: `POST /admin/reset?consent=seed` restores them. The runner, the test fixtures and the journeys' per-test reset ask for the seed's values, so no answer key, target or seed file changes. The console's Reset control and the documented hand command use the default, so the demo starts with every user unconsented.

## Items

1. **Plan** (this file). Controller's commit.

2. **The wallet: reset and migrate clear consent.**
   - `SeedReset.ResetAsync(WalletDbContext db, SeedSet seed, DateTimeOffset now, ConsentReset consent, CancellationToken cancellationToken)`, with `public enum ConsentReset { Cleared, Seed }` in `Wallet.MockApi.Data`. After `SeedData.Apply` and inside the same transaction, `Cleared` sets `ConsentGiven` false on every user (`ExecuteUpdateAsync`); `Seed` leaves the seed's values. `SeedData.Apply` is unchanged in what it writes but returns `bool`: true when it seeded, false when the database already had a user.
   - `POST /admin/reset` takes an optional query parameter `consent`: `cleared` (the default when absent) or `seed`; any other value is 422 with code `invalid_consent` (built like the other wallet problems). The response is unchanged.
   - `migrate` (`src/Wallet.MockApi/Program.cs`): when `Apply` seeded, clear consent for every user the same way (a shared static `SeedData.ClearConsent(WalletDbContext db)` used by both paths is fine); when the database already had data, nothing changes. The printed line is unchanged.
   - Callers that need the seed's roles pass `?consent=seed`: `WalletAdmin.ResetAsync` (`src/Agent.Eval/Running/WalletAdmin.cs`), `WalletApiFixture.ResetAsync` (`tests/Agent.Core.Tests/Wallet/WalletApiFixture.cs`, which `AgentHost` uses), `JourneyHost.ResetAsync` (`tests/Agent.Web.Journeys/JourneyHost.cs`). Every other caller keeps the default.
   - Tests (`tests/Agent.Core.Tests/Wallet/`): `SeedApplyTests`: a reset with `ConsentReset.Cleared` leaves every user with `ConsentGiven` false and everything else as the seed; with `Seed` the consent column equals the seed's (the existing assertion). `ResetTests`: `POST /admin/reset` without the parameter answers every user not consented through `GET /users/{id}`; with `?consent=seed` U1 is consented and U5 is not; `?consent=other` is 422 `invalid_consent` and changes nothing. `ReadEndpointsTests.ConsentCanBeGivenOnce` keeps its meaning (the fixture's reset asks for the seed). No test runs `migrate`; the shared clearing method is covered through the reset.
   - Docs: `docs/wallet-api.md` (the admin routes table, the Reset section, the consent paragraph under `GET /users/{id}`, the `migrate` line), `README.md` (the `migrate` line), `CLAUDE.md` (the "Create and seed" and "Reset the wallet to the seed" lines: both clear consent; `?consent=seed` keeps the seed's), `docs/agent-eval.md` (the driver's reset asks for the seed's consent, line and diagram).
   - One commit.

3. **The console: a Reset control next to the fault panel, behind the admin key.**
   - `WalletFaults` (`src/Agent.Web/Services/WalletFaults.cs`) gains `ResetAsync(CancellationToken)`: `POST admin/reset` (no query, so consent is cleared), returning a new `WalletResetResult` (`Ok` with the row counts the wallet returns, or `Failed(problem)` like `WalletDebitResult.Failed`). It still names no payment route (rule 1).
   - A new component `src/Agent.Web/Components/Console/ResetPanel.razor`, rendered in `Home.razor`'s trace column directly above `FaultPanel`. One `MudPaper` with `data-testid="reset-panel"`, the heading "Reset", a caption saying what it does, one button `data-testid="reset-wallet"` ("Reset wallet"), a result line `data-testid="reset-result"` after a reset, and an error line `data-testid="reset-error"` on failure, with the fault panel's rules: a fresh scope's client per call, no key shown, no exception message shown (review M1), calls cancelled at disposal (review R3), busy while running.
   - What a reset does, in order: `WalletFaults.ResetAsync`; then, in a fresh scope, `PendingStore.ExpireAllOpenAsync("wallet_reset", token)` (the runner's reason, as `CaseDriver` does), since the wallet's ids start again; then `ChatSession.RestartAsync()` on the circuit's own session. The audit log and the learning log are kept (ruling B: they are the record). Other circuits are not told; a page refresh starts them over.
   - `ChatSession.RestartAsync()`: unsubscribes and disposes the current `Conversation`, builds a new one for the same demo user, clears the messages and ids, raises `Changed`, then runs the open check of item 4.
   - Tests: `FaultPanelServicesTests` (or a sibling in the same folder) shows the side door's reset reaches the in-process wallet and the result carries the counts; a `ChatSessionTests` case shows `RestartAsync` empties the messages and a new message afterwards gets its card.
   - Docs: `docs/agent-web.md` (the services table row of `WalletFaults`; a short "The reset control" section next to "The fault panel"; the `data-testid` contract; the layout diagram line; the known gaps row about the wallet data component, extended to the reset).
   - One commit.

4. **The consent screen on opening the chat.**
   - `Conversation.OpenAsync()`: takes the turn like every action; runs once per conversation (a second call returns at once); reads the user through `WalletClient.GetUserAsync`. Not consented: audit `consent_shown` (the existing event), trace `consent_checked` with detail `required`, `ConsentShown` true, a `ConversationKind.Consent` message with `ActionMessageIndex` on it. Consented: trace `consent_checked` with `given`, nothing shown. No usable answer from the wallet: trace `consent_checked` with `unavailable`, nothing shown, and the first request's gate (unchanged) catches it. `GiveConsentAsync` already handles a tap with no pending request (it records consent and shows nothing more; no new phone string).
   - `ChatSession.OpenAsync()` routes the trace and calls it. `PhoneFrame.OnAfterRenderAsync(firstRender)` calls `Session.OpenAsync()` (never during prerendering, so the audit row is written once per circuit).
   - The runner (`CaseDriver`) does not call it: the keys were written for the first-request gate (ruling C).
   - Tests: `ConversationTests`: for U5, `OpenAsync` shows the consent message with its button, writes one `consent_shown` row and sets `ConsentShown`; a second `OpenAsync` adds nothing; for U1 it shows nothing; a wallet that does not answer the user read shows nothing and the next message gets the consent screen from the gate (U5) or its card (U1). `ChatSessionTests`: the opened session's one message is `Reply_consent` with `ChatAction.Consent` and `ActionMessageId` on it.
   - Docs: `docs/agent-core.md` (the Conversation action table, the consent gate note), `docs/agent-web.md` (the phone's lifecycle line), `docs/failure-modes.md` only if a new failure appears (none expected).
   - One commit.

5. **Journeys** (`tests/Agent.Web.Journeys/ConsoleJourneys.cs`):
   - `AFreshUserSeesTheConsentScreenBeforeTyping`: U5's console; without typing, the last agent message is `Reply_consent` with `consent-button` visible and the wallet says not consented; tap; the button is gone and the wallet says consented; then CLN-31 answers the balance straight away, with exactly one `consent_shown` audit row.
   - `ResetClearsConsentAndThePhoneStartsAgain`: U1's console; CLN-01 gives a card; click `reset-wallet`; `reset-result` appears; the wallet says U1 is not consented; the phone shows no card and exactly one agent message, `Reply_consent`, with the button; a tap gives consent and CLN-01 gives a card again.
   - `ConsentScreenComesBeforeTheBalance` stays as the fallback-gate journey (the balance typed under the open screen's button shows the gate's screen again; it passes as written, or with its first-message check adjusted).
   - One commit, with the journeys list in `docs/agent-web.md`.

6. **Record.** Under milestone 7 in `docs/build-plan.md`, after the "Done and tagged" paragraph: the finding, ruling A, and the commits of items 2 to 5. Controller's commit; push.

Rules in force: `CLAUDE.md` (the nine rules; the seed is never edited; no Arabic in C#; no new phone string, so nothing for Khalil to write; limits are configuration; commits carry Khalil's name only, no `Co-Authored-By` line). Process: one implementer (Opus) for items 2 to 5, one commit each, briefed in `.superpowers/sdd/milestone-7-consent-reset/items-2-5-brief.md`; the controller (Fable) reviews the implementer's diff, runs the full suite and the journeys once more, writes item 6 and pushes.
