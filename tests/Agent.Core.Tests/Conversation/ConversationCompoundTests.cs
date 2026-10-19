using System.Net.Http.Json;
using Agent.Core.Confirm;
using Agent.Core.Conversation;
using Agent.Core.Data;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Agent.Core.Trace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.MockApi.Contracts;
using AgentConversation = Agent.Core.Conversation.Conversation;

namespace Agent.Core.Tests.Conversation;

/// <summary>
/// D7 and rule 21 (milestone 9c): a message with several requests runs one part at a time, in the order spoken. The next
/// part runs only after the previous is finished (done, not done, cancelled or expired, or a refusal or reply that needs
/// no tap), each part's checks run against the balance when its turn comes, and a new request drops the rest. The
/// compound cases of the development set run on the oracle (<see cref="DevSetText"/>); the other inputs of the review
/// focus run on a <see cref="CapturingParser"/> with English sentences.
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class ConversationCompoundTests(AgentHost host)
{
    private const int U1 = SeedFacts.DemoUserId;

    private static readonly Lazy<ScriptedParser> OracleParser = new(
        () => ScriptedParser.Load(Path.Combine(SeedHashTests.RepositoryRoot(), "data", ScriptedParser.DevSetFileName)));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string HammoudiAlias => SeedFacts.Contact("S01").Alias!;

    private async Task<ConversationHarness> StartAsync(IParser? parser = null, int userId = U1)
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        return await ConversationHarness.StartAsync(host, parser ?? OracleParser.Value, userId);
    }

    /// <summary>A parser that answers each sentence (masked as typed) with its requests, in order.</summary>
    private static CapturingParser Says(params (string Text, ParsedRequest[] Requests)[] says)
        => new(says.ToDictionary(s => Masker.Mask(s.Text).Text, s => new ParsedMessage(s.Requests), StringComparer.Ordinal));

    private static bool Traced(ConversationHarness harness, string step, string? detail)
        => harness.Trace.Entries.Any(e => e.Step == step && e.Detail == detail);

    private static int CountOf(AgentConversation conversation, ConversationKind kind, string? code = null)
        => conversation.Messages.Count(m => m.Kind == kind && (code is null || m.Code == code));

    private async Task<PendingState> StateOfAsync(ConversationHarness harness, Guid pendingId)
    {
        await using var scope = harness.CreateScope();
        var record = await scope.ServiceProvider.GetRequiredService<PendingStore>().FindAsync(pendingId, U1, Token);
        return Assert.IsType<PendingRecord>(record).State;
    }

    private Task<long> BalanceAsync() => host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone);

    private static CardView LiveTransferTo(AgentConversation conversation, string contact, long amountIqd)
    {
        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal(("transfer", SeedFacts.Contact(contact).Number, amountIqd), (card.Type, card.ToPhone, card.AmountIqd));
        return card;
    }

    [Fact]
    public async Task CMP01_TwoCardsInTheOrderSpokenOneTapEach()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-01"));

        var first = LiveTransferTo(conversation, "S04", 25_000);
        Assert.Equal(1, conversation.QueuedRequests);
        Assert.True(Traced(harness, TraceSteps.RequestsSplit, "2 requests, 2 kept"));

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));

        Assert.Equal(2, conversation.CardsIssued.Count);
        Assert.Equal(first, conversation.CardsIssued[0]);
        var second = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(("purchase", "AC-CR-5"), (second.Type, second.ProductCode));
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal(0, conversation.QueuedRequests);
        Assert.True(Traced(harness, TraceSteps.NextRequest, "2 of 2"));

        await conversation.CancelAsync();

        Assert.Equal(CardStatus.Cancelled, conversation.CardState);
        Assert.Equal(2, conversation.CardsIssued.Count);
        Assert.Equal(724_000, await BalanceAsync());
    }

    [Fact]
    public async Task CMP03_AnExpiredCardStillLeadsToTheNext()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-03"));
        var first = LiveTransferTo(conversation, "S18", 20_000);

        host.Clock.Advance(first.ExpiresAt - host.Clock.GetUtcNow() + TimeSpan.FromSeconds(1));
        await using (var scope = harness.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PendingStore>().ExpireDueAsync(Token);
        await conversation.RefreshAsync();

        Assert.Equal(PendingState.Expired, await StateOfAsync(harness, first.PendingId));
        var second = LiveTransferTo(conversation, "S19", 25_000);
        Assert.Equal(724_000, second.BalanceAfterIqd);

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        Assert.Equal(724_000, await BalanceAsync());
    }

    [Fact]
    public async Task CMP05_AQuestionIsAskedWhenItsPartComesAndTheSecondWaits()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-05"));

        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal(QuestionKinds.WhichBill, question.Kind);
        Assert.Equal(["BS1", "BS2"], question.Options.Select(o => o.Value).Order(StringComparer.Ordinal));
        Assert.Null(conversation.Card);
        Assert.Equal(1, conversation.QueuedRequests);

        await conversation.TapOptionAsync("BS2");
        var bill = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(("bill", "BS2"), (bill.Type, bill.BillCode));
        Assert.Equal(1, conversation.QueuedRequests);

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        var transfer = LiveTransferTo(conversation, "S01", 50_000);
        Assert.Equal(571_000, transfer.BalanceAfterIqd);

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        Assert.Equal(571_000, await BalanceAsync());
    }

    [Fact]
    public async Task CMP09_TheBalanceIsAnsweredThenTheCardIsBuilt()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-09"));

        var messages = conversation.Messages;
        var balance = messages.Select((m, i) => (m, i)).Single(x => x.m.Kind == ConversationKind.Balance);
        Assert.Equal(SeedFacts.DemoBalance, balance.m.AmountIqd);
        LiveTransferTo(conversation, "S15", 50_000);
        Assert.True(balance.i < conversation.CardMessageIndex);

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        Assert.Equal(699_000, await BalanceAsync());
    }

    [Fact]
    public async Task APartRefusedRunsOnToTheNextInTheSameAction()
    {
        const string sentence = "send ten thousand to zzyzx and fifty thousand to my brother";
        var parser = Says((sentence,
        [
            ParsedRequest.Transfer("zzyzx", 10, AmountUnit.Thousand),
            ParsedRequest.Transfer(HammoudiAlias, 50, AmountUnit.Thousand),
        ]));
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await conversation.SendAsync(sentence);

        Assert.Equal(1, CountOf(conversation, ConversationKind.Refusal, ReasonCodes.RecipientNotFound));
        Assert.Contains(await harness.LearningRowsAsync(), r => r.Reason == ReasonCodes.RecipientNotFound);
        LiveTransferTo(conversation, "S01", 50_000);
        Assert.True(
            conversation.Messages.ToList().FindIndex(m => m.Code == ReasonCodes.RecipientNotFound) < conversation.CardMessageIndex);
        Assert.Equal(0, conversation.QueuedRequests);
        Assert.True(Traced(harness, TraceSteps.NextRequest, "2 of 2"));
    }

    [Fact]
    public async Task AFailedCardStillLeadsToTheNext()
    {
        const string sentence = "send two hundred thousand to my brother and twenty five thousand to my sister";
        var parser = Says((sentence,
        [
            ParsedRequest.Transfer(HammoudiAlias, 200, AmountUnit.Thousand),
            ParsedRequest.Transfer(SeedFacts.Contact("S19").SavedName, 25, AmountUnit.Thousand),
        ]));
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await conversation.SendAsync(sentence);
        var first = LiveTransferTo(conversation, "S01", 200_000);

        // Money leaves the wallet from outside while card 1 waits, so the recheck at Confirm refuses it.
        using (var debit = await host.Wallet.Client.PostAsJsonAsync("/admin/debit", new DebitRequest(U1, 600_000), Token))
            Assert.True(debit.IsSuccessStatusCode, debit.StatusCode.ToString());

        var outcome = Assert.IsType<ConfirmOutcome.NotDone>(await conversation.ConfirmAsync(null));
        Assert.Equal(ReasonCodes.InsufficientBalance, outcome.Reason);
        Assert.Equal(PendingState.NotDone, await StateOfAsync(harness, first.PendingId));

        // Card 2 is built against the balance then (rule 21).
        var second = LiveTransferTo(conversation, "S19", 25_000);
        Assert.Equal(150_000 - second.TotalIqd, second.BalanceAfterIqd);
        Assert.Equal(150_000, await BalanceAsync());
    }

    [Fact]
    public async Task ACancelledCardStillLeadsToTheNext()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-01"));
        var first = LiveTransferTo(conversation, "S04", 25_000);

        await conversation.SendAsync(AgentHost.TestNoWord);

        Assert.Equal(PendingState.Cancelled, await StateOfAsync(harness, first.PendingId));
        var second = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(("purchase", CardStatus.Live), (second.Type, conversation.CardState));
        Assert.Equal(SeedFacts.DemoBalance, await BalanceAsync());
    }

    [Fact]
    public async Task ACancelledQuestionStillLeadsToTheNext()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-05"));
        Assert.Equal(QuestionKinds.WhichBill, Assert.IsType<AgentReply.Question>(conversation.Question).Kind);

        await conversation.DeclineQuestionAsync();

        Assert.Equal(1, CountOf(conversation, ConversationKind.Refusal, ReasonCodes.QuestionCancelled));
        Assert.Null(conversation.Question);
        LiveTransferTo(conversation, "S01", 50_000);
        Assert.Equal(0, conversation.QueuedRequests);
    }

    [Fact]
    public async Task ANewMessageWhileACardWaitsDropsTheRest()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-01"));
        var first = LiveTransferTo(conversation, "S04", 25_000);

        await conversation.SendAsync(DevSetText.Say("CLN-01"));

        Assert.Equal(PendingState.Cancelled, await StateOfAsync(harness, first.PendingId));
        Assert.True(Traced(harness, TraceSteps.RequestsDropped, "1"));
        Assert.Equal(0, conversation.QueuedRequests);
        LiveTransferTo(conversation, "S01", 50_000);

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));

        Assert.Equal(2, conversation.CardsIssued.Count);
        Assert.Equal(CardStatus.Done, conversation.CardState);
        Assert.Equal(699_000, await BalanceAsync());
    }

    [Fact]
    public async Task AYesAnUnclearMessageAndAnAnswerKeepTheQueue()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-01"));
        var first = LiveTransferTo(conversation, "S04", 25_000);

        await conversation.SendAsync(AgentHost.TestYesWord);
        Assert.Equal(ReasonCodes.PointsToConfirm, conversation.Messages[^1].Code);
        Assert.Equal(1, conversation.QueuedRequests);

        await conversation.SendAsync("zzz qqq");
        Assert.Equal((first, CardStatus.Live), (conversation.Card, conversation.CardState));
        Assert.Equal(1, conversation.QueuedRequests);
        Assert.DoesNotContain(harness.Trace.Entries, e => e.Step == TraceSteps.RequestsDropped);
    }

    [Fact]
    public async Task AnAnswerReadInCodeKeepsTheQueue()
    {
        const string sentence = "send to my brother and twenty five thousand to my sister";
        var parser = Says((sentence,
        [
            ParsedRequest.Transfer(HammoudiAlias, null, AmountUnit.None),
            ParsedRequest.Transfer(SeedFacts.Contact("S19").SavedName, 25, AmountUnit.Thousand),
        ]));
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await conversation.SendAsync(sentence);
        Assert.Equal(QuestionKinds.WhichAmount, Assert.IsType<AgentReply.Question>(conversation.Question).Kind);
        Assert.Equal(1, conversation.QueuedRequests);

        await conversation.SendAsync("30000");

        Assert.Contains(harness.Trace.Entries, e => e.Step == "answer_parsed");
        LiveTransferTo(conversation, "S01", 30_000);
        Assert.Equal(1, conversation.QueuedRequests);
        Assert.Single(parser.Calls);

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        LiveTransferTo(conversation, "S19", 25_000);
    }

    [Fact]
    public async Task TheConsentScreenOnPartOneLosesNothing()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        // The plain reset leaves every user not consented.
        using (var reset = await host.Wallet.Client.PostAsync("/admin/reset", null, Token))
            Assert.True(reset.IsSuccessStatusCode, reset.StatusCode.ToString());

        await conversation.SendAsync(DevSetText.Say("CMP-01"));

        Assert.IsType<AgentReply.ConsentScreen>(conversation.LastReply);
        Assert.Null(conversation.Card);
        Assert.Equal(1, conversation.QueuedRequests);

        await conversation.GiveConsentAsync();
        LiveTransferTo(conversation, "S04", 25_000);
        Assert.Equal(1, conversation.QueuedRequests);

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        Assert.Equal("purchase", Assert.IsType<CardView>(conversation.Card).Type);
        Assert.Equal(CardStatus.Live, conversation.CardState);
    }

    [Fact]
    public async Task AnAmountRefusalOnAMiddlePartDoesNotOpenTheRetry()
    {
        const string sentence = "send six million to my brother and what is my balance";
        var parser = Says((sentence,
        [
            ParsedRequest.Transfer(HammoudiAlias, 6, AmountUnit.Million),
            ParsedRequest.Balance(),
        ]));
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await conversation.SendAsync(sentence);

        Assert.Equal(1, CountOf(conversation, ConversationKind.Refusal, ReasonCodes.OverPerTransactionCap));
        Assert.Equal(1, CountOf(conversation, ConversationKind.Balance));

        await conversation.SendAsync("30000");

        Assert.DoesNotContain(harness.Trace.Entries, e => e.Step == "amount_retry");
        Assert.Null(conversation.Card);
        Assert.Equal(2, parser.Calls.Count);
    }

    [Fact]
    public async Task TheQueueNeverExecutes()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-01"));

        Assert.Equal(1, conversation.QueuedRequests);
        await using (var scope = harness.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            var records = await db.PendingTransactions.AsNoTracking().ToListAsync(Token);
            Assert.Equal(PendingState.Pending, Assert.Single(records).State);
        }

        Assert.Equal(SeedFacts.DemoBalance, await BalanceAsync());
    }
}
