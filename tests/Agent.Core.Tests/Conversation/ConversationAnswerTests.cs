using Agent.Core.Confirm;
using Agent.Core.Conversation;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Conversation;

/// <summary>
/// D3: an answer, tapped or typed, goes into the pending request instead of starting a new one (AMB-03, AMB-06, AMB-09,
/// AMB-13, AMB-19). Over the seed through <see cref="SeedFacts"/>; scripted sentences in English.
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class ConversationAnswerTests(AgentHost host)
{
    private const string SharedNameSentence = "send forty thousand to hussein";
    private const string NoAmountSentence = "send to hammoudi";
    private const string NoRecipientSentence = "send twenty thousand";
    private const string AmountOnlySentence = "thirty thousand";
    private const string RecipientOnlySentence = "to hammoudi";
    private const string OtherTransferSentence = "send fifteen thousand to my sister";
    private const string InternetSentence = "pay the internet";
    private const string ChooseAccountSentence = "send thirty thousand to hammoudi from my savings";
    private const string ChooseAccountSharedSentence = "send thirty thousand to hussein from my savings";
    private const string PartOfElectricitySentence = "pay five thousand of the electricity";

    private static ParsedRequest ChooseAccount(string recipient) => new()
    {
        Intent = RequestIntent.Unsupported,
        UnsupportedReason = "choose_account",
        RecipientText = recipient,
        AmountValue = 30,
        AmountUnit = AmountUnit.Thousand,
    };

    /// <summary>U2: no internet bill, so "pay the internet" is a store card (rule 17).</summary>
    private const int U2 = SeedFacts.TightUserId;

    private static string HammoudiAlias => SeedFacts.Contact("S01").Alias!;

    private static ScriptedParser Parser() => new(
    [
        new(SharedNameSentence, ParsedRequest.Transfer(SeedFacts.Contact("S08").SavedName, 40, AmountUnit.Thousand)),
        new(NoAmountSentence, ParsedRequest.Transfer(HammoudiAlias, null, AmountUnit.None)),
        new(NoRecipientSentence, ParsedRequest.Transfer(null, 20, AmountUnit.Thousand)),
        new(AmountOnlySentence, ParsedRequest.Transfer(null, 30, AmountUnit.Thousand)),
        new(RecipientOnlySentence, ParsedRequest.Transfer(HammoudiAlias, null, AmountUnit.None)),
        new(OtherTransferSentence, ParsedRequest.Transfer(SeedFacts.Contact("S19").SavedName, 15, AmountUnit.Thousand)),
        new(InternetSentence, ParsedRequest.Bill("internet", null, null, AmountUnit.None)),
        new(ChooseAccountSentence, ChooseAccount(HammoudiAlias)),
        new(ChooseAccountSharedSentence, ChooseAccount(SeedFacts.Contact("S08").SavedName)),
        new(PartOfElectricitySentence, new ParsedRequest
        {
            Intent = RequestIntent.Unsupported,
            UnsupportedReason = "part_of_bill",
            BillerCode = "electricity",
            AmountValue = 5,
            AmountUnit = AmountUnit.Thousand,
        }),
    ]);

    private async Task<ConversationHarness> StartAsync(int userId = SeedFacts.DemoUserId)
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        return await ConversationHarness.StartAsync(host, Parser(), userId);
    }

    [Fact]
    public async Task ATapOnOneOfTwoContactsWithTheSameNameGivesThatContactsCard()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(SharedNameSentence);
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal(QuestionKinds.WhichContact, question.Kind);
        Assert.Equal(["S08", "S09"], question.Options.Select(o => o.Value).Order(StringComparer.Ordinal));

        await conversation.TapOptionAsync("S09");

        var card = Assert.IsType<CardView>(conversation.Card);
        var s09 = SeedFacts.Contact("S09");
        Assert.Equal(s09.Number, card.ToPhone);
        Assert.Equal(s09.SavedName, card.DisplayName);
        Assert.Equal(40_000, card.AmountIqd);
        Assert.Single(conversation.QuestionsAsked);
        Assert.Equal("S09", conversation.LastAnswers.ContactOption);
        Assert.Contains(harness.Trace.Entries, e => e.Step == "recipient_resolved" && e.Detail == "option");
    }

    [Fact]
    public async Task AValueThatWasNotOfferedIsStillRejected()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        await conversation.SendAsync(SharedNameSentence);
        var before = conversation.Messages.Count;

        await conversation.TapOptionAsync("S01");

        Assert.Equal(before, conversation.Messages.Count);
        Assert.Contains(harness.Trace.Entries, e => e.Step == "option_rejected");
        Assert.Null(conversation.Card);
        Assert.NotNull(conversation.Question);
    }

    [Fact]
    public async Task ATypedAmountThatParsesAsATransferMergesIntoThePendingTransfer()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(NoAmountSentence);
        Assert.Equal(QuestionKinds.WhichAmount, Assert.IsType<AgentReply.Question>(conversation.Question).Kind);

        await conversation.SendAsync(AmountOnlySentence);

        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(SeedFacts.HammoudiPhone, card.ToPhone);
        Assert.Equal(30_000, card.AmountIqd);
        Assert.Single(conversation.QuestionsAsked);
        Assert.Equal(HammoudiAlias, conversation.LastRequest?.RecipientText);
    }

    [Fact]
    public async Task ATypedRecipientThatParsesAsATransferMergesIntoThePendingTransfer()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(NoRecipientSentence);
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal((QuestionKinds.WhichContact, QuestionAnswer.Typed), (question.Kind, question.Answer));

        await conversation.SendAsync(RecipientOnlySentence);

        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(SeedFacts.HammoudiPhone, card.ToPhone);
        Assert.Equal(20_000, card.AmountIqd);
        Assert.Single(conversation.QuestionsAsked);
    }

    [Fact]
    public async Task ACompleteTransferAfterATypedQuestionReplacesThePendingRequest()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(NoAmountSentence);
        await conversation.SendAsync(OtherTransferSentence);

        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(SeedFacts.Contact("S19").Number, card.ToPhone);
        Assert.Equal(15_000, card.AmountIqd);
    }

    [Fact]
    public async Task ChooseAccountGivesTheNoteBeforeTheCardAndConfirmMovesTheMoney()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(ChooseAccountSentence);

        var messages = conversation.Messages;
        var note = new ConversationMessage(ConversationRole.Agent, ConversationKind.Info, ReasonCodes.AccountChoiceUnavailable);
        Assert.Equal(
            [
                new ConversationMessage(ConversationRole.User, ConversationKind.Typed),
                note,
                new ConversationMessage(ConversationRole.Agent, ConversationKind.Card),
            ],
            messages);
        Assert.Equal(2, conversation.CardMessageIndex);
        Assert.Equal(new global::Agent.Core.Learning.LearningEntry("choose_account", null), conversation.LastLearningEntry);
        Assert.Equal(RequestIntent.Unsupported, conversation.LastRequest?.Intent);

        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        Assert.Equal(SeedFacts.DemoBalance - 31_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task ChooseAccountWithASharedNameReachesTheCardWithTheNoteAfterTheTap()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(ChooseAccountSharedSentence);
        Assert.Equal(QuestionKinds.WhichContact, Assert.IsType<AgentReply.Question>(conversation.Question).Kind);

        await conversation.TapOptionAsync("S08");

        Assert.Equal(SeedFacts.Contact("S08").Number, Assert.IsType<CardView>(conversation.Card).ToPhone);
        Assert.Contains(conversation.Messages, m => m.Kind == ConversationKind.Info && m.Code == ReasonCodes.AccountChoiceUnavailable);
        Assert.Equal(new global::Agent.Core.Learning.LearningEntry("choose_account", null), conversation.LastLearningEntry);
        Assert.Single(conversation.QuestionsAsked);
    }

    [Fact]
    public async Task ATapOnTheBillOfBillOrStoreGivesTheBillCard()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(InternetSentence);
        Assert.Equal(QuestionKinds.BillOrStore, Assert.IsType<AgentReply.Question>(conversation.Question).Kind);

        await conversation.TapOptionAsync("BS4");

        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(("bill", "BS4", 35_000L), (card.Type, card.BillCode, card.AmountIqd));
        Assert.Single(conversation.QuestionsAsked);
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
    }

    [Fact]
    public async Task ATapOnAProductGivesThatProductsCard()
    {
        await using var harness = await StartAsync(U2);
        var conversation = harness.Conversation;

        await conversation.SendAsync(InternetSentence);
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal(QuestionKinds.WhichProduct, question.Kind);
        Assert.Contains(question.Options, o => o.Value == "AC-NET-10");

        await conversation.TapOptionAsync("AC-NET-10");

        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(("purchase", "AC-NET-10", 10_000L), (card.Type, card.ProductCode, card.AmountIqd));
        Assert.Single(conversation.QuestionsAsked);
        Assert.Equal(RequestIntent.Purchase, conversation.LastRequest?.Intent);
    }

    /// <summary>A parser that knows only the given dev-set say steps (by their masked text), so any other parse call shows.</summary>
    private static CapturingParser KnowsOnly(params (string Text, ParsedRequest Request)[] says)
        => new(says.ToDictionary(s => Masker.Mask(s.Text).Text, s => s.Request, StringComparer.Ordinal));

    private async Task<ConversationHarness> StartAsync(IParser parser, Action<AgentOptions>? configure = null)
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        return await ConversationHarness.StartAsync(host, parser, SeedFacts.DemoUserId, configure);
    }

    [Fact]
    public async Task AnEndearmentIsDroppedAndTheTransferGoesStraightToTheCard()
    {
        // ADD-19 end to end, with the console's endearments: no question, the card to S14.
        var parser = KnowsOnly((DevSetText.Say("ADD-19", 0), ParsedRequest.Transfer(DevSetText.SidecarRecipient("ADD-19", 0), 150, AmountUnit.Thousand)));
        await using var harness = await StartAsync(parser, o => o.Endearments = DevSetText.ConfiguredEndearments());
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("ADD-19", 0));

        Assert.Empty(conversation.QuestionsAsked);
        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(SeedFacts.Contact("S14").Number, card.ToPhone);
        Assert.Equal(SeedFacts.Contact("S14").SavedName, card.DisplayName);
        Assert.Equal(150_000, card.AmountIqd);
        Assert.IsType<ConfirmOutcome.Done>(await conversation.ConfirmAsync(null));
        Assert.Equal(SeedFacts.DemoBalance - 151_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task ATapOnWhichBillAfterAPartOfBillRequestGivesTheRefusalNotACard()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.SendAsync(PartOfElectricitySentence);
        Assert.Equal(QuestionKinds.WhichBill, Assert.IsType<AgentReply.Question>(conversation.Question).Kind);

        await conversation.TapOptionAsync("BS2");

        Assert.Null(conversation.Card);
        var refusal = conversation.Messages.Last();
        var bs2 = Resolver.SeedWallet.BillsOf("U1").Single(b => b.Code == "BS2");
        Assert.Equal((ConversationKind.Refusal, ReasonCodes.BillsPaidInFull, (long?)bs2.AmountDueIqd), (refusal.Kind, refusal.Code, refusal.AmountIqd));
        Assert.Single(conversation.QuestionsAsked);
    }
}
