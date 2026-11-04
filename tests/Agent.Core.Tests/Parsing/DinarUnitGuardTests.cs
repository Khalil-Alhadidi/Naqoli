using System.Text.Json;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Requests;
using Agent.Core.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The dinar-unit guard (Khalil, 4 Nov 2026, the Flash round): a bare amount in a message that holds a dinar word
/// becomes dinars; everything else passes through. The words come from the console's settings
/// (<see cref="DevSetText.ConfiguredDinarWords"/>) and the messages from the development set, never as literals.
/// </summary>
public class DinarUnitGuardTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string[] Words => DevSetText.ConfiguredDinarWords();

    /// <summary>ADD-13 as typed and masked: "500 dinars" to a contact, below the 1,000-dinar minimum.</summary>
    private static string Add13 => Masker.Mask(DevSetText.Say("ADD-13")).Text;

    /// <summary>A transfer message of the development set with no dinar word (ADD-01).</summary>
    private static string NoDinarWord
    {
        get
        {
            var text = Masker.Mask(DevSetText.Say("ADD-01")).Text;
            Assert.False(DinarUnitGuard.HasDinarWord(text, Words), "ADD-01 holds no dinar word.");
            return text;
        }
    }

    private static ParsedRequest Bare(decimal? value, AmountUnit unit) => ParsedRequest.Transfer("x", value, unit);

    [Fact]
    public void TheConsoleShipsTheDinarWordsAndTheDefaultIsEmpty()
    {
        Assert.NotEmpty(Words);
        Assert.DoesNotContain(Words, string.IsNullOrWhiteSpace);
        Assert.Empty(new AgentOptions().DinarWords);
    }

    [Fact]
    public void ABareAmountInAMessageWithADinarWordBecomesDinars()
    {
        var guarded = DinarUnitGuard.Apply([Bare(500, AmountUnit.None)], Add13, Words);

        Assert.Equal(Bare(500, AmountUnit.Dinars), Assert.Single(guarded));
    }

    [Fact]
    public void ABareAmountInAMessageWithNoDinarWordStaysBare()
    {
        var requests = new[] { Bare(500, AmountUnit.None) };

        Assert.Same(requests, DinarUnitGuard.Apply(requests, NoDinarWord, Words));
    }

    [Theory]
    [InlineData(AmountUnit.Thousand)]
    [InlineData(AmountUnit.Million)]
    [InlineData(AmountUnit.Dinars)]
    public void AWrittenUnitIsKept(AmountUnit unit)
    {
        var requests = new[] { Bare(50, unit) };

        Assert.Same(requests, DinarUnitGuard.Apply(requests, Add13, Words));
    }

    [Fact]
    public void ARequestWithNoAmountIsLeftAsItIs()
    {
        var requests = new[] { Bare(null, AmountUnit.None), new ParsedRequest { Intent = RequestIntent.Balance } };

        Assert.Same(requests, DinarUnitGuard.Apply(requests, Add13, Words));
    }

    [Fact]
    public void OnlyTheBareRequestsOfTheMessageChange()
    {
        var guarded = DinarUnitGuard.Apply([Bare(50, AmountUnit.Thousand), Bare(500, AmountUnit.None)], Add13, Words);

        Assert.Equal([Bare(50, AmountUnit.Thousand), Bare(500, AmountUnit.Dinars)], guarded);
    }

    [Fact]
    public void NoWordsTurnTheGuardOff()
    {
        var requests = new[] { Bare(500, AmountUnit.None) };

        Assert.Same(requests, DinarUnitGuard.Apply(requests, Add13, []));
    }

    [Fact]
    public void EveryConfiguredWordIsMatchedAsAWordOfTheMessage()
    {
        foreach (var word in Words)
        {
            var guarded = DinarUnitGuard.Apply([Bare(500, AmountUnit.None)], "500 " + word, Words);
            Assert.Equal(AmountUnit.Dinars, Assert.Single(guarded).AmountUnit);
        }
    }

    [Fact]
    public void AWordIsMatchedByItsKeySoTheArticleDoesNotMatter()
    {
        // The form with the article and the bare form share a key (ArabicText.Key drops the leading al).
        var (bare, withArticle) = KeyPair();

        Assert.True(DinarUnitGuard.HasDinarWord("500 " + bare, [withArticle]));
        Assert.True(DinarUnitGuard.HasDinarWord("500 " + withArticle, [bare]));
    }

    [Fact]
    public void AnAttachedPrefixIsADifferentWordUnlessItIsListed()
    {
        // A configured form with an attached prefix (not the article) has a key of its own, ending in another word's key:
        // it matches only itself, so the comparison is whole word, not a substring.
        var prefixed = Words.Single(w => Words.Count(o => ArabicText.Key(o) == ArabicText.Key(w)) == 1
            && Words.Any(o => ArabicText.Key(o) != ArabicText.Key(w) && ArabicText.Key(w).EndsWith(ArabicText.Key(o), StringComparison.Ordinal)));
        var others = Words.Where(w => w != prefixed).ToArray();

        Assert.True(DinarUnitGuard.HasDinarWord("500 " + prefixed, Words));
        Assert.False(DinarUnitGuard.HasDinarWord("500 " + prefixed, others));

        // A word that is not a dinar word (one of the configured endearments) never matches.
        Assert.False(DinarUnitGuard.HasDinarWord("500 " + DevSetText.ConfiguredEndearments()[0], Words));
    }

    [Fact]
    public async Task TheModelParserKeepsTheWrittenUnitOnAdd13()
    {
        // Flash's reply on ADD-13 (4 Nov 2026): the amount with no unit, which rule 9 would read as 500,000.
        var client = new FakeChatClient();
        client.Reply = (_, _, _) => Task.FromResult(FakeChatClient.Json(BareReply(500)));
        var parser = new ClaudeParser(
            client,
            Microsoft.Extensions.Options.Options.Create(ClaudeParserTests.Options()),
            new ParsePacer(TimeProvider.System),
            TimeProvider.System,
            new ClaudeParserTests.RecordingTrace(),
            NullLogger<ClaudeParser>.Instance);
        var masked = Masker.Mask(DevSetText.Say("ADD-13"));

        var guarded = (await parser.ParseAsync(masked.Text, new ParseContext(null, masked.Map, Words), Token)).First;
        var unguarded = (await parser.ParseAsync(masked.Text, new ParseContext(null, masked.Map), Token)).First;

        Assert.Equal((500m, AmountUnit.Dinars), (guarded.AmountValue, guarded.AmountUnit));
        Assert.Equal((500m, AmountUnit.None), (unguarded.AmountValue, unguarded.AmountUnit));
    }

    [Fact]
    public async Task TheGuardChangesNoParseOfTheOracle()
    {
        // Every say step of the development set parses the same with and without the words: the oracle's parses carry units.
        var path = Path.Combine(RepositoryRoot.Find(), "data", ScriptedParser.DevSetFileName);
        var parser = ScriptedParser.Load(path);
        var says = 0;
        foreach (var line in File.ReadLines(path).Where(l => l.Length > 0))
        {
            using var document = JsonDocument.Parse(line);
            foreach (var step in document.RootElement.GetProperty("steps").EnumerateArray())
            {
                if (!step.TryGetProperty("say", out var say))
                    continue;
                says++;
                var masked = Masker.Mask(say.GetString()!);
                Assert.Equal(
                    await parser.ParseAsync(masked.Text, new ParseContext(null, masked.Map), Token),
                    await parser.ParseAsync(masked.Text, new ParseContext(null, masked.Map, Words), Token));
            }
        }

        Assert.True(says > 0);
    }

    private static (string Bare, string WithArticle) KeyPair()
    {
        foreach (var w in Words)
        {
            if (Words.FirstOrDefault(o => o != w && ArabicText.Key(o) == ArabicText.Key(w) && o.Length > w.Length) is { } longer)
                return (w, longer);
        }

        throw new InvalidOperationException("The configured dinar words hold no bare form beside its form with the article.");
    }

    private static string BareReply(int value)
        => "{\"requests\":[{\"intent\":\"transfer\",\"recipient_text\":\"x\",\"amount_value\":" + value
            + ",\"amount_unit\":\"none\",\"biller_code\":null,\"biller_text\":null,\"bill_label_text\":null,\"operator_text\":null,"
            + "\"product_type\":null,\"product_value\":null,\"unsupported_reason\":null,\"currency_text\":null}]}";
}
