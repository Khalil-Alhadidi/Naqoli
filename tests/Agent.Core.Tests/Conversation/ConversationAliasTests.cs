using System.Collections.Concurrent;
using Agent.Core.Aliases;
using Agent.Core.Audit;
using Agent.Core.Confirm;
using Agent.Core.Conversation;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Agent.Core.Text;
using Agent.Core.Trace;
using Agent.Core.Wallet;
using Microsoft.Extensions.DependencyInjection;
using AgentConversation = Agent.Core.Conversation.Conversation;

namespace Agent.Core.Tests.Conversation;

/// <summary>
/// Rule 18 on the conversation (milestone 9a): after a completed transfer whose recipient needed a question, the user's
/// own words are offered as an alias, saved only on the tap; the next action closes the offer. U1 over the oracle (the
/// scripted parser over the development set); sentences by case id and step, names from the wallet.
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class ConversationAliasTests(AgentHost host)
{
    private const int U1 = SeedFacts.DemoUserId;
    private const string Amb19 = "AMB-19";
    private const string Amb19Variant = "AMB-19-v1";

    /// <summary>The step of AMB-19 (and its variant) whose confirm expects the offer: the first confirm.</summary>
    private const int FirstConfirmStep = 2;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string DevSetPath => Path.Combine(SeedHashTests.RepositoryRoot(), "data", ScriptedParser.DevSetFileName);

    private static ScriptedParser Oracle() => ScriptedParser.Load(DevSetPath);

    private async Task<ConversationHarness> StartAsync(IParser? parser = null)
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        return await ConversationHarness.StartAsync(host, parser ?? Oracle(), U1);
    }

    [Fact]
    public async Task AMB19OffersTheAliasAfterTheDonePaymentAndTheSecondRequestAsksAgain()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        var seedAliases = (await ContactsAsync()).Aliases.Count;

        // Steps 0 to 2: the sentence, the tap on S08, the confirm.
        await PayS08Async(conversation, Say(Amb19, 0));

        var expected = DevSetText.AliasOffer(Amb19, FirstConfirmStep);
        var offer = Assert.IsType<AliasOffer>(conversation.AliasOffer);
        Assert.Equal(expected, offer.Text);
        Assert.Equal(S08AccountId(await ContactsAsync()), offer.AccountId);
        var s08Registered = S08(await ContactsAsync()).RegisteredName;
        Assert.NotNull(s08Registered);
        Assert.Equal(s08Registered, offer.RecipientName);
        var index = Assert.IsType<int>(conversation.AliasOfferMessageIndex);
        Assert.Equal(ConversationKind.AliasOffer, conversation.Messages[index].Kind);
        Assert.Null(conversation.Messages[index].Code);
        Assert.Single(harness.Trace.Entries, e => e.Step == "alias_offered" && e.Detail is null);

        // Step 3: the same recipient again asks again; the offer is gone.
        await conversation.SendAsync(Say(Amb19, 1));
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal(QuestionKinds.WhichContact, question.Kind);
        Assert.Equal(2, question.Options.Count);
        Assert.Null(conversation.AliasOffer);
        Assert.Null(conversation.AliasOfferMessageIndex);

        // Steps 4 and 5: tap and confirm; offered again, never saved.
        await conversation.TapOptionAsync("S08");
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(null));
        Assert.Equal(expected, conversation.AliasOffer?.Text);
        Assert.Equal(seedAliases, (await ContactsAsync()).Aliases.Count);
    }

    [Fact]
    public async Task SaveOnTheOfferWritesTheAliasOnce()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        var before = await ContactsAsync();
        await PayS08Async(conversation, Say(Amb19, 0));
        var card = Assert.IsType<CardView>(conversation.Card);
        var walletVersion = conversation.WalletVersion;

        var saved = Assert.IsType<AliasResult.Saved>(await conversation.SaveAliasAsync());

        var expected = DevSetText.AliasOffer(Amb19, FirstConfirmStep);
        Assert.Equal(expected, saved.Alias.Text);
        var after = await ContactsAsync();
        Assert.Equal(before.Aliases.Count + 1, after.Aliases.Count);
        var listed = Assert.Single(after.Aliases, a => a.Text == expected);
        Assert.Equal(S08AccountId(after), listed.TargetAccountId);
        Assert.Equal(ConversationKind.AliasSaved, conversation.Messages[^1].Kind);
        Assert.Equal(walletVersion + 1, conversation.WalletVersion);
        Assert.Null(conversation.AliasOffer);
        Assert.Single(await harness.AuditRowsAsync(), r => r.Event == AuditEvents.AliasSaved);

        // A second tap has no offer to save: nothing is written.
        var messages = conversation.Messages.Count;
        Assert.Null(await conversation.SaveAliasAsync());
        Assert.Equal(messages, conversation.Messages.Count);
        Assert.Equal(after.Aliases, (await ContactsAsync()).Aliases);
        Assert.Single(await harness.AuditRowsAsync(), r => r.Event == AuditEvents.AliasSaved);

        // The payment's record is untouched.
        Assert.Equal(PendingState.Done, (await RecordAsync(card.PendingId))!.State);
    }

    [Fact]
    public async Task AfterSavingTheAliasTheSameSentenceGoesStraightToTheCard()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        await PayS08Async(conversation, Say(Amb19, 0));
        Assert.IsType<AliasResult.Saved>(await conversation.SaveAliasAsync());
        var questions = conversation.QuestionsAsked.Count;

        await conversation.SendAsync(Say(Amb19, 1));

        Assert.Null(conversation.Question);
        Assert.Equal(questions, conversation.QuestionsAsked.Count);
        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal(DevSetText.AliasOffer(Amb19, FirstConfirmStep), card.DisplayName);
        Assert.Equal(S08(await ContactsAsync()).RegisteredName, card.RegisteredName);
        Assert.Equal(SeedFacts.Contact("S08").Number, card.ToPhone);
    }

    [Fact]
    public async Task ANearSpellingOfTheSavedAliasAsksDidYouMean()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        var spoken = DevSetText.AliasOffer(Amb19Variant, FirstConfirmStep)!;
        Assert.IsType<AliasResult.Saved>(await conversation.AddAliasAsync(spoken, S08AccountId(await ContactsAsync())));

        await conversation.SendAsync(Say(Amb19, 1));

        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal(QuestionKinds.DidYouMean, question.Kind);
        var option = Assert.Single(question.Options);
        Assert.Equal(spoken, option.Display);

        await conversation.TapOptionAsync(option.Value);

        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(spoken, card.DisplayName);
        Assert.Equal(SeedFacts.Contact("S08").Number, card.ToPhone);
    }

    [Fact]
    public async Task TheNextMessageClearsTheOffer()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        var before = await ContactsAsync();
        await PayS08Async(conversation, Say(Amb19, 0));
        Assert.NotNull(conversation.AliasOffer);

        await conversation.SendAsync(Say(Amb19, 1));

        Assert.Null(conversation.AliasOffer);
        Assert.Null(await conversation.SaveAliasAsync());
        Assert.Equal(before.Aliases, (await ContactsAsync()).Aliases);
    }

    [Fact]
    public async Task ARecipientTheModelDidNotCopyVerbatimGetsNoOffer()
    {
        // The fake stands in for a model that returned a recipient the user did not type as it is (ruling 3).
        var sentence = Say(Amb19, 0);
        var recipient = DevSetText.SidecarRecipient(Amb19)! + "x";
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>(StringComparer.Ordinal)
        {
            [Masker.Mask(sentence).Text] = ParsedRequest.Transfer(recipient, 10, AmountUnit.Thousand),
        });
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await PayS08Async(conversation, sentence);

        Assert.Equal(CardStatus.Done, conversation.CardState);
        Assert.Null(conversation.AliasOffer);
        Assert.DoesNotContain(conversation.Messages, m => m.Kind == ConversationKind.AliasOffer);
        Assert.DoesNotContain(harness.Trace.Entries, e => e.Step == "alias_offered");
    }

    [Fact]
    public async Task ASavedAliasNeverReachesTheParser()
    {
        var parser = new RecordingParser(Oracle());
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;
        await PayS08Async(conversation, Say(Amb19, 0));
        var saved = Assert.IsType<AliasResult.Saved>(await conversation.SaveAliasAsync());
        var sentToParser = parser.Calls.Count;

        await conversation.SendAsync(Say(Amb19, 1));

        Assert.True(parser.Calls.Count > sentToParser);
        var alias = ArabicText.Normalize(saved.Alias.Text);
        foreach (var (text, context) in parser.Calls.Skip(sentToParser))
        {
            // The user's own sentence holds the words they typed; what the code adds (the pending note) never does.
            Assert.DoesNotContain(alias, context.PendingNote ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal(ArabicText.Normalize(Masker.Mask(Say(Amb19, 1)).Text), ArabicText.Normalize(text));
        }

        Assert.Equal(saved.Alias.Text, conversation.Card?.DisplayName);
    }

    [Fact]
    public async Task AFailingScopeGivesTheDirectoryReadUnavailableAndATracedError()
    {
        // Security review M1: a failure before the wallet is asked (here, the scope itself) is an unavailable read with
        // a traced error, never an exception that would end the phone's circuit.
        var trace = new ConversationHarness.RecordingTrace();
        var options = new ServiceCollection().AddOptions<AgentOptions>().Services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AgentOptions>>();
        using var conversation = new AgentConversation(
            new ThrowingScopes(), new CapturingParser(new Dictionary<string, ParsedRequest>()), TimeProvider.System, options, trace, U1);

        var read = await conversation.ListAliasesAsync();

        var unavailable = Assert.IsType<WalletResult<WalletContacts>.Unavailable>(read);
        Assert.Equal("error", unavailable.Reason);
        Assert.Contains(trace.Entries, e => e.Step == "error" && e.Detail == nameof(InvalidOperationException));
        Assert.False(conversation.Busy);
    }

    [Fact]
    public async Task CancelOnTheOfferClosesItSavesNothingAndTheNextRequestAsksAgain()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        var before = await ContactsAsync();
        await PayS08Async(conversation, Say(Amb19, 0));
        Assert.NotNull(conversation.AliasOffer);
        var walletVersion = conversation.WalletVersion;
        var auditRows = (await harness.AuditRowsAsync()).Count;

        Assert.True(await conversation.DeclineAliasOfferAsync());

        Assert.Null(conversation.AliasOffer);
        Assert.Null(conversation.AliasOfferMessageIndex);
        var last = conversation.Messages[^1];
        Assert.Equal(ConversationRole.Agent, last.Role);
        Assert.Equal(ConversationKind.Refusal, last.Kind);
        Assert.Equal(ReasonCodes.QuestionCancelled, last.Code);
        Assert.Equal(walletVersion, conversation.WalletVersion);
        Assert.Equal(before.Aliases, (await ContactsAsync()).Aliases);
        // The same refusal row as a question's cancel (Khalil, 22 Oct 2026): every tap that ends something leaves a row.
        var rows = await harness.AuditRowsAsync();
        Assert.Equal(auditRows + 1, rows.Count);
        Assert.Equal(AuditEvents.Refused, rows[^1].Event);
        Assert.Equal(ReasonCodes.QuestionCancelled, rows[^1].Reason);
        Assert.Null(rows[^1].Detail);
        Assert.Single(harness.Trace.Entries, e => e.Step == TraceSteps.DeclineAliasTapped && e.Zone == TraceZone.User && e.Detail is null);

        // A second tap has no offer to close: nothing is added.
        var messages = conversation.Messages.Count;
        Assert.False(await conversation.DeclineAliasOfferAsync());
        Assert.Equal(messages, conversation.Messages.Count);

        // Save after the Cancel has no offer to save.
        Assert.Null(await conversation.SaveAliasAsync());
        Assert.Equal(messages, conversation.Messages.Count);
        Assert.Equal(before.Aliases, (await ContactsAsync()).Aliases);

        // Close only: the same recipient again asks again.
        await conversation.SendAsync(Say(Amb19, 1));
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal(QuestionKinds.WhichContact, question.Kind);
    }

    private static string Say(string id, int step) => DevSetText.Say(id, step);

    /// <summary>A scope factory whose every scope fails, as a torn-down circuit's provider would.</summary>
    private sealed class ThrowingScopes : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("no scope");
    }

    /// <summary>The sentence, the tap on S08 and the confirm (steps 0 to 2 of AMB-19), ending Done.</summary>
    private static async Task PayS08Async(AgentConversation conversation, string sentence)
    {
        await conversation.SendAsync(sentence);
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Contains("S08", question.Options.Select(o => o.Value));
        await conversation.TapOptionAsync("S08");
        Assert.IsType<CardView>(conversation.Card);
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(null));
    }

    private static WalletContact S08(WalletContacts contacts) => Assert.Single(contacts.Contacts, c => c.Code == "S08");

    private static int S08AccountId(WalletContacts contacts) => S08(contacts).TargetAccountId;

    private async Task<WalletContacts> ContactsAsync()
    {
        await using var scope = host.Services.CreateAsyncScope();
        var read = await scope.ServiceProvider.GetRequiredService<WalletClient>().GetContactsAsync(U1, Token);
        return Assert.IsType<WalletResult<WalletContacts>.Ok>(read).Value;
    }

    private async Task<PendingRecord?> RecordAsync(Guid pendingId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PendingStore>().FindAsync(pendingId, U1, Token);
    }

    /// <summary>Records every text and context handed to the parse call, then answers as <paramref name="inner"/> does.</summary>
    private sealed class RecordingParser(IParser inner) : IParser
    {
        private readonly ConcurrentQueue<(string Text, ParseContext Context)> calls = new();

        public string Name => inner.Name;

        public IReadOnlyList<(string Text, ParseContext Context)> Calls => [.. calls];

        public Task<ParsedMessage> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken)
        {
            calls.Enqueue((text, context));
            return inner.ParseAsync(text, context, cancellationToken);
        }
    }
}
