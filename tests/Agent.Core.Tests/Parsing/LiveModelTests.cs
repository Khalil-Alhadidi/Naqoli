using System.Text.Json;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Eval.Running;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The real model, gated: runs only with <c>ZAINCASH_MODEL_TESTS=1</c> and a key (<c>Anthropic:ApiKey</c> in the web
/// console's user secrets, or <c>ANTHROPIC_API_KEY</c>). Three example sentences of the frozen prompt go through the
/// real client for each candidate of <c>src/Agent.Web/appsettings.json</c>; the intent and the slots must equal the
/// prompt's own answer. The usage of each call is printed. The sentences are read from the prompt file, so this file
/// holds no Arabic.
/// </summary>
public class LiveModelTests(ITestOutputHelper output)
{
    /// <summary>The web console's user-secrets id (<c>src/Agent.Web/Agent.Web.csproj</c>).</summary>
    private const string UserSecretsId = EvalCommand.WebUserSecretsId;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Candidates => new("haiku", "sonnet", "opus");

    [Theory]
    [MemberData(nameof(Candidates))]
    public async Task ThreePromptExamplesParseAsThePromptSays(string candidate)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("ZAINCASH_MODEL_TESTS") == "1", "Set ZAINCASH_MODEL_TESTS=1 to call the real model.");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(EvalPaths.RepositoryRoot(), "src", "Agent.Web", "appsettings.json"), optional: false)
            .AddUserSecrets(UserSecretsId)
            .AddEnvironmentVariables()
            .Build();
        var key = ParserFactory.ApiKey(configuration);
        Assert.SkipUnless(key is not null, "No Anthropic key: set Anthropic:ApiKey in user secrets or ANTHROPIC_API_KEY.");

        var options = configuration.GetSection(ModelOptions.SectionName).Get<ModelOptions>() ?? throw new InvalidOperationException("No Model section.");
        options.Candidate = candidate;
        var parser = new ClaudeParser(
            ParserFactory.CreateChatClient(key, options.Candidates[candidate]),
            Microsoft.Extensions.Options.Options.Create(options),
            TimeProvider.System,
            new ClaudeParserTests.RecordingTrace(),
            NullLogger<ClaudeParser>.Instance);

        foreach (var (message, answer) in ClaudeParser.Examples().Take(3))
        {
            var masked = Masker.Mask(message);
            var expected = ParsedRequestValidator.Validate(JsonSerializer.Deserialize<ParsedRequestsDto>(answer)!, masked.Map).Request;

            var parsed = await parser.ParseAsync(masked.Text, new ParseContext(null, masked.Map), Token);

            var usage = parser.LastUsage;
            output.WriteLine($"{candidate}: {usage?.InputTokens} in, {usage?.OutputTokens} out, {usage?.CostUsd} USD, {usage?.Duration.TotalMilliseconds:0} ms");
            Assert.Equal(expected, parsed);
        }
    }
}
