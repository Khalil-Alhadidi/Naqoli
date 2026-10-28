using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Agent.Core.Parsing;

/// <summary>
/// Picks the parser from <c>Agent:Parser</c>: <c>scripted</c> (the default) or <c>model</c> (<c>claude</c> is a
/// synonym). With <c>scripted</c> no model client is constructed. With <c>model</c> the <c>Model</c> section must name a
/// valid candidate and its key must be set: for an <c>anthropic</c> candidate <c>Anthropic:ApiKey</c> in user secrets,
/// or <c>ANTHROPIC_API_KEY</c>; for a <c>compatible</c> candidate the setting its <c>ApiKeySetting</c> names
/// (<c>&lt;Vendor&gt;:ApiKey</c>). Both are checked at start, and the key never appears in a message, a log or the trace.
/// </summary>
public static class ParserFactory
{
    public const string ParserSetting = AgentOptions.SectionName + ":" + nameof(AgentOptions.Parser);
    public const string DevSetPathSetting = AgentOptions.SectionName + ":" + nameof(AgentOptions.DevSetPath);
    public const string Scripted = ScriptedParser.ParserName;
    public const string Claude = "claude";

    /// <summary>The parser value for the model candidate in use, whatever its provider; <see cref="Claude"/> is a synonym.</summary>
    public const string Model = "model";
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
    /// The key for <paramref name="candidate"/>: for <c>anthropic</c> the lookup of
    /// <see cref="ApiKey(IConfiguration, Func{string, string?}?)"/>; for <c>compatible</c> the setting its
    /// <see cref="ModelCandidate.ApiKeySetting"/> names. Null when not set.
    /// </summary>
    public static string? ApiKey(IConfiguration configuration, ModelCandidate candidate, Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsCompatible)
            return ApiKey(configuration, environment);
        if (string.IsNullOrWhiteSpace(candidate.ApiKeySetting))
            return null;
        var configured = configuration[candidate.ApiKeySetting];
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }

    /// <summary>
    /// The message when <paramref name="candidate"/>'s key is not set: <see cref="MissingKeyMessage"/> for
    /// <c>anthropic</c>; for <c>compatible</c> one that names its setting and the environment variable form of it (the
    /// <c>:</c> written <c>__</c>), never a value.
    /// </summary>
    public static string MissingKeyMessageFor(ModelCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsCompatible)
            return MissingKeyMessage;
        var setting = candidate.ApiKeySetting ?? string.Empty;
        return "Agent:Parser is 'model' but no key is set for the candidate. Set " + setting
            + " in user secrets (dotnet user-secrets set \"" + setting + "\" <key> --project src/Agent.Web) or the environment variable "
            + setting.Replace(':', '_').Replace("_", "__", StringComparison.Ordinal) + ".";
    }

    /// <summary>
    /// Binds <see cref="ModelOptions"/> (shown read-only by the console whatever the parser) and registers the parser
    /// <c>Agent:Parser</c> names as <see cref="IParser"/>: the singleton <see cref="ScriptedParser"/> over the development
    /// set <c>Agent:DevSetPath</c> names, or (for <c>model</c> or <c>claude</c>) a scoped <see cref="ClaudeParser"/> (it
    /// writes to the scope's <c>ITraceSink</c>) over a singleton <see cref="IChatClient"/> for the candidate's provider. The scripted parser's files are found and read when
    /// it is first resolved, so a host without the repository starts and fails only when a message is parsed.
    /// </summary>
    /// <param name="environment">Reads an environment variable (tests); the process environment by default.</param>
    /// <exception cref="InvalidOperationException"><c>Agent:Parser</c> is not <c>scripted</c>, <c>model</c> or <c>claude</c>.</exception>
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
            case Model:
                services.AddSingleton<IValidateOptions<ModelOptions>>(new ModelOptionsValidator(c => ApiKey(configuration, c, environment) is not null));
                model.ValidateOnStart();
                services.AddSingleton(sp =>
                {
                    var candidate = sp.GetRequiredService<IOptions<ModelOptions>>().Value.Selected!;
                    var key = ApiKey(configuration, candidate, environment)
                        ?? throw new InvalidOperationException(MissingKeyMessageFor(candidate));
                    return CreateChatClient(key, candidate);
                });
                services.AddScoped<IParser, ClaudeParser>();
                return services;

            default:
                throw new InvalidOperationException(ParserSetting + " must be '" + Scripted + "', '" + Model + "' or '" + Claude + "'.");
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

    /// <summary>
    /// An <see cref="IChatClient"/> for <paramref name="candidate"/> with <paramref name="apiKey"/>: over the Anthropic
    /// client for <c>anthropic</c>, over the OpenAI-compatible one (<see cref="CreateCompatibleClient"/>) for <c>compatible</c>.
    /// </summary>
    public static IChatClient CreateChatClient(string apiKey, ModelCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate.IsCompatible
            ? CreateCompatibleClient(apiKey, candidate)
            : CreateChatClient(CreateAnthropicClient(apiKey), candidate);
    }

    /// <summary>
    /// The OpenAI SDK's chat client for <paramref name="candidate"/>'s model at its <see cref="ModelCandidate.BaseUrl"/>,
    /// with <paramref name="apiKey"/> and no retries, as an <see cref="IChatClient"/>.
    /// </summary>
    /// <param name="httpClient">The HTTP client to send through (tests pass a stubbed one); the SDK's own when null.</param>
    public static IChatClient CreateCompatibleClient(string apiKey, ModelCandidate candidate, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(candidate);
        // No retries, as for Anthropic: one parse call per message (rule 2); a 429 shows the handover message at once.
        var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(candidate.BaseUrl!),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            Transport = httpClient is null ? null : new HttpClientPipelineTransport(httpClient),
        });
        return client.GetChatClient(candidate.ModelId).AsIChatClient();
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
/// The start-up check of <see cref="ModelOptions"/> when <c>model</c> or <c>claude</c> is chosen: every problem of the
/// candidate in use (<see cref="ModelOptions.Problems"/>), and the absence of its key. The messages name settings, never
/// a value.
/// </summary>
internal sealed class ModelOptionsValidator(Func<ModelCandidate, bool> hasKey) : IValidateOptions<ModelOptions>
{
    public ValidateOptionsResult Validate(string? name, ModelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>(options.Problems());
        if (options.Selected is { } candidate && !hasKey(candidate))
            failures.Add(ParserFactory.MissingKeyMessageFor(candidate));
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
