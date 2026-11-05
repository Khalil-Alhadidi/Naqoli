using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Requests;
using Agent.Eval.Running;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The first thing to run on a <c>compatible</c> candidate (docs/history/plans/model-providers.md, "Tests"), gated: runs only with
/// <c>ZAINCASH_MODEL_TESTS=1</c>, when the <c>Model:Candidate</c> of <c>src/Agent.Web/appsettings.json</c>, the web
/// console's user secrets or the environment is a <c>compatible</c> candidate and its <c>&lt;Vendor&gt;:ApiKey</c> is set.
/// Four clean development cases, one per intent, must parse as the scripted parser's oracle line in
/// <c>data/requests.dev.parses.jsonl</c> holds for them: the intent, and the one slot that names the request. Every
/// sentence and every expected value is read from those files, so this file holds no Arabic and no hand-written answer;
/// the held-out set is never read. A failure on the output mode is fixed by setting that candidate's
/// <c>OutputMode</c> to <c>json_object</c>.
/// </summary>
public class LiveCompatibleTests(ITestOutputHelper output)
{
    /// <summary>One pacer for the class, so the candidate's pace holds across the four live calls, as in a real run.</summary>
    private static readonly ParsePacer Pacer = new(TimeProvider.System);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A transfer, a bill, a purchase and a balance question, with the sidecar field each is checked on (null: intent only).</summary>
    public static TheoryData<string, string?> Cases => new()
    {
        { "CLN-01", "recipient_text" },
        { "CLN-11", "biller_code" },
        { "CLN-27", "product_type" },
        { "CLN-31", null },
    };

    /// <summary>The parser for the selected <c>compatible</c> candidate; skips the test without the gate, such a candidate, or its key.</summary>
    private static (ClaudeParser Parser, string Candidate) Parser()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("ZAINCASH_MODEL_TESTS") == "1", "Set ZAINCASH_MODEL_TESTS=1 to call the real model.");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(EvalPaths.RepositoryRoot(), "src", "Agent.Web", "appsettings.json"), optional: false)
            .AddUserSecrets(EvalCommand.WebUserSecretsId)
            .AddEnvironmentVariables()
            .Build();
        var options = configuration.GetSection(ModelOptions.SectionName).Get<ModelOptions>() ?? throw new InvalidOperationException("No Model section.");
        var candidate = options.Selected;
        Assert.SkipUnless(candidate is { IsCompatible: true }, "Model:Candidate is not a compatible candidate: set it in user secrets (for example gemini).");
        // The candidate is checked before any setting is read as its key.
        Assert.Empty(options.Problems());
        var key = ParserFactory.ApiKey(configuration, candidate);
        Assert.SkipUnless(key is not null, "No key for the candidate: set " + candidate.ApiKeySetting + " in user secrets.");

        var parser = new ClaudeParser(
            ParserFactory.CreateChatClient(key, candidate),
            Microsoft.Extensions.Options.Options.Create(options),
            Pacer,
            TimeProvider.System,
            new ClaudeParserTests.RecordingTrace(),
            NullLogger<ClaudeParser>.Instance);
        return (parser, options.Candidate!);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ACleanCaseParsesAsTheOracleLine(string id, string? field)
    {
        var (parser, candidate) = Parser();
        var masked = Masker.Mask(DevSetText.Say(id));

        var parsed = (await parser.ParseAsync(masked.Text, new ParseContext(null, masked.Map), Token)).First;

        var usage = parser.LastUsage;
        output.WriteLine($"{candidate} {id}: {usage?.InputTokens} in, {usage?.CacheReadTokens} cached, {usage?.OutputTokens} out, {usage?.CostUsd} USD, {usage?.Duration.TotalMilliseconds:0} ms, problem {parsed.Problem ?? "none"}");
        var intent = DevSetText.SidecarText(id, 0, 0, "intent")!;
        Assert.Equal(Enum.Parse<RequestIntent>(intent, ignoreCase: true), parsed.Intent);
        if (field is null)
            return;

        var actual = field switch
        {
            "recipient_text" => parsed.RecipientText,
            "biller_code" => parsed.BillerCode,
            "product_type" => parsed.ProductType,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a field this test checks."),
        };
        Assert.Equal(DevSetText.SidecarText(id, 0, 0, field), actual);
    }
}
