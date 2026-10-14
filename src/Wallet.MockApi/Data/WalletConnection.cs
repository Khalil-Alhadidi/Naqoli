namespace Wallet.MockApi.Data;

/// <summary>
/// The wallet connection string name and the message shown when it is missing.
/// Shared by the running app and the design-time factory.
/// </summary>
public static class WalletConnection
{
    public const string ConnectionStringName = "Wallet";

    public const string MissingConnectionStringMessage =
        "No connection string 'Wallet'. Set ConnectionStrings:Wallet in appsettings, user secrets or the ConnectionStrings__Wallet environment variable.";
}
