using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Agent.Core.Data;

/// <summary>
/// Builds the context for the EF Core tools (dotnet ef, with <c>--startup-project src/Agent.Web</c>) without starting
/// the web host. The environment is taken from, in order: an <c>--environment &lt;name&gt;</c> or
/// <c>--environment=&lt;name&gt;</c> argument (passed after <c>--</c> on the dotnet ef command line), the
/// ASPNETCORE_ENVIRONMENT variable, the DOTNET_ENVIRONMENT variable, and finally Development. Configuration is read
/// from the startup project's build output (<see cref="AppContext.BaseDirectory"/>), lowest to highest precedence:
/// appsettings.json, appsettings.{environment}.json, Agent.Web's user secrets (only when the environment is
/// Development), environment variables.
/// </summary>
public class AgentDbContextFactory : IDesignTimeDbContextFactory<AgentDbContext>
{
    private const string DefaultEnvironment = "Development";

    public AgentDbContext CreateDbContext(string[] args)
    {
        var environment = ResolveEnvironment(args);

        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true);

        if (string.Equals(environment, DefaultEnvironment, StringComparison.OrdinalIgnoreCase))
            builder.AddUserSecrets(AgentConnection.WebUserSecretsId);

        var configuration = builder
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString(AgentConnection.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(AgentConnection.MissingConnectionStringMessage);

        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AgentDbContext(options);
    }

    private static string ResolveEnvironment(string[] args)
    {
        const string option = "--environment";

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, option, StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && !string.IsNullOrWhiteSpace(args[i + 1]))
                return args[i + 1];

            if (arg.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase)
                && arg.Length > option.Length + 1)
                return arg[(option.Length + 1)..];
        }

        return NonEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"))
            ?? NonEmpty(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"))
            ?? DefaultEnvironment;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
