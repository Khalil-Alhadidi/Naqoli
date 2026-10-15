# Held-out set seal

| Item | Value |
|---|---|
| File | `data/requests.heldout.jsonl` |
| Cases | 62 (per `data/README.md`: the split is 62 development cases plus 10 variants, and 62 held-out cases; the development file has 72 lines) |
| Size | 197,609 bytes |
| SHA-256 | `3e396362fd6cea5fa81e26ad9e4c17e9c4de2066ee186e963690a14c195ca1c1` |
| Committed in | `462187f` (14 Oct 2026) |
| Sealed on | 15 Oct 2026 |

Held-out set sealed before any prompt or model tuning; the file itself is committed at MVP freeze.

The hash was computed from the shell (`sha256sum`) without opening the file. It equals the value recorded in `docs/disclosure.md`, which `Agent.Eval heldout --milestone-11-run` checks before the one permitted run (rule 7 in `CLAUDE.md`). The case count comes from `data/README.md`, not from the file.
