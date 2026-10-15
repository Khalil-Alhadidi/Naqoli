using Agent.Core;
using Agent.Core.Parsing;
using Agent.Core.Trace;
using Agent.Web.Resources;
using Microsoft.Extensions.Localization;

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
    /// <see cref="RuntimeSettings"/> saves agent settings live (D8).
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
        services.AddAgentParser(configuration);
        services.AddRuntimeSettings(configuration);
        services.AddScoped<ChatSession>();
        services.AddScoped<ConsoleNavigation>();
        return services;
    }
}
