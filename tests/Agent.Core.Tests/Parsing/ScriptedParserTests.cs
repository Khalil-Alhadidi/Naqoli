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
    /// <summary>The English placeholder sentence Khalil replaces with his Iraqi Arabic one (TODO-AR).</summary>
    private const string HammoudiPlaceholder = "send fifty thousand to my brother Hammoudi";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly ParseContext NothingPending = new(PendingNote: null);

    [Fact]
    public async Task ThePlaceholderSentenceIsTheHammoudiTransfer()
    {
        var parsed = await new ScriptedParser().ParseAsync(HammoudiPlaceholder, NothingPending, Token);

        Assert.Equal(ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand), parsed);
    }

    [Fact]
    public async Task ExtraWhitespaceStillMatches()
    {
        var parsed = await new ScriptedParser().ParseAsync("  send  fifty thousand to my\tbrother Hammoudi ", NothingPending, Token);

        Assert.Equal(RequestIntent.Transfer, parsed.Intent);
    }

    [Fact]
    public async Task TheBalanceSentenceIsTheBalanceQuestion()
        => Assert.Equal(ParsedRequest.Balance(), await new ScriptedParser().ParseAsync("balance", NothingPending, Token));

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
}
