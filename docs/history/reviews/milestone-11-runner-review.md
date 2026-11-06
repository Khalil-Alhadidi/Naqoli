# Milestone 11 review: the eval runner's changes after the freeze

Reviewer: the `dotnet-security-reviewer` agent, 6 Nov 2026, read-only, on the three changes made to `src/Agent.Eval` after the tag `milestone-11-freeze`: a step's `reply` may carry its `amount`; the loader names every bad line in one refusal; the held-out set is loaded tolerantly (`docs/agent-eval.md`, "The held-out guard"). `Agent.Core`, the prompt and the settings were frozen and are not part of it. The review ran after the two held-out runs were committed; the reviewer read the two reports (ids, codes and figures) and did not open the sealed set.

Khalil's decision, 6 Nov 2026: nothing is fixed after the run; the findings are recorded as they are. The runner in the repository is the runner that produced the two reports.

## Verdict on the two held-out reports

No route was found by which either committed report turned a fail into a pass, and no target figure moves. The six cases read in part that passed (AMB-18, AMB-22, NOGO-08, CMP-04, CMP-08, FLT-01) did not pass through a default graded in silence; the four that failed (CMP-10, FLT-02, GAP-02, GAP-05) failed on parts that were read. The reports do not attest the sixteen unread expectations themselves (known gap 50).

## Findings, recorded and not changed

| # | Severity | Finding | In the held-out run |
|---|---|---|---|
| 1 | Important | The tolerant walk treats the case, its steps and its fault like the answer key: a wrong-typed `twice` or `pin`, a wrong-typed `fault.times` or an unknown step or fault member would be dropped and named, and the case would then play differently from how it was written. An unknown tap, wait, fault code or fault moment still refuses the file. A dropped step member and a dropped step expectation would carry the same name. | Did not happen: see "The check" below. |
| 2 | Minor | A JSON name claimed by a property that cannot be set (`not_read`, `no_card` when its value reads as a table, `alias_offer_set`, `loaded_alias_offer`, `option_texts`, `value_text`) is skipped by the serializer without being named; and the `NotRead` property makes a strict load accept a top-level `not_read`. Such a member would be an expectation left ungraded and unlisted, never a wrong verdict. | Did not happen: see "The check" below. |
| 3 | Minor | A key that writes an expectation only where the format does not know it (an alias or a question on a step, nothing at the case level) still has the case-level default graded (`final.alias_saved` null, `asks.count` 0). | Did not happen: the three cases with such step names (AMB-18, AMB-22, GAP-02) also carry the case-level `asks`, and AMB-18's `final.alias_saved` was itself not read and so not graded. |
| 4 | Minor | An `either` key chooses its branch before the not-read parts are marked, so a part dropped inside a branch can fail that branch. False fail only. | Did not happen: no `either` name was not read. |
| Note | | CMP-10 fails on `replies[0]`: the key writes `not_supported`, the agent answered the specific reason `withdrawal`. The refusal grader accepts an unsupported reply as `not_supported` whatever its code; the replies grader compares the code exactly. That fail may be the key's form and not the agent's miss. | Listed with this note in `docs/results.md`, "Every miss"; it belongs to Khalil's reading (known gap 51). |

Approved without remark: the step `amount` grading; that a dropped required value still refuses the file and that lists of records are never dropped whole; the rule that marks a verdict on a not-read part as not graded, for the field names the grader emits; the targets, which respect not-graded verdicts; the report readers, which skip the new `not_read` list; exceptions and the absence of case text in names and messages.

## The check

To see whether findings 1 and 2 had touched the run, the two fixes the reviewer proposed were applied in a working copy only, never committed: the tolerant load reads only the answer keys (the case's `expect` and each step's `expect`) and refuses anything about a case, a step or a fault; and a name a property claims but cannot set is named. With them the held-out file was loaded once by the run command with an `--only` id no case has, so that the file is loaded and nothing is played (no model call, no wallet call). The loader printed the same 16 parts of the same 10 cases as before the run. So none of the parts dropped in the run was about how a case is played (the eight `steps[k]` names are step expectations), and none was dropped in silence: the list of 16 is complete. The working copy was then restored to the committed code.

The held-out set is not loaded or run again, and the tolerant load is used for no other set, so the findings have no further effect; they would be fixed first if the runner were ever given another sealed set.
