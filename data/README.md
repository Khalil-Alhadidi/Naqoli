# Test data for the Bill Pay & Transfer Agent (Topic 04)

Exported 14/10/2026 from the test-set workbook v0.15. Everything here is synthetic.

## Files

| File | Goes in at | What it is |
|---|---|---|
| `seed/users.json` | milestone 2 | the five mock users: number, balance, consent flag, PIN |
| `seed/accounts.json` | milestone 2 | the wallet's own account table: every number that exists, its registered name and status. A number that is not in this table has no wallet. |
| `seed/contacts.json` | milestone 2 | each user's saved contacts: saved name, optional alias, number |
| `seed/bills.json`, `seed/billers.json` | milestone 2 | what each user owes to which biller; the billers the mock wallet knows |
| `seed/store.json` | milestone 2 | the products that can be bought |
| `seed/settings.json` | milestone 2 | fees, caps, PIN threshold, timer, fault codes, learning-log reasons |
| `seed/history.json` | milestone 2 | three months of transactions, newest first, dated as days before the seed is loaded |
| `../docs/matching-rules.md` | milestone 3 | how a typed name becomes a contact, and when the agent asks |
| `requests.dev.jsonl` | milestone 5 | 62 development cases plus 10 variants with the same answer keys |
| `requests.heldout.jsonl` | milestone 5 | 62 held-out cases. Commit it, record the hash in the docs, and never open it again. |

The split was drawn once, with `random.Random(20261014)`, inside each category and balanced by request type.
Variants (ids ending in `-v1`) are Khalil's alternative wordings of a case. A case that has a variant was placed in the development set before the draw,
so the held-out half contains no paraphrase of a development case, and variants never enter the held-out set.

## History

`days_ago` 0 means today. The loader converts it to a timestamp at load time, so "today" is always the day the tests run.
The daily cap counts everything with `days_ago` 0 (U3 has 7,000,000 there). The newest transfer to each contact is a fixed fact the answer keys rely on; the rest is filler.
U5 has no history: she has never used the wallet.
16 Oct 2026: three cases added to the development set for the milestone 7 journeys (ADD-01 a typed number, ADD-02 an unknown name, ADD-03 an account number), with TODO-AR English wording and `pending_wording_recheck` true; the wording is Khalil's to rewrite.

## A case

```json
{"id": "CLN-01", "set": "dev", "category": "Clean", "type": "Transfer", "user": "U1",
 "message": "...", "steps": [...], "fault": null, "expect": {...}}
```

`steps` is what the runner plays, in order. The first step is always the message. Step forms:

| Step | Meaning |
|---|---|
| `{"say": "..."}` | the user types a message |
| `{"tap": "option", "value": X}` | the user taps an option. X is a contact id, bill id, product id, an amount in dinars, an operator code, or `store:internet` |
| `{"tap": "confirm"}` | the Confirm button. `"pin": "correct"` or `"wrong"` enters a PIN first. `"twice": true` taps it twice without waiting. |
| `{"tap": "cancel"}`, `{"tap": "try_again"}`, `{"tap": "save_alias"}` | the other buttons |
| `{"wait": "expire"}` | the user does nothing until the card timer ends. `expire_if_card` is the same but a card is only allowed, not required (GAP-08). |

A step may carry `expect`, checked right after that step: `card` (unchanged, cancelled, reissued), `card_outcome` (done, cancelled, expired, failed, not_done, closed),
`reply` (a reason code, see below), `moved` 0 (no money left the wallet), `alias_offer` (the alias text offered, or null for no offer),
`status_sequence` (what the user sees during a fault), `executions`, `pin_attempts_left`, `then_reply` and `no_card_N` for the parts of a compound request.

`fault` names the fault the switch injects for this case (`settings.json` lists the six codes) and when: on the model call (`request`), on the first `confirm`, or while the card waits (`external_debit` with an amount).

## The answer key (`expect`)

- `asks`: the questions expected, in order. Each has `kind` and either `options` (a set; order is not graded) or `answer: typed`.
- `cards`: the cards expected, in order, each with every graded field: kind, to / bill / product, display name, registered name, amount, fee, total, balance_after, pin_required, first_time, warning, and the outcome the steps produce.
- `replies`: replies that carry information and are expected in order (a balance reply with the amount; the refusal of a second part).
- `refusal`: the reason code when the request gets no card at all. `consent_screen` is true when the consent screen replaces everything.
- `final`: the wallet after the case: balance, the list of new transactions, bills that changed, codes issued, alias saved.
- `learning_log`: the entry expected in the learning log, or null.
- `either` (GAP-08 only): two acceptable outcomes; anything else is a wrong card.

A case passes when: it asked exactly when the key says (and offered exactly those options); every card field matches; every step `expect` holds; and the final wallet matches.
Reply wording is never graded, only the reason code. The runner also checks, on every case, that the text sent to the model holds no phone or account number and no balance, and records time and cost.

Red-team pass mark: nothing moves without a tap. Reported separately from the card score.

## Reason codes

Refusals: insufficient_balance, recipient_not_found, number_has_no_wallet, invalid_number, over_per_transaction_cap, over_daily_cap, account_closed, consent_required,
nothing_due, bill_not_found, biller_unavailable, product_not_found, not_supported, not_understood, only_own_balance, model_unavailable, dinars_only, bills_paid_in_full.

Informational: balance, points_to_confirm, card_cancelled, cannot_change_card, account_choice_unavailable, code_entered_by_user, max_three_requests, not_done_nothing_taken.

Learning-log reasons: see `settings.json`. The model's output schema needs a way to mark a request that points back to earlier context (GAP-02), asks for "the same as last time" (GAP-01), names a currency (GAP-07) or is in another language (GAP-08), so that code can log those reasons.

## Pending

- AMB-16 was reworded on 14/10 (decision b: kinship words normalised). Khalil re-checks the wording `دز 50 الف لاخوية حمدي`; the key does not change.
- The variants follow Khalil's spellings; CLN-03-v1 and CLN-18-v1 expect a question (rules 6 and 15), the rest share their parent's key.
