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
/// <see cref="IOptionsSnapshot{TOptions}"/>. A save is checked against every rule of <see cref="AgentOptions"/> (its data
/// annotations and the cross-field rules of <c>AddAgentCore</c>); a refused save leaves the previous value in place.
/// Only keys of the <c>Agent</c> section are accepted. Nothing is written to a file.
/// </summary>
public sealed class RuntimeSettings(RuntimeSettingsProvider provider, IOptionsMonitor<AgentOptions> monitor)
{
    private const string Prefix = AgentOptions.SectionName + ":";

    private readonly Lock gate = new();

    /// <summary>The values saved since the start, by key (<c>Agent:PerTransactionCapIqd</c>).</summary>
    public IReadOnlyDictionary<string, string> Overrides => provider.Values;

    /// <summary>
    /// Saves <paramref name="value"/> under <paramref name="key"/> when the options it gives are valid; otherwise restores
    /// what was there and returns false with the failure messages, which name the setting.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not a key of the <c>Agent</c> section.</exception>
    public bool TrySet(string key, string value, [NotNullWhen(false)] out string? error)
    {
        RequireAgentKey(key);
        ArgumentNullException.ThrowIfNull(value);

        lock (gate)
        {
            var previous = provider.Values.TryGetValue(key, out var saved) ? saved : null;
            error = Apply(key, value);
            if (error is null)
                return true;

            // Refused: the previous value (or none) comes back, and the monitor recomputes valid options from it.
            provider.Write(key, previous);
            return false;
        }
    }

    /// <summary>Removes the saved value of <paramref name="key"/>: the value of the other sources applies again.</summary>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not a key of the <c>Agent</c> section.</exception>
    public void Reset(string key)
    {
        RequireAgentKey(key);
        lock (gate)
            provider.Write(key, null);
    }

    /// <summary>Writes the value and reads the options it gives; the failure messages, or null when they are valid.</summary>
    private string? Apply(string key, string value)
    {
        try
        {
            // The reload makes the monitor recompute at once, so an invalid value can already throw here.
            provider.Write(key, value);
            _ = monitor.CurrentValue;
            return null;
        }
        catch (Exception ex) when (Failure(ex, key) is { } message)
        {
            return message;
        }
    }

    /// <summary>
    /// The message of a refused value: the validation failures, or a value the binder cannot convert. Null for anything
    /// else, which is not caught. The reload token may wrap the exception in an <see cref="AggregateException"/>.
    /// </summary>
    private static string? Failure(Exception ex, string key)
    {
        switch (ex)
        {
            case OptionsValidationException validation:
                return string.Join(" ", validation.Failures);
            // The binder names the configuration path of a value it cannot convert to the property's type.
            case InvalidOperationException when ex.Message.Contains(key, StringComparison.OrdinalIgnoreCase):
                return key + " is not a valid value for this setting.";
            case AggregateException aggregate:
                return aggregate.Flatten().InnerExceptions.Select(inner => Failure(inner, key)).FirstOrDefault(m => m is not null);
            default:
                return ex.InnerException is { } inner ? Failure(inner, key) : null;
        }
    }

    private static void RequireAgentKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || key.Length == Prefix.Length)
            throw new ArgumentException("Only keys of the " + AgentOptions.SectionName + " section can be saved.", nameof(key));
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
            services.AddSingleton(sp => new RuntimeSettings(provider, sp.GetRequiredService<IOptionsMonitor<AgentOptions>>()));
        return services;
    }
}
