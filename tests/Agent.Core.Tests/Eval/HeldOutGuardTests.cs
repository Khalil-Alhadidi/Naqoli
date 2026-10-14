using System.Security.Cryptography;
using System.Text;
using Agent.Eval.Running;

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
    public void TheParserIsReadFromTheCommandLine(string[] args, string expected)
    {
        var parsed = EvalArguments.Parse(args, out var error);

        Assert.Null(error);
        Assert.Equal(expected, parsed?.Parser);
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

    [Fact]
    public void TheReportNameAndTheEstimateFollowTheParser()
    {
        var haiku = new global::Agent.Core.Parsing.ModelCandidate { ModelId = "claude-haiku-5-5", InputPricePerMTok = 0.10m, OutputPricePerMTok = 0.50m };

        Assert.Equal("claude-haiku", EvalCommand.ReportParserName("claude:haiku"));
        Assert.Equal("scripted", EvalCommand.ReportParserName("scripted"));
        // 72 cases x (1,500 x 0.10 + 400 x 0.50) / 1,000,000.
        Assert.Equal(0.0252m, EvalCommand.EstimatedCostUsd(72, haiku));
    }
}
