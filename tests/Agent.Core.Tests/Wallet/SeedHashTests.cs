using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

public class SeedHashTests
{
    [Fact]
    public void SeedHashMatchesCommittedValue()
    {
        var committed = File.ReadAllText(Path.Combine(RepositoryRoot(), "data", "seed", "seed-hash.txt")).Trim();

        Assert.True(
            string.Equals(committed, SeedData.ComputeHash(), StringComparison.Ordinal),
            "The seed changed. If that is intended, update data/seed/seed-hash.txt and re-check every answer key (build plan, milestone 2).");
    }

    [Fact]
    public void SeedHashIsStableAcrossCalls()
        => Assert.Equal(SeedData.ComputeHash(), SeedData.ComputeHash());

    internal static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ZainCashAgent.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("ZainCashAgent.slnx not found above " + AppContext.BaseDirectory);
    }
}
