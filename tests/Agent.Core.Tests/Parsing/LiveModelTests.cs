using System.Text.Json;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Requests;
using Agent.Eval.Cases;
using Agent.Eval.Running;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The real model, gated: runs only with <c>ZAINCASH_MODEL_TESTS=1</c> and a key (<c>Anthropic:ApiKey</c> in the web
/// console's user secrets, or <c>ANTHROPIC_API_KEY</c>), for each candidate of <c>src/Agent.Web/appsettings.json</c>.
/// Three example sentences of the frozen prompt must parse as the prompt's own answers, and the balance questions of the
/// development set must be the balance intent (D22). The usage of each call is printed. Every sentence is read from a
/// file (the prompt, the development set), so this file holds no Arabic; the held-out set is never read.
/// </summary>
public class LiveModelTests(ITestOutputHelper output)
{
    private const string UserSecretsId = EvalCommand.WebUserSecretsId;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Candidates => new("haiku", "sonnet", "opus");

    /// <summary>The Claude parser for <paramref name="candidate"/>; skips the test without the gate or a key.</summary>
    private static ClaudeParser Parser(string candidate)
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
        return new ClaudeParser(
            ParserFactory.CreateChatClient(key, options.Candidates[candidate]),
            Microsoft.Extensions.Options.Options.Create(options),
            TimeProvider.System,
            new ClaudeParserTests.RecordingTrace(),
            NullLogger<ClaudeParser>.Instance);
    }

    private void Print(string candidate, ClaudeParser parser)
    {
        var usage = parser.LastUsage;
        output.WriteLine($"{candidate}: {usage?.InputTokens} in, {usage?.OutputTokens} out, {usage?.CostUsd} USD, {usage?.Duration.TotalMilliseconds:0} ms");
    }

    [Theory]
    [MemberData(nameof(Candidates))]
    public async Task ThreePromptExamplesParseAsThePromptSays(string candidate)
    {
        var parser = Parser(candidate);

        foreach (var (message, answer) in ClaudeParser.Examples().Take(3))
        {
            var masked = Masker.Mask(message);
            var expected = ParsedRequestValidator.Validate(JsonSerializer.Deserialize<ParsedRequestsDto>(answer)!, masked.Map).Request;

            var parsed = await parser.ParseAsync(masked.Text, new ParseContext(null, masked.Map), Token);

            Print(candidate, parser);
            Assert.Equal(expected, parsed);
        }
    }

    [Theory]
    [MemberData(nameof(Candidates))]
    public async Task TheBalanceQuestionIsItsOwnIntent(string candidate)
    {
        var parser = Parser(candidate);
        var questions = CaseLoader.Load(Path.Combine(EvalPaths.RepositoryRoot(), "data", "requests.dev.jsonl"))
            .Where(c => c.Category == "Clean" && c.Type == "Balance")
            .ToList();
        Assert.NotEmpty(questions);

        foreach (var question in questions)
        {
            var masked = Masker.Mask(question.Message);

            var parsed = await parser.ParseAsync(masked.Text, new ParseContext(null, masked.Map), Token);

            Print(candidate, parser);
            Assert.Equal(RequestIntent.Balance, parsed.Intent);
        }
    }
}
