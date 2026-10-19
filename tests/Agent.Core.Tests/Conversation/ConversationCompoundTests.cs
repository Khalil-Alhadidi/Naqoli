using System.Net.Http.Json;
using Agent.Core.Confirm;
using Agent.Core.Conversation;
using Agent.Core.Data;
using Agent.Core.Learning;
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

    // ARABIC LETTER ALEF, LAM, AIN and MEEM: a synthetic name with al, and its lam form (no Arabic literal in C#).
    private const char Alef = '\u0627';
    private const char Lam = '\u0644';
    private const char Ain = '\u0639';
    private const char Meem = '\u0645';

    private async Task<ConversationHarness> StartAsync(IParser? parser = null, int userId = U1, Action<AgentOptions>? configure = null)
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        return await ConversationHarness.StartAsync(host, parser ?? OracleParser.Value, userId, configure);
    }

    /// <summary>The <c>max_three_requests</c> replies so far (rule 22).</summary>
    private static List<ConversationMessage> NotPreparedReplies(AgentConversation conversation)
        => [.. conversation.Messages.Where(m => m.Kind == ConversationKind.Info && m.Code == ReasonCodes.MaxThreeRequests)];

    private static ParsedRequest TransferOf(string recipient) => ParsedRequest.Transfer(recipient, 10, AmountUnit.Thousand);

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

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(null));

        Assert.Equal(2, conversation.CardsIssued.Count);
        Assert.Equal(first, conversation.CardsIssued[0]);
        var second = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(("purchase", "AC-CR-5"), (second.Type, second.ProductCode));
        Assert.Equal(CardStatus.Live, conversation.CardState);
        Assert.Equal(0, conversation.QueuedRequests);
        Assert.True(Traced(harness, TraceSteps.NextRequest, "2 of 2"));

        await conversation.CancelShownAsync();

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

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(null));
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

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(null));
        var transfer = LiveTransferTo(conversation, "S01", 50_000);
        Assert.Equal(571_000, transfer.BalanceAfterIqd);

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(null));
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

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(null));
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

        var outcome = Assert.IsType<ConfirmOutcome.NotDone>(await conversation.ConfirmShownAsync(null));
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

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(null));

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

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(null));
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

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync(null));
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

    [Fact]
    public async Task CMP07_FourRequestsGiveThreeCardsAndTheReplyNamesTheFourth()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-07"));
        Assert.True(Traced(harness, TraceSteps.RequestsSplit, "4 requests, 3 kept"));
        Assert.Equal(2, conversation.QueuedRequests);

        foreach (var contact in new[] { "S04", "S19", "S18" })
        {
            LiveTransferTo(conversation, contact, 10_000);
            Assert.Empty(NotPreparedReplies(conversation));
            Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync());
        }

        // After the third Done outcome: the reply that names the fourth part, in the user's own words (rule 22).
        var reply = Assert.Single(NotPreparedReplies(conversation));
        Assert.Equal(reply, conversation.Messages[^1]);
        var part = Assert.Single(reply.NotPrepared!);
        Assert.Equal((RequestIntent.Transfer, DevSetText.NotPrepared("CMP-07")), (part.Intent, part.Text));
        Assert.Equal(new LearningEntry(LearningLogRule.MoreThanThreeRequests, "4"), conversation.LastLearningEntry);
        var row = Assert.Single(await harness.LearningRowsAsync(), r => r.Reason == LearningLogRule.MoreThanThreeRequests);
        Assert.Equal("4", row.Detail);
        Assert.True(Traced(harness, "learning_logged", LearningLogRule.MoreThanThreeRequests));
        Assert.Equal(3, conversation.CardsIssued.Count);
        Assert.Equal(717_000, await BalanceAsync());
    }

    [Fact]
    public async Task ACapOfOneNamesTheSecondPart()
    {
        await using var harness = await StartAsync(configure: o => o.MaxRequestsPerMessage = 1);
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-01"));
        LiveTransferTo(conversation, "S04", 25_000);
        Assert.Equal(0, conversation.QueuedRequests);
        Assert.True(Traced(harness, TraceSteps.RequestsSplit, "2 requests, 1 kept"));
        Assert.Empty(NotPreparedReplies(conversation));

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync());

        var part = Assert.Single(Assert.Single(NotPreparedReplies(conversation)).NotPrepared!);
        Assert.Equal((RequestIntent.Purchase, DevSetText.SidecarText("CMP-01", 0, 1, "operator_text")), (part.Intent, part.Text));
        Assert.Equal(new LearningEntry(LearningLogRule.MoreThanThreeRequests, "2"), conversation.LastLearningEntry);
        Assert.Single(conversation.CardsIssued);
    }

    [Fact]
    public async Task FivePartsAtTheCapOfThreeNameTwoInOrder()
    {
        var first = SeedFacts.Contact("S04").SavedName;
        var second = SeedFacts.Contact("S19").SavedName;
        var sentence = "send ten thousand each to " + HammoudiAlias + ", " + first + ", " + second + ", zaid and huda";
        var parser = Says((sentence,
            [TransferOf(HammoudiAlias), TransferOf(first), TransferOf(second), TransferOf("zaid"), TransferOf("huda")]));
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await conversation.SendAsync(sentence);
        for (var card = 0; card < 3; card++)
            Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync());

        var reply = Assert.Single(NotPreparedReplies(conversation));
        Assert.Equal(["zaid", "huda"], reply.NotPrepared!.Select(p => p.Text));
        Assert.Equal("5", conversation.LastLearningEntry?.Detail);
        Assert.Equal(3, conversation.CardsIssued.Count);
    }

    [Fact]
    public async Task ANameTheUserDidNotTypeIsReplacedByTheType()
    {
        // Rules 3 and 4: the parser's recipient is not in the typed text, so no text of it reaches the reply.
        const string sentence = "send ten thousand to my brother and to the other one";
        var parser = Says((sentence, [TransferOf(HammoudiAlias), TransferOf("omar")]));
        await using var harness = await StartAsync(parser, configure: o => o.MaxRequestsPerMessage = 1);
        var conversation = harness.Conversation;

        await conversation.SendAsync(sentence);
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync());

        var part = Assert.Single(Assert.Single(NotPreparedReplies(conversation)).NotPrepared!);
        Assert.Equal((RequestIntent.Transfer, (string?)null), (part.Intent, part.Text));
    }

    [Fact]
    public async Task ANameTypedWithTheLamJoinedIsTheUsersOwnWords()
    {
        // Ruling 2 as amended (19 Oct 2026): "to the X" is typed lam, then X without the alef of al; X is still the
        // user's own words. The text the parser returns, X with its al, is shown.
        var name = new string([Alef, Lam, Ain, Meem]);
        var typedName = new string([Lam, Lam, Ain, Meem]);
        var sentence = "send ten thousand to " + HammoudiAlias + " and ten thousand " + typedName;
        var parser = Says((sentence, [TransferOf(HammoudiAlias), TransferOf(name)]));
        await using var harness = await StartAsync(parser, configure: o => o.MaxRequestsPerMessage = 1);
        var conversation = harness.Conversation;

        await conversation.SendAsync(sentence);
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync());

        var part = Assert.Single(Assert.Single(NotPreparedReplies(conversation)).NotPrepared!);
        Assert.Equal(name, part.Text);
    }

    [Fact]
    public async Task TheReplyIsNotShownWhenANewMessageDropsTheRest()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-07"));
        await conversation.SendAsync(DevSetText.Say("CLN-01"));
        Assert.True(Traced(harness, TraceSteps.RequestsDropped, "2"));

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync());

        Assert.Empty(NotPreparedReplies(conversation));
        Assert.DoesNotContain(await harness.LearningRowsAsync(), r => r.Reason == LearningLogRule.MoreThanThreeRequests);
        Assert.Equal(2, conversation.CardsIssued.Count);
    }

    [Fact]
    public async Task TwoConfirmsOnCardOneExecuteOnceAndNeverReachCardTwo()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-01"));
        var first = LiveTransferTo(conversation, "S04", 25_000).PendingId;

        var outcomes = await Task.WhenAll(conversation.ConfirmAsync(first, null), conversation.ConfirmAsync(first, null));

        Assert.Single(outcomes, o => o is ConfirmOutcome.Done);
        Assert.Single(outcomes, o => o is null);
        Assert.Equal(1, CountOf(conversation, ConversationKind.Outcome, OutcomeCodes.AlreadyHandled));
        Assert.True(Traced(harness, TraceSteps.TapRejected, "stale_card"));
        Assert.Equal(("purchase", CardStatus.Live), (conversation.Card?.Type, conversation.CardState));
        Assert.Equal(724_000, await BalanceAsync());
    }

    [Fact]
    public async Task AConfirmWithAStaleIdIsAlreadyHandledAndMovesNothing()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("CMP-01"));
        var first = LiveTransferTo(conversation, "S04", 25_000).PendingId;
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync());
        var second = Assert.IsType<CardView>(conversation.Card);

        Assert.Null(await conversation.ConfirmAsync(first, null));
        Assert.Equal(OutcomeCodes.AlreadyHandled, conversation.Messages[^1].Code);
        await conversation.CancelAsync(first);
        Assert.Equal(OutcomeCodes.AlreadyHandled, conversation.Messages[^1].Code);

        Assert.Equal(2, harness.Trace.Entries.Count(e => e.Step == TraceSteps.TapRejected && e.Detail == "stale_card"));
        Assert.Equal((second, CardStatus.Live), (conversation.Card, conversation.CardState));
        Assert.Equal(PendingState.Pending, await StateOfAsync(harness, second.PendingId));
        Assert.Equal(724_000, await BalanceAsync());
    }

    [Fact]
    public async Task AConfirmWithNoCardIsRejected()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        Assert.Null(await conversation.ConfirmAsync(Guid.NewGuid(), null));
        await conversation.CancelAsync(Guid.NewGuid());

        Assert.Empty(conversation.Messages);
        Assert.Equal(2, harness.Trace.Entries.Count(e => e.Step == TraceSteps.TapRejected && e.Detail == "no_card"));
    }

    [Fact]
    public async Task ASendAgainTapWhileAPartWaitsOnItsQuestionIsRejected()
    {
        const string sentence = "send fifty thousand to my brother and pay the electricity";
        var parser = Says((sentence,
        [
            ParsedRequest.Transfer(HammoudiAlias, 50, AmountUnit.Thousand),
            ParsedRequest.Bill("electricity", null, null, AmountUnit.None),
        ]));
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await conversation.SendAsync(sentence);
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmShownAsync());
        Assert.Equal(QuestionKinds.WhichBill, Assert.IsType<AgentReply.Question>(conversation.Question).Kind);

        // The Done card still offers Send again, but the bill's question is open: the tap does nothing.
        await conversation.ReissueAsync();

        Assert.Single(conversation.CardsIssued);
        Assert.Equal(QuestionKinds.WhichBill, Assert.IsType<AgentReply.Question>(conversation.Question).Kind);
        Assert.True(Traced(harness, "reissue_rejected", "question_open"));
    }

    // ---- security review of milestone 9c ----

    /// <summary>A <see cref="AgentConversation.Changed"/> handler that throws once, on the first message <paramref name="when"/> accepts.</summary>
    private static void ThrowOnceWhen(AgentConversation conversation, Func<ConversationMessage, bool> when)
    {
        var thrown = 0;
        conversation.Changed += () =>
        {
            if (conversation.Messages.Count > 0 && when(conversation.Messages[^1]) && Interlocked.Exchange(ref thrown, 1) == 0)
                throw new InvalidOperationException("Test: a handler failed.");
        };
    }

    [Fact]
    public async Task AnExceptionOnThePartRunningEndsItsMessage()
    {
        // M1: part 1 throws while its card is shown; the message is over, so part 2 is dropped and never runs later.
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        ThrowOnceWhen(conversation, m => m.Kind == ConversationKind.Card);

        await conversation.SendAsync(DevSetText.Say("CMP-01"));

        Assert.Equal(ReasonCodes.NotDoneNothingTaken, conversation.Messages[^1].Code);
        Assert.Equal(0, conversation.QueuedRequests);
        Assert.True(Traced(harness, TraceSteps.RequestsDropped, "1"));
    }

    [Fact]
    public async Task APinLikeAnswerDoesNotSuppressTheRowsOfTheNextPart()
    {
        // M2: a six-digit answer writes no row of its own (it may be a PIN), but the next part's row is written under the
        // compound message's masked text.
        const string sentence = "send to my brother and twenty five thousand to zzyzx";
        var parser = Says((sentence,
        [
            ParsedRequest.Transfer(HammoudiAlias, null, AmountUnit.None),
            ParsedRequest.Transfer("zzyzx", 25, AmountUnit.Thousand),
        ]));
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await conversation.SendAsync(sentence);
        Assert.Equal(QuestionKinds.WhichAmount, Assert.IsType<AgentReply.Question>(conversation.Question).Kind);
        await conversation.SendAsync("900000");

        Assert.Equal(1, CountOf(conversation, ConversationKind.Refusal, ReasonCodes.InsufficientBalance));
        Assert.Equal(1, CountOf(conversation, ConversationKind.Refusal, ReasonCodes.RecipientNotFound));
        var rows = await harness.LearningRowsAsync();
        Assert.DoesNotContain(rows, r => r.Reason == ReasonCodes.InsufficientBalance);
        var row = Assert.Single(rows, r => r.Reason == ReasonCodes.RecipientNotFound);
        Assert.Equal(Masker.Mask(sentence).Text, row.MaskedText);
        Assert.Equal(ReasonCodes.RecipientNotFound, conversation.LastLearningEntry?.Reason);
    }

    [Fact]
    public async Task AConfirmWhoseFollowingReplyThrowsReturnsNoOutcomeAndKeepsThePayment()
    {
        // M3: the outcome is shown and the payment done; a throw while the "not prepared" reply is added is the action's
        // failure, never an exception out of the Confirm (it would end the phone's circuit).
        await using var harness = await StartAsync(configure: o => o.MaxRequestsPerMessage = 1);
        var conversation = harness.Conversation;
        await conversation.SendAsync(DevSetText.Say("CMP-01"));
        ThrowOnceWhen(conversation, m => m.Code == ReasonCodes.MaxThreeRequests);

        var outcome = await conversation.ConfirmShownAsync();

        Assert.Null(outcome);
        Assert.Equal(CardStatus.Done, conversation.CardState);
        Assert.Equal(ReasonCodes.NotDoneNothingTaken, conversation.Messages[^1].Code);
        Assert.Equal(724_000, await BalanceAsync());
    }
}
