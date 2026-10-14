using Agent.Core.Audit;
using Agent.Core.Confirm;
using Agent.Core.Data;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
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
    /// <see cref="WalletClient"/>, the scoped <see cref="Checks"/>, <see cref="AuditLog"/>, <see cref="PendingStore"/>,
    /// <see cref="CardBuilder"/>, <see cref="Orchestrator"/> and <see cref="ConfirmHandler"/>. Nothing connects until it is used, so the app starts without the wallet or the database.
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
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

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
        services.AddScoped<PendingStore>();
        services.AddScoped<CardBuilder>();
        services.AddScoped<Orchestrator>();
        services.AddScoped<ConfirmHandler>();

        return services;
    }
}
