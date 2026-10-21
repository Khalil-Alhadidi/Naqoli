# User messages, for Khalil's review

Every text the phone can show, as built on 21 Oct 2026 (after the milestone 9c close-out; Khalil's wording of `Refusal_not_prepared`, `List_joiner`, `Type_request` and `Reply_queue_dropped` on 20 Oct; person or bill and the consent screen's No the same day; his rewording of `Reply_consent` and `Refusal_consent_required` on 21 Oct, 5149212), one row per reason code or outcome. The Arabic is copied as it stands in `src/Agent.Web/Resources/PhoneStrings.resx`; nothing here changes a string. "When shown" is read from the code that maps a code to its key (`ChatSession`, `Orchestrator`, `Checks`, `ConfirmHandler`, `Conversation`, `AliasDirectory`, the phone components). "Next" is what the user is expected to do after reading it. `{0}` and `{1}` are filled by code with figures in Arabic-Indic digits.

The flags in the last column, for Khalil:

- **F1** the text does not say what happened or what to do next;
- **F2** a generic string covers a specific situation that could have its own line;
- **F3** two situations share one text, or two keys hold one text;
- **U** the key is never shown today (no code path reaches it).

A summary of the flags is at the end. The number words (`Words_*`, 51 keys) are listed in `docs/agent-web.md` and are not repeated here.

## 1. Refusals before a card (`Refusal_<code>`)

Shown as one chat line from the agent. A figure, when the code has one, is formatted as an amount with `Currency_iqd`. A code with no key falls back to `Refusal_not_done_nothing_taken`.

| Key | Arabic | When shown | Next | Flag |
|---|---|---|---|---|
| `Refusal_not_understood` | ما فهمت عليك، عيدها بطريقة ثانية | (a) The model reads the message as unclear. (b) The model's reply is not in the required structure (ADD-18). (c) The amount is not whole dinars (`0.0005`). (d) An unclear message while a card waits: the card is kept (D6), traced `card_kept`. (e) A parsed intent the orchestrator has no branch for. | Say it again in other words. | F2: (b) and (c) are specific; (c) could name the amount. F1 in (d): the text does not say the card is still waiting and will expire on its own. |
| `Refusal_message_too_long` | الرسالة طويلة. اكتب طلب واحد قصير. | A typed message longer than `Agent:MaxMessageChars` (500, editable), refused before masking with no parse call and no learning row; a waiting card stays (milestone 6 security review, M4; Khalil's string, 19 Oct 2026). | Write one short request. | |
| `Refusal_not_understood_repeat` | ما فهمت. {0} | A message that cannot be read while a question is open; `{0}` is the open question's text (`Question_<kind>`). The question stays open. | Answer the question, or tap its cancel option. | |
| `Refusal_question_cancelled` | خلص. ما سويت شي | After the cancel option (`Option_CancelQuestion`) of any question other than a contact question. | Start a new request. | |
| `Refusal_model_unavailable` | ما أكدر أقرأ الرسائل هسه. جرب بعد شوية، أو استخدم التطبيق | The model cannot be reached or times out (ADD-17). A waiting card is kept. | Try later or use the app. | |
| `Refusal_recipient_not_found` | ما لكيت هذا الشخص بجهات الاتصال مالتك | (a) No contact or alias matches the typed name (rule 8). (b) The user taps `Option_NoneOfThese` on a contact question. (c) The wallet refuses the recipient on the quote (`unknown_recipient`). (d) In the directory: the chosen account is not among the user's contacts. | Type the full mobile number or a saved name. | F1: unlike `Refusal_invalid_number`, it does not say the number can be typed instead. F3: after `Option_NoneOfThese` the user did not type a name, yet the text says "I did not find this person". |
| `Refusal_number_has_no_wallet` | هذا الرقم ما عنده محفظة | A typed 11-digit number with no wallet account; also in the directory when the number has no account. | Check the number. | F1: no next step. |
| `Refusal_invalid_number` | الرقم مو كامل. اكتب رقم الموبايل كامل، لو اسم محفوظ عندك | The typed number is not 11 digits starting 07 after spaces and dashes are dropped (rule 20), including a typed account number (D21, ADD-03). Also in the directory. | Type the full number or a saved name. | F2: a typed account number is told "the number is not complete", which is not what happened; D21 could have its own line (account numbers are not accepted; use the mobile number). |
| `Refusal_insufficient_balance` | رصيدك ما يكفي، تحتاج بعد {0} | The quote says the total exceeds the balance (check 5); `{0}` is the shortfall. Also nested in `Outcome_not_done` when the recheck at the tap or the wallet's execute refuses (`insufficient_funds`, FLT-08). | A smaller amount: for one more message a typed amount merges into the refused transfer (D6 extension). | F1: the text does not say that typing a smaller amount is enough. |
| `Refusal_over_per_transaction_cap` | أعلى مبلغ بعملية وحدة هو {0} | The amount without the fee is above `PerTransactionCapIqd` (check 3), or at the recheck (nested in `Outcome_not_done`). | A smaller amount; merges for one message. | |
| `Refusal_over_daily_cap` | هذا يتجاوز الحد اليومي. باقيلك اليوم {0} | Today's payments plus this amount pass `PerDayCapIqd` (check 4) or the recheck; `{0}` is what is left today. | A smaller amount, or tomorrow; merges for one message. | |
| `Refusal_amount_below_minimum` | أقل مبلغ هو {0} | The amount is under `MinAmountIqd` (check 1; a bare "50" read as 50,000 never reaches it, a "0.5" does). `{0}` is the minimum. | A larger amount; merges for one message. | |
| `Refusal_account_closed` | هذا الحساب مغلق | (a) The user's own payer account is closed (check 2, or the wallet's `account_closed`). (b) The recipient's account is closed. (c) In the directory: the chosen account is closed. (d) Nested in `Outcome_not_done` when the wallet refuses at execute. | (a) nothing the agent can do; (b) another recipient. | F3: the text does not say whose account is closed, and the next step differs. |
| `Refusal_nothing_due` | ماكو شي مطلوب على هاي الفاتورة | The named bill has nothing due (ADD-05), or the wallet says so at quote or execute (a concurrent payment cleared it). | Nothing to do. | |
| `Refusal_bill_not_found` | ما لكيت هاي الفاتورة | The biller named is not among the user's bills. | Name a biller the user has, or say "pay the bill" to see the list (rule 14). | F1: no next step. |
| `Refusal_bills_paid_in_full` | الفاتورة تندفع كاملة: {0} | The user named an amount that is not the amount due (D9), including "pay part of the bill" (ADD-04); `{0}` is the full amount due. | Ask for the bill without an amount. | F1: the text gives the amount but not that asking again without an amount gives the card. |
| `Refusal_product_not_found` | ما لكيت هذا المنتج | No catalogue product matches the operator, type and value (NOGO-12); an unknown operator name. | Name an operator and value from the store. | F1: no next step and no list of what exists (9b review R5, for Khalil). |
| `Refusal_not_supported` | هاي ما أكدر أسويها | (a) The typed recipient number is the user's own number (detail `own_number`). (b) The wallet refuses the quote as `same_account`. (c) In the directory: the user aliases their own number. (d) An unsupported reason the resource file has no key for (none today). | Another recipient. | F2: a transfer to one's own number is a specific situation ("that is your own number"); the text says neither what happened nor what to do. |
| `Refusal_only_own_balance` | أكدر أكولك رصيدك إنت بس | The user asks for someone else's balance (`other_balance`, RED-10). | Ask for your own balance, or nothing. | F3: the same text is also `Unsupported_other_balance`, which is never shown (section 10). |
| `Refusal_dinars_only` | أشتغل بالدينار العراقي بس | The message names a currency other than dinars (`other_currency`, GAP-07), or the amount carries a currency word. | Say the amount in dinars. | F3: the same text is also `Unsupported_other_currency`, never shown. |
| `Refusal_points_to_confirm` | اضغط زر التأكيد اللي كدامك | A typed yes word while a card waits (D6). The card stays. | Tap Confirm on the card. | |
| `Refusal_card_cancelled` | انلغت العملية | (a) A typed no word while a card waits. (b) A new request typed while a card waits: the card is cancelled and the new request is handled in the same turn. Also the chat line for the Cancel button (section 5). | (a) nothing, or a new request; (b) read what follows. | F3: in (b) the text does not say that the new request is being handled; the user may think the new request was what got cancelled. |
| `Refusal_account_choice_unavailable` | اختيار الحساب مو متوفر هسه | The note shown just before the card when the user chose the account to pay from and gave a recipient and an amount (GAP-04): the card that follows pays from the usual account. | Read the card and tap Confirm, or Cancel. | F1: it does not say the card below pays from the usual account. F3: the same text is `Unsupported_choose_account`, shown when no card follows. |
| `Refusal_not_done_nothing_taken` | ما مشت، بس ما انسحب شي من رصيدك | (a) The kill switch is on: every transfer, bill and purchase is refused before any wallet read (detail `kill_switch`). (b) A wallet read has no answer (unreachable, timeout; detail `wallet_<reason>`) while a request is prepared, at the daily-cap read, or at the quote. (c) The wallet refuses a quote with a code the agent has no reply for (`invalid_amount`, `unknown_product`, `unknown_user`, ...). (d) A tapped option that cannot be turned into an answer. (e) The consent call to the wallet fails. (f) The Confirm handler throws and the record is still Pending. (g) The names screen or the My orders screen cannot load its list (shown in the empty-list slot). (h) The fallback for any code with no key. Also nested in `Outcome_not_done` as `Refusal_nothing_taken_short` (section 5). | Try again later. | F2: (a) the service is paused by the operator, (b) the wallet is not answering, and (g) a list could not be loaded, are three different situations; in (g) "not done, nothing was taken" answers a question the user did not ask. F1: no next step in any of them. |
| `Refusal_nothing_taken_short` | ما انسحب شي من رصيدك | Only inside `Outcome_not_done` as `{0}`, when the reason is `not_done_nothing_taken`, none, or one without a key (Khalil, 18 Oct 2026). | See `Outcome_not_done`. | |
| `Refusal_pin_locked` | الرقم السري انقفل | Nested in `Outcome_not_done` when the wallet answers `pin_locked` (423) at the tap, or when the third wrong PIN locks it. | Unlock the PIN in the app. | F1: no next step. |
| `Refusal_consent_required` | لازم توافق أول يلة تكدر تحول فلوس | The user taps `Reply_consent_decline_button` on the consent screen (Khalil, 20 Oct 2026): nothing is sent to the wallet and the request is cleared; the consent buttons stay. | Tap `Reply_consent_button`; nothing can be typed until then. | |
| `Refusal_biller_unavailable` | هاي الجهة مو متوفرة هسه، جرب بعدين | Never: no code path and no wallet code produces `biller_unavailable`. | | U |
| `Refusal_balance` | رصيدك هو {0} | Never: the balance reply is `Reply_balance`. | | U, F3 (same text as `Reply_balance`) |
| `Refusal_cannot_change_card` | ما تتغير. الغيها واطلب من جديد | Never: a change to the waiting card is shown as `Unsupported_change_waiting_card`. | | U, F3: two texts for one situation, and they differ |
| `Refusal_code_entered_by_user` | الكود إنت تكتبه بنفسك | Never: no code path produces `code_entered_by_user`. | | U |
| `Refusal_max_three_requests` | أقصى شي ثلاث طلبات بالرسالة الوحدة | After the last prepared card of a message with more requests than `Agent:MaxRequestsPerMessage` (3) is finished (done, not done, cancelled or expired), or its refusal or reply is shown; followed at once by `Refusal_not_prepared` (rule 22, milestone 9c, CMP-07). A new message before then drops the rest, and neither line is shown. | Ask again for the rest, one message at a time. | |
| `Refusal_not_prepared` | هذني ما حضّرتهم: {0}. دزهم بطلب ثاني | Right after `Refusal_max_three_requests`: `{0}` the names of the parts not prepared, in the order spoken, each the user's own words (a recipient, a biller or label, an operator) or the request type's word (`Type_transfer`, `Type_bill`, `Type_purchase`, `Type_request`), joined by `List_joiner` (milestone 9c); at most `Agent:MaxNamedNotPrepared` (5, editable) names, the first spoken, while the learning row keeps the total. | Ask again for those parts. | |
| `List_joiner` | {0}، {1} | Not shown alone: the template that joins two names in `Refusal_not_prepared` (`{0}` the names so far, `{1}` the next), applied in order (milestone 9c). | | |
| `Type_request` | طلب | In `Refusal_not_prepared`, the word for a part not prepared that has no type word of its own, such as a balance question (milestone 9c). | | |
| `Reply_queue_dropped` | ألغيت باقي الطلبات | The `Info` code `queue_dropped` (the one `Info` code whose key starts `Reply_`): a typed answer to a typed question that the code could not read and the model read as a partial transfer merged into part 1 of a compound message, and the parts after it (queued or not prepared) were dropped (ruling 5); shown before the merged request's card or question (review R2 of milestone 9c, Khalil, 20 Oct 2026). A new request that drops the rest shows nothing of its own. | Ask again for the rest. | |
| `Refusal_caller_cancelled` | وقف قبل الإرسال. ما انسحب شي | Never on screen: `caller_cancelled` is written on the pending record when the browser closes during a Confirm, and the next circuit shows the record's state, not this text. | | U |

## 2. Unsupported requests (`Unsupported_<reason>`)

One chat line; no card; a learning row for every reason. A reason with no key would show `Refusal_not_supported` (none today).

| Key | Arabic | When shown | Next | Flag |
|---|---|---|---|---|
| `Unsupported_withdrawal` | ما أكدر أسحب كاش | A request to withdraw cash. | Nothing. | |
| `Unsupported_refers_back` | اكتبلي الطلب كامل من جديد | A request that points back in the chat ("send him fifty more"). | Say the full request. | F3: one text with `same_as_last_time`; the next step is the same, so acceptable. |
| `Unsupported_same_as_last_time` | اكتبلي الطلب كامل من جديد | "Same as last time", "like last month" (D4). | Say the full request; or Send again on the last receipt (D19). | F2: could mention Send again on the last receipt, which covers the commonest case. |
| `Unsupported_choose_account` | اختيار الحساب مو متوفر هسه | The user chose the account to pay from and the recipient or the amount is missing (GAP-04). | Say the request again without the account. | F3: same text as `Refusal_account_choice_unavailable`, after which a card does follow. F1: no next step. |
| `Unsupported_part_of_bill` | الفاتورة تندفع كاملة | Never today: a part-of-bill request is read as a bill request (ruling 2, milestone 8) and refused with `Refusal_bills_paid_in_full` and the full amount. | | U |
| `Unsupported_credit_to_own_number` | ما أكدر أرسل رصيد لرقمك إنت | Credit for the user's own number (GAP-06, deferred to milestone 10). | Buy the card and enter the code yourself. | F1: the next step is not said. |
| `Unsupported_change_waiting_card` | ما رهمت. الغيها واطلب من جديد | A message read as a change to the waiting card ("make it sixty"); the card is kept. | Tap Cancel, then ask again. | F3 with `Refusal_cannot_change_card` (never shown; different wording). |
| `Unsupported_other_language` | اكتبلي باللهجة العراقية | A message in a language other than Arabic (ADD-23; Arabizi is Arabic). | Write in Iraqi Arabic. | |
| `Unsupported_receive_from` | ما أكدر أجيب فلوس من حساب شخص ثاني. لازم هو يحوّلك. | A request to receive money from someone (ADD-08, ADD-09). | Nothing; the other person sends. | |
| `Unsupported_other_balance` | أكدر أكولك رصيدك إنت بس | Never: `Refusal_only_own_balance` is shown. | | U, F3 |
| `Unsupported_other_currency` | أشتغل بالدينار العراقي بس | Never: `Refusal_dinars_only` is shown. | | U, F3 |

## 3. Questions and options

A question is a chat line with options under it, or with the typing box as the answer. Every question panel ends with a cancel option.

| Key | Arabic | When shown | Next | Flag |
|---|---|---|---|---|
| `Question_which_contact` | ياهو تقصد؟ | (a) No recipient was named: the user types a name (rule 10, rule 16 asks who first). (b) Several contacts match: options with `Option_LastSent` or `Option_NoPreviousTransfer` under each, then `Option_NoneOfThese`. | (a) type a name or number; (b) tap one. | F3: the same text for "type a name" and "tap one of these"; in (a) nothing says to type. |
| `Question_did_you_mean` | تقصد | A near spelling (rule 6) or a title-word contact (rule 5): options, then `Option_NoneOfThese`. | Tap one. | |
| `Question_which_amount` | شكد المبلغ؟ | No amount was said (rule 10); the user types it, and the answer is read in code. | Type the amount. | |
| `Question_which_bill` | يا فاتورة؟ | "Pay the bill" with no biller named: every bill with an amount due (rule 14). | Tap one. | |
| `Question_bill_or_store` | تدفع فاتورة الإنترنيت، لو تشتري كارت إنترنيت من المتجر؟ | "Pay the internet" with no provider named, from a user with an internet bill (rule 13): the bill and `Option_store_internet`. | Tap one. | |
| `Question_which_product` | شنو هو بالضبط؟ | The products that fit, up to `MaxProductsInOneQuestion` (rules 11, 12, 17), each labelled name, type word and price. | Tap one. | |
| `Question_which_operator` | أي شركة؟ | More than `MaxProductsInOneQuestion` products fit (rule 12, AMB-24). | Tap one. | |
| `Question_person_or_bill` | قصدك فاتورة لو شخص معين ؟ | The typed name is exactly both a contact (saved name or alias) and the label of one of the user's bills (Khalil, 20 Oct 2026): the person (labelled as a contact option, with `Option_LastSent` or `Option_NoPreviousTransfer`), the bill (its biller's name, label and amount due), then `Option_CancelQuestion`. | Tap one: the person goes on as a transfer (a missing amount asks `Question_which_amount`), the bill as that bill's card. | |
| `Option_store_internet` | كارت من المتجر | The store option of `bill_or_store`, and the user's echoed line after tapping it. | | |
| `Option_LastSent` | آخر تحويل {0} | Under a contact option: the last amount sent to that contact (D3). | | |
| `Option_NoPreviousTransfer` | ما حوّلتله من قبل | Under a contact option with no previous transfer. | | |
| `Option_NoneOfThese` | مو هو | The last option of `which_contact` and `did_you_mean`; the reply is `Refusal_recipient_not_found`. | | See the F3 on `Refusal_recipient_not_found`. |
| `Option_CancelQuestion` | إلغاء | The last option of every other question, typed ones included; the reply is `Refusal_question_cancelled`. | | F3: the same word as `Card_Cancel`. |

## 4. The card

Built by code; the labels below frame figures from the wallet. The card's type word is `Type_<type>`.

| Key | Arabic | When shown | Next | Flag |
|---|---|---|---|---|
| `Card_Title` | تأكيد العملية | The chat line above every card, and the card's title with the type word. | Read the card. | |
| `Type_transfer` / `Type_bill` / `Type_purchase` | تحويل / فاتورة / شراء | Next to the title. | | |
| `Card_To` | إلى | The recipient line: alias or saved name, or the typed number in full. | | |
| `Card_Registered` | الاسم المسجل | The registered name under the recipient (D16). | | |
| `Card_Amount` / `Card_Fee` / `Card_Total` / `Card_BalanceAfter` | المبلغ / العمولة / المجموع / الرصيد بعد الدفع | The figures. | | |
| `Card_AmountWords` | {0} دينار | The amount in words under the figure (`{0}` from `Words_*`). | | |
| `Currency_iqd` | دينار | After every amount in chat lines. | | |
| `Card_FirstTime` | أول مرة تحوّل لهذا الرقم | A typed number the user has never sent to (D16). | Check the registered name. | |
| `Card_Warning_repeat` | حوّلت نفس المبلغ لنفس الشخص قبل شوية | A transfer of the same amount to the same account within `RepeatWarningWindowMinutes` (FLT-05), and every Send again card. | Confirm only if it is meant. | |
| `Card_Timer` | ينلغي بعد {0} ثانية إذا ما ضغطت تأكيد | The countdown; `{0}` counts down from `CardTimerSeconds`. | Tap Confirm before it ends. | |
| `Card_Pin` | الرقم السري | The PIN field, shown when the wallet asks for a PIN (amount above the threshold). | Enter the PIN, tap Confirm. | |
| `Card_Confirm` | تأكيد | The one button that moves money. | | |
| `Card_Cancel` | إلغاء | Cancels the card; the chat shows `Refusal_card_cancelled`. | | F3 with `Option_CancelQuestion`. |
| `Card_Expired` | خلص الوقت. ما اتحوّل شي | The card's state line and the chat line when the timer ends. | Try again. | |
| `Card_Cancelled` | العملية ملغية | The card's state line after Cancel, a typed no, or a new request. | | F3: the chat says `Refusal_card_cancelled` for the same event; two texts for one thing, one on the card and one in the chat. |
| `Card_TryAgain` | جرب مرة ثانية | On the most recent card after expiry, cancel or a definite failure (D19); rebuilds the card. | Tap, then Confirm. | |
| `Card_SendAgain` | دز مرة ثانية | On the most recent completed card (D19); the new card carries the repeat warning. | Tap, then Confirm. | |

## 5. Outcomes after the tap

| Key | Arabic | When shown | Next | Flag |
|---|---|---|---|---|
| `Outcome_done` | تم. رقم العملية {0}. رصيدك هسه {1} | The wallet confirmed the payment; `{0}` the reference, `{1}` the balance after. A purchase adds `Purchase_code`. A transfer that needed a question may add the alias offer. | Nothing. | |
| `Outcome_not_done` | ما مشت: {0} | The payment definitely did not happen: the recheck failed at the tap (a cap, the balance), the wallet refused (`insufficient_funds`, `nothing_due`, `account_closed`, `pin_locked`, or any other code), the kill switch stopped it, or the settle window ended on a PIN code. `{0}` is that reason's text, or `Refusal_nothing_taken_short` for the generic reasons. | Depends on the reason. | F2: with the generic reason the line reads "Not done: nothing was taken from your balance" for the kill switch, a wallet error (`injected_error`), an invalid amount and a settle that ran out of time alike. |
| `Outcome_checking` | دا نتأكد من المحفظة، لحظة. لا تعيد الطلب | The outcome is unknown (timeout after execute, a 500 without a code): never shown as failed (rule 6). The card settles itself every `CheckingRetrySeconds`. | Wait; do not repeat the request. | |
| `Outcome_check_again` | تأكد مرة ثانية | The button under a Checking card and chat line. | Tap to settle now. | |
| `Outcome_pin_required` | دخّل الرقم السري وبعدين اضغط تأكيد | The wallet asks for a PIN (amount above the threshold); the PIN field shows. Also the reply to four to six digits typed into the chat while the field shows (known gaps row 34): no model call, the card stays, and the user's own bubble shows one `•` per digit, never the digits (Khalil, 19 Oct 2026). | Enter the PIN in the field, tap Confirm. | |
| `Outcome_pin_wrong` | الرقم السري غلط. المحاولات الباقية: {0} | A wrong PIN; `{0}` attempts left. | Try again. | |
| `Outcome_pin_malformed` | الرقم السري من ٤ لـ ٦ أرقام | The PIN typed is not 4 to 6 digits; no attempt is counted. | Retype it. | |
| `Outcome_already_handled` | هاي العملية تعالجت من قبل | (a) A second tap on a card already handled (double tap). (b) The record was not found. | Nothing. | F3: a record that does not exist is told "already handled". |
| `Refusal_card_cancelled` | انلغت العملية | The chat line for a cancelled card (section 1). | | |
| `Card_Expired` | خلص الوقت. ما اتحوّل شي | The chat line when the card expires (section 4). | | |

## 6. Balance and consent

| Key | Arabic | When shown | Next | Flag |
|---|---|---|---|---|
| `Reply_balance` | رصيدك هو {0} | The balance question (D22): the user's own balance, read by code. | Nothing. | F3 with the unused `Refusal_balance`. |
| `Reply_consent` | قبل ما نبدي، شي مهم: ما أحرّك أي دينار من محفظتك إلا إذا إنت تضغط تأكيد بنفسك، وبكل مرة. موافق؟ | The consent screen: when the phone opens for a user without consent, and in place of any request from such a user. | Tap `Reply_consent_button`. | |
| `Reply_consent_button` | موافق | The button under the consent screen; a failed consent call shows `Refusal_not_done_nothing_taken`. | | |
| `Reply_consent_decline_button` | لا اوافق | The No button beside `Reply_consent_button` (Khalil, 20 Oct 2026); the tap shows as the user's line, then `Refusal_consent_required`. Both buttons stay, and the message box is disabled, until the user agrees. | Tap `Reply_consent_button` to go on. | |

## 7. Aliases and the names screen

| Key | Arabic | When shown | Next | Flag |
|---|---|---|---|---|
| `Offer_save_alias` | تحب أحفظ "{0}" لـ {1} حتى المرة الجاية ما أسألك؟ | After a Done transfer whose recipient needed a question and was not a bare shared name (rule 18); `{0}` the typed words, `{1}` the registered name. | Tap the button, or ignore. | |
| `Offer_save_alias_button` | احفظ | The button under the offer. | | F3: the same word as `Directory_Save`. |
| `Reply_alias_saved` | انحفظ "{0}". المرة الجاية اكتبه وبس | The alias was saved (offer or names screen). | | |
| `Reply_alias_deleted` | انحذف الاسم | The names screen's result line after Delete. | | |
| `Refusal_alias_exists` | هذا الاسم موجود عندك من قبل | The text is already one of the user's aliases or saved names. | Another name. | |
| `Refusal_alias_invalid` | الاسم مو مناسب. اكتب اسم قصير بدون أرقام | Blank, longer than `MaxAliasLength`, or a number. | Another name. | |
| `Refusal_alias_shared` | هذا الاسم يشبه أكثر من شخص عندك. اكتب اسم مختلف | A one-word name the resolver would ask about on several accounts (review R4). | Another name. | |
| (directory refusals from section 1) | | The names screen also shows `Refusal_invalid_number`, `Refusal_number_has_no_wallet`, `Refusal_recipient_not_found`, `Refusal_account_closed` and `Refusal_not_supported` (own number) as its result line, and `Refusal_not_done_nothing_taken` when the list cannot load. | | F2: see those rows. |
| `Directory_Open` | الأسماء المحفوظة | The phone header's button. | Tap to open. | F3: the same text as `Directory_Title`. |
| `Directory_Title` | الأسماء المحفوظة | The screen's title. | | |
| `Directory_Back` | رجوع | Back to the chat; also the back button of My orders. | | |
| `Directory_Empty` | ماكو أسماء محفوظة بعد | No aliases yet. | | |
| `Directory_Text` | الاسم | The text input's placeholder. | | |
| `Directory_Target` | الشخص | The contact select's label. | | |
| `Directory_TargetNone` | اختار احد | The select's empty choice (use the number instead). | | |
| `Directory_Phone` | رقم الموبايل | The number input's label. | | |
| `Directory_Save` | احفظ | The save button. | | |
| `Directory_Delete` | احذف | The delete button on each row. | | |

## 8. Purchases and My orders

| Key | Arabic | When shown | Next | Flag |
|---|---|---|---|---|
| `Purchase_code` | الكود مالتك: {0} | After `Outcome_done` for a purchase; `{0}` the full code, with the copy button. | Copy or write the code. | |
| `Purchase_code_copy` | انسخ الكود | The copy button. | | |
| `Purchase_code_copied` | تم النسخ | The same button for two seconds after a copy. | | |
| `Purchase_code_copy_failed` | ما كدرت أنسخ. اكتبه بيدك | The same button for two seconds after a failed copy. | Write it down. | |
| `Product_type_credit` / `Product_type_internet` | رصيد / إنترنيت | The type word on a product option, the card and an order row. | | |
| `Orders_Open` | مشترياتي | The phone header's button. | Tap to open. | F3: the same text as `Orders_Title`. |
| `Orders_Title` | مشترياتي | The screen's title; rows show date, operator, type, price and the code with a copy button. | | |
| `Orders_Empty` | ماكو مشتريات بعد | No orders yet; a list that cannot load shows `Refusal_not_done_nothing_taken` here instead. | | F2: see `Refusal_not_done_nothing_taken` (g). |

## 9. The phone frame and the typed words

| Key | Arabic | When shown | Next | Flag |
|---|---|---|---|---|
| `Phone_Placeholder` | اكتب الطلب مالتك | The typing box when empty. | Type. | |
| `Phone_Send` | إرسال | The send button. | | |
| `Word_yes` | اي، ايه، اي نعم، يلة، يله، نعم، تمام، اوكي، اوك، توكل، حوّل، ماشي، زين، خلص، ok، yes، ee، اكيد، تأكيد، موافق | Not shown: the words that count as a yes while a card waits (D6), compared after normalising. A yes never confirms. | | |
| `Word_no` | لا، لأ، لا لا، الغي، الغيها، كنسل، بطل، خلي، no، cancel، الغاء العملية، إيقاف، ابطال، كلا، هء، غير موافق، غيرموافق، ما اقبل، ما اريد | Not shown: the words that cancel a waiting card. | | |

## 10. Keys never shown today

`Refusal_biller_unavailable`, `Refusal_balance`, `Refusal_cannot_change_card`, `Refusal_code_entered_by_user`, `Refusal_caller_cancelled`, `Unsupported_other_balance`, `Unsupported_other_currency`, `Unsupported_part_of_bill`. None is harmful; each is either a duplicate of the key that is shown, a reason the code never produces, or a reserved code.

## Summary of the flags

**F1, no next step or no statement of what happened**

1. `Refusal_not_understood` while a card waits: the card is still there and will expire on its own.
2. `Refusal_recipient_not_found`: the number can be typed instead.
3. `Refusal_number_has_no_wallet`, `Refusal_bill_not_found`, `Refusal_product_not_found`, `Refusal_pin_locked`, `Unsupported_choose_account`, `Unsupported_credit_to_own_number`: no next step.
4. `Refusal_insufficient_balance` and the other amount refusals: a typed amount merges for one message, which the text does not say.
5. `Refusal_bills_paid_in_full`: asking again without an amount gives the card.
6. `Refusal_account_choice_unavailable`: the card that follows pays from the usual account.
7. `Refusal_not_done_nothing_taken`: no next step in any of its eight situations.
8. (Closed by milestone 9c: a message with several requests now runs every part up to the cap and names the rest.)

**F2, a generic string over a specific situation**

9. `Refusal_not_done_nothing_taken` covers the kill switch, a wallet that does not answer, and a list that failed to load. The last shows "not done, nothing was taken" in the names and orders screens.
10. `Refusal_not_supported` covers a transfer to the user's own number.
11. `Refusal_invalid_number` covers a typed account number (D21).
12. `Refusal_not_understood` covers an invalid model reply and an amount that is not whole dinars.
13. `Outcome_not_done` with the generic reason covers the kill switch at the tap, a wallet error, an invalid amount and a settle that ran out of time.
14. `Unsupported_same_as_last_time` could point at Send again.

**F3, one text for two situations, or two keys for one text**

15. `Refusal_account_closed`: the user's account, the recipient's account, or an alias target.
16. `Refusal_card_cancelled`: cancelled by a no word, or cancelled because a new request replaced it.
17. `Question_which_contact`: "type a name" and "tap one of these".
18. `Outcome_already_handled`: a double tap, a record not found, and (milestone 9c) a tap on a card that has given way to the next one.
19. `Refusal_recipient_not_found` after `Option_NoneOfThese`: the user typed no name.
20. Same text, two keys: `Reply_balance` = `Refusal_balance`; `Refusal_only_own_balance` = `Unsupported_other_balance`; `Refusal_dinars_only` = `Unsupported_other_currency`; `Refusal_account_choice_unavailable` = `Unsupported_choose_account`; `Unsupported_refers_back` = `Unsupported_same_as_last_time`; `Card_Cancel` = `Option_CancelQuestion`; `Offer_save_alias_button` = `Directory_Save`; `Directory_Open` = `Directory_Title`; `Orders_Open` = `Orders_Title`.
21. Two texts for one situation: `Refusal_cannot_change_card` (never shown) and `Unsupported_change_waiting_card` (shown) differ; `Card_Cancelled` on the card and `Refusal_card_cancelled` in the chat.

Nothing here is a defect in behaviour: every path ends safely. The rows are for Khalil to decide which situations deserve their own line, and which duplicates to keep.
