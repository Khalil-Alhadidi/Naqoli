# Security review: milestone 10 (model choice, learning data, results), range c59a723..991f88c

Reviewer: the `dotnet-security-reviewer` agent (Claude Opus), 3 Nov 2026, on the branch diff of `src` and `tests`, `CLAUDE.md`, the design (`docs/plans/milestone-10-model-choice.md`) and the 9c review for format. The held-out file and the workbook were not opened. The four fixture reports were scanned for Arabic characters (none) and for digit runs of nine or more (SHA-256 values and percentage fractions only). No file was modified by the review.

**Verdict: pass with changes.** Critical 0, Important 0, Minor 4, Notes 7.

## Summary

- **Rule 1 (no tap, no money):** nothing new writes money. The only new write is `LearningLog.SetReviewedAsync`, one bool in the agent database. No new code reaches the wallet client or the Confirm handler.
- **Rules 2 to 5:** neither the prompt nor the parser input changed. The Learning data panel shows `MaskedText`, `Reason` and `Detail` as stored (the user's masked words and a masked detail; no balance, PIN, purchase code or model text), on the console, through Razor encoding. No `MarkupString` anywhere in the range. In the Results panel every string from a report is rendered as text or an encoded attribute; `VerdictClass` can only add CSS class names.
- **Rule 7:** `ReportReader.Patterns` is `dev-*.json` and `heldout-*.json`; `requests.heldout.jsonl` matches neither (prefix and extension). The reader binds the header, totals, targets, time, cost, groups and runs; the `cases` array is never bound.
- **Rule 9:** the new setting is a directory path, shown read only, pinned as not editable by `RuntimeSettingsTests`. Failure texts show only the exception type name.
- **Path handling: sound.** A posted select value is opened only if it equals a `FileName` in the listing, and every listed name comes from `Path.GetFileName` of a file enumerated in the resolved directory, so a parent-directory name, a rooted name or a UNC name never reaches `Path.Combine`. Only the configured `Console:ReportsDirectory` can point outside the repository, and it is operator configuration on a loopback console.
- **Blazor:** subscriptions to the singleton `LearningCount.Changed` are removed in `MainLayout.Dispose`, which cancels before disposing the token source; `ReadCountAsync` checks cancellation before touching the token; cross-circuit handlers marshal with `InvokeAsync`; field-like event add and remove are thread safe.
- **Checkbox input:** the handler captures `row.Id` on the server; `data-id` is display only. A client can fire only the handlers of rows rendered in its own circuit, so it cannot tick an arbitrary id. It can tick any listed row on a console with no sign-in, acceptable for a loopback-only demo console. No worse write is possible.

## Findings

### Minor

**M1. The Results panel can end the circuit on inputs it does not catch.** `ResultsPanel` catches only `InvalidOperationException` around `Resolve`; `Directory.EnumerateFiles` in `ReportReader.List` runs outside the per-file try; `Read` checks only `Set` and `Parser`, so a report with a header and targets but no `totals`, `time`, `groups` or `runs` lists, opens, then throws `NullReferenceException` in render; the same `Resolve` call runs in `SettingsPanel`. An unhandled exception in a lifecycle method or in rendering is fatal to the circuit. Fix: `Read` throws `JsonException` when `Single` lacks `Totals` or `Time`, or `Repeat` lacks `Groups` or `Runs`, so `List` skips the file; the enumeration in `List` is inside the same `IOException or UnauthorizedAccessException` catch (an empty listing); both `Resolve` catches widen to `InvalidOperationException or ArgumentException or NotSupportedException or IOException`; one test per case.

**M2. After a failed tick, the checkbox keeps its toggled state, and the error line can be wiped.** On a save failure `LoadAsync` re-reads the same `Reviewed=false`; the render-tree value has not changed, so Blazor's diff does not rewrite the `checked` property and the DOM box stays ticked while the stored value is unticked. If a load is already running when the toggle fails, the running loop clears `failure` after the toggle set it. Fix: a `loadVersion` counter incremented in each `LoadAsync` and `@key="(row.Id, loadVersion)"` on the row, so rows are rebuilt after a reload; the tick's problem kept in its own field that the load never clears.

**M3. The guard that keeps a posted file name inside the directory lives in a Razor component and has no test.** It is the one line between a browser-posted string and `File.OpenRead`. Fix: `ReportReader.Open(string directory, ReportListing listing, string fileName)` returning null unless the name is listed and `Path.GetFullPath(Path.Combine(directory, name))` starts with `directory + Path.DirectorySeparatorChar`; a test with a parent-directory name, a rooted path and a UNC name.

**M4. The listing has no size bound and is rebuilt on every panel open.** Every `dev-*` and `heldout-*` file is parsed in full on each open; more than 100 files today, growing with every eval run. Not exploitable from the browser. Fix (later): a size cap counted as skipped; optionally a cache by `(path, LastWriteTimeUtc)`; or a `Utf8JsonReader` that stops at `targets`.

### Notes

- **N1.** `(bool)e.Value!` throws `InvalidCastException` on a crafted non-bool post, before the try, ending that client's own circuit only. Use `e.Value is true`.
- **N2.** `SetReviewedAsync` is find then save with no concurrency token: last write wins (fine for an absolute bool); a row deleted between find and save throws `DbUpdateConcurrencyException`, reported as "tick not saved". A single `ExecuteUpdateAsync` returning `> 0` is one round trip and turns the reset race into a plain `false`.
- **N3.** `LearningCount.NotifyChanged` runs handlers synchronously inside the toggling circuit's try; every handler only schedules `InvokeAsync`, so a throwing subscriber is theoretical.
- **N4.** Only ticks raise `LearningCount.Changed`; a learning row written by another circuit's action updates that circuit's own count, and other open consoles keep a stale count until their next action or menu click. As documented.
- **N5.** `ReportReader` follows a symlink or junction named `dev-*.json`, unlike `ScriptedParser.Load`, which refuses links. The browser cannot reach outside the directory; a JSONL target fails `JsonDocument.Parse` and is skipped. Optionally skip `FileInfo.LinkTarget is not null`.
- **N6.** `Task.Run` around synchronous file IO in `ResultsPanel` has no cancellation; a read finishing after the panel is gone sets fields and requests a render the renderer ignores. Harmless; async file reads under a token cancelled in `Dispose` would match the guidance better.
- **N7. Test gaps:** `ResolveGivesARootedFullPath`'s `Assert.Equal(Path.GetFullPath(resolved), resolved)` cannot fail; no test round-trips a written aggregate. The other new tests can each fail and match their claims; the polling `LearningRowsAsync(minimum: 1)` fixes the single-read flake noted in the UI pass.

## Checked and found sound

`FindAsync([id], token)` binds the boxed `long` to `LearningEntryRow.Id`; `ReadLatestAsync` guards `take < 1`, no tracking, `At` then `Id`, at most `Agent:LogPageSize` rows; `CountUnreviewedAsync` is a single `COUNT`; `ConsoleOptions.ReportsDirectory` is `[Required(AllowEmptyStrings = false)]` with a message naming the key and no value, the default the same in code and `appsettings.json`; invariant culture in every figure, date and id; Sonnet the default in both settings files, pinned by `TheDefaultCandidateIsSonnetInBothProjects`; the journeys pin `Agent:Parser=scripted` and point `Console:ReportsDirectory` at the fixtures.

## Must fix before merge and tag

None of the findings is a rule breach or a security hole. Recommended for the closing wave: M1, M2, M3. M4 and the notes can follow later.

## Disposition (controller, 3 Nov 2026)

One fix wave, one commit (this file's commit).

- **M1 fixed.** `ReportReader.Read` throws `JsonException` for a single run without `totals` or `time` and an aggregate without `groups` or `runs`, so `List` skips and counts it; the enumeration in `List` sits in the `IOException or UnauthorizedAccessException` catch (an empty listing, Skipped 0); the `Resolve` catches in `ResultsPanel` and `SettingsPanel` take `InvalidOperationException or ArgumentException or NotSupportedException or IOException`. Tests: `ReportReaderTests.ListSkipsAReportWithTargetsButNoTotals`, `ListSkipsAnAggregateWithTargetsButNoRuns`.
- **M2 fixed.** `LearningDataPanel` raises `loadVersion` on every read and keys each row `@key="(row.Id, loadVersion)"`, so rows are rebuilt after a reload; a failed tick's problem is its own field and line (`learning-tick-problem`), never cleared by a load, cleared by the next tick that saves. Shown by the build and `ConsoleJourneys.LearningRowCanBeTickedReviewedAndTheMenuCountFollows` (the tick path); the failure path has no journey.
- **M3 fixed.** `ReportReader.Open(directory, listing, fileName)` returns null unless the name is a listed `FileName` and its full path starts with the directory plus a separator; `ResultsPanel` opens through it. Test: `ReportReaderTests.OpenRefusesNamesOutsideTheListing` (parent-directory, rooted, UNC and unlisted names null; a listed name opens).
- **N1 fixed.** The checkbox handler passes `e.Value is true`. Shown by the build and the Learning data journey.
- **N2 fixed.** `SetReviewedAsync` is one `ExecuteUpdateAsync` on `Where(r => r.Id == id)`, returning `rows > 0`. Test: `LearningLogTests` unchanged, 7 of 7 with `ZAINCASH_TEST_SQL=1` (`SetReviewedUpdatesOneRowAndReportsAnAbsentId`).
- **N7 fixed.** `ResolveGivesARootedFullPath` now asserts the relative input resolves to `docs/reports` (joined by `Path.Combine`) under `EvalPaths.RepositoryRoot()`; `TheWriterAndTheReaderAgreeOnAnAggregate` writes a two-run aggregate with `ReportWriter.WriteRepeat`, reads it and compares targets, runs and groups with `ReportWriter.Repeat` by record equality.
- **M4 deferred:** a size cap or a cache by file time when the report count makes the open slow; at about 130 files it is not.
- **N3 deferred:** every handler only schedules `InvokeAsync`; a throwing subscriber is theoretical.
- **N4 deferred:** documented; another console's count catches up at its next action or menu click.
- **N5 deferred:** the directory is operator configuration on a loopback console; the browser cannot leave it (M3).
- **N6 deferred:** harmless today; a late read sets fields on a disposed component and its render request is ignored.
