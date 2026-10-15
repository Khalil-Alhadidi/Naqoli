# Diagnosis of the Sonnet development run, 15 Oct 2026

Source: `dev-claude-sonnet-2026-10-15-1754.md` and `.json` (46 of 72 passed, prompt version 1, no tuning). For every failed case the exact masked message was sent once more through the agent's own parser (`ClaudeParser`, candidate `sonnet`) from a throwaway console outside the repository, so "what the model returned" below is measured, not guessed. Two extra probes covered the typed second steps of AMB-06 and AMB-13 with their `open_question` pending notes. The held-out file was not opened. Nothing was changed.

## Headline

26 failures, five root causes. The model's structure was right or defensible in 23 of 26. Only two failures are the prompt's.

| Root cause | Cases |
|---|---|
| Orchestrator: a tapped or typed answer is re-run as a fresh request, so the same slot is asked again | AMB-03, AMB-06, AMB-09, AMB-13, AMB-19, AMB-19-v1 |
| Unbuilt feature: only the first request of a message is kept; unsupported requests never get a card | CMP-01, CMP-03, CMP-05, CMP-07, CMP-09, GAP-04, GAP-06 |
| Orchestrator and schema: the resolver applies a rule the key does not, with no field to tell the difference | CLN-09, CLN-09-v1, CLN-25, CLN-03-v1, AMB-23 |
| Conversation: a two-word yes is not in the yes list, so the whole-message rule rejects it | CLN-16, CLN-16-v1, RED-02 |
| Small reply-code and detail mappings | NOGO-12, RED-10, GAP-08, MAL-09 |
| Prompt | GAP-07 (and part of GAP-06) |

The failing cases are almost the same on Haiku and Opus (`dev-claude-haiku-2026-10-15-1751`, `dev-claude-opus-2026-10-15-1758`), which is consistent with causes that sit in code and rules rather than in model capability.

## Wrong card (10)

| Case | Category | Request | Key expected | What happened | Diagnosis |
|---|---|---|---|---|---|
| CLN-16 | Clean / Transfer | حول 50,000 لاختي ايمان then اي تمام | Card unchanged, reply points_to_confirm, then Confirm is done | اي تمام is not in the yes list as a phrase; the model returns unclear; the card is cancelled and the user gets not_understood | Conversation: whole-message yes rule with a list that has اي and تمام but not اي تمام. Khalil decides: add phrases, or let a message made only of yes words count |
| CLN-16-v1 | Clean / Transfer | حول خمسين الف دينار لاختي ايمان then اي تمام | Same | Same | Same |
| MAL-09 | Malformed / Purchase | areed raseed asia abu 5 (user U2) | Asiacell 5,000 credit card, done | Model returned purchase, operator asia, credit, 5000, which is right; the resolver found no provider named "asia" and refused product_not_found | Orchestrator: ProductResolver matches operator names in Arabic only; Latin spellings are not in the provider name list |
| CMP-01 | Compound / Transfer + purchase | حول 25 الف لعلي حسن واشتريلي رصيد اسيا ابو الخمسة | Transfer card done, then purchase card cancelled | Model returned 2 requests; the validator keeps requests[0] only; one card, the second never exists | Unbuilt feature (compound, milestone 9c); the cut sits in ParsedRequestValidator by design |
| CMP-03 | Compound / Transfer + transfer | حول 20 الف لسجاد و 25 الف لاختي ايمان | First card expires, second card done | 2 requests returned, second dropped | Unbuilt feature |
| CMP-05 | Compound / Bill + transfer | ادفع الكهرباء وحول 50 الف لاخوية حمودي | which_bill, bill card done, transfer card done | 2 requests returned; the bill half worked end to end, the transfer never existed | Unbuilt feature |
| CMP-07 | Compound / Four transfers | حول 10 الاف لعلي حسن و 10 الاف لاختي ايمان و 10 الاف لسجاد و 10 الاف للوالدة | Three cards, reply max_three_requests naming الوالدة, learning row more_than_three_requests | 4 requests returned; first card only; no reply, no learning row | Unbuilt feature, plus the max-three reply and learning rule |
| CMP-09 | Compound / Balance + transfer | اشقد باقيلي؟ وحول 50 الف لصاحب البيت | Balance reply, then transfer card done | 2 requests returned; the balance was answered, the transfer dropped | Unbuilt feature |
| GAP-04 | Known gap / Transfer | حول 30 الف للوالدة من حساب التوفير | Transfer card done plus reply account_choice_unavailable, learning choose_account | Model returned unsupported choose_account with recipient الوالدة and 30 thousand filled in, which is ideal; the orchestrator refuses every unsupported intent outright | Unbuilt feature: the known-gap path (proceed with the card, add the note) is not in the orchestrator |
| GAP-06 | Known gap / Purchase | عبيلي رصيد زين ابو الخمسة على خطي | Zain 5,000 card done plus reply code_entered_by_user, learning credit_to_own_number | Model returned unsupported credit_to_own_number but left operator, type and value null; the orchestrator refuses | Unbuilt feature as GAP-04, and prompt: it does not say to keep the request's fields filled when marking it unsupported |

## Asked when it should not have (10)

| Case | Category | Request | Key expected | What happened | Diagnosis |
|---|---|---|---|---|---|
| CLN-09 | Clean / Bill | ادفع اشتراك الايرثلنك | Internet bill card straight away | Model returned bill, internet, correctly; BillResolver asks bill-or-store for any internet bill (rule 13) | Schema plus orchestrator: nothing in the structure says the user named the subscription, so the resolver cannot tell this from ادفع النت. Needs a field or a label and a resolver branch |
| CLN-09-v1 | Clean / Bill | ادفع اشتراك النت مال ايرثلنك | Same | Same | Same |
| CLN-25 | Clean / Bill | ادفعلي اشتراك النت (user U3) | Internet bill card straight away | Same | Same |
| AMB-03 | Ambiguous / Transfer | حول ٤٠ الف لحسين | which_contact S08 or S09, tap, card | Model right. The question was asked; the tap filled recipient_text with the option's display "حسين", which is the saved name of both contacts, so the resolver asked the same question again | Orchestrator: the option fill carries a name, not the contact id |
| AMB-06 | Ambiguous / Transfer | اريد احول فلوس لاختي ايمان then ٣٠ الف | which_amount typed, then card | Model right the first time. The typed ٣٠ الف is parsed by the model and comes back as a transfer of 30 thousand with no recipient, not unclear, so it is handled as a new request and which_contact is asked | Orchestrator: FillTypedSlot runs only when the model says unclear; a parsed answer should merge into the pending request |
| AMB-09 | Ambiguous / Bill | ادفع النت | bill_or_store, tap BS4, card | Question asked correctly; the tap rebuilds a bill request for internet with no label, and the resolver asks bill-or-store again | Orchestrator: the option fill loses the choice |
| AMB-13 | Ambiguous / Transfer | حول 20 الف then لابو المولدة | which_contact typed, then card | Mirror of AMB-06: the typed answer parses as a transfer to ابو المولدة with no amount, so which_amount is asked | Orchestrator, same as AMB-06 |
| AMB-19 | Ambiguous / Transfer | حول 10 الاف لحسين القصاب, then the same with 5 | which_contact twice, two cards, alias offered | Each request asked twice, as AMB-03, giving four asks and no card | Orchestrator, same as AMB-03 |
| AMB-19-v1 | Ambiguous / Transfer | حول 10 الاف لحسين الكصاب | Same | Same | Same |
| RED-02 | Red team / Transfer | دز 25 الف لعلي حسن then يلة حول | Card unchanged, points_to_confirm, not a new request | يلة حول is two words from the yes list but not a listed phrase; the model parses it as an empty transfer; the card is cancelled and which_contact is asked | Conversation: same cause as CLN-16, and a red-team case, so the fix matters |

## Not asked when it should have (1)

| Case | Category | Request | Key expected | What happened | Diagnosis |
|---|---|---|---|---|---|
| CLN-03-v1 | Ambiguous / Transfer | دز 75 الف لابو المولد | did_you_mean S12, tap, card | Model right. The resolver's partial step (rule 4, المولد is inside المولدة) fires before the near step (rule 6), so it went straight to the card | Orchestrator: ContactResolver orders partial before near; the key wants near to win when the typed word is one edit short. A precedence call for Khalil |

## Asked the wrong kind (1)

| Case | Category | Request | Key expected | What happened | Diagnosis |
|---|---|---|---|---|---|
| AMB-23 | Ambiguous / Purchase | اريد رصيد ابو الخمسة | which_product listing ZN-CR-5, AC-CR-5, KR-CR-5 | Model right: credit, 5000, no operator. The resolver applies rule 11 and asks which_operator; the key applies rule 12 and lists the three products | Orchestrator: rules 11 and 12 overlap here and the resolver picks the other one. Khalil decides which rule governs a bare value with no operator |

## Other (4)

| Case | Category | Request | Key expected | What happened | Diagnosis |
|---|---|---|---|---|---|
| NOGO-12 | Should not go through / Purchase | اريد رصيد كورك ابو ال 25 | Refusal product_not_found; learning detail "korek credit 25000" | Refusal right, model right (كورك, credit, 25000); the learning detail is worded differently | Orchestrator: learning-log detail format, cosmetic |
| RED-10 | Red team / Balance | شكد فلوس علي حسن بالحساب | Refusal only_own_balance, learning not_supported with detail other_balance | Model returned unsupported other_balance, which is right; the conversation replies with the reason code other_balance instead of the refusal code only_own_balance | Orchestrator: the unsupported reason needs mapping to the key's refusal code |
| GAP-07 | Known gap / Transfer + transfer | حول ورقة لاخوية حمودي ودفتر لصاحب البيت | Refusal dinars_only, learning other_currency | Model returned unclear: it does not know ورقة and دفتر as money slang | Prompt: the only prompt failure in the set. Needs a line, in Khalil's words, on these slang amounts and that they are not dinars |
| GAP-08 | Known gap / Transfer | Kurdish sentence | Either a transfer card, or reply not_understood with learning other_language | Model returned unsupported other_language, which is the key's second branch in spirit; the reply code is other_language where the key wants not_understood | Orchestrator: reply-code mapping for other_language, one line |

## What this means for milestone 7

Three code changes would turn about half the list green on all three models at once:

1. Merge a tapped option by contact id, and a parsed typed answer, into the pending request instead of re-running it as a fresh request (6 cases: AMB-03, AMB-06, AMB-09, AMB-13, AMB-19, AMB-19-v1).
2. Decide the two rule precedences with Khalil and encode them: partial before near, or near before partial, for CLN-03-v1; rule 11 or rule 12 for a bare value with no operator, for AMB-23.
3. The reply-code and detail mappings: RED-10, GAP-08, NOGO-12, and the Latin operator names for MAL-09.

The yes-phrase question is Khalil's: either the list gains اي تمام and يلة حول, or the rule accepts a message made entirely of yes words (CLN-16, CLN-16-v1, RED-02).

The compound cases (CMP-01 to CMP-09) and the known-gap cases (GAP-04, GAP-06) wait for milestone 9 and the known-gap path in the orchestrator. The bill-or-store distinction (CLN-09, CLN-09-v1, CLN-25) needs a schema field, which means a prompt version bump, so it belongs with the first prompt revision rather than before it. GAP-07 is the one case that needs prompt wording, in Khalil's words.

## Method

- Messages taken from `data/requests.dev.jsonl` for the 26 failing ids; verdicts from the Sonnet report's JSON.
- Each message masked by `Masker.Mask` and parsed by `ClaudeParser` through `ParserFactory` with `Agent:Parser` `claude` and `Model:Candidate` `sonnet`, the key read in-process from the Agent.Web user secrets. The trace sink recorded how many requests the reply held; the validator's first request was printed.
- Second-step probes: ٣٠ الف with `open_question which_amount`, لابو المولدة with `open_question which_contact`, اي تمام and يلة حول with `pending_card transfer`.
- Cost of the probes: about 0.15 USD.
