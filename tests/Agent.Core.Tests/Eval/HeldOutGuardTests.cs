using System.Security.Cryptography;
using System.Text;
using Agent.Core.Parsing;
using Agent.Eval.Running;
using Microsoft.Extensions.Configuration;

namespace Agent.Core.Tests.Eval;

/// <summary>
/// Rule 7's guard on a temporary set directory: a dummy <c>requests.heldout.jsonl</c> of two synthetic English lines
/// and a dummy disclosure file. The real held-out file is never touched.
/// </summary>
public sealed class HeldOutGuardTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("eval-guard-");

    public void Dispose() => directory.Delete(recursive: true);

    private string HeldOutPath => Path.Combine(directory.FullName, HeldOutGuard.HeldOutFileName);

    private string DisclosurePath => Path.Combine(directory.FullName, "disclosure.md");

    private string WriteDummySet()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "{\"id\": \"DUMMY-01\", \"message\": \"send ten to a friend\"}\n{\"id\": \"DUMMY-02\", \"message\": \"what is my balance\"}\n");
        File.WriteAllBytes(HeldOutPath, bytes);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private void WriteDisclosure(string hash)
        => File.WriteAllText(DisclosurePath, "# Disclosure\n\n- Other line `" + new string('b', 64) + "`.\n- Held-out file `data/requests.heldout.jsonl`: SHA-256 `" + hash + "`.\n");

    [Fact]
    public void WithoutTheFlagTheGuardRefusesAndOpensNothing()
    {
        var hash = WriteDummySet();
        WriteDisclosure(hash);

        // The dummy file is locked against every other open: had the guard tried to read it, it would throw.
        HeldOutDecision decision;
        using (new FileStream(HeldOutPath, FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(DisclosurePath, FileMode.Open, FileAccess.Read, FileShare.None))
            decision = HeldOutGuard.Check(["heldout"], directory.FullName, DisclosurePath);

        Assert.False(decision.Allowed);
        Assert.Null(decision.FileHash);
        Assert.Null(decision.CommittedHash);
        Assert.Equal(HeldOutGuard.RuleSentence, decision.Message);
    }

    [Fact]
    public void WithoutTheFlagTheGuardNeedsNoDirectoryAtAll()
    {
        var missing = Path.Combine(directory.FullName, "does-not-exist");

        var decision = HeldOutGuard.Check(["heldout", "--only", "DUMMY-01"], missing, Path.Combine(missing, "disclosure.md"));

        Assert.False(decision.Allowed);
        Assert.Equal(HeldOutGuard.RuleSentence, decision.Message);
    }

    [Fact]
    public void WithTheFlagTheHashIsComputedAndComparedWithTheCommittedOne()
    {
        var hash = WriteDummySet();
        WriteDisclosure(hash);

        var decision = HeldOutGuard.Check(["heldout", HeldOutGuard.Milestone11Flag], directory.FullName, DisclosurePath);

        Assert.True(decision.Allowed);
        Assert.Equal((hash, hash), (decision.FileHash, decision.CommittedHash));
        Assert.Contains(hash, decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithTheFlagAChangedFileIsRefused()
    {
        WriteDummySet();
        var committed = new string('c', 64);
        WriteDisclosure(committed);

        var decision = HeldOutGuard.Check(["heldout", HeldOutGuard.Milestone11Flag], directory.FullName, DisclosurePath);

        Assert.False(decision.Allowed);
        Assert.Equal(committed, decision.CommittedHash);
        Assert.NotNull(decision.FileHash);
        Assert.Contains(decision.FileHash!, decision.Message, StringComparison.Ordinal);
        Assert.Contains(committed, decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithTheFlagAndNoCommittedHashTheGuardRefuses()
    {
        WriteDummySet();
        File.WriteAllText(DisclosurePath, "# Disclosure\n");

        var decision = HeldOutGuard.Check(["heldout", HeldOutGuard.Milestone11Flag], directory.FullName, DisclosurePath);

        Assert.False(decision.Allowed);
        Assert.Null(decision.FileHash);
    }

    [Fact]
    public void TheDevSetPassesTheGuardWithoutReadingAnything()
    {
        var decision = HeldOutGuard.Check(["dev"], Path.Combine(directory.FullName, "none"), Path.Combine(directory.FullName, "none.md"));

        Assert.True(decision.Allowed);
        Assert.Null(decision.FileHash);
    }

    [Fact]
    public void TheRepositoryDisclosureRecordsAHeldOutHash()
    {
        // Reads docs/disclosure.md only, never the held-out file.
        var disclosure = Path.Combine(EvalPaths.RepositoryRoot(), "docs", "disclosure.md");

        var committed = HeldOutGuard.CommittedHash(disclosure);

        Assert.NotNull(committed);
        Assert.Equal(64, committed!.Length);
    }

    [Fact]
    public async Task TheCommandWithoutTheFlagExitsTwoAndPrintsTheRule()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();

        var code = await EvalCommand.RunAsync(["heldout"], output, errors, TestContext.Current.CancellationToken);

        Assert.Equal(EvalCommand.Refused, code);
        Assert.Contains(HeldOutGuard.RuleSentence, errors.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData]
    [InlineData("staging")]
    [InlineData("dev", "--milestone-11-run")]
    [InlineData("dev", "--only")]
    [InlineData("dev", "--wallet", "not a url")]
    public void BadCommandLinesAreRefused(params string[] args)
    {
        Assert.Null(EvalArguments.Parse(args, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TheCommandLineIsParsed()
    {
        var parsed = EvalArguments.Parse(["dev", "--only", "A-01, B-02", "--wallet", "http://localhost:5999", "--out", "reports"], out _);

        Assert.NotNull(parsed);
        Assert.Equal(["A-01", "B-02"], parsed!.Only);
        Assert.Equal(new Uri("http://localhost:5999"), parsed.Wallet);
        Assert.Equal("reports", parsed.Out);
    }

    [Theory]
    [InlineData(new[] { "dev" }, "scripted")]
    [InlineData(new[] { "dev", "--parser", "scripted" }, "scripted")]
    [InlineData(new[] { "dev", "--parser", "claude:haiku" }, "claude:haiku")]
    [InlineData(new[] { "dev", "--only", "A-01", "--parser", "claude:opus" }, "claude:opus")]
    [InlineData(new[] { "dev", "--parser", "compatible:gemini" }, "compatible:gemini")]
    public void TheParserIsReadFromTheCommandLine(string[] args, string expected)
    {
        var parsed = EvalArguments.Parse(args, out var error);

        Assert.Null(error);
        Assert.Equal(expected, parsed?.Parser);
    }

    [Fact]
    public void TheRepeatCountIsReadFromTheCommandLine()
    {
        Assert.Equal(3, EvalArguments.Parse(["dev", "--parser", "claude:haiku", "--repeat", "3"], out var error)?.Repeat);
        Assert.Null(error);
        Assert.Equal(1, EvalArguments.Parse(["dev"], out _)?.Repeat);
        Assert.Equal(20, EvalArguments.Parse(["dev", "--repeat", "20"], out _)?.Repeat);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("21")]
    [InlineData("x")]
    [InlineData("-1")]
    [InlineData("2.5")]
    public void ARepeatOutsideOneToTwentyIsRefused(string repeat)
    {
        Assert.Null(EvalArguments.Parse(["dev", "--repeat", repeat], out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("M7A")]
    [InlineData("m7a b")]
    [InlineData("../m7a")]
    [InlineData("")]
    public void ALabelOutsideThePatternIsRefused(string label)
    {
        Assert.Null(EvalArguments.Parse(["dev", "--label", label], out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("gpt")]
    [InlineData("claude")]
    [InlineData("claude:")]
    [InlineData("claude:Haiku 5")]
    public void AnUnknownParserIsRefused(string parser)
    {
        Assert.Null(EvalArguments.Parse(["dev", "--parser", parser], out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("compatible:")]
    [InlineData("gemini")]
    [InlineData("model:gemini")]
    public void AParserWithoutAKnownPrefixIsRefused(string parser)
    {
        Assert.Null(EvalArguments.Parse(["dev", "--parser", parser], out var error));
        Assert.Equal("--parser needs 'scripted', 'claude:<candidate>' or 'compatible:<candidate>', a candidate of the Model section.", error);
    }

    [Fact]
    public void TheCandidateAndLabelFollowThePrefix()
    {
        var compatible = EvalArguments.Parse(["dev", "--parser", "compatible:gemini"], out _);
        var claude = EvalArguments.Parse(["dev", "--parser", "claude:haiku"], out _);
        var scripted = EvalArguments.Parse(["dev"], out _);

        Assert.Equal(("gemini", "compatible"), (compatible?.Candidate, compatible?.ProviderLabel));
        Assert.Equal(("haiku", "claude"), (claude?.Candidate, claude?.ProviderLabel));
        Assert.Equal(((string?)null, (string?)null), (scripted?.Candidate, scripted?.ProviderLabel));
    }

    [Fact]
    public void TheHeaderOfACompatibleCandidateSaysProviderModeAndPacing()
    {
        var gemini = Gemini();

        var header = EvalCommand.ModelSettings("gemini", gemini).ToDictionary();

        Assert.Equal("compatible", header["Model:Provider"]);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai", header["Model:BaseUrl"]);
        Assert.Equal("json_schema", header["Model:OutputMode"]);
        Assert.Equal("not sent", header["Model:RequestFields"]);
        Assert.Equal("not sent", header["Model:Effort"]);
        Assert.Equal("not sent", header["Model:PromptCacheTtl"]);
        Assert.Equal("0", header["Model:MinMillisecondsBetweenCalls"]);
        Assert.Equal("gemini-test", header["Model:ModelId"]);
    }

    [Fact]
    public void TheHeaderOfAnAnthropicCandidateIsUnchangedPlusProviderAndTtl()
    {
        var haiku = new ModelCandidate { ModelId = "claude-haiku-5-5" };

        var header = EvalCommand.ModelSettings("haiku", haiku).ToDictionary();

        Assert.Equal(ClaudeParser.PromptVersion, header["Model:PromptVersion"]);
        Assert.Equal("not sent", header["Model:Temperature"]);
        Assert.Equal("low", header["Model:Effort"]);
        Assert.Equal("anthropic", header["Model:Provider"]);
        Assert.Equal("not sent", header["Model:BaseUrl"]);
        Assert.Equal("5m", header["Model:PromptCacheTtl"]);
        Assert.Equal("not sent", header["Model:OutputMode"]);
        Assert.Equal("not sent", header["Model:RequestFields"]);
        Assert.Equal("0", header["Model:MinMillisecondsBetweenCalls"]);
    }

    [Fact]
    public void TheHeaderCarriesTheRequestFieldsOfACompatibleCandidateAsCompactJson()
    {
        var gemini = Gemini();
        gemini.RequestFields = """{ "reasoning": { "effort": "low" } }""";

        var header = EvalCommand.ModelSettings("gemini", gemini).ToDictionary();

        Assert.Equal("""{"reasoning":{"effort":"low"}}""", header["Model:RequestFields"]);
    }

    [Theory]
    [InlineData("scripted", "scripted")]
    [InlineData("claude:haiku", "claude")]
    [InlineData("compatible:gemini", "model")]
    public void TheRunnerSetsAgentParserByThePrefix(string parser, string expected)
    {
        // Final review M3: a claude: run keeps Agent:Parser claude, so its report header matches the committed reports.
        Assert.Equal(expected, EvalCommand.ParserSettingFor(EvalArguments.Parse(["dev", "--parser", parser], out _)!));
    }

    [Fact]
    public void TheRunnerCopiesOnlyAVendorKeyShapedSetting()
    {
        Assert.Equal("Gemini:ApiKey", EvalCommand.SecretToCopy(Runner(), "gemini"));
        Assert.Null(EvalCommand.SecretToCopy(Runner(("Gemini:ApiKey", "runner-has-it")), "gemini"));
        Assert.Null(EvalCommand.SecretToCopy(Runner(("Model:Candidates:gemini:ApiKeySetting", "ConnectionStrings:Agent")), "gemini"));
        Assert.Equal(ParserFactory.ApiKeySetting, EvalCommand.SecretToCopy(Runner(), "haiku"));
        Assert.Null(EvalCommand.SecretToCopy(Runner((ParserFactory.ApiKeySetting, "runner-has-it")), "haiku"));
    }

    [Fact]
    public void APrefixThatDoesNotMatchTheProviderIsAUsageError()
    {
        var configuration = Runner();

        Assert.Equal(
            "--parser claude:gemini names 'gemini', whose provider is 'compatible'.",
            EvalCommand.PrefixMismatch(configuration, EvalArguments.Parse(["dev", "--parser", "claude:gemini"], out _)!));
        Assert.Equal(
            "--parser compatible:haiku names 'haiku', whose provider is 'anthropic'.",
            EvalCommand.PrefixMismatch(configuration, EvalArguments.Parse(["dev", "--parser", "compatible:haiku"], out _)!));
        Assert.Null(EvalCommand.PrefixMismatch(configuration, EvalArguments.Parse(["dev", "--parser", "compatible:gemini"], out _)!));
        Assert.Null(EvalCommand.PrefixMismatch(configuration, EvalArguments.Parse(["dev", "--parser", "claude:haiku"], out _)!));
        Assert.Null(EvalCommand.PrefixMismatch(configuration, EvalArguments.Parse(["dev"], out _)!));
    }

    /// <summary>A runner configuration with the <c>haiku</c> and <c>gemini</c> candidates, plus <paramref name="extra"/>.</summary>
    private static IConfiguration Runner(params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Model:Candidates:haiku:Provider"] = "anthropic",
            ["Model:Candidates:haiku:ModelId"] = "claude-haiku-5-5",
            ["Model:Candidates:gemini:Provider"] = "compatible",
            ["Model:Candidates:gemini:BaseUrl"] = "https://generativelanguage.googleapis.com/v1beta/openai",
            ["Model:Candidates:gemini:ApiKeySetting"] = "Gemini:ApiKey",
            ["Model:Candidates:gemini:ModelId"] = "gemini-test",
        };
        foreach (var (key, value) in extra)
            settings[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    /// <summary>The valid <c>compatible</c> candidate <c>gemini</c> of <c>ModelOptionsTests</c>.</summary>
    private static ModelCandidate Gemini() => Parsing.ModelOptionsTests.Compatible().Selected!;

    [Fact]
    public void TheReportNameAndTheEstimateFollowTheParser()
    {
        var haiku = new global::Agent.Core.Parsing.ModelCandidate { ModelId = "claude-haiku-5-5", InputPricePerMTok = 0.10m, OutputPricePerMTok = 0.50m };

        Assert.Equal("claude-haiku", EvalCommand.ReportParserName("claude:haiku"));
        Assert.Equal("compatible-gemini", EvalCommand.ReportParserName("compatible:gemini"));
        Assert.Equal("scripted", EvalCommand.ReportParserName("scripted"));

        // The label goes into the report stem after the parser (ReportWriterTests.TheLabelFollowsTheParserInTheName).
        Assert.Equal("m7a", EvalArguments.Parse(["dev", "--parser", "claude:haiku", "--label", "m7a"], out _)?.Label);
        Assert.Null(EvalArguments.Parse(["dev"], out _)?.Label);
        // 72 cases x (1,500 x 0.10 + 400 x 0.50) / 1,000,000.
        Assert.Equal(0.0252m, EvalCommand.EstimatedCostUsd(72, haiku));

        // The header records the prompt version with the sampling settings sent.
        var header = EvalCommand.ModelSettings("haiku", haiku).ToDictionary();
        Assert.Equal(global::Agent.Core.Parsing.ClaudeParser.PromptVersion, header["Model:PromptVersion"]);
        Assert.Equal("not sent", header["Model:Temperature"]);
        Assert.Equal("low", header["Model:Effort"]);
        haiku.Temperature = 0.5f;
        haiku.Effort = "medium";
        header = EvalCommand.ModelSettings("haiku", haiku).ToDictionary();
        Assert.Equal(("0.5", "medium"), (header["Model:Temperature"], header["Model:Effort"]));
    }
}
