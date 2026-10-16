using Agent.Core;
using Agent.Core.Parsing;
using Agent.Core.Text;
using Agent.Core.Trace;
using Agent.Web.Resources;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Agent.Web.Services;

/// <summary>Registers the web console's services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds <c>AddAgentCore</c> (with the hosted sweeper when configured), localisation for the phone strings, the
    /// per-circuit <see cref="ChatSession"/>, <see cref="ConsoleNavigation"/> and <see cref="TraceLog"/>, the
    /// <see cref="ITraceSink"/> that routes every action's steps to its circuit's log, and the parser
    /// <c>Agent:Parser</c> names as <see cref="IParser"/> (<see cref="ParserFactory.AddAgentParser"/>). The D6 words
    /// (<see cref="AgentOptions.YesWords"/>, <see cref="AgentOptions.NoWords"/>) are set from the resource file
    /// (<c>Word_yes</c>, <c>Word_no</c>), so <c>Agent.Core</c> holds no phone text (rule 4). When
    /// <paramref name="configuration"/> has a <see cref="RuntimeSettingsSource"/> (Program adds it), the singleton
    /// <see cref="RuntimeSettings"/> saves agent settings live (D8). The parser is wrapped in <see cref="FaultingParser"/>
    /// (<see cref="AddFaultingParser"/>), and the fault panel gets <see cref="ConsoleOptions"/> (validated at start) and
    /// the typed <see cref="WalletFaults"/> client.
    /// </summary>
    public static IServiceCollection AddAgentWebServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddLocalization();
        services.AddScoped<TraceLog>();

        // In the circuit's scope this is the circuit's log. In an action's own scope it is the log of the circuit whose
        // action is running (TraceLog.Current), so the orchestrator and the Confirm handler write to the right trace.
        // Registered before AddAgentCore, whose TryAddScoped NullTraceSink then does nothing.
        services.AddScoped<ITraceSink>(sp => TraceLog.Current.Value ?? sp.GetRequiredService<TraceLog>());

        services.AddAgentCore(configuration);
        services.AddOptions<AgentOptions>().PostConfigure<IStringLocalizer<PhoneStrings>>((options, text) =>
        {
            options.YesWords = PhoneWords.Split(text["Word_yes"].Value);
            options.NoWords = PhoneWords.Split(text["Word_no"].Value);
        });
        // The card's amount in words: the resource file's number words, built once (Khalil's list, 17 Oct 2026).
        services.AddSingleton(sp => AmountWordParts.FromResources(sp.GetRequiredService<IStringLocalizer<PhoneStrings>>()));
        services.AddAgentParser(configuration);
        services.AddFaultingParser();
        services.AddOptions<ConsoleOptions>()
            .Bind(configuration.GetSection(ConsoleOptions.SectionName))
            .ValidateDataAnnotations()
            // Review R2 of milestone 8 (Khalil, 16 Oct 2026): outside Development the placeholder would only fail at the
            // first click (403), so it fails at start instead. The message names the setting, never a value.
            .Validate<IServiceProvider>(
                (o, sp) => sp.GetService<IHostEnvironment>()?.IsDevelopment() == true || ConsoleOptions.IsUsableOutsideDevelopment(o.WalletAdminKey),
                ConsoleOptions.KeyRequiredMessage)
            .ValidateOnStart();
        services.AddHttpClient<WalletFaults>((sp, http) => WalletFaults.Configure(
            http,
            sp.GetRequiredService<IOptions<AgentOptions>>().Value,
            sp.GetRequiredService<IOptions<ConsoleOptions>>().Value,
            sp.GetService<IHostEnvironment>()));
        services.AddRuntimeSettings(configuration);
        services.AddScoped<ChatSession>();
        services.AddScoped<ConsoleNavigation>();
        return services;
    }

    /// <summary>
    /// Wraps the last registered <see cref="IParser"/> in <see cref="FaultingParser"/> (the console's model fault,
    /// milestone 8), keeping its lifetime: the original descriptor's instance, factory or implementation type builds the
    /// parser underneath. Adds the singleton <see cref="ModelFaultSwitch"/> once. Called by
    /// <see cref="AddAgentWebServices"/> after <see cref="ParserFactory.AddAgentParser"/>; a host that registers its own
    /// parser afterwards calls it again to wrap that one.
    /// </summary>
    /// <exception cref="InvalidOperationException">No <see cref="IParser"/> is registered.</exception>
    public static IServiceCollection AddFaultingParser(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ModelFaultSwitch>();
        var index = -1;
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(IParser) && !services[i].IsKeyedService)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            throw new InvalidOperationException("No IParser is registered to wrap.");

        var original = services[index];
        services[index] = ServiceDescriptor.Describe(
            typeof(IParser),
            sp => new FaultingParser(Inner(sp, original), sp.GetRequiredService<ModelFaultSwitch>()),
            original.Lifetime);
        return services;
    }

    private static IParser Inner(IServiceProvider services, ServiceDescriptor original)
    {
        if (original.ImplementationInstance is IParser instance)
            return instance;
        if (original.ImplementationFactory is { } factory)
            return (IParser)factory(services);
        if (original.ImplementationType is { } type)
            return (IParser)ActivatorUtilities.CreateInstance(services, type);
        throw new InvalidOperationException("The IParser registration has no instance, factory or type.");
    }
}
