using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agent.Core.Parsing;

/// <summary>
/// Picks the parser from <c>Agent:Parser</c>: <c>scripted</c> (the default) or <c>claude</c>. With <c>scripted</c>
/// nothing of Anthropic is constructed. With <c>claude</c> the <c>Model</c> section must name a valid candidate and a
/// key must be set (<c>Anthropic:ApiKey</c> in user secrets, or <c>ANTHROPIC_API_KEY</c>); both are checked at start,
/// and the key never appears in a message, a log or the trace.
/// </summary>
public static class ParserFactory
{
    public const string ParserSetting = AgentOptions.SectionName + ":" + nameof(AgentOptions.Parser);
    public const string DevSetPathSetting = AgentOptions.SectionName + ":" + nameof(AgentOptions.DevSetPath);
    public const string Scripted = ScriptedParser.ParserName;
    public const string Claude = "claude";
    public const string ApiKeySetting = "Anthropic:ApiKey";
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";

    /// <summary>The message when <c>claude</c> is chosen and no key is set (it names where to set one, never a value).</summary>
    public const string MissingKeyMessage =
        "Agent:Parser is 'claude' but no Anthropic key is set. Set " + ApiKeySetting + " in user secrets (dotnet user-secrets set \""
        + ApiKeySetting + "\" <key> --project src/Agent.Web) or the " + ApiKeyVariable + " environment variable.";

    /// <summary>The key from <c>Anthropic:ApiKey</c>, else from <c>ANTHROPIC_API_KEY</c>; null when neither is set.</summary>
    public static string? ApiKey(IConfiguration configuration, Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration[ApiKeySetting];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;
        var variable = (environment ?? Environment.GetEnvironmentVariable)(ApiKeyVariable);
        return string.IsNullOrWhiteSpace(variable) ? null : variable;
    }

    /// <summary>
    /// Binds <see cref="ModelOptions"/> (shown read-only by the console whatever the parser) and registers the parser
    /// <c>Agent:Parser</c> names as <see cref="IParser"/>: the singleton <see cref="ScriptedParser"/> over the development
    /// set <c>Agent:DevSetPath</c> names, or a scoped <see cref="ClaudeParser"/> (it writes to the scope's
    /// <c>ITraceSink</c>) over a singleton <see cref="IChatClient"/>. The scripted parser's files are found and read when
    /// it is first resolved, so a host without the repository starts and fails only when a message is parsed.
    /// </summary>
    /// <param name="environment">Reads an environment variable (tests); the process environment by default.</param>
    /// <exception cref="InvalidOperationException"><c>Agent:Parser</c> is neither <c>scripted</c> nor <c>claude</c>.</exception>
    public static IServiceCollection AddAgentParser(
        this IServiceCollection services, IConfiguration configuration, Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var model = services.AddOptions<ModelOptions>().Bind(configuration.GetSection(ModelOptions.SectionName));
        var parser = configuration[ParserSetting];
        switch (string.IsNullOrWhiteSpace(parser) ? Scripted : parser)
        {
            case Scripted:
                services.AddSingleton<IParser>(_ => ScriptedParser.Load(DevSetPath(configuration)));
                return services;

            case Claude:
                services.AddSingleton<IValidateOptions<ModelOptions>>(new ModelOptionsValidator(() => ApiKey(configuration, environment) is not null));
                model.ValidateOnStart();
                services.AddSingleton(sp =>
                {
                    var candidate = sp.GetRequiredService<IOptions<ModelOptions>>().Value.Selected!;
                    var key = ApiKey(configuration, environment) ?? throw new InvalidOperationException(MissingKeyMessage);
                    return CreateChatClient(key, candidate);
                });
                services.AddScoped<IParser, ClaudeParser>();
                return services;

            default:
                throw new InvalidOperationException(ParserSetting + " must be '" + Scripted + "' or '" + Claude + "'.");
        }
    }

    /// <summary>
    /// The full path of <c>Agent:DevSetPath</c> (its default when unset): itself when rooted, else under the repository
    /// root (<see cref="RepositoryRoot.Find"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The path is relative and no repository is found; the message names the path.</exception>
    public static string DevSetPath(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration[DevSetPathSetting];
        var path = string.IsNullOrWhiteSpace(configured) ? AgentOptions.DefaultDevSetPath : configured;
        if (Path.IsPathRooted(path))
            return path;

        try
        {
            return Path.GetFullPath(Path.Combine(RepositoryRoot.Find(), path));
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                DevSetPathSetting + " '" + path + "' is relative to the repository root, and none was found (" + ex.Message + ")", ex);
        }
    }

    /// <summary>An <see cref="IChatClient"/> for <paramref name="candidate"/> with <paramref name="apiKey"/>.</summary>
    public static IChatClient CreateChatClient(string apiKey, ModelCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return CreateChatClient(CreateAnthropicClient(apiKey), candidate);
    }

    /// <summary>The Anthropic client with <paramref name="apiKey"/> and no retries.</summary>
    public static AnthropicClient CreateAnthropicClient(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        // No retries: one parse call per message (rule 2). A 429 or 529 shows the handover message (model_unavailable) at
        // once instead of several requests inside the timeout, and the cost record counts the one call made.
        return new AnthropicClient { ApiKey = apiKey, MaxRetries = 0 };
    }

    /// <summary>An <see cref="IChatClient"/> for <paramref name="candidate"/> over <paramref name="client"/> (tests pass a stubbed one).</summary>
    public static IChatClient CreateChatClient(AnthropicClient client, ModelCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(candidate);
        return client.AsIChatClient(candidate.ModelId, candidate.MaxOutputTokens);
    }
}

/// <summary>
/// The start-up check of <see cref="ModelOptions"/> when <c>claude</c> is chosen: every problem of the candidate in use
/// (<see cref="ModelOptions.Problems"/>), and the key's absence. The messages name settings, never a value.
/// </summary>
internal sealed class ModelOptionsValidator(Func<bool> hasKey) : IValidateOptions<ModelOptions>
{
    public ValidateOptionsResult Validate(string? name, ModelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>(options.Problems());
        if (!hasKey())
            failures.Add(ParserFactory.MissingKeyMessage);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
