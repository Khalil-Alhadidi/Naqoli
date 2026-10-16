# Security review: milestone 7 part 2 (complete), range 35a498a..256510d

Written by the security reviewer agent (Claude Opus 5.5, `dotnet-security-reviewer`) on 16 Oct 2026, saved by the controller. Sources: the code-and-tests diff of the milestone and the current files under `src/`. The reviewer did not open, search or hash the held-out file (rule 7); the only "heldout" string it looked at is a test's throwaway temp-file name.

**Verdict: pass with changes.** Critical 0, Important 3, Minor 5, Recommendations 4. The fix wave that followed is recorded at the end.

## Summary

The nine rules hold.

- **Rule 1:** only the Confirm handler reaches `POST /transactions`. Both reissue paths are proved by IL never to reach execute or the Confirm entries.
- **Rules 2 and 5:** the oracle parser loads only a file named `requests.dev.jsonl`. It returns masked tokens for typed numbers and puts its own parses through `ParsedRequestValidator`. Nothing from the wallet reaches it.
- **Rule 6:** a kill-switched Confirm leaves Pending exactly once and ends NotDone. A reissue is a new record with a new ID, key and timer.
- **Wallet route:** `/settings` sits behind `ClientKeyFilter` and returns five numbers.

The three Important items: the kill switch is checked once, before the recheck, so a flip during the recheck reads still lets the execute go; Try again and Send again rebuild a transfer to the stored account ID and registered name while the status check runs on the account read fresh; two "live" settings, `MaxYesNoWords` and `MaxRequestsPerMessage`, never take effect.

## Findings

### Important

**I1. The kill switch does not stop a Confirm that is already in its recheck (and does not stop a card already in its checks).**
- Where: `src/Agent.Core/Confirm/ConfirmHandler.cs` checks the switch once, before `RecheckAsync` (balance and history reads, each up to `WalletTimeoutSeconds`); the execute follows with no second check. Same pattern in `Orchestrator.cs`: checked before the wallet reads, not before `store.CreateAsync` in `CheckAndIssueAsync`.
- What: a flip while a Confirm is between the first check and the execute (a window of up to about 20 s when the wallet is slow, which is when an operator reaches for the switch) does not stop the payment. A request already past the orchestrator's check still issues a card after the flip.
- Why it matters: the plan says the switch gates "the Confirm handler before any execute". No money moves without a tap (rule 1 holds), but the stated control has a gap.
- Smallest fix: read `killSwitch.IsOn` again right after `recheck_ok`, before the execute, and end NotDone through the same path (nothing has been sent, so NotDone is correct under rule 6); add the same check in `CheckAndIssueAsync` just before `store.CreateAsync`. Test with a stub wallet whose balance read flips the switch: 0 executes, the record NotDone `kill_switch`.

**I2. A rebuilt transfer card (D19) pays the stored account and shows the stored registered name, while the status check uses the account read fresh.**
- Where: `Orchestrator.ReissueAsync`: `CardSubject` takes `previous.ToAccountId` and `previous.RegisteredName`; `CheckInput` passes `previous.ToAccountId` with the fresh `account.Status`; execute later sends `record.ToAccountId`.
- What: if the phone now resolves to a different account than the stored one, the card passes the status check on account X, shows the old registered name, and executes to account Y. The normal transfer path takes the account ID and registered name from the same wallet read.
- Why it matters: D19 promises fresh checks; the registered name is the user's main safeguard against a wrong recipient (the spirit of rule 3). The mock never reassigns numbers, so this is latent.
- Smallest fix: build the subject and the `CheckInput` from `account.AccountId` and `account.RegisteredName`; add an `OrchestratorTests` case where the by-phone read returns a different account ID.

**I3. Two of the editable settings never apply, though the panel says "A saved value applies to the next action".**
- Where: `ChatSession` injects `IOptions<AgentOptions>` and builds the `Conversation` with it; `Conversation` reads `MaxYesNoWords` once, in its constructor. `IOptions<T>` is computed once per application and never reloads (Microsoft's options-pattern docs). `MaxRequestsPerMessage` is read nowhere in `src/`.
- What: saving `MaxYesNoWords` or `MaxRequestsPerMessage` shows the new value but changes nothing, not even in a new circuit.
- Smallest fix: give `Conversation` an `IOptionsMonitor<AgentOptions>` and read `CurrentValue.MaxYesNoWords` per message (the yes/no entry lists can stay cached); show `MaxRequestsPerMessage` read-only until something reads it; add a test for `MaxYesNoWords`.

### Minor

**M1. A save publishes the value before validating it, so other circuits can briefly see invalid options.** `RuntimeSettings.Apply` writes, lets the reload validate, then writes the old value back. Between the two writes a scope created on another circuit gets an `IOptionsSnapshot<AgentOptions>` that throws when resolved. This fails closed (a `ConfirmHandler` that cannot be built never reaches `TryBeginExecuteAsync`), but a concurrent user gets an error for nothing. Fix: validate a candidate first (bind a fresh `AgentOptions` from the root plus the one value, run the registered post-configure and validators), then publish.

**M2. `RuntimeSettings` accepts any `Agent:` key, not just the editable ones.** `Agent:WalletClientKey`, `Agent:WalletBaseUrl`, `Agent:DemoUserId`, `Agent:Parser` and `Agent:DevSetPath` would all be accepted; only the UI's `Editable` list limits this. Fix (defence in depth): keep the allow-list in `RuntimeSettings` itself and have `SettingsPanel` read it from there; refuse `Agent:WalletClientKey` and `Agent:DevSetPath` in the tests.

**M3. A kill-switch flip whose audit write fails ends the circuit and leaves the flip unaudited.** `SettingsPanel` sets the switch, then writes the audit row with no `try`/`catch`; an unhandled exception in a Blazor event handler is fatal to the circuit (Microsoft's Blazor error-handling docs). Fix: catch it, show an error line, keep the switch state; note in `docs/agent-web.md` that `kill_switch` rows are console actions attributed to `DemoUserId`.

**M4. Nothing stops `MinAmountIqd > PerTransactionCapIqd` or `PerTransactionCapIqd > PerDayCapIqd`.** Inconsistent limits fail closed (everything is refused), but a typo can silently make the console refuse everything. Suggestion, Khalil's call as a new validation rule: a cross-field `Validate` in `AddAgentCore`.

**M5. The console has no sign-in, so anyone who reaches it can turn the kill switch off or raise the caps.** By design for the demo; the console listens on localhost. Fix: a line in `docs/failure-modes.md` and the README that the console must stay on loopback, and that hosting it anywhere else needs ASP.NET Core authorization on the settings component.

### Recommendations

- **R1.** `ScriptedParser.Load` refuses by name, so a symbolic or hard link named `requests.dev.jsonl` would get through. Low risk (a deliberate act); cheap hardening: refuse when the file or the sidecar has a link target.
- **R2.** `NoTapNoMoneyTests.CallGraph` follows direct calls by declaring type, so interface and virtual dispatch are not followed. Acceptable because `ExecuteIsInternal` and the single-caller test cover execute globally; say so in the test's summary.
- **R3.** `LogPanel.LoadAsync` uses `CancellationToken.None`; hold a `CancellationTokenSource`, cancel it in `Dispose`, pass it to `ReadLatestAsync`.
- **R4.** The kill-switch state in `SettingsPanel` is read at render only, so a second console tab shows a stale switch; re-render on a change event so every open console shows the true state.

## Checked and found sound

- **Rule 1.** `TheReissuePathNeverReachesExecute` walks the Agent.Core call graph from both `ReissueAsync` methods, including state machines and lambdas: `PendingStore.CreateAsync` is reached; execute, `ConfirmAsync` and `SettleCheckingAsync` are not; no web or runner `ReissueAsync` calls the conversation's Confirm or settle. The kill-switch settle returns Checking without a resend. Tests `TheKillSwitchEndsALiveCardNotDoneWithoutAnExecute` and `TheKillSwitchLeavesACheckingRecordCheckingWithoutAResend`.
- **Rule 6 and D19.** `Conversation.ReissueAsync` re-reads the record scoped to the user and rejects Executing, Checking and still-live Pending; an expired-but-unswept Pending record can be rebuilt, but the old one can no longer be confirmed (`TryBeginExecuteAsync` returns Expired). `Orchestrator.ReissueAsync` checks ownership again. A double tap is stopped by `EnterAsync` and the panel's `acting` flag. The new card goes through consent, the ordered checks and `store.CreateAsync`; bill and purchase rebuilds re-read the amount due and the catalogue price.
- **Kill switch scope.** The orchestrator check runs after `choose_account` has become a Transfer, so GAP-04 is covered; the balance question answers before it; `volatile bool` is enough for one flag; the singleton is `TryAddSingleton`.
- **Rules 2, 5 and 7 (oracle).** `Load` refuses any name other than the exact, case-sensitive `requests.dev.jsonl` before touching the file system; the sidecar name is fixed; only `id` and `steps[].say` are read, never the answer keys; each parse is masked with `MaskMap.Tokenize` and checked by `ParsedRequestValidator` against the message's map; the strict reader disallows unknown and duplicate properties; error messages name the case and step, never the text; the test sets live in a temp directory; `DevSet.Say`, `ScriptedParserTests` and `EvalPaths` resolve `data/requests.dev.jsonl` only; no new code path names the held-out file.
- **Masking (D21).** `ATypedAccountNumberIsMaskedRefusedAndGetsNoCard` (10 and 12 digits with separators) and the journey `AccountNumberIsRefusedAndMasked` (14 digits).
- **D6.** A message typed while a card waits is parsed with the card live; only `Unclear` (including a refused structure) or `change_waiting_card` keeps the card; a parser outage keeps it too; a typed yes never confirms; the pending note is the type only.
- **PIN handling.** `SeedPins` reads the synthetic seed and never prints a PIN; the journey searches chat, trace, audit and learning rows for both PINs in both digit forms; `CardPanel` clears the PIN before the call; `AuditLog` has no PIN, balance or code parameter; the log component shows only those columns, HTML-encoded.
- **Settings component.** Input limited to ASCII or Arabic-Indic digits; overflow and conversion failures caught and reported with the key name; a refused save restores the previous override or none; keys outside `Agent:` refused; nothing written to a file; no key displayed.
- **Provider thread safety.** `Write` replaces `Data` with a new dictionary under a lock; saves and resets are serialised; the source is added last so saved values win.
- **Options.** Scoped pipeline services take `IOptionsSnapshot<AgentOptions>` (per scope, recomputed per action); long-lived components read `IOptionsMonitor.CurrentValue`; `AScopeOpenBeforeTheSaveKeepsItsValue` shows an open action is not changed mid-flight (exception: I3).
- **Wallet route.** `GET /settings` is inside the client-key group; returns five numbers and no key; reads no database; `GetSettingsAsync` maps a connection failure to `Unavailable`.
- **Blazor Server patterns.** Each action uses its own scope; `LogPanel` unsubscribes in `Dispose`, uses `InvokeAsync`, coalesces reloads; the reissue button is hidden while live and disabled while busy.
- **EF Core.** `ReadLatestAsync` is `AsNoTracking`, deterministically ordered, bounded by `Take` (1 to 1,000), refuses a size below 1; the test covers ties on `At`.
- **Journeys.** Every console pins `Agent:Parser=scripted`; databases must end in `_Test`; `ResetAsync` clears learning rows and resets every console's kill switch and saved settings.

## Fix wave (controller, 16 Oct 2026)

Fixed through the implementer in one wave after this review: I1, I2, I3, M1, M2, M3, M5, R1, R2, R3, R4. M4 (the cross-field rule `MinAmountIqd` <= `PerTransactionCapIqd` <= `PerDayCapIqd` in `AddAgentCore`) was added on 16 Oct 2026 at Khalil's request, after the tag. The commit and the re-verification are recorded in `docs/build-plan.md`, milestone 7 status.
