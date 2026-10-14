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
    /// <see cref="ITraceSink"/> that routes every action's steps to its circuit's log, and the singleton
    /// <see cref="ScriptedParser"/> as <see cref="IParser"/> (the only parser until milestone 6). The D6 words
    /// (<see cref="AgentOptions.YesWords"/>, <see cref="AgentOptions.NoWords"/>) are set from the resource file
    /// (<c>Word_yes</c>, <c>Word_no</c>), so <c>Agent.Core</c> holds no phone text (rule 4).
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
            options.YesWords = [text["Word_yes"].Value];
            options.NoWords = [text["Word_no"].Value];
        });
        // A factory, not the type: DI would pick the constructor that takes the sentences and pass an empty list.
        services.AddSingleton<IParser>(_ => new ScriptedParser());
        services.AddScoped<ChatSession>();
        services.AddScoped<ConsoleNavigation>();
        return services;
    }
}
