using System.Globalization;
using System.Net;
using Microsoft.Data.SqlClient;

namespace Agent.Web.Journeys;

/// <summary>
/// The journey databases are named per process and per host, so two runs on one SQL Server never drop each other's
/// databases. The fixture host is the first of the two; a second host starts beside it, then drops its own databases.
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class JourneyHostTests(JourneyHost host)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASecondHostStartsBesideTheFirstOnItsOwnDatabases()
    {
        Assert.SkipWhen(host.SkipReason is not null, host.SkipReason ?? string.Empty);

        var second = new JourneyHost();
        string[] secondNames;
        try
        {
            await second.InitializeAsync();
            Assert.Null(second.SkipReason);

            secondNames = [second.WalletDatabaseName, second.AgentDatabaseName];
            string[] all = [host.WalletDatabaseName, host.AgentDatabaseName, .. secondNames];
            Assert.Equal(4, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            var processId = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            Assert.All(all, name =>
            {
                Assert.EndsWith("_Test", name, StringComparison.Ordinal);
                Assert.Contains(processId, name, StringComparison.Ordinal);
            });

            foreach (var wallet in new[] { host.Wallet, second.Wallet })
            {
                using var health = await wallet.GetAsync("/health", Token);
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            }

            using var client = new HttpClient();
            foreach (var console in new[] { host.WebAddress, second.WebAddress })
            {
                using var page = await client.GetAsync(console, Token);
                Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            }
        }
        finally
        {
            await second.DisposeAsync();
        }

        Assert.Equal(0, await CountDatabasesAsync(second.WalletConnectionString, secondNames));
        Assert.Equal(2, await CountDatabasesAsync(second.WalletConnectionString, [host.WalletDatabaseName, host.AgentDatabaseName]));
    }

    /// <summary>How many of the two <paramref name="names"/> exist on the server of <paramref name="connectionString"/>.</summary>
    private static async Task<int> CountDatabasesAsync(string connectionString, string[] names)
    {
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", Pooling = false };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name IN (@a, @b)";
        command.Parameters.Add(new SqlParameter("@a", System.Data.SqlDbType.NVarChar, 128) { Value = names[0] });
        command.Parameters.Add(new SqlParameter("@b", System.Data.SqlDbType.NVarChar, 128) { Value = names[1] });
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }
}
