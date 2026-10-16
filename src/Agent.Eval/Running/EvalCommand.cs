using System.Globalization;
using System.Text.Json;
using Agent.Core;
using Agent.Core.Data;
using Agent.Core.Parsing;
using Agent.Core.Wallet;
using Agent.Eval.Cases;
using Agent.Eval.Grading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Agent.Eval.Running;

/// <summary>
/// The eval command line: builds the host (<c>AddAgentEval</c>: the agent's services, <see cref="EvalClock"/> as the
/// <see cref="TimeProvider"/>), runs the held-out guard before any set file is opened, plays every case through a
/// <see cref="CaseDriver"/> with the parser <c>--parser</c> names inside a <see cref="RecordingParser"/>, grades it with
/// the <see cref="CaseGrader"/>, and writes the report. It prints case ids, codes and figures, never case text.
/// </summary>
public static class EvalCommand
{
    /// <summary>The run completed (whatever its pass rate).</summary>
    public const int Completed = 0;

    /// <summary>The wallet or the agent database is unreachable, or the set file is missing or invalid.</summary>
    public const int Unavailable = 1;

    /// <summary>The held-out guard refused, or the command line or the settings are wrong.</summary>
    public const int Refused = 2;

    /// <summary>The input tokens one case is estimated to take before a model run (the prompt and one message).</summary>
    public const int EstimatedInputTokensPerCase = 1_500;

    /// <summary>The output tokens one case is estimated to take before a model run (thinking is billed as output).</summary>
    public const int EstimatedOutputTokensPerCase = 400;

    /// <summary>The web console's user-secrets id (<c>src/Agent.Web/Agent.Web.csproj</c>), where Khalil sets the key once.</summary>
    public const string WebUserSecretsId = "2caa1826-0a12-43da-8479-d86df1b92e41";

    private static readonly TimeSpan AdminTimeout = TimeSpan.FromMinutes(2);

    /// <summary>The parser's name as it appears in a report file name: <c>claude:haiku</c> becomes <c>claude-haiku</c>.</summary>
    public static string ReportParserName(string parserName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parserName);
        return parserName.Replace(':', '-');
    }

    /// <summary>
    /// The report header's entries for the candidate measured: its name and model, the prompt version, and the sampling
    /// settings the call sends with it (<c>Model:Temperature</c>, <c>not sent</c> when unset; <c>Model:Effort</c>).
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> ModelSettings(string candidateName, ModelCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateName);
        ArgumentNullException.ThrowIfNull(candidate);
        yield return new("Model:Candidate", candidateName);
        yield return new("Model:ModelId", candidate.ModelId);
        yield return new("Model:PromptVersion", ClaudeParser.PromptVersion);
        yield return new("Model:Temperature", candidate.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "not sent");
        yield return new("Model:Effort", candidate.Effort);
    }

    /// <summary>The cost estimated before a run: <paramref name="cases"/> × (1,500 input + 400 output tokens) at the candidate's prices.</summary>
    public static decimal EstimatedCostUsd(int cases, ModelCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return cases * candidate.CostUsd(EstimatedInputTokensPerCase, EstimatedOutputTokensPerCase);
    }

    /// <summary>Runs the command of <paramref name="args"/>; returns the exit code.</summary>
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter errors, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        var parsed = EvalArguments.Parse(args, out var usageError);
        if (parsed is null)
        {
            await errors.WriteLineAsync(usageError);
            await errors.WriteLineAsync(EvalArguments.Usage);
            return Refused;
        }

        using var host = BuildHost(parsed);
        EvalOptions options;
        try
        {
            options = host.Services.GetRequiredService<IOptions<EvalOptions>>().Value;
        }
        catch (OptionsValidationException ex)
        {
            await errors.WriteLineAsync("Invalid settings: " + ex.Message);
            return Refused;
        }

        var root = EvalPaths.RepositoryRoot();
        var setDirectory = EvalPaths.Resolve(options.SetDirectory, root);

        // Rule 7: the guard decides first, before any set file is opened and before the agent's settings are checked.
        var decision = HeldOutGuard.Check(args, setDirectory, Path.Combine(root, "docs", "disclosure.md"));
        if (!decision.Allowed)
        {
            await errors.WriteLineAsync(decision.Message);
            return parsed.Milestone11Run && !File.Exists(Path.Combine(setDirectory, HeldOutGuard.HeldOutFileName)) ? Unavailable : Refused;
        }

        AgentOptions agentOptions;
        try
        {
            await host.StartAsync(cancellationToken);
            agentOptions = host.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
        }
        catch (OptionsValidationException ex)
        {
            await errors.WriteLineAsync("Invalid settings: " + ex.Message);
            return Refused;
        }

        if (decision.FileHash is not null)
            await output.WriteLineAsync(decision.Message);

        var setPath = Path.Combine(setDirectory, "requests." + parsed.Set + ".jsonl");
        if (!File.Exists(setPath))
        {
            await errors.WriteLineAsync("The set file " + setPath + " is missing.");
            return Unavailable;
        }

        IReadOnlyList<EvalCase> cases;
        string setHash;
        try
        {
            setHash = decision.FileHash ?? HeldOutGuard.FileHash(setPath);
            cases = CaseLoader.Load(setPath);
        }
        catch (CaseFileException ex)
        {
            await errors.WriteLineAsync(ex.Message);
            return Unavailable;
        }

        if (parsed.Only is { } only)
        {
            var unknown = only.Where(id => cases.All(c => c.Id != id)).ToList();
            if (unknown.Count > 0)
            {
                await errors.WriteLineAsync("Unknown case id(s): " + string.Join(",", unknown) + ".");
                return Refused;
            }

            cases = [.. cases.Where(c => only.Contains(c.Id, StringComparer.Ordinal))];
        }

        var seed = SeedPins.Load(EvalPaths.SeedDirectory(options, root));
        using var http = new HttpClient { BaseAddress = agentOptions.WalletBaseUrl, Timeout = AdminTimeout };
        http.DefaultRequestHeaders.Add(WalletClient.ClientKeyHeader, agentOptions.WalletClientKey ?? AgentOptions.DevelopmentWalletClientKey);

        if (!await WalletAnswersAsync(http, cancellationToken))
        {
            await errors.WriteLineAsync("The wallet does not answer at " + agentOptions.WalletBaseUrl + ". Start it with: dotnet run --project src/Wallet.MockApi");
            return Unavailable;
        }

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.MigrateAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await errors.WriteLineAsync("The agent database is not reachable (ConnectionStrings:Agent): " + ex.GetType().Name + ".");
            return Unavailable;
        }

        // The parser Agent:Parser names (the command line set it): resolved once, in a scope that lasts the run.
        await using var runScope = host.Services.CreateAsyncScope();
        var measured = runScope.ServiceProvider.GetRequiredService<IParser>();
        var model = host.Services.GetRequiredService<IOptions<ModelOptions>>().Value;
        var candidate = parsed.Candidate is null ? null : model.Selected;
        var settings = new Dictionary<string, string>(Settings(options, agentOptions), StringComparer.Ordinal);
        if (candidate is not null)
        {
            var estimate = EstimatedCostUsd(cases.Count, candidate);
            foreach (var (key, value) in ModelSettings(parsed.Candidate!, candidate))
                settings[key] = value;
            settings["Model:EstimatedCostUsd"] = Usd(estimate);
            await output.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"Model {parsed.Candidate} ({candidate.ModelId}), prompt version {ClaudeParser.PromptVersion}. Estimated cost: {cases.Count} case(s) x ({EstimatedInputTokensPerCase} in + {EstimatedOutputTokensPerCase} out tokens) = {Usd(estimate)} USD per run."));
            if (parsed.Repeat > 1)
            {
                await output.WriteLineAsync(string.Create(
                    CultureInfo.InvariantCulture, $"{parsed.Repeat} runs: estimated {Usd(estimate * parsed.Repeat)} USD in all."));
            }
        }

        var admin = new WalletAdmin(http, options.WalletAdminKey);
        var parser = new RecordingParser(measured);
        var driver = new CaseDriver(host.Services, admin, seed, host.Services.GetRequiredService<EvalClock>(), parser, options);
        var grader = new CaseGrader(seed);

        var directory = EvalPaths.Resolve(parsed.Out ?? options.ReportDirectory, root);
        var runs = new List<RunSummary>(parsed.Repeat);
        var runPaths = new List<ReportPaths>(parsed.Repeat);
        for (var run = 1; run <= parsed.Repeat; run++)
        {
            if (parsed.Repeat > 1)
                await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Run {run} of {parsed.Repeat}."));

            var started = TimeProvider.System.GetUtcNow();
            var runSettings = new Dictionary<string, string>(settings, StringComparer.Ordinal);
            var results = new List<CaseResult>(cases.Count);
            long inputTokens = 0;
            long outputTokens = 0;
            foreach (var evalCase in cases)
            {
                CaseObservation observation;
                try
                {
                    observation = await driver.PlayAsync(evalCase, cancellationToken);
                }
                catch (HttpRequestException)
                {
                    await errors.WriteLineAsync("The wallet stopped answering during case " + evalCase.Id + ".");
                    return Unavailable;
                }

                inputTokens += observation.Usage.InputTokens;
                outputTokens += observation.Usage.OutputTokens;
                var result = grader.Grade(evalCase, observation);
                results.Add(result);
                await output.WriteLineAsync(evalCase.Id + " " + (result.Passed ? "passed" : "failed" + (result.FirstFailing is { } f ? " at " + f.Field : string.Empty)));
            }

            if (candidate is not null)
            {
                var cost = results.Sum(r => r.CostUsd);
                runSettings["Model:CostUsd"] = Usd(cost);
                runSettings["Model:InputTokens"] = inputTokens.ToString(CultureInfo.InvariantCulture);
                runSettings["Model:OutputTokens"] = outputTokens.ToString(CultureInfo.InvariantCulture);
                await output.WriteLineAsync(string.Create(
                    CultureInfo.InvariantCulture, $"Model cost: {Usd(cost)} USD ({inputTokens} tokens in, {outputTokens} out)."));
            }

            var summary = new RunSummary(
                parsed.Set, setHash, ReportParserName(measured.Name), started, runSettings, results, parsed.Label, parsed.Repeat > 1 ? run : null);
            var paths = ReportWriter.Write(summary, directory);
            runs.Add(summary);
            runPaths.Add(paths);

            foreach (var score in ReportWriter.Scores(results, r => r.Category))
            {
                await output.WriteLineAsync(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{score.Name}: {score.Passed}/{score.Cases} passed ({score.Percent:0.0} %, Wilson 95% {ReportWriter.Interval(score)})"));
            }

            var total = ReportWriter.Score("All cases", results);
            await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"All cases: {total.Passed}/{total.Cases} passed ({total.Percent:0.0} %)"));
            await output.WriteAsync(ReportWriter.TargetsConsoleSummary(ReportWriter.Targets(results)));
            await output.WriteLineAsync("Report: " + paths.MarkdownPath);
            await output.WriteLineAsync("Report: " + paths.JsonPath);
        }

        if (parsed.Repeat > 1)
        {
            var repeat = ReportWriter.WriteRepeat(runs, runPaths, directory);
            await output.WriteAsync(ReportWriter.RepeatConsoleSummary(ReportWriter.Repeat(runs)));
            await output.WriteLineAsync("Repeat report: " + repeat.MarkdownPath);
            await output.WriteLineAsync("Repeat report: " + repeat.JsonPath);
        }

        await host.StopAsync(cancellationToken);
        return Completed;
    }

    /// <summary>
    /// The settings the report header shows: every simple <see cref="EvalOptions"/> and <see cref="AgentOptions"/>
    /// value; keys are shown only as set or not, and word lists only as their count.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Settings(EvalOptions options, AgentOptions agentOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(agentOptions);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(settings, EvalOptions.SectionName, options);
        Add(settings, AgentOptions.SectionName, agentOptions);
        return settings;
    }

    private static void Add<T>(Dictionary<string, string> settings, string section, T options)
    {
        // Serialised, not read by reflection: the effective values (defaults included), as JSON scalars.
        foreach (var property in JsonSerializer.SerializeToElement(options).EnumerateObject())
        {
            var value = property.Value;
            settings[section + ":" + property.Name] = value.ValueKind switch
            {
                _ when property.Name.Contains("Key", StringComparison.Ordinal) => value.ValueKind == JsonValueKind.Null ? "not set" : "set",
                JsonValueKind.Array => value.GetArrayLength().ToString(CultureInfo.InvariantCulture) + " word(s)",
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Null => "null",
                _ => value.GetRawText(),
            };
        }
    }

    private static IHost BuildHost(EvalArguments parsed)
    {
        // The parsed arguments are applied below; the raw ones are not handed to the configuration, so 'dev' or a flag
        // never becomes a setting. The runner works against the local mock wallet, so Development is the default
        // environment (any non-empty client key works there); DOTNET_ENVIRONMENT overrides it.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environments.Development,
        });

        var overrides = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (parsed.Wallet is { } wallet)
            overrides[AgentOptions.SectionName + ":" + nameof(AgentOptions.WalletBaseUrl)] = wallet.ToString();

        // --parser claude:<candidate> selects the Claude parser and the candidate; the key comes from the web console's
        // user secrets (the same UserSecretsId) or ANTHROPIC_API_KEY, and is checked when the host starts.
        overrides[ParserFactory.ParserSetting] = parsed.Candidate is null ? ParserFactory.Scripted : ParserFactory.Claude;
        if (parsed.Candidate is { } name)
        {
            overrides[ModelOptions.SectionName + ":" + nameof(ModelOptions.Candidate)] = name;

            // Only the key is taken from the web console's user secrets: its other secrets (its own agent database)
            // must never reach the runner, which empties the tables of the database it uses.
            if (builder.Environment.IsDevelopment() && builder.Configuration[ParserFactory.ApiKeySetting] is null)
            {
                var key = new ConfigurationBuilder().AddUserSecrets(WebUserSecretsId).Build()[ParserFactory.ApiKeySetting];
                if (!string.IsNullOrWhiteSpace(key))
                    overrides[ParserFactory.ApiKeySetting] = key;
            }
        }
        if (overrides.Count > 0)
            builder.Configuration.AddInMemoryCollection(overrides);

        builder.Services.AddAgentEval(builder.Configuration);
        return builder.Build();
    }

    private static string Usd(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static async Task<bool> WalletAnswersAsync(HttpClient http, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await http.GetAsync(new Uri("health", UriKind.Relative), timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
