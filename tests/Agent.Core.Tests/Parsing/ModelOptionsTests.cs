using Agent.Core.Parsing;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The start-time validation of the <c>Model</c> section for both providers (docs/plans/model-providers.md, "Validation at
/// start"): the <c>compatible</c> fields, the pacing on either provider, the checks that apply to <c>anthropic</c> only,
/// the label each provider gives, and the shape of a vendor key setting.
/// </summary>
public class ModelOptionsTests
{
    private const string Prefix = "Model:Candidates:gemini:";

    /// <summary>A fresh, valid options object whose selected candidate is the <c>compatible</c> candidate <c>gemini</c>.</summary>
    internal static ModelOptions Compatible() => new()
    {
        Candidate = "gemini",
        Candidates =
        {
            ["gemini"] = new ModelCandidate
            {
                Provider = ModelOptions.CompatibleProvider,
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
                ApiKeySetting = "Gemini:ApiKey",
                ModelId = "gemini-test",
                InputPricePerMTok = 0,
                OutputPricePerMTok = 0,
                CacheWritePricePerMTok = 0,
                CacheReadPricePerMTok = 0,
            },
        },
    };

    [Fact]
    public void AValidCompatibleCandidateHasNoProblems()
    {
        Assert.Empty(Compatible().Problems());
    }

    [Fact]
    public void AnUnknownProviderIsOneProblem()
    {
        var options = Compatible();
        options.Selected!.Provider = "openai";

        Assert.Equal([Prefix + "Provider must be one of anthropic, compatible."], options.Problems());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://x")]
    [InlineData("relative/path")]
    public void ACompatibleCandidateNeedsAnAbsoluteHttpsBaseUrl(string? url)
    {
        var options = Compatible();
        options.Selected!.BaseUrl = url;

        Assert.Equal([Prefix + "BaseUrl must be an absolute https address."], options.Problems());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ConnectionStrings:Agent")]
    [InlineData("Gemini:Secret")]
    [InlineData("1Gemini:ApiKey")]
    public void ACompatibleCandidateNeedsAVendorKeySetting(string? setting)
    {
        var options = Compatible();
        options.Selected!.ApiKeySetting = setting;

        Assert.Equal([Prefix + "ApiKeySetting must be '<Vendor>:ApiKey'."], options.Problems());
    }

    [Theory]
    [InlineData("Anthropic:ApiKey", "https://api.anthropic.com/v1", true)]
    [InlineData("Anthropic:ApiKey", "https://API.anthropic.com/v1", true)]
    [InlineData("anthropic:ApiKey", "https://api.anthropic.com/v1", true)]
    [InlineData("Anthropic:ApiKey", "https://openrouter.ai/api/v1", false)]
    [InlineData("Anthropic:ApiKey", "https://api.anthropic.com.example.org/v1", false)]
    // Configuration keys are case-insensitive: these read the Anthropic key too.
    [InlineData("anthropic:ApiKey", "https://openrouter.ai/api/v1", false)]
    [InlineData("ANTHROPIC:ApiKey", "https://openrouter.ai/api/v1", false)]
    public void TheAnthropicKeyGoesOnlyToAnthropicsHost(string setting, string baseUrl, bool allowed)
    {
        var options = Compatible();
        options.Selected!.ApiKeySetting = setting;
        options.Selected.BaseUrl = baseUrl;

        string[] expected = allowed ? [] : [Prefix + "ApiKeySetting may be the Anthropic key only when BaseUrl is api.anthropic.com."];
        Assert.Equal(expected, options.Problems());
    }

    [Theory]
    [InlineData(30, 29_999, true)]
    [InlineData(30, 30_000, false)]
    [InlineData(1, 4_000, false)]
    public void ThePaceMustBeBelowTheTimeout(int timeoutSeconds, int paceMs, bool allowed)
    {
        var options = Compatible();
        options.Selected!.TimeoutSeconds = timeoutSeconds;
        options.Selected.MinMillisecondsBetweenCalls = paceMs;

        string[] expected = allowed ? [] : [Prefix + "MinMillisecondsBetweenCalls must be below TimeoutSeconds."];
        Assert.Equal(expected, options.Problems());
    }

    [Fact]
    public void AnUnknownOutputModeIsOneProblem()
    {
        var options = Compatible();
        options.Selected!.OutputMode = "tools";

        Assert.Equal([Prefix + "OutputMode must be one of json_schema, json_object."], options.Problems());
    }

    [Fact]
    public void NegativePacingIsOneProblemOnEitherProvider()
    {
        var compatible = Compatible();
        compatible.Selected!.MinMillisecondsBetweenCalls = -1;
        var anthropic = ClaudeParserTests.Options();
        anthropic.Selected!.MinMillisecondsBetweenCalls = -1;

        Assert.Equal([Prefix + "MinMillisecondsBetweenCalls must not be below 0."], compatible.Problems());
        Assert.Equal(["Model:Candidates:haiku:MinMillisecondsBetweenCalls must not be below 0."], anthropic.Problems());
    }

    [Fact]
    public void EffortAndCacheTtlAreNotCheckedOnACompatibleCandidate()
    {
        var options = Compatible();
        options.Selected!.Effort = "silly";
        options.Selected.PromptCacheTtl = "2h";

        Assert.Empty(options.Problems());
    }

    [Fact]
    public void TheDefaultsOfTheNewFieldsAreJsonSchemaAndNoPacing()
    {
        var candidate = new ModelCandidate();

        Assert.Equal(ModelOptions.OutputModeJsonSchema, candidate.OutputMode);
        Assert.Equal(0, candidate.MinMillisecondsBetweenCalls);
        Assert.True(candidate.IsAnthropic);
        Assert.False(candidate.IsCompatible);
        Assert.Empty(ClaudeParserTests.Options().Problems());
    }

    [Theory]
    [InlineData("anthropic", "claude")]
    [InlineData("compatible", "compatible")]
    public void TheLabelFollowsTheProvider(string provider, string label)
    {
        Assert.Equal(label, ModelOptions.LabelOf(provider));
        Assert.Equal(label, new ModelCandidate { Provider = provider }.Label);
    }

    [Fact]
    public void AnUnknownProviderHasNoLabel()
    {
        Assert.Throws<InvalidOperationException>(() => ModelOptions.LabelOf("openai"));
    }

    [Theory]
    [InlineData("Gemini:ApiKey", true)]
    [InlineData("OpenRouter:ApiKey", true)]
    [InlineData("Anthropic:ApiKey", true)]
    [InlineData("ConnectionStrings:Agent", false)]
    [InlineData("Gemini:ApiKey:Extra", false)]
    public void AVendorKeySettingIsVendorColonApiKey(string setting, bool expected)
    {
        Assert.Equal(expected, ModelOptions.IsVendorKeySetting(setting));
    }

    [Fact]
    public void RequestFieldsOnAnAnthropicCandidateIsOneProblem()
    {
        var options = ClaudeParserTests.Options();
        options.Selected!.RequestFields = """{"reasoning":{"effort":"low"}}""";

        Assert.Equal(["Model:Candidates:haiku:RequestFields is only for a compatible candidate."], options.Problems());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"reasoning\":")]
    [InlineData("[1,2]")]
    [InlineData("\"low\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void RequestFieldsThatIsNotAJsonObjectIsOneProblem(string fields)
    {
        var options = Compatible();
        options.Selected!.RequestFields = fields;

        Assert.Equal([Prefix + "RequestFields must be a JSON object."], options.Problems());
    }

    [Theory]
    [InlineData("model")]
    [InlineData("messages")]
    [InlineData("response_format")]
    [InlineData("max_completion_tokens")]
    [InlineData("max_tokens")]
    [InlineData("temperature")]
    [InlineData("stream")]
    [InlineData("tools")]
    [InlineData("tool_choice")]
    public void RequestFieldsMustNotSetAFieldTheParserSets(string key)
    {
        var options = Compatible();
        options.Selected!.RequestFields = "{\"reasoning\":{\"effort\":\"low\"},\"" + key + "\":1}";

        Assert.Equal([Prefix + "RequestFields must not set " + key + "."], options.Problems());
    }

    [Fact]
    public void TheReservedKeysAreComparedOrdinally()
    {
        var options = Compatible();
        options.Selected!.RequestFields = """{"Model":"x"}""";

        Assert.Empty(options.Problems());
    }

    [Theory]
    [InlineData(null)]
    // An empty value (a user secret that clears the shipped one) is absent, not a broken object.
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("{}")]
    [InlineData(" { } ")]
    [InlineData("""{"reasoning":{"effort":"low"}}""")]
    [InlineData("""{"provider":{"order":["a","b"],"allow_fallbacks":false},"top_k":40}""")]
    public void AnAbsentEmptyOrNestedObjectIsAccepted(string? fields)
    {
        var options = Compatible();
        options.Selected!.RequestFields = fields;

        Assert.Empty(options.Problems());
    }

    [Theory]
    [InlineData(ModelOptions.CompatibleProvider, null, null)]
    [InlineData(ModelOptions.CompatibleProvider, " {} ", null)]
    [InlineData(ModelOptions.CompatibleProvider, """{ "reasoning" : { "effort" : "low" } }""", """{"reasoning":{"effort":"low"}}""")]
    // Not an object: the validation refuses it on the selected candidate; elsewhere it is shown as written.
    [InlineData(ModelOptions.CompatibleProvider, " [1] ", "[1]")]
    [InlineData(ModelOptions.AnthropicProvider, """{"reasoning":{"effort":"low"}}""", null)]
    public void TheRequestFieldsTextIsTheCompactObjectOrNothing(string provider, string? fields, string? expected)
    {
        Assert.Equal(expected, new ModelCandidate { Provider = provider, RequestFields = fields }.RequestFieldsText());
    }

    [Fact]
    public void TheRequestFieldsParseToTheObjectOrToNothing()
    {
        Assert.Null(new ModelCandidate().RequestFieldsObject());
        Assert.Null(new ModelCandidate { RequestFields = "{}" }.RequestFieldsObject());
        var fields = new ModelCandidate { RequestFields = """{ "reasoning" : { "effort" : "low" } }""" }.RequestFieldsObject();
        Assert.Equal("""{"reasoning":{"effort":"low"}}""", fields!.ToJsonString());
    }
}
