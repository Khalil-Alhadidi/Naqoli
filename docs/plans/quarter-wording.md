# Quarter amounts (ربع, الا ربع): wording for Khalil to edit

## Checked by Khalil - Approved
Drafted by Claude on 22 Oct 2026 at Khalil's request. Every Arabic string below is a draft in the
style of the dev set (`حول 2 ونص لاخوية حمودي`, `حول مليون ونص للمحاسب`). Khalil edits the
Arabic column; the other columns are the spec Claude builds from. Delete a row to drop it.

When the file is back, Claude writes prompt version 6, the dev cases ADD-26 to ADD-30 with their
parse lines and answer keys, the tests, and runs the five cases once on Haiku.

## A. Forms the model must read (examples for the prompt)

Only the amount phrase is needed here. The model returns `amount_value` and `amount_unit`; code
multiplies and keeps whole dinars only, so no amount code changes.

| # | Arabic (edit me) | Meaning | Model returns | Dinars |
|---|------------------|---------|---------------|--------|
| A1 | 2 وربع | two and a quarter, bare | 2.25, none | 2,250 |
| A2 | ١٢ الف وربع | twelve thousand and a quarter | 12.25, thousand | 12,250 |
| A3 | مليون وربع | a million and a quarter | 1.25, million | 1,250,000 |
| A4 | مليون الا ربع | a million less a quarter | 0.75, million | 750,000 |
| A5 | الفين الا ربع | two thousand less a quarter | 1.75, thousand | 1,750 |
| A6 | 3 الا ربع | three less a quarter, bare | 2.75, none | 2,750 |
| A7 | ربع مليون | a quarter of a million | 0.25, million | 250,000 |
| A8 | مليون الا نص | a million less a half | 0.5, million | 500,000 |

Questions for Khalil:

- A8 is there so "less a half" reads the same way as "less a quarter". Keep or delete?
- Is "الا" the form you say, or "إلا" / "غير"? List every spelling the model should accept.
- Any other fraction word in use (ثلث, ثلاثة ارباع)? If yes, add a row with the reading.

## B. Dev-set cases (full messages)

Transfers only: bills and purchases carry fixed amounts. Fee is 1,000 as in the other cases;
Claude computes the answer keys from the seed.

| Case | Arabic (edit me) | Meaning | User, recipient | Amount | Outcome | Why this case |
|------|------------------|---------|-----------------|--------|---------|---------------|
| ADD-26 | حول 2 وربع لاخوية حمودي | send two and a quarter to my brother Hammoudi | U1, S01 | 2,250 | confirm, done | the plain case, like ADD-10 |
| ADD-27 | حول مليون الا ربع للمحاسب | send a million less a quarter to the accountant | U3, T03 | 750,000 | confirm with PIN, done | the "less" form, PIN path like CLN-08 |
| ADD-28 | حول ربع مليون لاخوية حمودي | send a quarter of a million to my brother Hammoudi | U1, S01 | 250,000 | confirm, done | quarter before the unit |
| ADD-29 | حول ربع لاخوية حمودي | send a quarter to my brother Hammoudi | U1, S01 | 250 | refused, below minimum | proves the checks catch it, no card |
| ADD-30 step 1 | اريد احول فلوس لاخوية حمودي | I want to send money to my brother Hammoudi | U1, S01 | asks which_amount | | like AMB-06 |
| ADD-30 step 2 | ٣ الف وربع | three thousand and a quarter (typed answer) | | 3,250 | confirm, done | a fraction in a typed amount |

Questions for Khalil:

- ADD-29: is `حول ربع لاخوية حمودي` something a person would type, or should the bare-quarter
  case be dropped?
- ADD-27 uses U3 and the accountant so the PIN is required (amount over the PIN threshold).
  Fine, or prefer U1 and a smaller "less a quarter" amount?

Note (Claude, after Khalil's approval): ADD-27's 750,000 is below the PIN threshold of 1,000,000 (`data/seed/settings.json`), so its key has `pin_required: false` and a plain Confirm.

## C. Matching rule 9 (Khalil's file)

If you want the rule to record it, add to the examples column of rule 9 in `docs/matching-rules.md`:
'2 وربع' is 2,250; 'مليون الا ربع' is 750,000; 'ربع مليون' is 250,000. Claude then updates
`docs/rules-coverage.md`.

Built 22 Oct 2026: prompt version 6, ADD-26 to ADD-30, Haiku run 5/5.
