namespace Agent.Core.Data;

/// <summary>The name of the agent's connection string and the message when it is missing.</summary>
public static class AgentConnection
{
    public const string ConnectionStringName = "Agent";

    public const string MissingConnectionStringMessage =
        "No connection string 'Agent'. Set ConnectionStrings:Agent in appsettings, user secrets or the ConnectionStrings__Agent environment variable.";

    /// <summary>The user secrets id of Agent.Web (its csproj <c>UserSecretsId</c>), read by the design-time factory.</summary>
    public const string WebUserSecretsId = "2caa1826-0a12-43da-8479-d86df1b92e41";
}
