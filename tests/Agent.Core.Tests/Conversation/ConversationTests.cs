extern alias AgentWeb;

using System.Collections.Concurrent;
using Agent.Core.Confirm;
using Agent.Core.Audit;
using Agent.Core.Conversation;
using Agent.Core.Data;
using Agent.Core.Parsing;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Store;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Agent.Core.Text;
using Agent.Core.Trace;
using Agent.Core.Wallet;
using AgentWeb::Agent.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Entities;
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
    /// <summary>The transfer sentence of these tests' scripted parser (English; the Arabic dev-set cases are the oracle's).</summary>
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
    private Task<Session> StartAsync(int userId = SeedFacts.DemoUserId, IParser? parser = null, Action<AgentOptions>? configure = null, params ScriptedSentence[] extra)
        => StartCoreAsync(userId, parser, configure, register: null, extra);

    /// <summary><see cref="StartAsync"/>, with <paramref name="register"/> adding services after <c>AddAgentCore</c>.</summary>
    private async Task<Session> StartCoreAsync(
        int userId, IParser? parser, Action<AgentOptions>? configure, Action<IServiceCollection>? register, ScriptedSentence[] extra)
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
            register?.Invoke(services);
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
            provider.GetRequiredService<IOptionsMonitor<AgentOptions>>(),
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

        // Not a yes (no pointer to the button) and not a plain no (nothing cancelled before the parse): the text goes to
        // the parser as a request. It is unclear to this parser, so the card stays until its timer ends (D6).
        Assert.DoesNotContain(conversation.Messages, m => m.Code == ReasonCodes.PointsToConfirm);
        Assert.Equal(2, session.Trace.Entries.Count(e => e.Zone == TraceZone.Model));
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal(PendingState.Pending, (await RecordAsync(session, card.PendingId)).State);
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
        // The parser was called for the second message: it is a request. This parser finds it unclear, so the card stays (D6).
        Assert.Equal(2, session.Trace.Entries.Count(e => e.Zone == TraceZone.Model));
        Assert.Equal(PendingState.Pending, (await RecordAsync(session, card.PendingId)).State);
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

        // Not a no: nothing is cancelled before the parse, and the parser is called. It finds the text unclear, so the
        // card stays (D6).
        Assert.Equal(2, session.Trace.Entries.Count(e => e.Zone == TraceZone.Model));
        Assert.Equal(PendingState.Pending, (await RecordAsync(session, card.PendingId)).State);
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

        // Over the limit, so not a yes: parsed as a request, unclear to this parser, and the card stays (D6).
        Assert.DoesNotContain(conversation.Messages, m => m.Code == ReasonCodes.PointsToConfirm);
        Assert.Equal(2, session.Trace.Entries.Count(e => e.Zone == TraceZone.Model));
        Assert.Equal(CardStatus.Live, conversation.CardState);
    }

    /// <summary>
    /// Security review I3: a <c>MaxYesNoWords</c> saved in the settings (through <see cref="RuntimeSettings"/>, as the
    /// console saves it) applies to the next message of a conversation that is already open.
    /// </summary>
    [Fact]
    public async Task AMaxYesNoWordsSavedInTheSettingsAppliesToTheNextMessage()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();
        var trace = new RecordingSink();
        var provider = host.BuildProvider((services, configuration) =>
        {
            // The host's configuration with the console's runtime source last, as Program.cs adds it.
            var live = new ConfigurationBuilder().AddConfiguration(configuration).AddRuntimeSettings().Build();
            services.AddSingleton<ITraceSink>(trace);
            services.AddAgentCore(live);
            services.PostConfigure(PhraseWords);
            services.AddRuntimeSettings(live);
        });
        disposables.Add(provider);
        var conversation = new AgentConversation(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ScriptedParser([new ScriptedSentence(HammoudiSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand))]),
            host.Clock,
            provider.GetRequiredService<IOptionsMonitor<AgentOptions>>(),
            trace,
            SeedFacts.DemoUserId);
        disposables.Add(new DisposeAction(conversation.Dispose));
        await conversation.SendAsync(HammoudiSentence);

        // Two words, within the default limit: a yes.
        await conversation.SendAsync("ok go");
        Assert.Single(conversation.Messages, m => m.Code == ReasonCodes.PointsToConfirm);

        var runtime = provider.GetRequiredService<RuntimeSettings>();
        Assert.True(runtime.TrySet("Agent:" + nameof(AgentOptions.MaxYesNoWords), "1", out var error), error);

        // The same two words in the same conversation are now over the limit: not a yes, and the card stays (D6).
        await conversation.SendAsync("ok go");
        Assert.Single(conversation.Messages, m => m.Code == ReasonCodes.PointsToConfirm);
        Assert.Equal(ReasonCodes.NotUnderstood, Assert.IsType<AgentReply.Refusal>(conversation.LastReply).Code);
        Assert.Equal(CardStatus.Live, conversation.CardState);
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

    /// <summary>A sentence the test parser reads as asking to change the waiting card.</summary>
    private const string ChangeSentence = "make it sixty instead";

    /// <summary>A second transfer to Hammoudi, with another amount.</summary>
    private const string TwentyThousandSentence = "send twenty thousand to my brother Hammoudi";

    private static async Task<List<AuditEntry>> AuditAsync(Session session, Guid pendingId)
    {
        await using var scope = session.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.AuditEntries.AsNoTracking().Where(a => a.PendingId == pendingId).OrderBy(a => a.Id).ToListAsync(Token);
    }

    [Fact]
    public async Task AnUnclearMessageLeavesTheCardUntilItsTimerEnds()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        // D6: neither a yes, nor a no, nor a request the parser understood.
        await conversation.SendAsync("hmm what was that");

        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Same(card, conversation.Card);
        Assert.Equal(ReasonCodes.NotUnderstood, Assert.IsType<AgentReply.Refusal>(conversation.LastReply).Code);
        Assert.Equal(
            new ConversationMessage(ConversationRole.Agent, ConversationKind.Refusal, ReasonCodes.NotUnderstood),
            LastAgentMessage(conversation));
        Assert.DoesNotContain(Outcome(OutcomeCodes.Cancelled), conversation.Messages);
        Assert.Contains((TraceZone.Code, "card_kept", "unclear"), session.Trace.Entries);
        Assert.Equal(PendingState.Pending, (await RecordAsync(session, card.PendingId)).State);
        Assert.DoesNotContain(await AuditAsync(session, card.PendingId), a => a.Event == AuditEvents.Cancelled);

        // The card still ends with its timer, and Confirm still works until then.
        var timer = session.Provider.GetRequiredService<IOptions<AgentOptions>>().Value.CardTimerSeconds;
        host.Clock.Advance(TimeSpan.FromSeconds(timer + 1));
        await conversation.RefreshAsync();
        Assert.Equal(CardStatus.Expired, conversation.CardState);
    }

    [Fact]
    public async Task AnUnclearMessageLeavesTheCardAndConfirmStillMovesTheMoney()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);

        await conversation.SendAsync("hmm what was that");

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        Assert.Equal(SeedFacts.DemoBalance - 51_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task AChangeRequestLeavesTheCard()
    {
        var change = new ScriptedSentence(ChangeSentence, new ParsedRequest { Intent = RequestIntent.Unsupported, UnsupportedReason = "change_waiting_card" });
        var session = await StartAsync(extra: [change]);
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        await conversation.SendAsync(ChangeSentence);

        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal(new AgentReply.Unsupported("change_waiting_card"), conversation.LastReply);
        Assert.Contains((TraceZone.Code, "card_kept", "change_waiting_card"), session.Trace.Entries);
        Assert.DoesNotContain(Outcome(OutcomeCodes.Cancelled), conversation.Messages);
        Assert.Equal(PendingState.Pending, (await RecordAsync(session, card.PendingId)).State);
    }

    [Fact]
    public async Task ANewRequestCancelsTheCardAndStartsFresh()
    {
        var twenty = new ScriptedSentence(TwentyThousandSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 20, AmountUnit.Thousand));
        var session = await StartAsync(extra: [twenty]);
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var first = conversation.Card!;

        await conversation.SendAsync(TwentyThousandSentence);

        Assert.Equal(PendingState.Cancelled, (await RecordAsync(session, first.PendingId)).State);
        Assert.Contains(await AuditAsync(session, first.PendingId), a => a.Event == AuditEvents.Cancelled);
        var second = Assert.IsType<CardView>(conversation.Card);
        Assert.NotEqual(first.PendingId, second.PendingId);
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal(20_000, second.AmountIqd);
        Assert.Equal(PendingState.Pending, (await RecordAsync(session, second.PendingId)).State);
        Assert.Equal([first, second], conversation.CardsIssued);
        Assert.DoesNotContain(session.Trace.Entries, e => e.Step == "card_kept");
    }

    [Fact]
    public async Task AnUnavailableParserLeavesALiveCard()
    {
        var parser = new UnavailableAfterFirstParser(
            new ScriptedParser([new ScriptedSentence(HammoudiSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand))]));
        var session = await StartAsync(SeedFacts.DemoUserId, parser);
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        await conversation.SendAsync(HammoudiSentence);

        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal(ReasonCodes.ModelUnavailable, Assert.IsType<AgentReply.Refusal>(conversation.LastReply).Code);
        Assert.Equal(PendingState.Pending, (await RecordAsync(session, card.PendingId)).State);
    }

    [Fact]
    public async Task AModelRefusalLeavesALiveCard()
    {
        var parser = new RefusalAfterFirstParser(
            new ScriptedParser([new ScriptedSentence(HammoudiSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand))]));
        var session = await StartAsync(SeedFacts.DemoUserId, parser);
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var card = conversation.Card!;

        await conversation.SendAsync(HammoudiSentence);

        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Same(card, conversation.Card);
        Assert.Equal(ReasonCodes.NotUnderstood, Assert.IsType<AgentReply.Refusal>(conversation.LastReply).Code);
        Assert.Contains(session.Trace.Entries, e => e.Zone == TraceZone.Code && e.Step == "card_kept");
        Assert.Equal(PendingState.Pending, (await RecordAsync(session, card.PendingId)).State);
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
            new ScriptedSentence("send fifty thousand to a shared name", ParsedRequest.Transfer(SeedFacts.Contact("S08").SavedName, 50_000, AmountUnit.Dinars)));
        var conversation = session.Conversation;

        // No question open: any tap is ignored.
        await conversation.TapOptionAsync("S08");
        Assert.Contains(session.Trace.Entries, e => e.Step == "option_rejected");

        await conversation.SendAsync("send fifty thousand to a shared name");
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal(QuestionKinds.WhichContact, question.Kind);
        var asked = Assert.Single(conversation.QuestionsAsked);
        Assert.Equal(QuestionKinds.WhichContact, asked.Kind);
        Assert.Equal(question.Options.Select(o => o.Value), asked.OptionValues);
        Assert.Equal(
            new ConversationMessage(ConversationRole.Agent, ConversationKind.Question, QuestionKinds.WhichContact),
            LastAgentMessage(conversation));
        var before = conversation.Messages.Count;

        // S01 is a contact of U1, but not one of the options offered.
        await conversation.TapOptionAsync("S01");

        Assert.Equal(before, conversation.Messages.Count);
        Assert.Equal(2, session.Trace.Entries.Count(e => e.Step == "option_rejected"));
        Assert.Null(conversation.Card);

        // The offered option picks the contact and gives the card.
        await conversation.TapOptionAsync("S08");
        Assert.Contains(new ConversationMessage(ConversationRole.User, ConversationKind.Option, "S08"), conversation.Messages);
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

        // The pending record keeps only the code's last four characters; no audit row of the purchase holds the code.
        var purchaseRecord = await RecordAsync(session, purchaseCard.PendingId, SeedFacts.TraderUserId);
        Assert.Equal(bought.PurchaseCode[^4..], purchaseRecord.PurchaseCodeLast4);
        await using (var scope = session.Provider.CreateAsyncScope())
        {
            var audit = await scope.ServiceProvider.GetRequiredService<AgentDbContext>().AuditEntries.AsNoTracking()
                .Where(a => a.PendingId == purchaseCard.PendingId)
                .ToListAsync(Token);
            Assert.Contains(audit, a => a.ToState == nameof(PendingState.Done));
            Assert.All(audit, a =>
            {
                foreach (var field in new[] { a.Detail, a.Reference, a.Reason })
                    Assert.DoesNotContain(bought.PurchaseCode, field ?? string.Empty, StringComparison.Ordinal);
            });
        }

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

    private static async Task<List<global::Agent.Core.Learning.LearningEntryRow>> LearningRowsAsync(Session session)
    {
        await using var scope = session.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentDbContext>().LearningEntries.AsNoTracking()
            .OrderBy(r => r.Id)
            .ToListAsync(Token);
    }

    [Fact]
    public async Task APinTypedIntoTheChatWhileAPinCardIsLiveRepliesPinRequiredWithoutAModelCall()
    {
        // Known gaps row 34 (Khalil, 19 Oct 2026): four to six digits typed while a PIN card is live never reach the
        // parse call or the learning log; the card stays live and the PIN field still pays.
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>(StringComparer.Ordinal)
        {
            ["pay the trader"] = ParsedRequest.Transfer(SeedFacts.Contact("T01").SavedName, 1_100_000, AmountUnit.Dinars),
        });
        var session = await StartAsync(SeedFacts.TraderUserId, parser);
        var conversation = session.Conversation;
        var pin = SeedFacts.PinOf("U3");

        await conversation.SendAsync("pay the trader");
        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.True(conversation.PinVisible);
        var callsBefore = parser.Calls.Count;
        var rowsBefore = (await LearningRowsAsync(session)).Count;

        await conversation.SendAsync(" " + ArabicDigits.ToArabicIndic(pin) + " ");

        Assert.Equal(callsBefore, parser.Calls.Count);
        Assert.Equal(Outcome(OutcomeCodes.PinRequired), LastAgentMessage(conversation));
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Same(card, conversation.Card);
        Assert.True(conversation.PinVisible);
        Assert.Null(conversation.LastLearningEntry);
        Assert.Equal(rowsBefore, (await LearningRowsAsync(session)).Count);
        Assert.Contains(session.Trace.Entries, e => e.Step == TraceSteps.PinTypedInChat && e.Detail is null);
        foreach (var entry in session.Trace.Entries)
        {
            foreach (var secret in new[] { pin, ArabicDigits.ToArabicIndic(pin) })
            {
                Assert.DoesNotContain(secret, entry.Step, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, entry.Detail ?? string.Empty, StringComparison.Ordinal);
            }
        }

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(pin));
    }

    [Fact]
    public async Task AFourToSixDigitMessageWithNoCardIsParsedButWritesNoLearningRow()
    {
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>(StringComparer.Ordinal));
        var session = await StartAsync(SeedFacts.DemoUserId, parser);
        var conversation = session.Conversation;

        await conversation.SendAsync("12345");

        Assert.Single(parser.Calls);
        Assert.Equal(ReasonCodes.NotUnderstood, LastAgentMessage(conversation).Code);
        Assert.Null(conversation.LastLearningEntry);
        Assert.Empty(await LearningRowsAsync(session));
    }

    [Fact]
    public async Task AThreeDigitAndASevenDigitMessageKeepTheirLearningRows()
    {
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>(StringComparer.Ordinal));
        var session = await StartAsync(SeedFacts.DemoUserId, parser);
        var conversation = session.Conversation;

        await conversation.SendAsync("500");
        Assert.NotNull(conversation.LastLearningEntry);
        await conversation.SendAsync("1234567");
        Assert.NotNull(conversation.LastLearningEntry);

        var rows = await LearningRowsAsync(session);
        Assert.Equal(["500", "<NUM_1>"], rows.Select(r => r.MaskedText));
        Assert.All(rows, r => Assert.Equal(ReasonCodes.NotUnderstood, r.Reason));
    }

    [Fact]
    public async Task ListingOrdersJoinsTheCatalogueAndTracesOnlyTheCount()
    {
        var session = await StartAsync(
            SeedFacts.TraderUserId,
            null,
            null,
            new ScriptedSentence("zain credit five", ParsedRequest.Purchase("zain", "credit", 5_000)));
        var conversation = session.Conversation;

        await conversation.SendAsync("zain credit five");
        var purchaseCard = Assert.IsType<CardView>(conversation.Card);
        var bought = Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        Assert.NotNull(bought.PurchaseCode);
        var messages = conversation.Messages.Count;

        var orders = Assert.IsType<WalletResult<IReadOnlyList<PurchaseOrder>>.Ok>(await conversation.ListOrdersAsync()).Value;

        var product = global::Agent.Core.Tests.Resolver.SeedWallet.Products().Single(p => p.Code == purchaseCard.ProductCode);
        var first = orders[0];
        Assert.Equal(bought.PurchaseCode, first.Code);
        Assert.Equal((product.ProviderNameAr, product.Type, (long?)product.PriceIqd), (first.ProviderNameAr, first.ProductType, first.PriceIqd));
        Assert.DoesNotContain(first.Code, first.ToString(), StringComparison.Ordinal);

        var read = Assert.Single(session.Trace.Entries, e => e.Step == TraceSteps.OrdersRead);
        Assert.Equal(orders.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), read.Detail);
        foreach (var entry in session.Trace.Entries)
        {
            foreach (var order in orders)
            {
                Assert.DoesNotContain(order.Code, entry.Step, StringComparison.Ordinal);
                Assert.DoesNotContain(order.Code, entry.Detail ?? string.Empty, StringComparison.Ordinal);
            }
        }

        Assert.Equal(messages, conversation.Messages.Count);
        Assert.False(conversation.Busy);
    }

    [Fact]
    public async Task ListingOrdersWhileStoppedIsUnavailable()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        conversation.Dispose();

        var read = Assert.IsType<WalletResult<IReadOnlyList<PurchaseOrder>>.Unavailable>(await conversation.ListOrdersAsync());

        Assert.Equal("stopped", read.Reason);
        Assert.DoesNotContain(session.Trace.Entries, e => e.Step == TraceSteps.OrdersRead);
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
            new ScriptedParser([new ScriptedSentence(HammoudiSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand))]),
            host.Clock,
            provider.GetRequiredService<IOptionsMonitor<AgentOptions>>(),
            new RecordingSink(),
            SeedFacts.DemoUserId);

        var sending = conversation.SendAsync(HammoudiSentence);
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
    public async Task OpenShowsTheConsentScreenOnceToAUserWhoHasNotConsented()
    {
        var session = await StartAsync(NoConsentUserId);
        var conversation = session.Conversation;

        await conversation.OpenAsync();

        var only = Assert.Single(conversation.Messages);
        Assert.Equal(new ConversationMessage(ConversationRole.Agent, ConversationKind.Consent), only);
        Assert.Equal(0, conversation.ActionMessageIndex);
        Assert.True(conversation.ConsentShown);
        Assert.Equal(1, await ConsentShownRowsAsync(session, NoConsentUserId));
        Assert.Contains(session.Trace.Entries, e => e.Step == TraceSteps.ConsentChecked && e.Detail == "required");
        Assert.False(conversation.Busy);

        // A second open adds nothing: no message, no audit row, no trace step.
        var steps = session.Trace.Entries.Count;
        await conversation.OpenAsync();

        Assert.Single(conversation.Messages);
        Assert.Equal(1, await ConsentShownRowsAsync(session, NoConsentUserId));
        Assert.Equal(steps, session.Trace.Entries.Count);

        // The tap with no pending request records consent and shows nothing more.
        await conversation.GiveConsentAsync();

        Assert.Single(conversation.Messages);
        Assert.Null(conversation.ActionMessageIndex);
        Assert.Contains(session.Trace.Entries, e => e.Step == "consent_given");
    }

    [Fact]
    public async Task OpenShowsNothingToAUserWhoHasConsented()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;

        await conversation.OpenAsync();

        Assert.Empty(conversation.Messages);
        Assert.False(conversation.ConsentShown);
        Assert.Null(conversation.ActionMessageIndex);
        Assert.Equal(0, await ConsentShownRowsAsync(session, SeedFacts.DemoUserId));
        Assert.Contains(session.Trace.Entries, e => e.Step == TraceSteps.ConsentChecked && e.Detail == "given");
    }

    [Theory]
    [InlineData(NoConsentUserId)]
    [InlineData(SeedFacts.DemoUserId)]
    public async Task OpenWithNoAnswerFromTheWalletShowsNothingAndTheFirstRequestsGateStillRuns(int userId)
    {
        var failOnce = new UserReadFailsOnceHandler(userId);
        var session = await StartCoreAsync(
            userId,
            parser: null,
            configure: null,
            register: services => services.AddHttpClient<WalletClient>().AddHttpMessageHandler(() => failOnce),
            extra: []);
        var conversation = session.Conversation;

        await conversation.OpenAsync();

        Assert.True(failOnce.Failed);
        Assert.Empty(conversation.Messages);
        Assert.False(conversation.ConsentShown);
        Assert.Contains(session.Trace.Entries, e => e.Step == TraceSteps.ConsentChecked && e.Detail == "unavailable");
        Assert.Equal(0, await ConsentShownRowsAsync(session, userId));

        await conversation.SendAsync(HammoudiSentence);

        if (userId == NoConsentUserId)
        {
            Assert.Equal(new ConversationMessage(ConversationRole.Agent, ConversationKind.Consent), LastAgentMessage(conversation));
            Assert.True(conversation.ConsentShown);
            Assert.Null(conversation.Card);
            Assert.Equal(1, await ConsentShownRowsAsync(session, userId));
        }
        else
        {
            Assert.NotNull(conversation.Card);
            Assert.Equal(CardStatus.Live, conversation.CardState);
        }
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

    private static async Task<int> ConsentShownRowsAsync(Session session, int userId)
    {
        await using var scope = session.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.AuditEntries.AsNoTracking().CountAsync(a => a.UserId == userId && a.Event == AuditEvents.ConsentShown, Token);
    }

    /// <summary>Drops the first <c>GET users/{id}</c> of <paramref name="userId"/> as a lost connection; every other call passes.</summary>
    private sealed class UserReadFailsOnceHandler(int userId) : DelegatingHandler
    {
        private int failed;

        public bool Failed => Volatile.Read(ref failed) == 1;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isUserRead = request.Method == HttpMethod.Get
                && request.RequestUri?.AbsolutePath.EndsWith(FormattableString.Invariant($"/users/{userId}"), StringComparison.Ordinal) == true;
            if (isUserRead && Interlocked.Exchange(ref failed, 1) == 0)
                throw new HttpRequestException("Stub: the wallet did not answer.");
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>A trace sink that keeps every step.</summary>
    /// <summary>The card's timer runs out and the sweep marks its record Expired.</summary>
    private async Task ExpireTheCardAsync(Session session)
    {
        var timer = session.Provider.GetRequiredService<IOptions<AgentOptions>>().Value.CardTimerSeconds;
        host.Clock.Advance(TimeSpan.FromSeconds(timer + 1));
        await using (var scope = session.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<PendingStore>().ExpireDueAsync(Token);
        await session.Conversation.RefreshAsync();
        Assert.Equal(CardStatus.Expired, session.Conversation.CardState);
    }

    /// <summary>
    /// Puts the live card's record in Executing and then <paramref name="state"/> through the store, and lets the
    /// conversation see it: a Confirm on a record that has left Pending is "already handled" and shows its state.
    /// </summary>
    private static async Task MoveTheCardAsync(Session session, PendingState state)
    {
        var card = session.Conversation.Card!;
        await using (var scope = session.Provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<PendingStore>();
            Assert.IsType<TransitionResult.Begun>(await store.TryBeginExecuteAsync(card.PendingId, SeedFacts.DemoUserId, Token));
            if (state == PendingState.Checking)
                await store.MarkCheckingAsync(card.PendingId, SeedFacts.DemoUserId, "test", Token);
            else
                Assert.IsType<TransitionResult.Completed>(await store.CompleteAsync(card.PendingId, SeedFacts.DemoUserId, state, "test", null, null, null, Token));
        }

        Assert.Equal(new ConfirmOutcome.AlreadyHandled(state), await session.Conversation.ConfirmAsync(null));
    }

    [Fact]
    public async Task TryAgainAfterExpiryOpensANewCardAndMovesNothing()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var first = conversation.Card!;
        await ExpireTheCardAsync(session);

        await conversation.ReissueAsync();

        // D19: a new record with its own ID, key and timer; the old one stays Expired, and nothing has moved.
        var second = Assert.IsType<CardView>(conversation.Card);
        Assert.NotEqual(first.PendingId, second.PendingId);
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal((50_000L, 1_000L, 51_000L), (second.AmountIqd, second.FeeIqd, second.TotalIqd));
        Assert.Equal(first.DisplayName, second.DisplayName);
        Assert.Null(second.Warning);
        var timer = session.Provider.GetRequiredService<IOptions<AgentOptions>>().Value.CardTimerSeconds;
        Assert.Equal(conversation.Now.AddSeconds(timer), second.ExpiresAt);
        Assert.Equal([first, second], conversation.CardsIssued);

        var old = await RecordAsync(session, first.PendingId);
        var fresh = await RecordAsync(session, second.PendingId);
        Assert.Equal(PendingState.Expired, old.State);
        Assert.Equal(PendingState.Pending, fresh.State);
        Assert.NotEqual(old.IdempotencyKey, fresh.IdempotencyKey);
        Assert.Equal(0, await WalletRowsAsync(old.IdempotencyKey));
        Assert.Equal(0, await WalletRowsAsync(fresh.IdempotencyKey));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Contains((TraceZone.User, "reissue_tapped", null), session.Trace.Entries);
        Assert.Contains((TraceZone.Code, TraceSteps.Reissue, "try_again"), session.Trace.Entries);
        Assert.DoesNotContain(session.Trace.Entries, e => e.Step is TraceSteps.BeginExecute or TraceSteps.ExecuteSent);

        // Nothing moves until Confirm.
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        Assert.Equal(1, await WalletRowsAsync(fresh.IdempotencyKey));
        Assert.Equal(SeedFacts.DemoBalance - 51_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task TryAgainAfterACancelOrANotDoneOpensANewCard()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        await conversation.CancelAsync();
        Assert.Equal(CardStatus.Cancelled, conversation.CardState);

        await conversation.ReissueAsync();
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal(2, conversation.CardsIssued.Count);

        await MoveTheCardAsync(session, PendingState.NotDone);
        Assert.Equal(CardStatus.NotDone, conversation.CardState);

        await conversation.ReissueAsync();
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal(3, conversation.CardsIssued.Count);
        Assert.Equal(2, session.Trace.Entries.Count(e => e == (TraceZone.Code, TraceSteps.Reissue, "try_again")));
    }

    [Fact]
    public async Task SendAgainAfterDoneOpensACardWithTheRepeatLine()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var first = conversation.Card!;
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        var paid = await RecordAsync(session, first.PendingId);

        // Past the repeat window, so the line comes from Send again itself, not from the history rule.
        var window = session.Provider.GetRequiredService<IOptions<AgentOptions>>().Value.RepeatWarningWindowMinutes;
        host.Clock.Advance(TimeSpan.FromMinutes(window + 1));

        await conversation.ReissueAsync();

        var second = Assert.IsType<CardView>(conversation.Card);
        Assert.NotEqual(first.PendingId, second.PendingId);
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal(CardBuilder.RepeatWarning, second.Warning);
        Assert.Equal(50_000, second.AmountIqd);
        Assert.Contains((TraceZone.Code, TraceSteps.Reissue, "send_again"), session.Trace.Entries);

        // The wallet still has the one transaction of the first card, and nothing under the new key.
        Assert.Equal(1, await WalletRowsAsync(paid.IdempotencyKey));
        Assert.Equal(0, await WalletRowsAsync((await RecordAsync(session, second.PendingId)).IdempotencyKey));
        Assert.Equal(PendingState.Done, (await RecordAsync(session, first.PendingId)).State);
        Assert.Equal(SeedFacts.DemoBalance - 51_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task ReissueIsRefusedWhileCheckingOrLive()
    {
        var session = await StartAsync();
        var conversation = session.Conversation;

        // No card yet.
        await conversation.ReissueAsync();
        Assert.Null(conversation.Card);

        await conversation.SendAsync(HammoudiSentence);
        var live = conversation.Card!;

        // A live card is not offered again.
        await conversation.ReissueAsync();
        Assert.Same(live, conversation.Card);
        Assert.Equal(CardStatus.Live, conversation.CardState);

        // Nor is one whose payment is checking.
        await MoveTheCardAsync(session, PendingState.Checking);
        Assert.Equal(CardStatus.Checking, conversation.CardState);
        var messages = conversation.Messages.Count;

        await conversation.ReissueAsync();

        Assert.Same(live, conversation.Card);
        Assert.Equal(CardStatus.Checking, conversation.CardState);
        Assert.Equal([live], conversation.CardsIssued);
        Assert.Equal(messages, conversation.Messages.Count);
        Assert.Equal(3, session.Trace.Entries.Count(e => e.Step == "reissue_rejected"));
        Assert.DoesNotContain(session.Trace.Entries, e => e.Step == TraceSteps.Reissue);
    }

    [Fact]
    public async Task TryAgainRunsFreshChecks()
    {
        var cap = new AgentOptions().PerTransactionCapIqd;
        var session = await StartAsync(configure: o => o.PerTransactionCapIqd = cap);
        var conversation = session.Conversation;
        await conversation.SendAsync(HammoudiSentence);
        var first = conversation.Card!;
        await ExpireTheCardAsync(session);

        // A cap lowered in the settings between the two cards refuses the second.
        cap = 20_000;
        await conversation.ReissueAsync();

        var overCap = Assert.IsType<AgentReply.Refusal>(conversation.LastReply);
        Assert.Equal(ReasonCodes.OverPerTransactionCap, overCap.Code);
        Assert.Equal(20_000, overCap.AmountIqd);
        Assert.Same(first, conversation.Card);
        Assert.Equal(CardStatus.Expired, conversation.CardState);
        Assert.Equal([first], conversation.CardsIssued);

        // So does the recipient's account, closed meanwhile.
        cap = new AgentOptions().PerTransactionCapIqd;
        await using (var scope = host.Wallet.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
            Assert.Equal(1, await db.Accounts
                .Where(a => a.Phone == SeedFacts.HammoudiPhone)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AccountStatus.Closed), Token));
        }

        await conversation.ReissueAsync();

        Assert.Equal(ReasonCodes.AccountClosed, Assert.IsType<AgentReply.Refusal>(conversation.LastReply).Code);
        Assert.Equal([first], conversation.CardsIssued);
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    private sealed class RecordingSink : ITraceSink
    {
        private readonly ConcurrentQueue<(TraceZone Zone, string Step, string? Detail)> entries = new();

        public IReadOnlyList<(TraceZone Zone, string Step, string? Detail)> Entries => [.. entries];

        public void Record(TraceZone zone, string step, string? detail) => entries.Enqueue((zone, step, detail));
    }

    /// <summary>A parser that answers once, then whose model is down.</summary>
    private sealed class UnavailableAfterFirstParser(IParser first) : IParser
    {
        private int calls;

        public string Name => "unavailable-after-first";

        public Task<ParsedRequest> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken)
            => Interlocked.Increment(ref calls) == 1
                ? first.ParseAsync(text, context, cancellationToken)
                : throw new ParserUnavailableException();
    }

    /// <summary>A parser that answers the first message and reports a model refusal for every later one.</summary>
    private sealed class RefusalAfterFirstParser(IParser first) : IParser
    {
        private int calls;

        public string Name => "refusal-after-first";

        public Task<ParsedRequest> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken)
            => Interlocked.Increment(ref calls) == 1
                ? first.ParseAsync(text, context, cancellationToken)
                : Task.FromResult(new ParsedRequest { Intent = RequestIntent.Unclear, Problem = ClaudeParser.RefusalProblem });
    }

    /// <summary>A parser whose model is down.</summary>
    /// <summary>The oracle: the scripted parser over the development set and its sidecar of parses.</summary>
    private static ScriptedParser Oracle()
        => ScriptedParser.Load(Path.Combine(SeedHashTests.RepositoryRoot(), "data", ScriptedParser.DevSetFileName));

    [Fact]
    public async Task ABareValueListsTheMatchingProductsAndTheTapGivesTheCard()
    {
        // AMB-23 on the oracle: rule 12 as decided lists the three credit cards of five across the operators.
        var session = await StartAsync(SeedFacts.DemoUserId, Oracle());
        var conversation = session.Conversation;

        await conversation.SendAsync(DevSetText.Say("AMB-23", 0));
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal(QuestionKinds.WhichProduct, question.Kind);
        Assert.Equal(["ZN-CR-5", "AC-CR-5", "KR-CR-5"], question.Options.Select(o => o.Value));

        await conversation.TapOptionAsync("KR-CR-5");

        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(("KR-CR-5", 5_000L), (card.ProductCode, card.AmountIqd));
    }

    [Fact]
    public async Task ALatinOperatorSpellingGoesStraightToTheCard()
    {
        // MAL-09 on the oracle: "asia", configured under the provider code, names Asiacell (U2).
        var session = await StartAsync(SeedFacts.TightUserId, Oracle(), o => o.ProviderSpellings["asiacell"] = ["asia"]);
        var conversation = session.Conversation;

        await conversation.SendAsync(DevSetText.Say("MAL-09", 0));

        Assert.Null(conversation.Question);
        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal("AC-CR-5", card.ProductCode);
    }

    [Fact]
    public async Task AnUnknownProductWritesTheLearningRowWithTheProviderCode()
    {
        // NOGO-12 on the oracle: Korek has no 25,000 credit card; the learning row names the provider code (ruling 2).
        var session = await StartAsync(SeedFacts.DemoUserId, Oracle());
        var conversation = session.Conversation;

        await conversation.SendAsync(DevSetText.Say("NOGO-12", 0));

        Assert.Null(conversation.Card);
        var refusal = LastAgentMessage(conversation);
        Assert.Equal((ConversationKind.Refusal, ReasonCodes.ProductNotFound), (refusal.Kind, refusal.Code));

        await using var scope = session.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var row = Assert.Single(await db.LearningEntries.AsNoTracking().ToListAsync(Token));
        Assert.Equal((ReasonCodes.ProductNotFound, "korek credit 25000"), (row.Reason, row.Detail));
    }

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
