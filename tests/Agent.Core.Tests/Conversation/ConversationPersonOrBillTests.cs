using System.Collections.Concurrent;
using Agent.Core.Aliases;
using Agent.Core.Audit;
using Agent.Core.Confirm;
using Agent.Core.Conversation;
using Agent.Core.Parsing;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Resolver;
using Agent.Core.Tests.Wallet;
using Agent.Core.Trace;
using Agent.Core.Wallet;
using Microsoft.Extensions.DependencyInjection;
using AgentConversation = Agent.Core.Conversation.Conversation;

namespace Agent.Core.Tests.Conversation;

/// <summary>
/// Person or bill on the conversation (Khalil, 20 Oct 2026): CLN-10 on the oracle after BS2's label is saved as an alias
/// of S04, so the name the user typed is both a contact and a bill. The agent asks <c>person_or_bill</c>; a tap on the
/// bill gives the bill card, a tap on the person the transfer path, cancel clears the request. The sentence comes by
/// case id, every name and label from the seed or the wallet.
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class ConversationPersonOrBillTests(AgentHost host)
{
    private const int U1 = SeedFacts.DemoUserId;
    private const string Cln10 = "CLN-10";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string DevSetPath => Path.Combine(SeedHashTests.RepositoryRoot(), "data", ScriptedParser.DevSetFileName);

    private static string Bs2Label => SeedWallet.LabelOf("BS2");

    private async Task<ConversationHarness> StartAsync(IParser? parser = null)
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var harness = await ConversationHarness.StartAsync(host, parser ?? ScriptedParser.Load(DevSetPath), U1);

        // The alias that makes BS2's label a person too, saved through the agent's alias directory.
        await using var scope = harness.CreateScope();
        var read = await scope.ServiceProvider.GetRequiredService<WalletClient>().GetContactsAsync(U1, Token);
        var s04 = Assert.Single(Assert.IsType<WalletResult<WalletContacts>.Ok>(read).Value.Contacts, c => c.Code == "S04");
        Assert.IsType<AliasResult.Saved>(
            await scope.ServiceProvider.GetRequiredService<AliasDirectory>().SaveAsync(U1, Bs2Label, s04.TargetAccountId, Token));
        return harness;
    }

    private static AgentReply.Question AssertPersonOrBill(AgentConversation conversation)
    {
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal(QuestionKinds.PersonOrBill, question.Kind);
        Assert.Equal(QuestionAnswer.Tap, question.Answer);
        Assert.Equal(["S04", "BS2"], question.Options.Select(o => o.Value));
        Assert.Null(conversation.Card);
        return question;
    }

    [Fact]
    public async Task Cln10AsksPersonOrBillAndTheBillTapGivesTheBs2Card()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say(Cln10));

        AssertPersonOrBill(conversation);
        Assert.Contains(harness.Trace.Entries, e => e.Step == TraceSteps.QuestionPrefix + QuestionKinds.PersonOrBill);
        Assert.Contains(await harness.AuditRowsAsync(), r => r.Event == AuditEvents.QuestionAsked && r.Reason == QuestionKinds.PersonOrBill);
        Assert.Empty(await harness.LearningRowsAsync());

        await conversation.TapOptionAsync("BS2");

        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(("bill", "BS2"), (card.Type, card.BillCode));
        Assert.Equal(SeedFacts.Seed.Bills.Single(b => b.Id == "BS2").AmountDue, card.AmountIqd);
        Assert.Null(conversation.Question);
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(null));
    }

    [Fact]
    public async Task ThePersonTapGivesTheTransferPathAndATypedAmountItsCard()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        await conversation.SendAsync(DevSetText.Say(Cln10));
        AssertPersonOrBill(conversation);

        await conversation.TapOptionAsync("S04");

        // CLN-10 names no amount: the transfer asks for it, typed.
        var amount = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal((QuestionKinds.WhichAmount, QuestionAnswer.Typed), (amount.Kind, amount.Answer));
        Assert.Null(conversation.Card);

        await conversation.SendAsync("50000");

        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(("transfer", 50_000L), (card.Type, card.AmountIqd));
        Assert.Equal(SeedFacts.Contact("S04").Number, card.ToPhone);
        Assert.Null(card.BillCode);
    }

    [Fact]
    public async Task CancelClearsTheRequest()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        await conversation.SendAsync(DevSetText.Say(Cln10));
        AssertPersonOrBill(conversation);

        await conversation.DeclineQuestionAsync();

        Assert.Null(conversation.Question);
        Assert.Null(conversation.Card);
        Assert.Equal(new AgentReply.Refusal(ReasonCodes.QuestionCancelled, null, null), conversation.LastReply);
        Assert.Equal(Option.CancelQuestion, conversation.Messages.Last(m => m.Kind == ConversationKind.Option).Code);
        Assert.Contains(harness.Trace.Entries, e => e.Step == "question_cancelled" && e.Detail == QuestionKinds.PersonOrBill);
    }

    [Fact]
    public async Task ThePendingNoteNamesOnlyTheQuestionAndNoNameOrCodeReachesTheParser()
    {
        var parser = new RecordingParser(ScriptedParser.Load(DevSetPath));
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;
        await conversation.SendAsync(DevSetText.Say(Cln10));
        AssertPersonOrBill(conversation);

        // A message the parser cannot read while the question is open: the question is asked again.
        await conversation.SendAsync("hello there");

        AssertPersonOrBill(conversation);
        var (text, context) = parser.Calls[^1];
        Assert.Equal("hello there", text);
        Assert.Equal("open_question " + QuestionKinds.PersonOrBill, context.PendingNote);

        var s04 = SeedFacts.Contact("S04");
        string[] fetched = ["S04", "BS2", s04.SavedName, s04.Number, SeedFacts.Account(s04.Number).RegisteredName!];
        foreach (var call in parser.Calls)
        {
            foreach (var value in fetched)
            {
                Assert.DoesNotContain(value, call.Text, StringComparison.Ordinal);
                Assert.DoesNotContain(value, call.Context.PendingNote ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Saves BS1's label as U1's alias of S05: a second name that is both a person and a bill.</summary>
    private async Task SaveBs1LabelOnS05Async(ConversationHarness harness)
    {
        await using var scope = harness.CreateScope();
        var read = await scope.ServiceProvider.GetRequiredService<WalletClient>().GetContactsAsync(U1, Token);
        var s05 = Assert.Single(Assert.IsType<WalletResult<WalletContacts>.Ok>(read).Value.Contacts, c => c.Code == "S05");
        Assert.IsType<AliasResult.Saved>(await scope.ServiceProvider.GetRequiredService<AliasDirectory>()
            .SaveAsync(U1, SeedWallet.LabelOf("BS1"), s05.TargetAccountId, Token));
    }

    [Fact]
    public async Task ARecipientTypedAfterThePersonTapIsAFreshNameAndAsksAgain()
    {
        // Security review M2: after the person tap, a recipient-only message naming another clashing name clears the
        // earlier answer, so the check runs again on the new name.
        const string other = "the other one";
        var parser = new OverlayParser(
            ScriptedParser.Load(DevSetPath),
            new() { [other] = ParsedMessage.One(ParsedRequest.Transfer(SeedWallet.LabelOf("BS1"), null, AmountUnit.None)) });
        await using var harness = await StartAsync(parser);
        await SaveBs1LabelOnS05Async(harness);
        var conversation = harness.Conversation;
        await conversation.SendAsync(DevSetText.Say(Cln10));
        AssertPersonOrBill(conversation);
        await conversation.TapOptionAsync("S04");
        Assert.Equal(QuestionKinds.WhichAmount, conversation.Question?.Kind);

        await conversation.SendAsync(other);

        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal(QuestionKinds.PersonOrBill, question.Kind);
        Assert.Equal(["S05", "BS1"], question.Options.Select(o => o.Value));
        Assert.Equal(new SlotAnswers(), conversation.LastAnswers);
        Assert.Null(conversation.Card);
    }

    [Fact]
    public async Task ACompoundMessageWaitsOnPersonOrBillAndTheSecondPartRunsAfterTheTap()
    {
        // Security review R2: the first part asks; the second part waits until the tap's card is done.
        const string both = "pay the shop and send hammoudi fifty";
        var parser = new OverlayParser(
            ScriptedParser.Load(DevSetPath),
            new()
            {
                [both] = new ParsedMessage(
                [
                    ParsedRequest.Bill("electricity", Bs2Label, null, AmountUnit.None),
                    ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand),
                ]),
            });
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await conversation.SendAsync(both);

        AssertPersonOrBill(conversation);
        await conversation.TapOptionAsync("BS2");
        Assert.Equal(("bill", "BS2"), (conversation.Card?.Type, conversation.Card?.BillCode));

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(null));

        var transfer = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(("transfer", 50_000L, SeedFacts.HammoudiPhone), (transfer.Type, transfer.AmountIqd, transfer.ToPhone));
    }

    [Fact]
    public async Task ANewRequestWhileTheQuestionIsOpenReplacesItWithNoAnswerCarried()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        await conversation.SendAsync(DevSetText.Say(Cln10));
        AssertPersonOrBill(conversation);

        await conversation.SendAsync(DevSetText.Say("CLN-01"));

        Assert.Null(conversation.Question);
        Assert.Equal(SlotAnswers.None, conversation.LastAnswers);
        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(("transfer", SeedFacts.HammoudiPhone), (card.Type, card.ToPhone));
    }

    [Fact]
    public async Task ATapWithAValueFromAnEarlierQuestionIsRejected()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        await conversation.SendAsync(DevSetText.Say("AMB-19"));
        Assert.Contains("S08", Assert.IsType<AgentReply.Question>(conversation.Question).Options.Select(o => o.Value));
        await conversation.DeclineQuestionAsync();
        await conversation.SendAsync(DevSetText.Say(Cln10));
        AssertPersonOrBill(conversation);

        await conversation.TapOptionAsync("S08");

        Assert.Contains(harness.Trace.Entries, e => e.Step == "option_rejected");
        AssertPersonOrBill(conversation);
    }

    /// <summary>Answers the texts it holds with their message, and every other text as <paramref name="inner"/> does.</summary>
    private sealed class OverlayParser(IParser inner, Dictionary<string, ParsedMessage> replies) : IParser
    {
        public string Name => inner.Name;

        public Task<ParsedMessage> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken)
            => replies.TryGetValue(text, out var message) ? Task.FromResult(message) : inner.ParseAsync(text, context, cancellationToken);
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
