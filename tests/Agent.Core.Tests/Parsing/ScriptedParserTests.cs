using System.Text.Json;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Requests;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The scripted parser, an oracle over the development set: an exact dev-set message (masked and normalised as the
/// conversation masks the typed text) returns that case's parse from <c>requests.dev.parses.jsonl</c>; anything else is
/// <see cref="RequestIntent.Unclear"/>. It loads only a file named <c>requests.dev.jsonl</c> (rule 7: the held-out set
/// is never opened), and the throwaway sets of these tests live in a temp directory, never at the held-out path.
/// </summary>
public sealed class ScriptedParserTests : IDisposable
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly ParseContext NothingPending = new(PendingNote: null);

    private static string DevSetPath => Path.Combine(RepositoryRoot.Find(), "data", ScriptedParser.DevSetFileName);

    private readonly string temp = Directory.CreateTempSubdirectory("scripted-parser-").FullName;

    public void Dispose() => Directory.Delete(temp, recursive: true);

    /// <summary>Writes a throwaway dev set and its sidecar, one JSON line each, in the temp directory; returns the dev set's path.</summary>
    private string WriteSet(string[] cases, string[] parses)
    {
        var dev = Path.Combine(temp, ScriptedParser.DevSetFileName);
        File.WriteAllLines(dev, cases);
        File.WriteAllLines(Path.Combine(temp, ScriptedParser.SidecarFileName), parses);
        return dev;
    }

    private static string Case(string id, params string[] says)
        => JsonSerializer.Serialize(new { id, steps = says.Select(s => new { say = s }).Append<object>(new { tap = "confirm" }) });

    private static string Line(string id, int step, string say, string requests)
        => "{\"id\":" + JsonSerializer.Serialize(id) + ",\"step\":" + step + ",\"say\":" + JsonSerializer.Serialize(say)
            + ",\"parse\":{\"requests\":[" + requests + "]}}";

    private const string Transfer25000 =
        "{\"intent\":\"transfer\",\"recipient_text\":\"x\",\"amount_value\":25000,\"amount_unit\":\"none\"}";

    [Theory]
    [InlineData("requests.heldout.jsonl")]
    [InlineData("requests.dev.jsonl.bak")]
    [InlineData("other.jsonl")]
    [InlineData("Requests.Dev.jsonl")]
    public void OnlyTheDevSetFileNameIsAccepted(string name)
    {
        // A throwaway file of that name exists, and the same name in a directory that does not: both are refused by
        // name, before the file system is touched (never a FileNotFoundException).
        var existing = Path.Combine(temp, name);
        File.WriteAllText(existing, string.Empty);
        var missing = Path.Combine(temp, "no-such-directory", name);

        Assert.Throws<ArgumentException>(() => ScriptedParser.Load(existing));
        Assert.Throws<ArgumentException>(() => ScriptedParser.Load(missing));
    }

    [Fact]
    public async Task TheDevSetLoadsAndEverySayStepHasAParse()
    {
        var parser = ScriptedParser.Load(DevSetPath);

        // Every say step parses to a request, except a typed yes or no while a card waits (the conversation never parses
        // it; its line is unclear).
        var says = 0;
        foreach (var line in File.ReadLines(DevSetPath).Where(l => l.Length > 0))
        {
            using var document = JsonDocument.Parse(line);
            var id = document.RootElement.GetProperty("id").GetString();
            foreach (var step in document.RootElement.GetProperty("steps").EnumerateArray())
            {
                if (!step.TryGetProperty("say", out var say))
                    continue;
                says++;
                var yesOrNo = step.TryGetProperty("expect", out var expect)
                    && expect.TryGetProperty("reply", out var reply)
                    && reply.GetString() == "points_to_confirm";

                var typed = Masker.Mask(say.GetString()!);
                var parsed = await parser.ParseAsync(typed.Text, new ParseContext(null, typed.Map), Token);

                Assert.True(parsed.Problem is null, id);
                Assert.True(yesOrNo == (parsed.Intent == RequestIntent.Unclear), id);
            }
        }

        Assert.True(says >= 72, "Every case says at least once.");
    }

    [Fact]
    public async Task TheHammoudiCaseIsTheTransferToS01()
    {
        var parser = ScriptedParser.Load(DevSetPath);
        var sentence = File.ReadLines(DevSetPath)
            .Select(l => JsonDocument.Parse(l).RootElement)
            .First(c => c.GetProperty("id").GetString() == "CLN-01")
            .GetProperty("message").GetString()!;

        var parsed = await parser.ParseAsync(sentence, NothingPending, Token);

        Assert.Equal(ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand), parsed);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("send fifty thousand to my brother")]
    [InlineData("")]
    public async Task AnUnknownTextIsUnclear(string typed)
    {
        var parser = ScriptedParser.Load(DevSetPath);

        Assert.Equal(new ParsedRequest { Intent = RequestIntent.Unclear }, await parser.ParseAsync(typed, NothingPending, Token));
    }

    [Fact]
    public async Task ATypedNumberComesBackAsItsToken()
    {
        // The sidecar names the number as typed; the parser returns its token, as the model does, and the conversation
        // un-masks it with the message's own map.
        var dev = WriteSet(
            [Case("T-01", "send 25000 to 0770 123 4567")],
            [Line("T-01", 0, "send 25000 to 0770 123 4567", "{\"intent\":\"transfer\",\"recipient_text\":\"0770 123 4567\",\"amount_value\":25000,\"amount_unit\":\"none\"}")]);
        var parser = ScriptedParser.Load(dev);

        var typed = Masker.Mask("send 25000 to 07701234567");
        var parsed = await parser.ParseAsync(typed.Text, new ParseContext(null, typed.Map), Token);

        Assert.Equal(ParsedRequest.Transfer("<NUM_1>", 25_000, AmountUnit.None), parsed);
    }

    [Fact]
    public async Task ArabicIndicDigitsAndExtraSpacesStillMatch()
    {
        // D20: typed input is accepted in either digit form, in any spacing.
        var dev = WriteSet([Case("T-01", "send 25000 to x")], [Line("T-01", 0, "send 25000 to x", Transfer25000)]);
        var parser = ScriptedParser.Load(dev);
        var expected = ParsedRequest.Transfer("x", 25_000, AmountUnit.None);

        Assert.Equal(expected, await parser.ParseAsync("send ٢٥٠٠٠ to x", NothingPending, Token));
        Assert.Equal(expected, await parser.ParseAsync("  send  25000\tto x ", NothingPending, Token));
    }

    [Fact]
    public void ASidecarTextThatDiffersFromTheDevSetIsRefused()
    {
        var dev = WriteSet([Case("T-01", "send 25000 to x")], [Line("T-01", 0, "send 25000 to y", Transfer25000)]);

        var error = Assert.Throws<InvalidDataException>(() => ScriptedParser.Load(dev));
        Assert.Contains("T-01", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("send", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingSidecarLineIsRefused()
    {
        var dev = WriteSet([Case("T-01", "send 25000 to x", "send 25000 to z")], [Line("T-01", 0, "send 25000 to x", Transfer25000)]);

        var error = Assert.Throws<InvalidDataException>(() => ScriptedParser.Load(dev));
        Assert.Contains("T-01", error.Message, StringComparison.Ordinal);
        Assert.Contains("step 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASidecarLineForAStepThatDoesNotSayIsRefused()
    {
        // Step 1 of the case is the tap.
        var dev = WriteSet(
            [Case("T-01", "send 25000 to x")],
            [Line("T-01", 0, "send 25000 to x", Transfer25000), Line("T-01", 1, "send 25000 to x", Transfer25000)]);

        var error = Assert.Throws<InvalidDataException>(() => ScriptedParser.Load(dev));
        Assert.Contains("step 1", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"intent\":\"Transfer\",\"recipient_text\":\"x\",\"amount_value\":25000,\"amount_unit\":\"none\"}")]
    [InlineData("{\"intent\":\"transfer\",\"recipient_text\":\"x\",\"amount_value\":25000,\"amount_unit\":\"thousands\"}")]
    [InlineData("{\"intent\":\"transfer\",\"recipient_text\":\"x\",\"amount_value\":25000}")]
    [InlineData("{\"intent\":\"bill\",\"biller_code\":\"gas\",\"amount_unit\":\"none\"}")]
    [InlineData("{\"intent\":\"unsupported\",\"unsupported_reason\":\"loan\",\"amount_unit\":\"none\"}")]
    [InlineData("{\"intent\":\"transfer\",\"recipient\":\"x\",\"amount_unit\":\"none\"}")]
    public void AParseOutsideTheSetsIsRefusedAtLoad(string request)
    {
        var dev = WriteSet([Case("T-01", "send 25000 to x")], [Line("T-01", 0, "send 25000 to x", request)]);

        var error = Assert.Throws<InvalidDataException>(() => ScriptedParser.Load(dev));
        Assert.Contains("T-01", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameTextWithTwoDifferentParsesIsRefused()
    {
        var dev = WriteSet(
            [Case("T-01", "send 25000 to x"), Case("T-02", "send  25000 to x")],
            [
                Line("T-01", 0, "send 25000 to x", Transfer25000),
                Line("T-02", 0, "send  25000 to x", "{\"intent\":\"transfer\",\"recipient_text\":\"x\",\"amount_value\":25,\"amount_unit\":\"thousand\"}"),
            ]);

        var error = Assert.Throws<InvalidDataException>(() => ScriptedParser.Load(dev));
        Assert.Contains("T-02", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItReportsZeroUsageAfterEachParse()
    {
        var parser = new ScriptedParser([new ScriptedSentence("balance", ParsedRequest.Balance())]);
        Assert.Null(parser.LastUsage);

        await parser.ParseAsync("balance", NothingPending, Token);

        var usage = Assert.IsType<ParseUsage>(parser.LastUsage);
        Assert.Equal((0, 0, 0m), (usage.InputTokens, usage.OutputTokens, usage.CostUsd));
        Assert.True(usage.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ASentenceWithAPhoneNumberMatchesTheMaskedText()
    {
        // The test constructor masks each sentence as the conversation masks the typed text.
        var request = ParsedRequest.Transfer("07701234567", 25_000, AmountUnit.Dinars);
        var parser = new ScriptedParser([new ScriptedSentence("send 25000 to 07701234567", request)]);

        var typed = Masker.Mask("send 25000 to 0770 123 4567");

        Assert.Equal("send 25000 to <NUM_1>", typed.Text);
        Assert.Equal(request, await parser.ParseAsync(typed.Text, new ParseContext(null, typed.Map), Token));
    }

    [Fact]
    public void TheNameIsScripted()
    {
        Assert.Equal("scripted", new ScriptedParser([]).Name);
    }
}
