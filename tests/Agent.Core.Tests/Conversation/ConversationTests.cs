using System.Collections.Concurrent;
using Agent.Core.Confirm;
using Agent.Core.Conversation;
using Agent.Core.Parsing;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Agent.Core.Text;
using Agent.Core.Trace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wallet.MockApi.Data;
using AgentConversation = Agent.Core.Conversation.Conversation;

namespace Agent.Core.Tests.Conversation;

/// <summary>
/// The conversation the phone and the runner share, without a screen: <c>AddAgentCore</c> over the agent host's
/// configuration (D6 words <see cref="AgentHost.TestYesWord"/> and <see cref="AgentHost.TestNoWord"/>), clock and
/// in-process wallet, a scripted parser, and a recording trace. Assertions are on codes and figures only.
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class ConversationTests(AgentHost host) : IAsyncDisposable
{
    /// <summary>The English placeholder sentence of <c>scripted-sentences.json</c> (TODO-AR, Khalil's to replace).</summary>
    private const string HammoudiSentence = "send fifty thousand to my brother Hammoudi";

    private const string BalanceSentence = "balance";

    /// <summary>U5: has not given consent yet.</summary>
    private const int NoConsentUserId = 5;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly List<IAsyncDisposable> disposables = [];

    private sealed record Session(AgentConversation Conversation, ServiceProvider Provider, RecordingSink Trace);

    /// <summary>
    /// A conversation for <paramref name="userId"/> with the Hammoudi and balance sentences plus <paramref name="extra"/>
    /// ones (built from the seed through <see cref="SeedFacts"/>, never Arabic literals), over <paramref name="parser"/>
    /// when given.
    /// </summary>
    private async Task<Session> StartAsync(int userId = SeedFacts.DemoUserId, IParser? parser = null, Action<AgentOptions>? configure = null, params ScriptedSentence[] extra)
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();

        var trace = new RecordingSink();
        var provider = host.BuildProvider((services, configuration) =>
        {
            // Registered before AddAgentCore, so its TryAddScoped NullTraceSink does nothing.
            services.AddSingleton<ITraceSink>(trace);
            services.AddAgentCore(configuration);
            if (configure is not null)
                services.PostConfigure(configure);
        });
        disposables.Add(provider);

        var sentences = new List<ScriptedSentence>
        {
            new(HammoudiSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand)),
            new(BalanceSentence, ParsedRequest.Balance()),
        };
        sentences.AddRange(extra);

        var conversation = new AgentConversation(
            provider.GetRequiredService<IServiceScopeFactory>(),
            parser ?? new ScriptedParser(sentences),
            host.Clock,
            provider.GetRequiredService<IOptions<AgentOptions>>(),
            trace,
            userId);
        disposables.Add(new DisposeAction(conversation.Dispose));
        return new Session(conversation, provider, trace);
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = disposables.Count - 1; i >= 0; i--)
            await disposables[i].DisposeAsync();
    }

    private static async Task<PendingRecord> RecordAsync(Session session, Guid id, int userId = SeedFacts.DemoUserId)
    {
        await using var scope = session.Provider.CreateAsyncScope();
        var record = await scope.ServiceProvider.GetRequiredService<PendingStore>().FindAsync(id, userId, Token);
        Assert.NotNull(record);
        return record;
    }

    private async Task<int> WalletRowsAsync(string key)
    {
        await using var scope = host.Wallet.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Transactions.AsNoTracking().CountAsync(t => t.IdempotencyKey == key, Token);
    }

    private static ConversationMessage LastAgentMessage(AgentConversation conversation)
        => conversation.Messages.Last(m => m.Role == ConversationRole.Agent);

    private static ConversationMessage Outcome(string code) => new(ConversationRole.Agent, ConversationKind.Outcome, code);

    [Fact]
    public async Task TheHammoudiSentenceGivesTheCardAndConfirmMovesTheMoney()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;

        await conversation.SendAsync(HammoudiSentence);

        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal((50_000L, 1_000L, 51_000L, 699_000L), (card.AmountIqd, card.FeeIqd, card.TotalIqd, card.BalanceAfterIqd));
        Assert.Equal(SeedFacts.Contact("S01").Alias, card.DisplayName);
        Assert.False(card.FirstTime);
        Assert.Equal([card], conversation.CardsIssued);
        Assert.IsType<AgentReply.CardIssued>(conversation.LastReply);
        Assert.Equal(
            [
                new ConversationMessage(ConversationRole.User, ConversationKind.Typed),
                new ConversationMessage(ConversationRole.Agent, ConversationKind.Card),
            ],
            conversation.Messages);
        Assert.Equal(1, conversation.CardMessageIndex);

        var outcome = await conversation.ConfirmAsync(null);

        var done = Assert.IsType<ConfirmOutcome.Done>(outcome);
        Assert.Equal(CardStatus.Done, conversation.CardState);
        Assert.Equal(
            new ConversationMessage(ConversationRole.Agent, ConversationKind.Outcome, OutcomeCodes.Done, 699_000, done.Reference),
            LastAgentMessage(conversation));
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Equal(1, conversation.WalletVersion);
    }

    [Fact]
    public async Task TypedYesPointsToConfirmAndMovesNothing()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        await conversation.SendAsync(AgentHost.TestYesWord);

        Assert.Equal(
            new ConversationMessage(ConversationRole.Agent, ConversationKind.Info, ReasonCodes.PointsToConfirm),
            LastAgentMessage(conversation));
        Assert.Equal(CardStatus.Live, conversation.CardState);
        var record = await RecordAsync(session, card.PendingId);
        Assert.Equal(PendingState.Pending, record.State);
        Assert.Equal(0, await WalletRowsAsync(record.IdempotencyKey));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task TypedNoCancelsTheCard()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        // The word is compared after normalising, so extra spaces still count.
        await conversation.SendAsync("  " + AgentHost.TestNoWord + " ");

        Assert.Equal(CardStatus.Cancelled, conversation.CardState);
        Assert.Equal(Outcome(OutcomeCodes.Cancelled), LastAgentMessage(conversation));
        Assert.Equal(PendingState.Cancelled, (await RecordAsync(session, card.PendingId)).State);
    }

    /// <summary>Khalil's words (his workbook of 15 Oct 2026): a yes word in Iraqi Arabic, and his example of a sentence that contains one.</summary>
    private const string KhalilYesWord = "حوّل";

    /// <summary>Khalil's example: a transfer that starts with a yes word, so a substring match would mistake it for a yes.</summary>
    private const string KhalilTransferSentence = "حوّل ٥٠ لعلي";

    [Theory]
    [InlineData("yes!")]
    [InlineData("YES")]
    [InlineData(" Yes. ")]
    [InlineData("yes?")]
    [InlineData("حوّل؟")]
    [InlineData("حوّل.")]
    public async Task AYesWithTrailingPunctuationOrAnotherCaseIsStillAYes(string typed)
    {
        var session = await StartAsync(configure: o => o.YesWords = [AgentHost.TestYesWord, KhalilYesWord]);
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);

        await conversation.SendAsync(typed);

        Assert.Equal(
            new ConversationMessage(ConversationRole.Agent, ConversationKind.Info, ReasonCodes.PointsToConfirm),
            LastAgentMessage(conversation));
        Assert.Equal(CardStatus.Live, conversation.CardState);
    }

    /// <summary>D6 phrase rule: a message with one word that is not a yes (or no) word is a request.</summary>
    [Theory]
    [InlineData("yes please")]
    [InlineData("no, send it")]
    public async Task ASentenceThatContainsAWordIsNotAYesOrANo(string typed)
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        await conversation.SendAsync(typed);

        // Not a yes (no pointer to the button) and not a plain no: the card is cancelled and the text goes on as a request.
        Assert.DoesNotContain(conversation.Messages, m => m.Code == ReasonCodes.PointsToConfirm);
        Assert.Equal(CardStatus.Cancelled, conversation.CardState);
        Assert.Equal(PendingState.Cancelled, (await RecordAsync(session, card.PendingId)).State);
        Assert.Equal(ReasonCodes.NotUnderstood, LastAgentMessage(conversation).Code);
    }

    private static readonly Action<AgentOptions> PhraseWords = o =>
    {
        o.YesWords = ["yes", "ok", "go"];
        o.NoWords = ["no", "stop", "cancel", "the operation"];
    };

    [Theory]
    [InlineData("ok go")]
    [InlineData("yes ok")]
    [InlineData("yes ok go yes")]
    [InlineData("yes, ok!")]
    [InlineData("go... ok?")]
    public async Task AMessageMadeOnlyOfYesWordsPointsToConfirm(string typed)
    {
        var session = await StartAsync(configure: PhraseWords);
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        await conversation.SendAsync(typed);

        Assert.Equal(
            new ConversationMessage(ConversationRole.Agent, ConversationKind.Info, ReasonCodes.PointsToConfirm),
            LastAgentMessage(conversation));
        Assert.Equal(CardStatus.Live, conversation.CardState);
        var record = await RecordAsync(session, card.PendingId);
        Assert.Equal(PendingState.Pending, record.State);
        Assert.Equal(0, await WalletRowsAsync(record.IdempotencyKey));
        Assert.Single(session.Trace.Entries, e => e.Zone == TraceZone.Model);
    }

    [Theory]
    [InlineData("yes ok go yes ok")]
    [InlineData("yes maybe")]
    [InlineData("yes yesterday")]
    [InlineData("yesterday")]
    [InlineData("okgo")]
    public async Task TooManyYesWordsOrAnyOtherWordIsANewRequest(string typed)
    {
        var session = await StartAsync(configure: PhraseWords);
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        await conversation.SendAsync(typed);

        Assert.DoesNotContain(conversation.Messages, m => m.Code == ReasonCodes.PointsToConfirm);
        Assert.Equal(PendingState.Cancelled, (await RecordAsync(session, card.PendingId)).State);
        // The parser was called for the second message: it is a request.
        Assert.Equal(2, session.Trace.Entries.Count(e => e.Zone == TraceZone.Model));
        Assert.Equal(ReasonCodes.NotUnderstood, LastAgentMessage(conversation).Code);
    }

    /// <summary>D6 (Khalil, 15 Oct 2026): the message is cut, left to right, into whole entries of the list.</summary>
    [Theory]
    [InlineData("stop no!")]
    [InlineData("the operation")]
    [InlineData("cancel the operation")]
    [InlineData("the operation, cancel")]
    public async Task AMessageMadeOfWholeNoEntriesIsANo(string typed)
    {
        var session = await StartAsync(configure: PhraseWords);
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        await conversation.SendAsync(typed);

        Assert.Equal(Outcome(OutcomeCodes.Cancelled), LastAgentMessage(conversation));
        Assert.Equal(PendingState.Cancelled, (await RecordAsync(session, card.PendingId)).State);
        // A no is not a request: the parser is not called.
        Assert.Single(session.Trace.Entries, e => e.Zone == TraceZone.Model);
    }

    /// <summary>A multi-word entry is one phrase: one of its words alone, or its words out of order, match nothing.</summary>
    [Theory]
    [InlineData("operation")]
    [InlineData("the")]
    [InlineData("operation the")]
    [InlineData("cancel operation")]
    public async Task OneWordOfAMultiWordEntryIsNotANo(string typed)
    {
        var session = await StartAsync(configure: PhraseWords);
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        await conversation.SendAsync(typed);

        // Not a no: the card is cancelled as for any request, and the parser is called.
        Assert.Equal(PendingState.Cancelled, (await RecordAsync(session, card.PendingId)).State);
        Assert.Equal(2, session.Trace.Entries.Count(e => e.Zone == TraceZone.Model));
        Assert.Equal(ReasonCodes.NotUnderstood, LastAgentMessage(conversation).Code);
    }

    [Fact]
    public async Task TheWordLimitIsReadFromTheOptions()
    {
        var session = await StartAsync(configure: o =>
        {
            PhraseWords(o);
            o.MaxYesNoWords = 1;
        });
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);

        await conversation.SendAsync("ok go");

        Assert.DoesNotContain(conversation.Messages, m => m.Code == ReasonCodes.PointsToConfirm);
        Assert.Equal(CardStatus.Cancelled, conversation.CardState);
        Assert.Equal(2, session.Trace.Entries.Count(e => e.Zone == TraceZone.Model));
    }

    [Fact]
    public async Task TheLimitCountsWordsNotEntries()
    {
        var session = await StartAsync(configure: o =>
        {
            PhraseWords(o);
            o.MaxYesNoWords = 2;
        });
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);

        // Two entries but three words: over the limit, so a request, not a no.
        await conversation.SendAsync("cancel the operation");

        Assert.Equal(2, session.Trace.Entries.Count(e => e.Zone == TraceZone.Model));
        Assert.Equal(ReasonCodes.NotUnderstood, LastAgentMessage(conversation).Code);
    }

    [Fact]
    public async Task KhalilsTransferThatStartsWithAYesWordIsANewRequest()
    {
        var session = await StartAsync(
            configure: o => o.YesWords = [AgentHost.TestYesWord, KhalilYesWord],
            extra: new ScriptedSentence(KhalilTransferSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand)));
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var first = conversation.Card!;

        await conversation.SendAsync(KhalilTransferSentence);

        Assert.DoesNotContain(conversation.Messages, m => m.Code == ReasonCodes.PointsToConfirm);
        var record = await RecordAsync(session, first.PendingId);
        Assert.Equal(PendingState.Cancelled, record.State);
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.NotEqual(first.PendingId, conversation.Card!.PendingId);
        Assert.Equal(0, await WalletRowsAsync(record.IdempotencyKey));
    }

    [Fact]
    public async Task AnotherPersonsBalanceRepliesOnlyOwnBalanceAndLogsOtherBalance()
    {
        const string sentence = "how much does ali have";
        var session = await StartAsync(
            extra: new ScriptedSentence(sentence, new ParsedRequest { Intent = RequestIntent.Unsupported, UnsupportedReason = "other_balance" }));
        var conversation = session.Conversation;

        await conversation.SendAsync(sentence);

        Assert.Equal(
            new ConversationMessage(ConversationRole.Agent, ConversationKind.Refusal, ReasonCodes.OnlyOwnBalance),
            LastAgentMessage(conversation));
        Assert.Equal(new global::Agent.Core.Learning.LearningEntry(ReasonCodes.NotSupported, "other_balance"), conversation.LastLearningEntry);
    }

    [Fact]
    public async Task ANewSentenceCancelsTheCardAndIsAnswered()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        await conversation.SendAsync(BalanceSentence);

        Assert.Equal(CardStatus.Cancelled, conversation.CardState);
        Assert.Equal(PendingState.Cancelled, (await RecordAsync(session, card.PendingId)).State);
        Assert.Contains(Outcome(OutcomeCodes.Cancelled), conversation.Messages);
        Assert.Equal(
            new ConversationMessage(ConversationRole.Agent, ConversationKind.Balance, AmountIqd: SeedFacts.DemoBalance),
            LastAgentMessage(conversation));
    }

    [Fact]
    public async Task TwoConfirmsAtOnceMoveTheMoneyOnce()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        var outcomes = await Task.WhenAll(conversation.ConfirmAsync(null), conversation.ConfirmAsync(null));

        Assert.Single(outcomes, o => o is ConfirmOutcome.Done);
        Assert.Single(outcomes, o => o == new ConfirmOutcome.AlreadyHandled(PendingState.Done));
        Assert.Single(conversation.Messages, m => m.Code == OutcomeCodes.Done);
        Assert.Single(conversation.Messages, m => m.Code == OutcomeCodes.AlreadyHandled);
        Assert.Equal(1, await WalletRowsAsync((await RecordAsync(session, card.PendingId)).IdempotencyKey));
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task TheTimerEndingMarksTheCardExpired()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var timer = session.Provider.GetRequiredService<IOptions<AgentOptions>>().Value.CardTimerSeconds;

        host.Clock.Advance(TimeSpan.FromSeconds(timer + 1));
        await conversation.RefreshAsync();

        Assert.Equal(CardStatus.Expired, conversation.CardState);
        Assert.Equal(Outcome(OutcomeCodes.Expired), LastAgentMessage(conversation));

        // A late Confirm (a stale page) still reaches nothing.
        Assert.IsType<ConfirmOutcome.Expired>(await conversation.ConfirmAsync(null));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task AnOptionThatWasNotOfferedIsIgnored()
    {
        var session = await StartAsync(
            SeedFacts.DemoUserId,
            null,
            null,
            new ScriptedSentence("send fifty to hammoudi", ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.None)));
        var conversation = session.Conversation;

        // No question open: any tap is ignored.
        await conversation.TapOptionAsync("50000");
        Assert.Contains(session.Trace.Entries, e => e.Step == "option_rejected");

        await conversation.SendAsync("send fifty to hammoudi");
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal(QuestionKinds.WhichAmount, question.Kind);
        var asked = Assert.Single(conversation.QuestionsAsked);
        Assert.Equal(QuestionKinds.WhichAmount, asked.Kind);
        Assert.Equal(question.Options.Select(o => o.Value), asked.OptionValues);
        Assert.Equal(
            new ConversationMessage(ConversationRole.Agent, ConversationKind.Question, QuestionKinds.WhichAmount),
            LastAgentMessage(conversation));
        var before = conversation.Messages.Count;

        await conversation.TapOptionAsync("50000000");

        Assert.Equal(before, conversation.Messages.Count);
        Assert.Equal(2, session.Trace.Entries.Count(e => e.Step == "option_rejected"));
        Assert.Null(conversation.Card);

        // The offered option fills the amount and gives the card.
        await conversation.TapOptionAsync("50000");
        Assert.Contains(new ConversationMessage(ConversationRole.User, ConversationKind.Option, "50000"), conversation.Messages);
        Assert.Equal(50_000, Assert.IsType<CardView>(conversation.Card).AmountIqd);
    }

    [Fact]
    public async Task ATypedAnswerFillsTheMissingAmount()
    {
        var session = await StartAsync(
            SeedFacts.DemoUserId,
            null,
            null,
            new ScriptedSentence("send to hammoudi", ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, null, AmountUnit.None)));
        var conversation = session.Conversation;

        await conversation.SendAsync("send to hammoudi");
        Assert.Equal(QuestionAnswer.Typed, Assert.IsType<AgentReply.Question>(conversation.Question).Answer);
        Assert.Equal(QuestionAnswer.Typed, Assert.Single(conversation.QuestionsAsked).Answer);

        // Arabic-Indic digits are accepted (D20).
        await conversation.SendAsync(ArabicDigits.ToArabicIndic("20000"));

        Assert.Equal(20_000, Assert.IsType<CardView>(conversation.Card).AmountIqd);
    }

    [Fact]
    public async Task TwoActionsAtOnceDoNotCollide()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);

        // A timer refresh and a typed message at the same moment: one at a time, each in its own scope.
        await Task.WhenAll(conversation.RefreshAsync(), conversation.SendAsync(BalanceSentence), conversation.RefreshAsync());

        Assert.DoesNotContain(session.Trace.Entries, e => e.Step == "error");
        Assert.Equal(
            new ConversationMessage(ConversationRole.Agent, ConversationKind.Balance, AmountIqd: SeedFacts.DemoBalance),
            LastAgentMessage(conversation));
        Assert.False(conversation.Busy);
    }

    [Fact]
    public async Task TheTraceKeepsNoPinKeyOrCodeAfterAPinPaymentAndAPurchase()
    {
        var session = await StartAsync(
            SeedFacts.TraderUserId,
            null,
            null,
            new ScriptedSentence("pay the trader", ParsedRequest.Transfer(SeedFacts.Contact("T01").SavedName, 1_100_000, AmountUnit.Dinars)),
            new ScriptedSentence("zain credit five", ParsedRequest.Purchase("zain", "credit", 5_000)));
        var conversation = session.Conversation;
        var pin = SeedFacts.PinOf("U3");

        await conversation.SendAsync("pay the trader");
        var pinCard = Assert.IsType<CardView>(conversation.Card);
        Assert.True(pinCard.PinRequired);
        Assert.True(conversation.PinVisible);
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(ArabicDigits.ToArabicIndic(pin)));
        var pinKey = (await RecordAsync(session, pinCard.PendingId, SeedFacts.TraderUserId)).IdempotencyKey;

        await conversation.SendAsync("zain credit five");
        var purchaseCard = Assert.IsType<CardView>(conversation.Card);
        var bought = Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        Assert.NotNull(bought.PurchaseCode);
        var purchaseKey = (await RecordAsync(session, purchaseCard.PendingId, SeedFacts.TraderUserId)).IdempotencyKey;
        Assert.Equal(bought.PurchaseCode, LastAgentMessage(conversation).PurchaseCode);
        Assert.Equal([pinCard, purchaseCard], conversation.CardsIssued);

        // The orchestrator and the handler wrote to the same trace, through each action's own scope.
        Assert.Contains(session.Trace.Entries, e => e.Step == "card_created");
        Assert.Contains(session.Trace.Entries, e => e.Step == "outcome:done");

        string[] secrets = [pin, ArabicDigits.ToArabicIndic(pin), pinKey, purchaseKey, bought.PurchaseCode];
        foreach (var entry in session.Trace.Entries)
        {
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, entry.Step, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, entry.Detail ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task DisposingTheConversationMidActionEndsItQuietly()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();
        var stall = new StallingHandler();
        var provider = host.BuildProvider((services, configuration) =>
        {
            services.AddAgentCore(configuration);
            // Every wallet call of this provider waits until it is cancelled: a wallet that never answers.
            services.AddHttpClient<global::Agent.Core.Wallet.WalletClient>().AddHttpMessageHandler(() => stall);
        });
        disposables.Add(provider);
        var conversation = new AgentConversation(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ScriptedParser(),
            host.Clock,
            provider.GetRequiredService<IOptions<AgentOptions>>(),
            new RecordingSink(),
            SeedFacts.DemoUserId);

        // This conversation has the embedded parser, so it is typed the sentence of the file, as Khalil wrote it.
        var embedded = ScriptedParser.EmbeddedSentences().First(s => s.Request.Intent == RequestIntent.Transfer).Sentence;
        var sending = conversation.SendAsync(embedded);
        await stall.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);

        // The user goes away while the orchestrator waits on the wallet.
        conversation.Dispose();

        await sending; // No exception escapes: the cancellation ends the action inside the conversation.
        Assert.False(conversation.Busy);
        Assert.Null(conversation.Card);

        // Disposing again, and an action after the end, are harmless.
        conversation.Dispose();
        await conversation.SendAsync(BalanceSentence);
        Assert.Single(conversation.Messages);
    }

    [Fact]
    public async Task AnUnavailableParserGivesModelUnavailableAndNoCard()
    {
        var session = await StartAsync(SeedFacts.DemoUserId, new UnavailableParser());
        var conversation = session.Conversation;

        await conversation.SendAsync(HammoudiSentence);

        Assert.Null(conversation.Card);
        Assert.Empty(conversation.CardsIssued);
        Assert.Equal(
            new ConversationMessage(ConversationRole.Agent, ConversationKind.Refusal, ReasonCodes.ModelUnavailable),
            LastAgentMessage(conversation));
        Assert.Equal(ReasonCodes.ModelUnavailable, Assert.IsType<AgentReply.Refusal>(conversation.LastReply).Code);
        Assert.Contains(session.Trace.Entries, e => e.Zone == TraceZone.Model && e.Step == "parse:unavailable");
        Assert.DoesNotContain(session.Trace.Entries, e => e.Step == "error");
        Assert.False(conversation.Busy);
    }

    [Fact]
    public async Task TheConsentScreenIsShownAndRecorded()
    {
        // U5 has not given consent in the seed.
        var session = await StartAsync(NoConsentUserId);
        var conversation = session.Conversation;
        Assert.False(conversation.ConsentShown);

        await conversation.SendAsync(BalanceSentence);

        Assert.True(conversation.ConsentShown);
        Assert.Equal(new ConversationMessage(ConversationRole.Agent, ConversationKind.Consent), LastAgentMessage(conversation));
        Assert.Equal(conversation.Messages.Count - 1, conversation.ActionMessageIndex);
    }

    [Fact]
    public async Task ChangedIsRaisedForEveryMessage()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        var counts = new List<int>();
        conversation.Changed += () => counts.Add(conversation.Messages.Count);

        await conversation.SendAsync(HammoudiSentence);

        // Busy, the typed message, the card message, then the end of the action.
        Assert.Contains(1, counts);
        Assert.Contains(2, counts);
        Assert.False(conversation.Busy);
    }

    /// <summary>A trace sink that keeps every step.</summary>
    private sealed class RecordingSink : ITraceSink
    {
        private readonly ConcurrentQueue<(TraceZone Zone, string Step, string? Detail)> entries = new();

        public IReadOnlyList<(TraceZone Zone, string Step, string? Detail)> Entries => [.. entries];

        public void Record(TraceZone zone, string step, string? detail) => entries.Enqueue((zone, step, detail));
    }

    /// <summary>A parser whose model is down.</summary>
    private sealed class UnavailableParser : IParser
    {
        public string Name => "unavailable";

        public Task<ParsedRequest> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken)
            => throw new ParserUnavailableException();
    }

    private sealed class DisposeAction(Action dispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Holds every request until its token is cancelled.</summary>
    private sealed class StallingHandler : DelegatingHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
