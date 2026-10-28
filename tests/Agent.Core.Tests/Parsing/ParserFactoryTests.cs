using System.Net;
using Agent.Core.Parsing;
using Agent.Core.Trace;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// <c>Agent:Parser</c> picks the parser: <c>scripted</c> (the default) constructs nothing of the model; <c>model</c>
/// (or its synonym <c>claude</c>) needs a known candidate, sane limits and a key, and refuses to start without them, never
/// printing the key. A <c>compatible</c> candidate reads its own <c>&lt;Vendor&gt;:ApiKey</c>, never the Anthropic one.
/// </summary>
public class ParserFactoryTests
{
    private const string SecretLookingKey = "sk-ant-test-secret-value-0001";

    private static Dictionary<string, string?> Haiku() => new()
    {
        ["Model:Candidate"] = "haiku",
        ["Model:Candidates:haiku:Provider"] = "anthropic",
        ["Model:Candidates:haiku:ModelId"] = "claude-haiku-5-5",
        ["Model:Candidates:haiku:InputPricePerMTok"] = "0.10",
        ["Model:Candidates:haiku:OutputPricePerMTok"] = "0.50",
    };

    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai";

    private static Dictionary<string, string?> CompatibleSettings() => new()
    {
        ["Agent:Parser"] = "model",
        ["Model:Candidate"] = "gemini",
        ["Model:Candidates:gemini:Provider"] = "compatible",
        ["Model:Candidates:gemini:BaseUrl"] = GeminiBaseUrl,
        ["Model:Candidates:gemini:ApiKeySetting"] = "Gemini:ApiKey",
        ["Model:Candidates:gemini:ModelId"] = "gemini-test",
        ["Model:Candidates:gemini:OutputMode"] = "json_schema",
    };

    private static ServiceProvider Build(Dictionary<string, string?> settings, string? environmentKey = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ITraceSink, NullTraceSink>();
        services.AddAgentParser(configuration, name => name == ParserFactory.ApiKeyVariable ? environmentKey : null);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void ScriptedIsTheDefaultAndBuildsNothingOfTheModel()
    {
        using var provider = Build([]);
        using var scope = provider.CreateScope();

        Assert.IsType<ScriptedParser>(scope.ServiceProvider.GetRequiredService<IParser>());
        Assert.Null(provider.GetService<IChatClient>());
    }

    [Fact]
    public void ClaudeWithAKeyFromSettingsGivesTheClaudeParser()
    {
        var settings = Haiku();
        settings["Agent:Parser"] = "claude";
        settings[ParserFactory.ApiKeySetting] = SecretLookingKey;
        using var provider = Build(settings);
        using var scope = provider.CreateScope();

        var parser = Assert.IsType<ClaudeParser>(scope.ServiceProvider.GetRequiredService<IParser>());
        Assert.Equal("claude:haiku", parser.Name);
        var options = provider.GetRequiredService<IOptions<ModelOptions>>().Value.Selected;
        Assert.Equal((1500, 20, (float?)null, "low"), (options?.MaxOutputTokens, options?.TimeoutSeconds, options?.Temperature, options?.Effort));
    }

    [Fact]
    public void ClaudeWithTheEnvironmentKeyStarts()
    {
        var settings = Haiku();
        settings["Agent:Parser"] = "claude";
        using var provider = Build(settings, environmentKey: SecretLookingKey);

        Assert.NotNull(provider.GetRequiredService<IOptions<ModelOptions>>().Value);
    }

    [Fact]
    public void ClaudeWithoutAKeyFailsWithAClearMessage()
    {
        var settings = Haiku();
        settings["Agent:Parser"] = "claude";
        using var provider = Build(settings);

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ModelOptions>>().Value);

        Assert.Contains(ParserFactory.ApiKeySetting, ex.Message, StringComparison.Ordinal);
        Assert.Contains(ParserFactory.ApiKeyVariable, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Model:Candidate", "gpt")]
    [InlineData("Model:Candidates:haiku:InputPricePerMTok", "-1")]
    [InlineData("Model:Candidates:haiku:CacheWritePricePerMTok", "-0.01")]
    [InlineData("Model:Candidates:haiku:CacheReadPricePerMTok", "-1")]
    [InlineData("Model:Candidates:haiku:PromptCacheTtl", "10m")]
    [InlineData("Model:Candidates:haiku:TimeoutSeconds", "0")]
    [InlineData("Model:Candidates:haiku:MaxOutputTokens", "0")]
    [InlineData("Model:Candidates:haiku:Provider", "openai")]
    [InlineData("Model:Candidates:haiku:ModelId", "")]
    [InlineData("Model:Candidates:haiku:Effort", "extreme")]
    public void AWrongCandidateFailsAndNeverShowsTheKey(string setting, string value)
    {
        var settings = Haiku();
        settings["Agent:Parser"] = "claude";
        settings[ParserFactory.ApiKeySetting] = SecretLookingKey;
        settings[setting] = value;
        using var provider = Build(settings);

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ModelOptions>>().Value);

        Assert.DoesNotContain(SecretLookingKey, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheModelClientNeverRetries()
    {
        // Review M2: one parse call per message (rule 2); a 429 or 529 shows the handover message instead of a retry.
        Assert.Equal(0, ParserFactory.CreateAnthropicClient(SecretLookingKey).MaxRetries);
    }

    [Fact]
    public void TheRunnerReadsTheKeyFromTheConsolesUserSecrets()
    {
        // Review R4: a regenerated UserSecretsId in Agent.Web would otherwise disconnect the runner from the key silently.
        var project = File.ReadAllText(Path.Combine(global::Agent.Eval.Running.EvalPaths.RepositoryRoot(), "src", "Agent.Web", "Agent.Web.csproj"));

        Assert.Contains("<UserSecretsId>" + global::Agent.Eval.Running.EvalCommand.WebUserSecretsId + "</UserSecretsId>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownParserIsRefused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(new Dictionary<string, string?> { ["Agent:Parser"] = "gpt" }));

        Assert.Contains("Agent:Parser", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'scripted'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'claude'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'model'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelWithACompatibleCandidateAndItsKeyGivesTheClaudeParser()
    {
        var settings = CompatibleSettings();
        settings["Gemini:ApiKey"] = SecretLookingKey;
        using var provider = Build(settings);
        using var scope = provider.CreateScope();

        var parser = Assert.IsType<ClaudeParser>(scope.ServiceProvider.GetRequiredService<IParser>());
        Assert.Equal("compatible:gemini", parser.Name);
    }

    [Fact]
    public void ClaudeIsASynonymOfModelForACompatibleCandidate()
    {
        var settings = CompatibleSettings();
        settings["Agent:Parser"] = "claude";
        settings["Gemini:ApiKey"] = SecretLookingKey;
        using var provider = Build(settings);
        using var scope = provider.CreateScope();

        Assert.IsType<ClaudeParser>(scope.ServiceProvider.GetRequiredService<IParser>());
    }

    [Fact]
    public void ACompatibleCandidateWithoutItsKeyFailsNamingTheSetting()
    {
        using var provider = Build(CompatibleSettings());

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ModelOptions>>().Value);

        Assert.Contains("Gemini:ApiKey", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Gemini__ApiKey", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ParserFactory.ApiKeySetting, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ParserFactory.ApiKeyVariable, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACompatibleCandidateIgnoresTheAnthropicKey()
    {
        var settings = CompatibleSettings();
        settings[ParserFactory.ApiKeySetting] = SecretLookingKey;
        using var provider = Build(settings, environmentKey: SecretLookingKey);

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ModelOptions>>().Value);

        Assert.Contains("Gemini:ApiKey", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretLookingKey, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCompatibleClientNeverRetriesAndTargetsTheBaseUrl()
    {
        // One parse call per message (rule 2): a 500 from the vendor is one HTTP call, never a retry.
        var handler = new CountingHandler();
        using var http = new HttpClient(handler);
        var candidate = ModelOptionsTests.Compatible().Selected!;
        using var client = ParserFactory.CreateCompatibleClient(SecretLookingKey, candidate, http);

        await Assert.ThrowsAnyAsync<Exception>(() => client.GetResponseAsync("hello", cancellationToken: TestContext.Current.CancellationToken));

        var uri = Assert.Single(handler.Requests);
        Assert.StartsWith(GeminiBaseUrl + "/", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    /// <summary>Answers every request with HTTP 500 and keeps each request's address.</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
