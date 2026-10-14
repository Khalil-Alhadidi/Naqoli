using Agent.Core;
using Agent.Core.Parsing;
using Agent.Core.Trace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Eval.Running;

/// <summary>Registers the agent's services as the runner uses them.</summary>
public static class EvalServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="EvalOptions"/> (section <c>Eval</c>, validated), the <see cref="EvalClock"/> as the agent's
    /// <see cref="TimeProvider"/>, the <see cref="EvalTrace"/> as its <see cref="ITraceSink"/>, and <c>AddAgentCore</c>.
    /// Both are registered before <c>AddAgentCore</c>, whose <c>TryAdd</c> registrations then keep them. Then
    /// <c>AddAgentParser</c>: the parser <c>Agent:Parser</c> names (the runner sets it from <c>--parser</c>), which the
    /// runner wraps in a <see cref="RecordingParser"/>.
    /// </summary>
    public static IServiceCollection AddAgentEval(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<EvalOptions>()
            .Bind(configuration.GetSection(EvalOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<EvalClock>();
        services.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<EvalClock>());
        services.AddSingleton<EvalTrace>();
        services.AddSingleton<ITraceSink>(sp => sp.GetRequiredService<EvalTrace>());
        services.AddAgentCore(configuration);
        services.AddAgentParser(configuration);
        return services;
    }
}
