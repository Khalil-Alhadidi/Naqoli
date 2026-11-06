# Documents

One line per document in `docs`, grouped by what it is for.

## Read first

- [architecture.md](architecture.md): the agreed design, decisions D1 to D22, and the decisions log.
- [results.md](results.md): what the agent scores on the development set, on each model, and how the model was chosen.
- [failure-modes.md](failure-modes.md): every way the system can fail that we found, and how each is handled.
- [known-gaps.md](known-gaps.md): every known limit of the agent as built, and what would close it.
- [disclosure.md](disclosure.md): every library, tool, model and data source used, the freezes and every fix after them.

## Reference

- [wallet-api.md](wallet-api.md): the mock wallet's contract: routes, fields, error codes, faults and the admin key.
- [agent-core.md](agent-core.md): the agent's write path: the structured request, resolvers, checks, pending states, Confirm and the audit log.
- [agent-model.md](agent-model.md): the parse call: masking, the structure, the validator, the guards, the prompt, the candidates and the choice.
- [agent-web.md](agent-web.md): the console and the phone frame.
- [agent-eval.md](agent-eval.md): the eval runner: how a request set is played, graded and reported.
- [matching-rules.md](matching-rules.md): Khalil's matching and question rules.
- [rules-coverage.md](rules-coverage.md): where each matching rule is built and tested.
- [targets.md](targets.md): the targets from the brief and how each measure is computed.
- [user-messages.md](user-messages.md): every text the phone can show, for Khalil's review.
- [tuning-backlog.md](tuning-backlog.md): the tuning items of milestone 10, each with the cases it would move and where the fix sits.
- [build-plan.md](build-plan.md): the milestones, their "Done when" lines and what was built.
- [HELDOUT_SEAL.md](HELDOUT_SEAL.md): the held-out set's seal and hash.

## Evidence

- [reports/](reports/): every eval report the runner wrote (`dev-*`, Markdown and JSON) and the hand-written model comparisons (`model-comparison-*.md`).

## History

- [history/](history/): the plans of each milestone and round (`history/plans/`), the security reviews (`history/reviews/`), the early research note on model access (`history/research/`) and the status note of 15 Oct 2026.
