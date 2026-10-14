namespace Agent.Core.Tests.Wallet;

/// <summary>
/// Every test that touches the wallet database belongs to this collection, so they share one probe,
/// one test database and one API host, and run one at a time.
/// </summary>
[CollectionDefinition(Name)]
public sealed class WalletApiCollection : ICollectionFixture<WalletApiFixture>
{
    public const string Name = "wallet-api";
}
