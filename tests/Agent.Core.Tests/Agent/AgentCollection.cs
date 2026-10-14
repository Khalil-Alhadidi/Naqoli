namespace Agent.Core.Tests.Agent;

/// <summary>
/// Every test that needs the agent's services, its test database or the wallet behind them belongs to this
/// collection. It owns its own wallet host, so it never shares a server with the <c>wallet-api</c> collection.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AgentCollection : ICollectionFixture<AgentHost>
{
    public const string Name = "agent";
}
