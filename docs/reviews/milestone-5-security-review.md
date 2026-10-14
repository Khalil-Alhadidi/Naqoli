# Security review: milestone 5 (test set and runner), range be5f676..d1a86bf

Written by the controller (Claude Fable 5.1) on 14 Oct 2026 from the diffs, under Khalil's one-agent rule (no security agent dispatched). Verdict: pass. Critical 0, Important 0, Minor 2, Recommendations 3.

## Approved
- **Rule 7 (the sealed set).** `HeldOutGuard.Check` runs before any set file is opened (`EvalCommand.cs:70-71`); `heldout` without `--milestone-11-run` is refused with the rule sentence and opens nothing, not even the disclosure file; with the flag the file's SHA-256 is compared with the value on the held-out line of `docs/disclosure.md` and a mismatch refuses the run. The only other reference to the file name outside the guard is an existence check in `EvalCommand`. No test, fixture or report touches the real file; the guard test uses a dummy file in a temporary directory. The committed reports (`docs/reports/dev-scripted-2026-10-14.*`) carry case ids, codes, figures and verdicts and contain no Arabic code point and no case text (checked by script and by the controller).
- **Rule 1 extended to the runner.** `NoTapNoMoneyTests` scans `Agent.Eval`: `CaseDriver` is the only caller of `Conversation.ConfirmAsync`/`SettleAsync` there; `Conversation` remains the only caller of the handler in `Agent.Core`; `ChatSession` in `Agent.Web`.
- **Rule 3 and 4 in the grader.** `CaseGrader` compares codes and figures only; names are compared but rendered as equal/different; no answer key, target or rule file was edited (the run records 0 of 72 passing, honestly).
- **PINs.** The runner reads the seed PINs from `data/seed/users.json` at run time, passes them through `Conversation.ConfirmAsync`, and never writes them to observations, reports or logs; `PinSeen` scans messages, trace and audit rows for the PIN after each step and the key's `pin_in_chat_or_logs` expectation is graded against it.
- **Configuration.** `src/Agent.Eval/appsettings.json` holds no secret: the admin and client keys default to `dev` for the wallet's Development open mode and come from `Eval:WalletAdminKey`/`Agent:WalletClientKey` otherwise; the runner uses its own agent database (`ZainCashAgent_Eval`) and clears only pending and audit rows; logging at Warning.
- **Faults.** Wallet faults are armed `once` through the admin routes and cleared after each case; parser faults are a wrapper in the runner (`RecordingParser`), never a code path in `Agent.Core`; the external-debit approximation is an ordinary wallet transfer from the user under a fresh key.
- **Rules 8, 9.** Synthetic data only (the runner's own fixtures are English placeholders marked as runner fixtures); no secrets.

## Minor
- **M1.** `Agent.Eval` prints exception type and message in `StepError`; our own exceptions carry no user text, but a future parser exception from a model SDK might echo input. Before milestone 6, scrub `StepError` to the exception type plus a fixed message, or have the model parser wrap SDK exceptions. (Carry-forward to the milestone 6 plan.)
- **M2.** A second run on the same day overwrites the committed report (`dev-scripted-<date>`). Add a time or a sequence suffix when the baseline reports of milestone 6 are produced. (Carry-forward.)

## Recommendations
- R1 Add the eval database to README's database section with the other `_Eval`/`_Test` names (done in the README run-eval section; keep them together).
- R2 The runner's `--wallet` option accepts any URL; outside loopback the client key travels in clear (same note as the agent's `WalletBaseUrl`).
- R3 When the model parser lands, the outbound-text check becomes meaningful: wire the masker before the `RecordingParser` so the recorded texts are the masked ones the model sees.

## Forward-looking for milestone 6 (model in)
The masker sits between `Conversation` and `IParser`; `RecordingParser` records the masked text; `IParserUsage` carries real tokens and cost; a `ParsedRequestValidator` turns invalid structures into `Unclear` (security review of milestone 3, I3); baseline reports per candidate are produced with `Agent.Eval dev` and committed under `docs/reports/` with the candidate's name in the file name.
