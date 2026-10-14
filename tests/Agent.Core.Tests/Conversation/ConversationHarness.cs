using System.Collections.Concurrent;
using Agent.Core.Data;
using Agent.Core.Learning;
using Agent.Core.Parsing;
using Agent.Core.Tests.Agent;
using Agent.Core.Trace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AgentConversation = Agent.Core.Conversation.Conversation;

namespace Agent.Core.Tests.Conversation;

/// <summary>
/// One conversation over the agent host (<c>AddAgentCore</c>, its clock, its in-process wallet and its test database)
/// with a given parser and a trace that keeps every step. Dispose it at the end of the test.
/// </summary>
internal sealed class ConversationHarness : IAsyncDisposable
{
    private readonly ServiceProvider provider;

    private ConversationHarness(ServiceProvider provider, AgentConversation conversation, RecordingTrace trace)
    {
        this.provider = provider;
        Conversation = conversation;
        Trace = trace;
    }

    public AgentConversation Conversation { get; }

    public RecordingTrace Trace { get; }

    /// <summary>Resets the host (wallet, agent tables, clock) and starts a conversation for <paramref name="userId"/>.</summary>
    public static async Task<ConversationHarness> StartAsync(AgentHost host, IParser parser, int userId)
    {
        await host.ResetAsync();
        var trace = new RecordingTrace();
        var provider = host.BuildProvider((services, configuration) =>
        {
            // Registered before AddAgentCore, so its TryAddScoped NullTraceSink does nothing.
            services.AddSingleton<ITraceSink>(trace);
            services.AddAgentCore(configuration);
        });

        var conversation = new AgentConversation(
            provider.GetRequiredService<IServiceScopeFactory>(),
            parser,
            host.Clock,
            provider.GetRequiredService<IOptions<AgentOptions>>(),
            trace,
            userId);
        return new ConversationHarness(provider, conversation, trace);
    }

    /// <summary>Every learning-log row, oldest first.</summary>
    public async Task<List<LearningEntryRow>> LearningRowsAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.LearningEntries.AsNoTracking().OrderBy(r => r.Id).ToListAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Conversation.Dispose();
        await provider.DisposeAsync();
    }

    /// <summary>A trace sink that keeps every step.</summary>
    internal sealed class RecordingTrace : ITraceSink
    {
        private readonly ConcurrentQueue<(TraceZone Zone, string Step, string? Detail)> entries = new();

        public IReadOnlyList<(TraceZone Zone, string Step, string? Detail)> Entries => [.. entries];

        public void Record(TraceZone zone, string step, string? detail) => entries.Enqueue((zone, step, detail));
    }
}
