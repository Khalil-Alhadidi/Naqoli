using Agent.Core.Aliases;
using Agent.Core.Audit;
using Agent.Core.Confirm;
using Agent.Core.Data;
using Agent.Core.Learning;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Store;
using Agent.Core.Trace;
using Agent.Core.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Agent.Core;

/// <summary>Registers the agent's services.</summary>
public static class AgentCoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="AgentOptions"/> (section <c>Agent</c>, validated at start), a <see cref="TimeProvider"/>
    /// unless one is registered, the <see cref="AgentDbContext"/> on <c>ConnectionStrings:Agent</c> and the typed
    /// <see cref="WalletClient"/>, the scoped <see cref="Checks"/>, <see cref="AuditLog"/>, <see cref="LearningLog"/>,
    /// <see cref="PendingStore"/>, <see cref="CardBuilder"/>, <see cref="Orchestrator"/>, <see cref="ConfirmHandler"/> and
    /// <see cref="AliasDirectory"/>, the
    /// singleton <see cref="KillSwitch"/> (off at start), a scoped
    /// <see cref="NullTraceSink"/> as <see cref="ITraceSink"/> unless one is registered, and the hosted
    /// <see cref="PendingSweeper"/> when <c>Agent:SweepIntervalSeconds</c> is above 0. No <c>IParser</c> is registered:
    /// the host picks one. Nothing connects until it is used, so the app starts without the wallet or the database.
    /// </summary>
    public static IServiceCollection AddAgentCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName))
            // An empty key means none, as an absent one does; post-configuration runs before validation.
            .PostConfigure(o => o.WalletClientKey = string.IsNullOrEmpty(o.WalletClientKey) ? null : o.WalletClientKey)
            .ValidateDataAnnotations()
            .Validate<IServiceProvider>(
                (o, sp) => o.WalletClientKey is not null || sp.GetService<IHostEnvironment>()?.IsDevelopment() == true,
                "Agent:WalletClientKey is required outside Development. Set it in user secrets or the environment.")
            .Validate(
                o => o.StaleExecutingOutlastsAConfirm,
                "Agent:StaleExecutingAfterSeconds must be more than three times Agent:WalletTimeoutSeconds: a Confirm makes three timed wallet calls while its record is Executing.")
            // Review M4 (Khalil, 16 Oct 2026): out-of-order limits would refuse everything without saying why.
            .Validate(
                o => o.LimitsAreOrdered,
                "Agent:MinAmountIqd must not be above Agent:PerTransactionCapIqd, and Agent:PerTransactionCapIqd must not be above Agent:PerDayCapIqd.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<KillSwitch>();

        services.AddDbContext<AgentDbContext>(o =>
        {
            var connectionString = configuration.GetConnectionString(AgentConnection.ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(AgentConnection.MissingConnectionStringMessage);
            o.UseSqlServer(connectionString);
        });

        services.AddHttpClient<WalletClient>((sp, http) => WalletClient.Configure(
            http,
            sp.GetRequiredService<IOptions<AgentOptions>>().Value,
            sp.GetService<IHostEnvironment>()));

        services.AddScoped<Checks>();
        services.AddScoped<AuditLog>();
        services.AddScoped<LearningLog>();
        services.AddScoped<PendingStore>();
        services.AddScoped<CardBuilder>();
        services.AddScoped<Orchestrator>();
        services.AddScoped<ConfirmHandler>();
        services.AddScoped<AliasDirectory>();
        services.AddScoped<OrdersReader>();
        services.TryAddScoped<ITraceSink, NullTraceSink>();

        // The sweep runs in the host only when it has an interval; the test hosts set 0 and sweep by hand.
        var sweepInterval = configuration.GetSection(AgentOptions.SectionName)
            .GetValue(nameof(AgentOptions.SweepIntervalSeconds), new AgentOptions().SweepIntervalSeconds);
        if (sweepInterval > 0)
            services.AddHostedService<PendingSweeper>();

        return services;
    }
}
