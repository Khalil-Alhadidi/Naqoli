using System.Diagnostics.CodeAnalysis;
using Agent.Core;
using Microsoft.Extensions.Options;

namespace Agent.Web.Services;

/// <summary>
/// The configuration source of the values saved in the settings component (D8). Add it last, so a saved value wins over
/// every other source; it holds nothing at start. One source has one provider, whatever builds it.
/// </summary>
public sealed class RuntimeSettingsSource : IConfigurationSource
{
    /// <summary>The provider this source gives every builder.</summary>
    public RuntimeSettingsProvider Provider { get; } = new();

    public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
}

/// <summary>
/// The saved values, in memory only: a restart forgets them. Each change replaces <see cref="ConfigurationProvider.Data"/>
/// with a new dictionary, so a configuration read on another thread never sees one half written, then raises the reload
/// token, which the options monitor and every new <see cref="IOptionsSnapshot{TOptions}"/> follow.
/// </summary>
public sealed class RuntimeSettingsProvider : ConfigurationProvider
{
    private readonly Lock gate = new();

    /// <summary>The saved values by key.</summary>
    public IReadOnlyDictionary<string, string> Values
    {
        get
        {
            lock (gate)
                return Data.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Saves <paramref name="value"/> under <paramref name="key"/> (null removes it), then raises the reload token.</summary>
    internal void Write(string key, string? value)
    {
        lock (gate)
        {
            var data = new Dictionary<string, string?>(Data, StringComparer.OrdinalIgnoreCase);
            if (value is null)
                data.Remove(key);
            else
                data[key] = value;
            Data = data;
        }

        OnReload();
    }
}

/// <summary>
/// Saves agent settings while the console runs (D8): a saved value applies to the next action, whose scope reads a fresh
/// <see cref="IOptionsSnapshot{TOptions}"/>. Only the keys of <see cref="EditableNames"/> can be saved. A save is checked
/// first on a candidate (the configuration as it is plus the one value, bound to a fresh <see cref="AgentOptions"/>, then
/// the registered post-configuration and every registered validator: the data annotations and the cross-field rules of
/// <c>AddAgentCore</c>); only a valid value is published, so no other circuit ever sees an invalid one. Nothing is
/// written to a file.
/// </summary>
public sealed class RuntimeSettings
{
    /// <summary>
    /// The agent settings the console may change while it runs: the limits, timers and counts that something reads
    /// (D8). Infrastructure (the wallet's address and key, the parser, the set's path, the user) is never editable.
    /// </summary>
    public static IReadOnlyList<string> EditableNames { get; } =
    [
        nameof(AgentOptions.MinAmountIqd),
        nameof(AgentOptions.PerTransactionCapIqd),
        nameof(AgentOptions.PerDayCapIqd),
        nameof(AgentOptions.CardTimerSeconds),
        nameof(AgentOptions.MaxProductsInOneQuestion),
        nameof(AgentOptions.RepeatWarningWindowMinutes),
        nameof(AgentOptions.HistoryDays),
        nameof(AgentOptions.MinSubstringWordLength),
        nameof(AgentOptions.MaxYesNoWords),
        nameof(AgentOptions.LogPageSize),
        nameof(AgentOptions.CheckingRetrySeconds),
        nameof(AgentOptions.MaxAliasLength),
        nameof(AgentOptions.MaxMessageChars),
        nameof(AgentOptions.MaxRequestsPerMessage),
        nameof(AgentOptions.MaxNamedNotPrepared),
    ];

    private const string Prefix = AgentOptions.SectionName + ":";

    private readonly RuntimeSettingsProvider provider;
    private readonly IConfiguration configuration;
    private readonly IEnumerable<IPostConfigureOptions<AgentOptions>> postConfigures;
    private readonly IEnumerable<IValidateOptions<AgentOptions>> validators;
    private readonly Lock gate = new();

    /// <param name="provider">Where a saved value is published.</param>
    /// <param name="configuration">The whole configuration, the runtime source included, that the options bind from.</param>
    /// <param name="postConfigures">The registered post-configuration of <see cref="AgentOptions"/>.</param>
    /// <param name="validators">The registered validators of <see cref="AgentOptions"/>.</param>
    public RuntimeSettings(
        RuntimeSettingsProvider provider,
        IConfiguration configuration,
        IEnumerable<IPostConfigureOptions<AgentOptions>> postConfigures,
        IEnumerable<IValidateOptions<AgentOptions>> validators)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(postConfigures);
        ArgumentNullException.ThrowIfNull(validators);

        this.provider = provider;
        this.configuration = configuration;
        this.postConfigures = postConfigures;
        this.validators = validators;
    }

    /// <summary>The values saved since the start, by key (<c>Agent:PerTransactionCapIqd</c>).</summary>
    public IReadOnlyDictionary<string, string> Overrides => provider.Values;

    /// <summary>
    /// Saves <paramref name="value"/> under <paramref name="key"/> when the options it gives are valid; otherwise publishes
    /// nothing and returns false with the failure messages, which name the setting.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not <c>Agent:</c> and one of <see cref="EditableNames"/>.</exception>
    public bool TrySet(string key, string value, [NotNullWhen(false)] out string? error)
    {
        RequireEditableKey(key);
        ArgumentNullException.ThrowIfNull(value);

        lock (gate)
        {
            error = Validate(key, value);
            if (error is not null)
                return false;

            provider.Write(key, value);
            return true;
        }
    }

    /// <summary>Removes the saved value of <paramref name="key"/>: the value of the other sources applies again.</summary>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not <c>Agent:</c> and one of <see cref="EditableNames"/>.</exception>
    public void Reset(string key)
    {
        RequireEditableKey(key);
        lock (gate)
            provider.Write(key, null);
    }

    /// <summary>
    /// The options the configuration gives with <paramref name="value"/> under <paramref name="key"/>, built and checked
    /// as the options pipeline builds them (bind, post-configure, validate) without publishing anything: the failure
    /// messages, or null when they are valid.
    /// </summary>
    private string? Validate(string key, string value)
    {
        var candidate = new ConfigurationBuilder()
            .AddConfiguration(configuration, shouldDisposeConfiguration: false)
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

        var options = new AgentOptions();
        try
        {
            candidate.GetSection(AgentOptions.SectionName).Bind(options);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(key, StringComparison.OrdinalIgnoreCase))
        {
            // The binder names the configuration path of a value it cannot convert to the property's type.
            return key + " is not a valid value for this setting.";
        }

        foreach (var postConfigure in postConfigures)
            postConfigure.PostConfigure(Options.DefaultName, options);

        var failures = validators
            .Select(v => v.Validate(Options.DefaultName, options))
            .Where(r => r.Failed)
            .SelectMany(r => r.Failures ?? [])
            .ToList();
        return failures.Count == 0 ? null : string.Join(" ", failures);
    }

    private static void RequireEditableKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var name = key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? key[Prefix.Length..] : null;
        if (name is null || !EditableNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Only the editable keys of the " + AgentOptions.SectionName + " section can be saved.", nameof(key));
    }
}

/// <summary>Adds the runtime settings source and service.</summary>
public static class RuntimeSettingsExtensions
{
    /// <summary>Adds a <see cref="RuntimeSettingsSource"/>; call it after every other source, so its values win.</summary>
    public static IConfigurationBuilder AddRuntimeSettings(this IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Add(new RuntimeSettingsSource());
    }

    /// <summary>
    /// Registers the singleton <see cref="RuntimeSettings"/> over the <see cref="RuntimeSettingsProvider"/> of
    /// <paramref name="configuration"/>. A configuration without one (a test host) registers nothing.
    /// </summary>
    public static IServiceCollection AddRuntimeSettings(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = (configuration as IConfigurationRoot)?.Providers.OfType<RuntimeSettingsProvider>().LastOrDefault();
        if (provider is not null)
            services.AddSingleton(sp => new RuntimeSettings(
                provider,
                configuration,
                sp.GetServices<IPostConfigureOptions<AgentOptions>>(),
                sp.GetServices<IValidateOptions<AgentOptions>>()));
        return services;
    }
}
