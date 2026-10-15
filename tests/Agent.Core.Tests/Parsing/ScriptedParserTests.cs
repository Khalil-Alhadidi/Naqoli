using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Requests;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The scripted parser that stands in for the model until milestone 6: a whole typed sentence, normalised, maps to the
/// structure written next to it in <c>scripted-sentences.json</c>; anything else is <see cref="RequestIntent.Unclear"/>.
/// </summary>
public class ScriptedParserTests
{
    /// <summary>The transfer sentence of <c>scripted-sentences.json</c>, as Khalil wrote it (never a literal here).</summary>
    private static string HammoudiPlaceholder
        => ScriptedParser.EmbeddedSentences().First(s => s.Request.Intent == RequestIntent.Transfer).Sentence;

    /// <summary>The balance sentence of <c>scripted-sentences.json</c>, as Khalil wrote it.</summary>
    private static string BalancePlaceholder
        => ScriptedParser.EmbeddedSentences().First(s => s.Request.Intent == RequestIntent.Balance).Sentence;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly ParseContext NothingPending = new(PendingNote: null);

    [Fact]
    public async Task ThePlaceholderSentenceIsTheHammoudiTransfer()
    {
        var parsed = await new ScriptedParser().ParseAsync(HammoudiPlaceholder, NothingPending, Token);

        Assert.Equal(ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand), parsed);
    }

    [Fact]
    public async Task ItReportsZeroUsageAfterEachParse()
    {
        var parser = new ScriptedParser();
        Assert.Null(parser.LastUsage);

        await parser.ParseAsync(HammoudiPlaceholder, NothingPending, Token);

        var usage = Assert.IsType<ParseUsage>(parser.LastUsage);
        Assert.Equal((0, 0, 0m), (usage.InputTokens, usage.OutputTokens, usage.CostUsd));
        Assert.True(usage.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ExtraWhitespaceStillMatches()
    {
        // The sentence with a leading and a trailing space, doubled spaces and a tab between its words.
        var words = HammoudiPlaceholder.Split(' ');
        var spaced = "  " + string.Join("  ", words[..^1]) + "\t" + words[^1] + " ";

        var parsed = await new ScriptedParser().ParseAsync(spaced, NothingPending, Token);

        Assert.Equal(RequestIntent.Transfer, parsed.Intent);
    }

    [Fact]
    public async Task TheBalanceSentenceIsTheBalanceQuestion()
        => Assert.Equal(ParsedRequest.Balance(), await new ScriptedParser().ParseAsync(BalancePlaceholder, NothingPending, Token));

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("send fifty thousand to my brother")]
    [InlineData("")]
    public async Task AnyOtherSentenceIsUnclear(string typed)
    {
        var parsed = await new ScriptedParser().ParseAsync(typed, NothingPending, Token);

        Assert.Equal(new ParsedRequest { Intent = RequestIntent.Unclear }, parsed);
    }

    [Fact]
    public async Task ArabicIndicDigitsInTheTypedTextMatchAsciiDigits()
    {
        // D20: typed input is accepted in either digit form.
        var request = ParsedRequest.Transfer("x", 25_000, AmountUnit.Dinars);
        var parser = new ScriptedParser([new ScriptedSentence("send 25000 to x", request)]);

        Assert.Equal(request, await parser.ParseAsync("send \u0662\u0665\u0660\u0660\u0660 to x", NothingPending, Token));
        Assert.Equal(request, await parser.ParseAsync("send \u06F2\u06F5\u06F0\u06F0\u06F0 to x", NothingPending, Token));
        Assert.Equal(request, await parser.ParseAsync("send 25000 to x", NothingPending, Token));
    }

    [Fact]
    public async Task TheFileHasNoYesOrNoSentence()
    {
        // D6: a typed yes or no is the session's business (it points to the button); the parser never sees it as a request.
        var parser = new ScriptedParser();

        Assert.Equal(RequestIntent.Unclear, (await parser.ParseAsync("yes", NothingPending, Token)).Intent);
        Assert.Equal(RequestIntent.Unclear, (await parser.ParseAsync("no", NothingPending, Token)).Intent);
    }

    [Fact]
    public async Task ASentenceWithAPhoneNumberMatchesTheMaskedText()
    {
        // The conversation masks the typed text before the parse call, so a sentence is masked the same way at load:
        // Khalil writes it with a real-looking number, and any spacing of that number matches.
        var request = ParsedRequest.Transfer("07701234567", 25_000, AmountUnit.Dinars);
        var parser = new ScriptedParser([new ScriptedSentence("send 25000 to 07701234567", request)]);

        var typed = Masker.Mask("send 25000 to 0770 123 4567");

        Assert.Equal("send 25000 to <NUM_1>", typed.Text);
        Assert.Equal(request, await parser.ParseAsync(typed.Text, new ParseContext(null, typed.Map), Token));
    }

    [Fact]
    public void TheNameIsScripted()
    {
        Assert.Equal("scripted", new ScriptedParser().Name);
    }
}
