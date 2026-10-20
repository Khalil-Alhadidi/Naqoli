extern alias AgentWeb;

using System.Net.Http.Json;
using Agent.Core.Aliases;
using Agent.Core.Confirm;
using Agent.Core.Audit;
using Agent.Core.Conversation;
using Agent.Core.Data;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Conversation;
using Agent.Core.Tests.Wallet;
using Agent.Core.Text;
using AgentWeb::Agent.Web.Resources;
using AgentWeb::Agent.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Wallet.MockApi.Contracts;
using ContactsOk = Agent.Core.Wallet.WalletResult<Agent.Core.Wallet.WalletContacts>.Ok;

namespace Agent.Core.Tests.Web;

/// <summary>
/// The phone's adapter without a browser: <c>AddAgentWebServices</c> over the agent host's configuration, clock and
/// in-process wallet. One DI scope stands for one circuit. The conversation's own behaviour is tested in
/// <c>ConversationTests</c>; these cases cover what the adapter adds: localised text, the resource file's D6 words,
/// message ids, <c>Changed</c>, and the circuit's trace.
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class ChatSessionTests(AgentHost host) : IAsyncDisposable
{
    /// <summary>The transfer sentence of these tests' scripted parser (English; the Arabic dev-set cases are the oracle's).</summary>
    private const string HammoudiSentence = "send fifty thousand to my brother Hammoudi";

    private readonly List<IAsyncDisposable> disposables = [];

    private sealed record Circuit(AsyncServiceScope Scope, ServiceProvider Provider)
    {
        public ChatSession Session => Scope.ServiceProvider.GetRequiredService<ChatSession>();

        public TraceLog Trace => Scope.ServiceProvider.GetRequiredService<TraceLog>();

        public IStringLocalizer<PhoneStrings> Text => Scope.ServiceProvider.GetRequiredService<IStringLocalizer<PhoneStrings>>();
    }

    /// <summary>
    /// A circuit for <paramref name="userId"/>, with a scripted parser of the Hammoudi sentence plus <paramref name="extra"/>
    /// ones (built from the seed through <see cref="SeedFacts"/>, never Arabic literals).
    /// </summary>
    private Task<Circuit> StartAsync(int userId = SeedFacts.DemoUserId, params ScriptedSentence[] extra)
    {
        var sentences = new List<ScriptedSentence>
        {
            new(HammoudiSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand)),
        };
        sentences.AddRange(extra);
        return StartAsync(new ScriptedParser(sentences), userId);
    }

    /// <summary>A circuit for <paramref name="userId"/> over <paramref name="parser"/>, with <paramref name="configure"/> applied to the agent's options.</summary>
    private async Task<Circuit> StartAsync(IParser parser, int userId = SeedFacts.DemoUserId, Action<AgentOptions>? configure = null)
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();

        var provider = host.BuildProvider((services, configuration) =>
        {
            services.AddAgentWebServices(configuration);
            services.Configure<AgentOptions>(o => o.DemoUserId = userId);
            if (configure is not null)
                services.PostConfigure(configure);
            services.AddSingleton<IParser>(parser);

            // As the console does: the parser in use is wrapped in the fault decorator (review M6 of milestone 8).
            services.AddFaultingParser();
        });
        var scope = provider.CreateAsyncScope();
        disposables.Add(provider);
        disposables.Add(scope);
        return new Circuit(scope, provider);
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = disposables.Count - 1; i >= 0; i--)
            await disposables[i].DisposeAsync();
    }

    private static ChatMessage LastAgentMessage(ChatSession session) => session.Messages.Last(m => m.Role == ChatRole.Agent);

    /// <summary>AMB-19's first sentence and its parse from the dev set (the recipient as the user typed it).</summary>
    private static ScriptedSentence Amb19Sentence()
        => new(DevSetText.Say("AMB-19"), ParsedRequest.Transfer(DevSetText.SidecarRecipient("AMB-19"), 10, AmountUnit.Thousand));

    /// <summary>AMB-19's first three steps on the phone: the sentence, the tap on S08, the confirm, ending Done with the offer.</summary>
    private static async Task PayS08Async(ChatSession session)
    {
        await session.SendAsync(DevSetText.Say("AMB-19"));
        await session.TapOptionAsync("S08");
        Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmShownAsync(null));
    }

    [Fact]
    public async Task TheAliasOfferIsLocalisedWithTheTypedTextAndCarriesTheSaveAction()
    {
        var circuit = await StartAsync(SeedFacts.DemoUserId, Amb19Sentence());
        var session = circuit.Session;

        await PayS08Async(session);

        var offer = Assert.IsType<AliasOffer>(session.AliasOffer);
        Assert.Equal(DevSetText.AliasOffer("AMB-19", 2), offer.Text);
        var registered = SeedFacts.Account(SeedFacts.Contact("S08").Number).RegisteredName;
        Assert.NotNull(registered);
        Assert.Equal(registered, offer.RecipientName);
        var message = LastAgentMessage(session);
        Assert.Equal(circuit.Text["Offer_save_alias", offer.Text, registered].Value, message.Text);
        Assert.Equal(ChatAction.SaveAlias, message.Action);
        Assert.Equal(message.Id, session.AliasOfferMessageId);
    }

    [Fact]
    public async Task AliasSavedIsLocalisedWithTheText()
    {
        var circuit = await StartAsync(SeedFacts.DemoUserId, Amb19Sentence());
        var session = circuit.Session;
        await PayS08Async(session);
        var text = session.AliasOffer!.Text;
        var version = session.WalletVersion;

        Assert.IsType<AliasResult.Saved>(await session.SaveAliasAsync());

        Assert.Equal(circuit.Text["Reply_alias_saved", text].Value, LastAgentMessage(session).Text);
        Assert.Null(session.AliasOffer);
        Assert.Null(session.AliasOfferMessageId);
        Assert.Equal(version + 1, session.WalletVersion);
    }

    [Fact]
    public async Task AliasRefusalsUseTheirKeys()
    {
        var circuit = await StartAsync(SeedFacts.DemoUserId, Amb19Sentence());
        var session = circuit.Session;
        await PayS08Async(session);
        var offer = session.AliasOffer!;

        // The same text saved first through the directory: the offer's Save is refused alias_exists, under its own key.
        Assert.IsType<AliasResult.Saved>(await session.AddAliasAsync(offer.Text, offer.AccountId));
        var refused = Assert.IsType<AliasResult.Refused>(await session.SaveAliasAsync());

        Assert.Equal(ReasonCodes.AliasExists, refused.Code);
        Assert.Equal(circuit.Text["Refusal_" + ReasonCodes.AliasExists].Value, LastAgentMessage(session).Text);

        // Both refusal codes have their own key, not the fallback.
        foreach (var code in new[] { ReasonCodes.AliasExists, ReasonCodes.AliasInvalid })
        {
            Assert.False(circuit.Text["Refusal_" + code].ResourceNotFound, code);
            Assert.NotEqual(circuit.Text["Refusal_" + ReasonCodes.NotDoneNothingTaken].Value, circuit.Text["Refusal_" + code].Value);
        }

        // The directory's own refusal adds no chat message.
        var messages = session.Messages.Count;
        var invalid = Assert.IsType<AliasResult.Refused>(await session.AddAliasAsync("   ", offer.AccountId));
        Assert.Equal(ReasonCodes.AliasInvalid, invalid.Code);
        Assert.Equal(messages, session.Messages.Count);
    }

    [Fact]
    public async Task TheConversationsCodesBecomeLocalisedMessages()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;

        await session.SendAsync(HammoudiSentence);

        // The user's words as typed, then the card's message with the card under it.
        Assert.Equal(new ChatMessage(1, ChatRole.User, HammoudiSentence, ChatMessageKind.Text), session.Messages[0]);
        var cardMessage = LastAgentMessage(session);
        Assert.Equal(new ChatMessage(2, ChatRole.Agent, circuit.Text["Card_Title"].Value, ChatMessageKind.Card), cardMessage);
        Assert.Equal(cardMessage.Id, session.CardMessageId);
        Assert.Equal(CardStatus.Live, session.CardState);

        // The yes word of the resource file reaches the conversation (D6): it points to the button.
        // The resource is a list; any one of its words is a yes.
        await session.SendAsync(PhoneWords.Split(circuit.Text["Word_yes"].Value)[0]);
        Assert.Equal(circuit.Text["Refusal_" + ReasonCodes.PointsToConfirm].Value, LastAgentMessage(session).Text);
        Assert.Equal(CardStatus.Live, session.CardState);

        var done = Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmShownAsync(null));

        var message = LastAgentMessage(session);
        Assert.Equal("done", message.Outcome);
        Assert.Equal(
            circuit.Text["Outcome_done", PhoneFormat.Digits(done.Reference), PhoneFormat.Amount(699_000, circuit.Text)].Value,
            message.Text);
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Equal(1, session.WalletVersion);
        Assert.Equal(session.Messages.Select(m => m.Id), Enumerable.Range(1, session.Messages.Count));
    }

    [Fact]
    public async Task NotDoneNothingTakenNestsTheShortText()
    {
        // FLT-03: the wallet answers 500 injected_error before writing anything. The outcome already says it did not go
        // through, so the nested text is the short "nothing taken" one, not the standalone refusal (Khalil, 18 Oct 2026).
        var circuit = await StartAsync();
        var session = circuit.Session;
        await session.SendAsync(HammoudiSentence);
        using (var fault = await host.Wallet.Client.PostAsJsonAsync("/admin/faults", new FaultRequest("error", null, Once: true), TestContext.Current.CancellationToken))
            Assert.True(fault.IsSuccessStatusCode, fault.StatusCode.ToString());

        var notDone = Assert.IsType<ConfirmOutcome.NotDone>(await session.ConfirmShownAsync(null));

        Assert.Equal(ReasonCodes.NotDoneNothingTaken, notDone.Reason);
        var message = LastAgentMessage(session);
        Assert.Equal("not_done", message.Outcome);
        Assert.Equal(circuit.Text["Outcome_not_done", circuit.Text["Refusal_nothing_taken_short"].Value].Value, message.Text);
        Assert.NotEqual(circuit.Text["Outcome_not_done", circuit.Text["Refusal_" + ReasonCodes.NotDoneNothingTaken].Value].Value, message.Text);
    }

    [Fact]
    public async Task NotDoneWithAnotherReasonNestsItsRefusalAndFigure()
    {
        // Money leaves the wallet from outside while the card waits: the recheck ends it insufficient_balance with the
        // shortfall, nested as before.
        var circuit = await StartAsync();
        var session = circuit.Session;
        await session.SendAsync(HammoudiSentence);
        using (var debit = await host.Wallet.Client.PostAsJsonAsync("/admin/debit", new DebitRequest(SeedFacts.DemoUserId, SeedFacts.DemoBalance - 20_000), TestContext.Current.CancellationToken))
            Assert.True(debit.IsSuccessStatusCode, debit.StatusCode.ToString());

        var notDone = Assert.IsType<ConfirmOutcome.NotDone>(await session.ConfirmShownAsync(null));

        Assert.Equal(ReasonCodes.InsufficientBalance, notDone.Reason);
        var shortfall = Assert.IsType<long>(notDone.AmountIqd);
        var refusal = circuit.Text["Refusal_" + ReasonCodes.InsufficientBalance, PhoneFormat.Amount(shortfall, circuit.Text)].Value;
        var message = LastAgentMessage(session);
        Assert.Equal("not_done", message.Outcome);
        Assert.Equal(circuit.Text["Outcome_not_done", refusal].Value, message.Text);
    }

    [Fact]
    public async Task ChangedIsRaisedAndTheAgentWritesToTheCircuitsTrace()
    {
        var circuit = await StartAsync(
            SeedFacts.DemoUserId,
            new ScriptedSentence("send fifty thousand to a shared name", ParsedRequest.Transfer(SeedFacts.Contact("S08").SavedName, 50_000, AmountUnit.Dinars)));
        var session = circuit.Session;
        var raised = 0;
        session.Changed += () => Interlocked.Increment(ref raised);

        await session.SendAsync("send fifty thousand to a shared name");

        Assert.True(raised > 0);
        Assert.Equal(circuit.Text["Question_" + QuestionKinds.WhichContact].Value, LastAgentMessage(session).Text);
        Assert.Equal(LastAgentMessage(session).Id, session.QuestionMessageId);

        // A tapped option shows the option's own text.
        await session.TapOptionAsync("S08");
        Assert.Contains(session.Messages, m => m.Role == ChatRole.User && m.Text == SeedFacts.Contact("S08").SavedName);

        // The orchestrator wrote to this circuit's trace through each action's own scope (TraceLog.Current).
        Assert.Contains(circuit.Trace.Entries, e => e.Step == "card_created");
        Assert.Contains(circuit.Trace.Entries, e => e.Step == "message_received");
    }

    [Fact]
    public async Task TryAgainAndSendAgainAreOfferedByStateAndOpenANewCard()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;

        Assert.Null(session.Reissue);
        await session.SendAsync(HammoudiSentence);
        var first = session.Card!;
        Assert.Null(session.Reissue);

        await session.CancelShownAsync();
        Assert.Equal(ReissueKind.TryAgain, session.Reissue);

        await session.ReissueAsync();

        Assert.NotEqual(first.PendingId, session.Card!.PendingId);
        Assert.Equal(CardStatus.Live, session.CardState);
        Assert.Null(session.Reissue);
        Assert.Equal(circuit.Text["Card_Title"].Value, LastAgentMessage(session).Text);
        Assert.Equal(LastAgentMessage(session).Id, session.CardMessageId);
        Assert.Contains(circuit.Trace.Entries, e => e.Step == "reissue");

        Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmShownAsync(null));
        Assert.Equal(ReissueKind.SendAgain, session.Reissue);
    }

    [Fact]
    public async Task TheOpenedSessionOfAUserWhoHasNotConsentedShowsTheConsentMessageWithItsButton()
    {
        // U5 has not given consent in the seed.
        var circuit = await StartAsync(userId: 5);
        var session = circuit.Session;

        await session.OpenAsync();

        var only = Assert.Single(session.Messages);
        Assert.Equal(new ChatMessage(1, ChatRole.Agent, circuit.Text["Reply_consent"].Value, ChatMessageKind.Text, null, ChatAction.Consent), only);
        Assert.Equal(only.Id, session.ActionMessageId);
        Assert.Contains(circuit.Trace.Entries, e => e.Step == "consent_checked");
    }

    [Fact]
    public async Task RestartEmptiesTheMessagesAndANewMessageGetsItsCard()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;
        await session.SendAsync(HammoudiSentence);
        var first = session.Card!;
        var raised = 0;
        session.Changed += () => Interlocked.Increment(ref raised);

        // As the reset panel does: the open records closed, then the phone started over.
        await using (var scope = circuit.Provider.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<PendingStore>().ExpireAllOpenAsync("wallet_reset", TestContext.Current.CancellationToken));
        await session.RestartAsync();

        Assert.Empty(session.Messages);
        Assert.Null(session.Card);
        Assert.Null(session.CardMessageId);
        Assert.True(raised > 0);

        await session.SendAsync(HammoudiSentence);

        Assert.NotEqual(first.PendingId, session.Card!.PendingId);
        Assert.Equal(CardStatus.Live, session.CardState);
        Assert.Equal(new ChatMessage(1, ChatRole.User, HammoudiSentence, ChatMessageKind.Text), session.Messages[0]);
        Assert.Equal(LastAgentMessage(session).Id, session.CardMessageId);
        Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmShownAsync(null));
    }

    /// <summary>The changes raised on the circuit's <see cref="WalletChanges"/> from now on (the handler may run on any thread).</summary>
    private static List<WalletChange> Record(Circuit circuit)
    {
        var raised = new List<WalletChange>();
        circuit.Provider.GetRequiredService<WalletChanges>().Changed += change =>
        {
            lock (raised)
                raised.Add(change);
        };
        return raised;
    }

    [Fact]
    public async Task AConfirmedTransferRaisesOnePaymentChangeForTheDemoUser()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;
        var raised = Record(circuit);

        await session.SendAsync(HammoudiSentence);
        Assert.Empty(raised);
        Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmShownAsync(null));

        Assert.Equal(new WalletChange(SeedFacts.DemoUserId, WalletChangeKind.Payment), Assert.Single(raised));
    }

    [Fact]
    public async Task AnAliasSavedFromTheOfferAndOneDeletedThroughTheDirectoryRaiseOneAliasChangeEach()
    {
        var circuit = await StartAsync(SeedFacts.DemoUserId, Amb19Sentence());
        var session = circuit.Session;
        await PayS08Async(session);
        var text = session.AliasOffer!.Text;
        var raised = Record(circuit);

        Assert.IsType<AliasResult.Saved>(await session.SaveAliasAsync());
        Assert.Equal(new WalletChange(SeedFacts.DemoUserId, WalletChangeKind.Alias), Assert.Single(raised));

        var listed = Assert.IsType<ContactsOk>(await session.ListAliasesAsync());
        var saved = Assert.Single(listed.Value.Aliases, a => a.Text == text);
        Assert.Single(raised);

        Assert.IsType<AliasResult.Deleted>(await session.DeleteAliasAsync(saved.AliasId));

        Assert.Equal(
            [new WalletChange(SeedFacts.DemoUserId, WalletChangeKind.Alias), new WalletChange(SeedFacts.DemoUserId, WalletChangeKind.Alias)],
            raised);
    }

    [Fact]
    public async Task RestartMakesTheWalletVersionLargerThanBefore()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;
        var start = session.WalletVersion;

        await session.RestartAsync();
        var afterEmptyRestart = session.WalletVersion;
        Assert.True(afterEmptyRestart > start);

        await session.SendAsync(HammoudiSentence);
        Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmShownAsync(null));
        var afterPayment = session.WalletVersion;
        Assert.True(afterPayment > afterEmptyRestart);

        // The new conversation starts again at 0; the session's version does not go back to any number it showed.
        await session.RestartAsync();
        Assert.True(session.WalletVersion > afterPayment);
    }

    private const string SharedNameSentence = "send fifty thousand to a shared name";

    private const string NoAmountSentence = "send to hammoudi";

    private static ScriptedSentence SharedName()
        => new(SharedNameSentence, ParsedRequest.Transfer(SeedFacts.Contact("S08").SavedName, 50_000, AmountUnit.Dinars));

    private static ScriptedSentence NoAmount()
        => new(NoAmountSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, null, AmountUnit.None));

    [Fact]
    public async Task ANotUnderstoodAnswerRepeatsTheQuestionsTextInTheRepeatTemplate()
    {
        var circuit = await StartAsync(SeedFacts.DemoUserId, SharedName());
        var session = circuit.Session;
        await session.SendAsync(SharedNameSentence);
        var questionId = session.QuestionMessageId;

        await session.SendAsync("something nobody can read");

        var expected = circuit.Text["Refusal_not_understood_repeat", circuit.Text["Question_" + QuestionKinds.WhichContact].Value].Value;
        Assert.Equal(expected, LastAgentMessage(session).Text);
        Assert.NotNull(session.Question);
        Assert.Equal(questionId, session.QuestionMessageId);
    }

    [Fact]
    public async Task AMessageLongerThanMaxMessageCharsShowsRefusalMessageTooLong()
    {
        // Khalil, 19 Oct 2026: the over-long message has its own string, not Refusal_not_understood.
        var circuit = await StartAsync();
        var session = circuit.Session;
        var max = circuit.Scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AgentOptions>>().CurrentValue.MaxMessageChars;

        await session.SendAsync(new string('x', max + 1));

        Assert.Equal(circuit.Text["Refusal_" + ReasonCodes.MessageTooLong].Value, LastAgentMessage(session).Text);
        Assert.False(circuit.Text["Refusal_" + ReasonCodes.MessageTooLong].ResourceNotFound);
    }

    private const string PayTheTraderSentence = "pay the trader";

    /// <summary>U3's transfer to the trader, large enough for the PIN field.</summary>
    private static ScriptedSentence PayTheTrader()
        => new(PayTheTraderSentence, ParsedRequest.Transfer(SeedFacts.Contact("T01").SavedName, 1_100_000, AmountUnit.Dinars));

    [Fact]
    public async Task APinTypedIntoTheChatWhileAPinCardIsLiveIsEchoedAsOneDotPerDigit()
    {
        // Khalil, 19 Oct 2026: the user's bubble shows dots, never the digits, in either digit form.
        var circuit = await StartAsync(SeedFacts.TraderUserId, PayTheTrader());
        var session = circuit.Session;
        var pin = SeedFacts.PinOf("U3");
        await session.SendAsync(PayTheTraderSentence);
        Assert.True(session.PinVisible);

        await session.SendAsync(ArabicDigits.ToArabicIndic(pin + "7"));
        Assert.Equal(new string('\u2022', 5), session.Messages.Last(m => m.Role == ChatRole.User).Text);

        await session.SendAsync(pin);
        Assert.Equal(new string('\u2022', pin.Length), session.Messages.Last(m => m.Role == ChatRole.User).Text);

        foreach (var message in session.Messages)
        {
            Assert.DoesNotContain(pin, message.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(ArabicDigits.ToArabicIndic(pin), message.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AFourDigitMessageWithNoPinCardLiveIsEchoedAsTyped()
    {
        var circuit = await StartAsync();
        var session = circuit.Session;

        await session.SendAsync("1234");

        Assert.Equal("1234", session.Messages.First(m => m.Role == ChatRole.User).Text);
    }

    [Fact]
    public async Task TheFinalOptionOfAContactQuestionIsNoneOfThese()
    {
        var circuit = await StartAsync(SeedFacts.DemoUserId, SharedName());
        var session = circuit.Session;
        await session.SendAsync(SharedNameSentence);

        await session.DeclineQuestionAsync();

        Assert.Equal(circuit.Text["Option_NoneOfThese"].Value, session.Messages.Last(m => m.Role == ChatRole.User).Text);
        Assert.Equal(circuit.Text["Refusal_" + ReasonCodes.RecipientNotFound].Value, LastAgentMessage(session).Text);
        Assert.Null(session.Question);
    }

    [Fact]
    public async Task TheFinalOptionOfATypedQuestionIsCancel()
    {
        var circuit = await StartAsync(SeedFacts.DemoUserId, NoAmount());
        var session = circuit.Session;
        await session.SendAsync(NoAmountSentence);
        Assert.Equal(QuestionKinds.WhichAmount, session.Question?.Kind);

        await session.DeclineQuestionAsync();

        Assert.Equal(circuit.Text["Option_CancelQuestion"].Value, session.Messages.Last(m => m.Role == ChatRole.User).Text);
        Assert.Equal(circuit.Text["Refusal_" + ReasonCodes.QuestionCancelled].Value, LastAgentMessage(session).Text);
        Assert.Null(session.Question);
    }
    private const string ZainCreditSentence = "zain credit five";

    private static ScriptedSentence ZainCredit() => new(ZainCreditSentence, ParsedRequest.Purchase("zain", "credit", 5_000));

    [Fact]
    public async Task TheDonePurchaseOutcomeCarriesTheCodeMessageWithTheCopyAction()
    {
        var circuit = await StartAsync(SeedFacts.DemoUserId, ZainCredit());
        var session = circuit.Session;

        await session.SendAsync(ZainCreditSentence);
        var bought = Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmShownAsync(null));
        Assert.NotNull(bought.PurchaseCode);

        var done = session.Messages.Where(m => m.Outcome == "done").ToList();
        Assert.Equal(2, done.Count);
        Assert.Equal(ChatAction.None, done[0].Action);
        Assert.Null(done[0].CopyText);
        Assert.Equal(circuit.Text["Purchase_code", PhoneFormat.Digits(bought.PurchaseCode)].Value, done[1].Text);
        Assert.Equal(ChatAction.CopyCode, done[1].Action);
        Assert.Equal(bought.PurchaseCode, done[1].CopyText);
    }

    [Fact]
    public async Task ProductOptionsAreLabelledWithTheTypeWord()
    {
        // AMB-23's parse: three credit cards across the operators; each label names the type (ruling 3 of milestone 9b).
        var circuit = await StartAsync(SeedFacts.DemoUserId, new ScriptedSentence("credit five", ParsedRequest.Purchase(null, "credit", 5_000)));
        var session = circuit.Session;
        var typeWord = circuit.Text["Product_type_credit"].Value;

        await session.SendAsync("credit five");
        var question = Assert.IsType<AgentReply.Question>(session.Question);
        Assert.Equal(QuestionKinds.WhichProduct, question.Kind);
        Assert.All(question.Options, o => Assert.Contains(typeWord, PhoneFormat.OptionLabel(o, circuit.Text), StringComparison.Ordinal));

        // The tap's echo is the same label.
        var tapped = question.Options[^1];
        var label = PhoneFormat.OptionLabel(tapped, circuit.Text);
        await session.TapOptionAsync(tapped.Value);
        Assert.Equal(label, session.Messages.Last(m => m.Role == ChatRole.User).Text);
    }

    [Fact]
    public async Task AnUnsupportedRequestWithNoReasonFallsToNotSupported()
    {
        // Rule 11 (ADD-24's parse): "return the card I bought" has no request type and no reason in the structure, so
        // the model marks it unsupported with no reason; the agent replies not_supported and logs it with no detail.
        const string sentence = "return the card I bought";
        var circuit = await StartAsync(SeedFacts.DemoUserId, new ScriptedSentence(sentence, new ParsedRequest { Intent = RequestIntent.Unsupported }));
        var session = circuit.Session;

        await session.SendAsync(sentence);

        Assert.Equal(circuit.Text["Refusal_" + ReasonCodes.NotSupported].Value, LastAgentMessage(session).Text);
        Assert.True(circuit.Text["Unsupported_" + ReasonCodes.NotSupported].ResourceNotFound);
        Assert.Null(session.CardMessageId);

        var db = circuit.Scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var refused = Assert.Single(await db.AuditEntries.AsNoTracking().Where(a => a.Event == AuditEvents.Refused).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ReasonCodes.NotSupported, refused.Reason);
        var row = Assert.Single(await db.LearningEntries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal((SeedFacts.DemoUserId, ReasonCodes.NotSupported, (string?)null), (row.UserId, row.Reason, row.Detail));
    }

    [Fact]
    public async Task ListOrdersReturnsTheConversationsOrders()
    {
        var circuit = await StartAsync(SeedFacts.DemoUserId, ZainCredit());
        var session = circuit.Session;
        await session.SendAsync(ZainCreditSentence);
        var bought = Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmShownAsync(null));

        var orders = Assert.IsType<global::Agent.Core.Wallet.WalletResult<IReadOnlyList<global::Agent.Core.Store.PurchaseOrder>>.Ok>(await session.ListOrdersAsync()).Value;

        Assert.Equal(bought.PurchaseCode, orders[0].Code);
        Assert.True(orders.Count > 1); // the seed's orders of U1 follow
    }

    // ---- compound requests (milestone 9c) ----

    private static ScriptedParser Oracle()
        => ScriptedParser.Load(Path.Combine(SeedHashTests.RepositoryRoot(), "data", ScriptedParser.DevSetFileName));

    [Fact]
    public async Task TheMaxThreeReplyIsTwoLinesAndNamesTheRestWithTheUsersWords()
    {
        // CMP-07: after the third Done, Khalil's line as it is, then the line that names the fourth part in the user's words.
        var circuit = await StartAsync(Oracle());
        var session = circuit.Session;

        await session.SendAsync(DevSetText.Say("CMP-07"));
        for (var card = 0; card < 3; card++)
            Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmShownAsync());

        var agent = session.Messages.Where(m => m.Role == ChatRole.Agent).ToList();
        Assert.Equal(circuit.Text["Refusal_" + ReasonCodes.MaxThreeRequests].Value, agent[^2].Text);
        Assert.Equal(circuit.Text["Refusal_not_prepared", DevSetText.NotPrepared("CMP-07")].Value, agent[^1].Text);
    }

    [Fact]
    public async Task ANotPreparedPartWithNoTextIsNamedByItsType()
    {
        // The parser's recipient for the second part is not in the typed text, so the type word names it (rules 3 and 4).
        const string sentence = "send ten thousand to my brother and the rest to the other one";
        var parser = new CapturingParser(new Dictionary<string, ParsedMessage>(StringComparer.Ordinal)
        {
            [sentence] = new([
                ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 10, AmountUnit.Thousand),
                ParsedRequest.Transfer("omar", 10, AmountUnit.Thousand),
                ParsedRequest.Balance(),
            ]),
        });
        var circuit = await StartAsync(parser, configure: o => o.MaxRequestsPerMessage = 1);
        var session = circuit.Session;

        await session.SendAsync(sentence);
        Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmShownAsync());

        // Two parts beyond the cap: a transfer with no text of the user's, and a balance question (Type_request).
        var names = circuit.Text["List_joiner", circuit.Text["Type_transfer"].Value, circuit.Text["Type_request"].Value].Value;
        Assert.Equal(circuit.Text["Refusal_not_prepared", names].Value, LastAgentMessage(session).Text);
        Assert.DoesNotContain("omar", LastAgentMessage(session).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AQueueDroppedReplyShowsKhalilsLine()
    {
        // Review R2 of milestone 9c (Khalil, 20 Oct 2026): the one Info code whose key starts Reply_.
        const string sentence = "send to my brother and twenty five thousand to my sister";
        const string answer = "the usual amount, thirty";
        var parser = new CapturingParser(new Dictionary<string, ParsedMessage>(StringComparer.Ordinal)
        {
            [sentence] = new([
                ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, null, AmountUnit.None),
                ParsedRequest.Transfer(SeedFacts.Contact("S19").SavedName, 25, AmountUnit.Thousand),
            ]),
            [answer] = new([ParsedRequest.Transfer(null, 30, AmountUnit.Thousand)]),
        });
        var circuit = await StartAsync(parser);
        var session = circuit.Session;

        await session.SendAsync(sentence);
        await session.SendAsync(answer);

        var line = circuit.Text["Reply_" + ReasonCodes.QueueDropped];
        Assert.False(line.ResourceNotFound);
        Assert.Contains(session.Messages, m => m.Role == ChatRole.Agent && m.Text == line.Value);
        Assert.NotNull(session.Card);
    }

    [Fact]
    public async Task ConfirmAndCancelPassTheCardId()
    {
        // Ruling 8: a tap for card 1 after card 2 is shown is already handled, and nothing moves.
        var circuit = await StartAsync(Oracle());
        var session = circuit.Session;

        await session.SendAsync(DevSetText.Say("CMP-01"));
        var first = session.Card!.PendingId;
        Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmAsync(first, null));
        var second = session.Card!.PendingId;
        Assert.NotEqual(first, second);

        Assert.Null(await session.ConfirmAsync(first, null));
        Assert.Equal(circuit.Text["Outcome_already_handled"].Value, LastAgentMessage(session).Text);
        await session.CancelAsync(first);
        Assert.Equal(circuit.Text["Outcome_already_handled"].Value, LastAgentMessage(session).Text);

        Assert.Equal((second, CardStatus.Live), (session.Card!.PendingId, session.CardState));
        Assert.Equal(724_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));

        await session.CancelAsync(second);
        Assert.Equal(CardStatus.Cancelled, session.CardState);
    }

    [Fact]
    public async Task ANotPreparedNameThatIsATypedNumberIsShownInArabicIndicDigits()
    {
        // Security review R3 of milestone 9c: the phone frame keeps Arabic-Indic digits, a typed number in a name too.
        var number = SeedFacts.Contact("S19").Number;
        var sentence = "send ten thousand to my brother and ten thousand to " + number;
        var parser = new CapturingParser(new Dictionary<string, ParsedMessage>(StringComparer.Ordinal)
        {
            [Masker.Mask(sentence).Text] = new([
                ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 10, AmountUnit.Thousand),
                ParsedRequest.Transfer(Masker.Mask(sentence).Map.Tokenize(number), 10, AmountUnit.Thousand),
            ]),
        });
        var circuit = await StartAsync(parser, configure: o => o.MaxRequestsPerMessage = 1);
        var session = circuit.Session;

        await session.SendAsync(sentence);
        Assert.IsType<ConfirmOutcome.Done>(await session.ConfirmShownAsync());

        Assert.Equal(circuit.Text["Refusal_not_prepared", PhoneFormat.Digits(number)].Value, LastAgentMessage(session).Text);
    }
}
