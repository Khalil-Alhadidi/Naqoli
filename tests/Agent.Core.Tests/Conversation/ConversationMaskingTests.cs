using Agent.Core.Learning;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Agent.Core.Text;
using Agent.Core.Trace;

namespace Agent.Core.Tests.Conversation;

/// <summary>
/// Rule 5 in the conversation: the parse call receives the typed text with every number masked, a token the parser
/// returns as the recipient is un-masked from this message's map only, and no trace step ever holds the typed number.
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class ConversationMaskingTests(AgentHost host)
{
    private const int U1 = SeedFacts.DemoUserId;

    private static ParsedRequest FiftyThousandTo(string recipient) => ParsedRequest.Transfer(recipient, 50_000, AmountUnit.Dinars);

    private async Task<ConversationHarness> StartAsync(CapturingParser parser, int userId = U1)
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        return await ConversationHarness.StartAsync(host, parser, userId);
    }

    /// <summary>The digits of <paramref name="text"/> as ASCII, without separators, so any typed form can be searched.</summary>
    private static string Digits(string? text)
        => text is null ? string.Empty : string.Concat(ArabicDigits.ToAscii(text).Where(char.IsAsciiDigit));

    [Fact]
    public async Task TheParserReceivesTheMaskedTextAndTheMap()
    {
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>());
        await using var session = await StartAsync(parser);

        await session.Conversation.SendAsync("send 50000 to " + SeedFacts.HammoudiPhone);

        var (text, context) = Assert.Single(parser.Calls);
        Assert.Equal("send 50000 to <NUM_1>", text);
        Assert.NotNull(context.Map);
        Assert.True(context.Map.TryUnmask("<NUM_1>", out var original));
        Assert.Equal(SeedFacts.HammoudiPhone, original);
    }

    [Theory]
    [InlineData("07805550101")]
    [InlineData("+964 780 555 0101")]
    [InlineData("0780-555-0101")]
    public async Task ATokenEchoResolvesToTheTypedNumber(string typed)
    {
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>
        {
            ["send 50000 to <NUM_1>"] = FiftyThousandTo("<NUM_1>"),
        });
        await using var session = await StartAsync(parser);

        await session.Conversation.SendAsync("send 50000 to " + typed);

        var card = Assert.IsType<CardView>(session.Conversation.Card);
        Assert.Equal((SeedFacts.HammoudiPhone, 50_000L), (card.ToPhone, card.AmountIqd));
        Assert.Equal(typed, session.Conversation.LastRequest?.RecipientText);
    }

    [Fact]
    public async Task AnArabicIndicNumberIsMaskedAndResolves()
    {
        var typed = ArabicDigits.ToArabicIndic(SeedFacts.HammoudiPhone);
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>
        {
            ["send 50000 to <NUM_1>"] = FiftyThousandTo("<NUM_1>"),
        });
        await using var session = await StartAsync(parser);

        await session.Conversation.SendAsync("send 50000 to " + typed);

        Assert.Equal("send 50000 to <NUM_1>", Assert.Single(parser.Calls).Text);
        Assert.Equal(SeedFacts.HammoudiPhone, session.Conversation.Card?.ToPhone);
    }

    [Fact]
    public async Task TheTraceNeverHoldsTheTypedNumber()
    {
        var typed = "0780 555 0101";
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>
        {
            ["send 50000 to <NUM_1>"] = FiftyThousandTo("<NUM_1>"),
            ["and to <NUM_1> too"] = FiftyThousandTo("Zorro"),
        });
        await using var session = await StartAsync(parser);

        await session.Conversation.SendAsync("send 50000 to " + typed);
        Assert.IsType<CardView>(session.Conversation.Card);
        Assert.NotNull(await session.Conversation.ConfirmAsync(null));
        await session.Conversation.SendAsync("and to " + typed + " too");

        Assert.Equal(ReasonCodes.RecipientNotFound, Assert.IsType<AgentReply.Refusal>(session.Conversation.LastReply).Code);
        var number = Digits(SeedFacts.HammoudiPhone)[1..];
        Assert.NotEmpty(session.Trace.Entries);
        Assert.All(session.Trace.Entries, e =>
        {
            Assert.DoesNotContain(number, Digits(e.Step), StringComparison.Ordinal);
            Assert.DoesNotContain(number, Digits(e.Detail), StringComparison.Ordinal);
        });
    }

    /// <summary>D21: a 10-digit and a 12-digit number (Hammoudi's number one digit short, and one digit long), with separators.</summary>
    public static TheoryData<string> AccountLikeNumbers => new()
    {
        SeedFacts.HammoudiPhone[..4] + " " + SeedFacts.HammoudiPhone[4..7] + " " + SeedFacts.HammoudiPhone[7..^1],
        SeedFacts.HammoudiPhone[..4] + "-" + SeedFacts.HammoudiPhone[4..7] + "-" + SeedFacts.HammoudiPhone[7..] + "1",
    };

    [Theory]
    [MemberData(nameof(AccountLikeNumbers))]
    public async Task ATypedAccountNumberIsMaskedRefusedAndGetsNoCard(string typed)
    {
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>
        {
            ["send 25000 to <NUM_1>"] = ParsedRequest.Transfer("<NUM_1>", 25_000, AmountUnit.Dinars),
        });
        await using var session = await StartAsync(parser);

        await session.Conversation.SendAsync("send 25000 to " + typed);

        Assert.Equal("send 25000 to <NUM_1>", Assert.Single(parser.Calls).Text);
        Assert.Equal(ReasonCodes.InvalidNumber, Assert.IsType<AgentReply.Refusal>(session.Conversation.LastReply).Code);
        Assert.Null(session.Conversation.Card);
        Assert.Empty(session.Conversation.CardsIssued);

        var digits = Digits(typed);
        Assert.InRange(digits.Length, 10, 12);
        Assert.NotEqual(11, digits.Length);
        Assert.All(session.Trace.Entries, e =>
        {
            Assert.DoesNotContain(digits, Digits(e.Step), StringComparison.Ordinal);
            Assert.DoesNotContain(digits, Digits(e.Detail), StringComparison.Ordinal);
        });

        var audit = await session.AuditRowsAsync();
        var refused = Assert.Single(audit);
        Assert.Equal(ReasonCodes.InvalidNumber, refused.Reason);
        Assert.All(audit, a => Assert.DoesNotContain(
            digits, Digits(string.Join('|', a.Event, a.FromState, a.ToState, a.Reason, a.Reference, a.Detail)), StringComparison.Ordinal));

        // invalid_number is neither logged nor on the not-logged list: no learning row, so the digits cannot reach it.
        Assert.Null(session.Conversation.LastLearningEntry);
        Assert.Empty(await session.LearningRowsAsync());
    }

    [Fact]
    public async Task ATokenOfAnotherMessageIsNotUnmasked()
    {
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>
        {
            ["send 50000 to <NUM_1>"] = FiftyThousandTo("<NUM_2>"),
        });
        await using var session = await StartAsync(parser);

        await session.Conversation.SendAsync("send 50000 to " + SeedFacts.HammoudiPhone);

        Assert.Null(session.Conversation.Card);
        Assert.Equal("<NUM_2>", session.Conversation.LastRequest?.RecipientText);
    }

    [Fact]
    public async Task AnInvalidReplyIsNotUnderstoodTracedAndLogged()
    {
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>
        {
            ["send 50000 to <NUM_1>"] = new() { Intent = RequestIntent.Unclear, Problem = "recipient_text" },
        });
        await using var session = await StartAsync(parser);

        await session.Conversation.SendAsync("send 50000 to " + SeedFacts.HammoudiPhone);

        Assert.Null(session.Conversation.Card);
        Assert.Equal(ReasonCodes.NotUnderstood, Assert.IsType<AgentReply.Refusal>(session.Conversation.LastReply).Code);
        Assert.Contains((TraceZone.Code, LearningLogRule.ModelInvalidReply, "recipient_text"), session.Trace.Entries);
        Assert.Equal(new LearningEntry(LearningLogRule.ModelInvalidReply, "recipient_text"), session.Conversation.LastLearningEntry);
        var row = Assert.Single(await session.LearningRowsAsync());
        Assert.Equal(
            (U1, LearningLogRule.ModelInvalidReply, "recipient_text", "send 50000 to <NUM_1>", false),
            (row.UserId, row.Reason, row.Detail, row.MaskedText, row.Reviewed));
    }

    [Fact]
    public async Task APlainUnclearReplyLogsNotUnderstood()
    {
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>());
        await using var session = await StartAsync(parser);

        await session.Conversation.SendAsync("something else");

        Assert.DoesNotContain(session.Trace.Entries, e => e.Step == LearningLogRule.ModelInvalidReply);
        var row = Assert.Single(await session.LearningRowsAsync());
        Assert.Equal((LearningLogRule.NotUnderstood, "something else"), (row.Reason, row.MaskedText));
    }

    [Fact]
    public async Task TheTraceNamesTheParser()
    {
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>());
        await using var session = await StartAsync(parser);

        await session.Conversation.SendAsync("something else");

        Assert.Contains((TraceZone.Model, "parse:" + CapturingParser.ParserName, nameof(RequestIntent.Unclear)), session.Trace.Entries);
    }

    [Fact]
    public async Task TheLastLearningEntryIsWhatWasWrittenForTheLastReply()
    {
        var parser = new CapturingParser(new Dictionary<string, ParsedRequest>
        {
            ["send 50000 to <NUM_1>"] = FiftyThousandTo("<NUM_1>"),
        });
        await using var session = await StartAsync(parser);

        await session.Conversation.SendAsync("something else");
        Assert.Equal(new LearningEntry(LearningLogRule.NotUnderstood, null), session.Conversation.LastLearningEntry);

        await session.Conversation.SendAsync("send 50000 to " + SeedFacts.HammoudiPhone);
        Assert.NotNull(session.Conversation.Card);
        Assert.Null(session.Conversation.LastLearningEntry);
    }
}
