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
    public async Task ATypedAmountIsReadByCodeAndNeverReachesTheModel()
    {
        // AMB-06: the amount is missing, so which_amount (typed); the answer is read in code, with no parse call.
        var parser = KnowsOnly((DevSetText.Say("AMB-06", 0), ParsedRequest.Transfer(DevSetText.SidecarRecipient("AMB-06", 0), null, AmountUnit.None)));
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("AMB-06", 0));
        Assert.Equal(QuestionKinds.WhichAmount, Assert.IsType<AgentReply.Question>(conversation.Question).Kind);

        await conversation.SendAsync(DevSetText.Say("AMB-06", 1));

        Assert.Single(parser.Calls);
        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(SeedFacts.Contact("S19").Number, card.ToPhone);
        Assert.Equal(30_000, card.AmountIqd);
        Assert.Contains(harness.Trace.Entries, e => e.Step == "answer_parsed" && e.Detail == QuestionKinds.WhichAmount);
        Assert.Single(conversation.QuestionsAsked);
    }

    [Fact]
    public async Task ATypedContactWithThePrepositionIsMatchedByCodeAndNeverReachesTheModel()
    {
        // AMB-13: no recipient, so which_contact (typed); the answer is S12's alias with the preposition lam against it.
        var parser = KnowsOnly((DevSetText.Say("AMB-13", 0), ParsedRequest.Transfer(null, 20, AmountUnit.Thousand)));
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await conversation.SendAsync(DevSetText.Say("AMB-13", 0));
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        Assert.Equal((QuestionKinds.WhichContact, QuestionAnswer.Typed), (question.Kind, question.Answer));

        await conversation.SendAsync(DevSetText.Say("AMB-13", 1));

        Assert.Single(parser.Calls);
        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(SeedFacts.Contact("S12").Number, card.ToPhone);
        Assert.Equal(SeedFacts.Contact("S12").Alias, card.DisplayName);
        Assert.Equal(20_000, card.AmountIqd);
        Assert.Contains(harness.Trace.Entries, e => e.Step == "answer_parsed" && e.Detail == QuestionKinds.WhichContact);
        Assert.Null(conversation.LastAnswers.ContactOption);
    }

    [Fact]
    public async Task ATypedAnswerTheCodeCannotReadGoesToTheModel()
    {
        var parser = KnowsOnly((DevSetText.Say("AMB-06", 0), ParsedRequest.Transfer(DevSetText.SidecarRecipient("AMB-06", 0), null, AmountUnit.None)));
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;
        await conversation.SendAsync(DevSetText.Say("AMB-06", 0));

        await conversation.SendAsync("not an amount at all");

        Assert.Equal(2, parser.Calls.Count);
        Assert.Equal("not an amount at all", parser.Calls[1].Text);
        Assert.DoesNotContain(harness.Trace.Entries, e => e.Step == "answer_parsed");
        Assert.Null(conversation.Card);
    }

    [Fact]
    public async Task ANotUnderstoodAnswerRepeatsTheQuestionAndKeepsItOpen()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        await conversation.SendAsync(SharedNameSentence);
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);
        var questionIndex = conversation.QuestionMessageIndex;

        await conversation.SendAsync("something nobody can read");

        Assert.Equal(
            new ConversationMessage(ConversationRole.Agent, ConversationKind.Refusal, ReasonCodes.NotUnderstood, QuestionKind: QuestionKinds.WhichContact),
            conversation.Messages[^1]);
        Assert.Same(question, conversation.Question);
        Assert.Equal(questionIndex, conversation.QuestionMessageIndex);
        Assert.Equal(new AgentReply.Refusal(ReasonCodes.NotUnderstood, null, null), conversation.LastReply);
        Assert.Contains(harness.Trace.Entries, e => e.Step == "question_repeated" && e.Detail == QuestionKinds.WhichContact);

        // The options still answer the same question.
        await conversation.TapOptionAsync("S09");

        Assert.Equal(SeedFacts.Contact("S09").Number, Assert.IsType<CardView>(conversation.Card).ToPhone);
        Assert.Single(conversation.QuestionsAsked);
        var refused = Assert.Single(await harness.AuditRowsAsync(), r => r.Event == global::Agent.Core.Audit.AuditEvents.Refused);
        Assert.Equal(ReasonCodes.NotUnderstood, refused.Reason);
        var learning = Assert.Single(await harness.LearningRowsAsync());
        Assert.Equal(ReasonCodes.NotUnderstood, learning.Reason);
    }

    [Fact]
    public async Task ANotUnderstoodTypedAmountRepeatsTheQuestionAndTheNextAmountCompletesIt()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        await conversation.SendAsync(NoAmountSentence);
        var question = Assert.IsType<AgentReply.Question>(conversation.Question);

        await conversation.SendAsync("something nobody can read");

        Assert.Equal(QuestionKinds.WhichAmount, conversation.Messages[^1].QuestionKind);
        Assert.Same(question, conversation.Question);
        Assert.Null(conversation.Card);

        await conversation.SendAsync("50");

        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(SeedFacts.HammoudiPhone, card.ToPhone);
        Assert.Equal(50_000, card.AmountIqd);
        Assert.Single(conversation.QuestionsAsked);
    }

    [Fact]
    public async Task ANotUnderstoodAnswerFromAnInvalidModelReplyRepeatsTheQuestion()
    {
        const string Answer = "an answer the validator refuses";
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>(StringComparer.Ordinal)
        {
            [SharedNameSentence] = ParsedRequest.Transfer(SeedFacts.Contact("S08").SavedName, 40, AmountUnit.Thousand),
            [Answer] = new() { Intent = RequestIntent.Unclear, Problem = "injected" },
        });
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;
        await conversation.SendAsync(SharedNameSentence);

        await conversation.SendAsync(Answer);

        Assert.Equal(QuestionKinds.WhichContact, conversation.Messages[^1].QuestionKind);
        Assert.NotNull(conversation.Question);
        Assert.Equal(new global::Agent.Core.Learning.LearningEntry("model_invalid_reply", "injected"), conversation.LastLearningEntry);
        var learning = Assert.Single(await harness.LearningRowsAsync());
        Assert.Equal(("model_invalid_reply", "injected"), (learning.Reason, learning.Detail));
    }

    [Fact]
    public async Task NoneOfTheseOnAContactQuestionIsRecipientNotFound()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        await conversation.SendAsync(SharedNameSentence);

        await conversation.DeclineQuestionAsync();

        var messages = conversation.Messages;
        Assert.Equal(new ConversationMessage(ConversationRole.User, ConversationKind.Option, Option.NoneOfThese), messages[^2]);
        Assert.Equal(new ConversationMessage(ConversationRole.Agent, ConversationKind.Refusal, ReasonCodes.RecipientNotFound), messages[^1]);
        Assert.Null(conversation.Question);
        Assert.Null(conversation.Card);
        Assert.Contains(harness.Trace.Entries, e => e.Step == "recipient_resolved" && e.Detail == "none_of_these");
        Assert.Equal(ReasonCodes.RecipientNotFound, Assert.Single(await harness.LearningRowsAsync()).Reason);
    }

    [Fact]
    public async Task CancelOnAQuestionClearsThePendingRequestSoTheNextAmountIsANewRequest()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;
        await conversation.SendAsync(NoAmountSentence);

        await conversation.DeclineQuestionAsync();

        var messages = conversation.Messages;
        Assert.Equal(new ConversationMessage(ConversationRole.User, ConversationKind.Option, Option.CancelQuestion), messages[^2]);
        Assert.Equal(new ConversationMessage(ConversationRole.Agent, ConversationKind.Refusal, ReasonCodes.QuestionCancelled), messages[^1]);
        Assert.Null(conversation.Question);
        Assert.Null(conversation.LastRequest);
        Assert.Equal(new AgentReply.Refusal(ReasonCodes.QuestionCancelled, null, null), conversation.LastReply);
        Assert.Null(conversation.LastLearningEntry);
        Assert.Contains(harness.Trace.Entries, e => e.Step == "question_cancelled" && e.Detail == QuestionKinds.WhichAmount);
        var refused = Assert.Single(await harness.AuditRowsAsync(), r => r.Event == global::Agent.Core.Audit.AuditEvents.Refused);
        Assert.Equal(ReasonCodes.QuestionCancelled, refused.Reason);

        // Nothing is pending, so an amount alone is a fresh message, not an answer: no card.
        await conversation.SendAsync("50");

        Assert.Null(conversation.Card);
        Assert.DoesNotContain(harness.Trace.Entries, e => e.Step == "answer_parsed");
        Assert.Equal(new ConversationMessage(ConversationRole.Agent, ConversationKind.Refusal, ReasonCodes.NotUnderstood), conversation.Messages[^1]);
        Assert.DoesNotContain(await harness.AuditRowsAsync(), r => r.Event == global::Agent.Core.Audit.AuditEvents.Created);
    }

    [Fact]
    public async Task DeclineWithNoOpenQuestionDoesNothing()
    {
        await using var harness = await StartAsync();
        var conversation = harness.Conversation;

        await conversation.DeclineQuestionAsync();

        Assert.Empty(conversation.Messages);
        Assert.Contains(harness.Trace.Entries, e => e.Step == "option_rejected");
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

    /// <summary>
    /// The typed text of an example of <c>answer-examples.json</c> (answers with filler words, 17 Oct 2026: the one marked
    /// <c>khalil</c> is his, the one marked <c>composed</c> is composed from his filler words) and the dinars it stands for.
    /// </summary>
    private static (string Text, long AmountIqd) AnswerExample(string example)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(SeedHashTests.RepositoryRoot(), "tests", "Agent.Core.Tests", "Conversation", "answer-examples.json")));
        var line = document.RootElement.EnumerateArray().Single(e => e.GetProperty("example").GetString() == example);
        return (line.GetProperty("text").GetString()!, line.GetProperty("amount_iqd").GetInt64());
    }

    /// <summary>AMB-06's first message: the transfer to S19 with no amount, so <c>which_amount</c> (typed).</summary>
    private static CapturingParser Amb06Parser()
        => KnowsOnly((DevSetText.Say("AMB-06", 0), ParsedRequest.Transfer(DevSetText.SidecarRecipient("AMB-06", 0), null, AmountUnit.None)));

    /// <summary>AMB-06, then the answer example: the card for its amount to S19, with no parse call for the answer.</summary>
    private async Task AFillerWordAtEitherEndOfTheAmountIsIgnored(string example)
    {
        var (text, amountIqd) = AnswerExample(example);
        var parser = Amb06Parser();
        await using var harness = await StartAsync(parser, o => o.FillerWords = DevSetText.ConfiguredFillerWords());
        var conversation = harness.Conversation;
        await conversation.SendAsync(DevSetText.Say("AMB-06", 0));

        await conversation.SendAsync(text);

        Assert.Single(parser.Calls);
        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(SeedFacts.Contact("S19").Number, card.ToPhone);
        Assert.Equal(amountIqd, card.AmountIqd);
        Assert.Contains(harness.Trace.Entries, e => e.Step == "answer_parsed" && e.Detail == QuestionKinds.WhichAmount);
    }

    [Fact]
    public Task AFillerWordAfterTheAmountIsIgnored() => AFillerWordAtEitherEndOfTheAmountIsIgnored("filler_after");

    [Fact]
    public Task AFillerWordBeforeTheAmountIsIgnored() => AFillerWordAtEitherEndOfTheAmountIsIgnored("filler_before");

    [Fact]
    public async Task AnAnswerOfFillerWordsOnlyGoesToTheModel()
    {
        var fillers = DevSetText.ConfiguredFillerWords();
        var parser = Amb06Parser();
        await using var harness = await StartAsync(parser, o => o.FillerWords = fillers);
        var conversation = harness.Conversation;
        await conversation.SendAsync(DevSetText.Say("AMB-06", 0));

        await conversation.SendAsync(fillers[0]);

        Assert.Equal(2, parser.Calls.Count);
        Assert.Equal(Masker.Mask(fillers[0]).Text, parser.Calls[1].Text);
        Assert.DoesNotContain(harness.Trace.Entries, e => e.Step == "answer_parsed");
        Assert.Null(conversation.Card);
    }

    [Fact]
    public void AFillerWordInTheMiddleStaysAndTheTextIsKeptAsTyped()
    {
        string[] fillers = ["only", "fine"];

        Assert.Equal("50,000 x", global::Agent.Core.Conversation.Conversation.WithoutFillerWords("fine 50,000 x only.", fillers));
        Assert.Equal("50 only 2", global::Agent.Core.Conversation.Conversation.WithoutFillerWords("50 only 2", fillers));
        Assert.Equal("  50  ", global::Agent.Core.Conversation.Conversation.WithoutFillerWords("  50  ", fillers));
        Assert.Null(global::Agent.Core.Conversation.Conversation.WithoutFillerWords("Only, fine", fillers));
        Assert.Equal("only 50", global::Agent.Core.Conversation.Conversation.WithoutFillerWords("only 50", []));
    }

    [Fact]
    public void TheFillerWordsAreEmptyByDefault() => Assert.Empty(new AgentOptions().FillerWords);

    /// <summary>A synthetic English sentence: a transfer to S14's saved name (as ADD-20's sidecar has it) of 800 thousand, above U1's balance.</summary>
    private const string TooMuchSentence = "send eight hundred thousand to my mother";

    /// <summary>A synthetic English sentence: 500 dinars to S01's alias, below the smallest amount.</summary>
    private const string TooLittleSentence = "send five hundred dinars to hammoudi";

    private static CapturingParser TooMuchParser()
        => KnowsOnly((TooMuchSentence, ParsedRequest.Transfer(DevSetText.SidecarRecipient("ADD-20"), 800, AmountUnit.Thousand)));

    [Fact]
    public async Task AnAmountRefusedForTheBalanceMergesTheNextTypedAmountAndChecksAgain()
    {
        var parser = TooMuchParser();
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await conversation.SendAsync(TooMuchSentence);
        Assert.Equal(ReasonCodes.InsufficientBalance, Assert.IsType<AgentReply.Refusal>(conversation.LastReply).Code);
        Assert.Null(conversation.Card);

        // ADD-21's second message, Khalil's typed amount: read in code and merged into the refused transfer.
        await conversation.SendAsync(DevSetText.Say("ADD-21", 1));

        Assert.Single(parser.Calls);
        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(SeedFacts.Contact("S14").Number, card.ToPhone);
        Assert.Equal(SeedFacts.Contact("S14").SavedName, card.DisplayName);
        Assert.Equal(600_000, card.AmountIqd);
        Assert.Contains(harness.Trace.Entries, e => e.Step == "amount_retry");
        Assert.Empty(conversation.QuestionsAsked);
    }

    [Fact]
    public async Task AnAmountBelowTheMinimumMergesTheNextTypedAmount()
    {
        var parser = KnowsOnly((TooLittleSentence, ParsedRequest.Transfer(HammoudiAlias, 500, AmountUnit.Dinars)));
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;

        await conversation.SendAsync(TooLittleSentence);
        Assert.Equal(ReasonCodes.AmountBelowMinimum, Assert.IsType<AgentReply.Refusal>(conversation.LastReply).Code);

        // AMB-06's typed answer, Khalil's 30 thousand.
        await conversation.SendAsync(DevSetText.Say("AMB-06", 1));

        Assert.Single(parser.Calls);
        var card = Assert.IsType<CardView>(conversation.Card);
        Assert.Equal(SeedFacts.HammoudiPhone, card.ToPhone);
        Assert.Equal(30_000, card.AmountIqd);
        Assert.Contains(harness.Trace.Entries, e => e.Step == "amount_retry");
    }

    [Fact]
    public async Task AnythingElseAfterAnAmountRefusalDropsTheRequest()
    {
        var parser = TooMuchParser();
        await using var harness = await StartAsync(parser);
        var conversation = harness.Conversation;
        await conversation.SendAsync(TooMuchSentence);
        Assert.Equal(ReasonCodes.InsufficientBalance, Assert.IsType<AgentReply.Refusal>(conversation.LastReply).Code);

        await conversation.SendAsync("not an amount at all");

        Assert.Equal(2, parser.Calls.Count);
        Assert.Null(conversation.Card);

        // The retry was for one message only: an amount typed now is a fresh message, which this parser does not know.
        await conversation.SendAsync(DevSetText.Say("ADD-21", 1));

        Assert.Equal(3, parser.Calls.Count);
        Assert.Null(conversation.Card);
        Assert.DoesNotContain(harness.Trace.Entries, e => e.Step == "amount_retry");
        Assert.Equal(new ConversationMessage(ConversationRole.Agent, ConversationKind.Refusal, ReasonCodes.NotUnderstood), conversation.Messages[^1]);
    }

    [Fact]
    public async Task ABillRefusedForTheBalanceDoesNotOpenTheRetry()
    {
        // NOGO-11: U2's school fees against her balance, as its sidecar parse has it (the biller code only).
        const string schoolFeesSentence = "pay the school fees";
        var parser = KnowsOnly((schoolFeesSentence, ParsedRequest.Bill("school_fees", null, null, AmountUnit.None)));
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await using var harness = await ConversationHarness.StartAsync(host, parser, U2);
        var conversation = harness.Conversation;

        await conversation.SendAsync(schoolFeesSentence);
        Assert.Equal(ReasonCodes.InsufficientBalance, Assert.IsType<AgentReply.Refusal>(conversation.LastReply).Code);

        await conversation.SendAsync(DevSetText.Say("AMB-06", 1));

        Assert.Equal(2, parser.Calls.Count);
        Assert.Null(conversation.Card);
        Assert.DoesNotContain(harness.Trace.Entries, e => e.Step == "amount_retry");
    }
}
