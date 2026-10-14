using Wallet.MockApi.Data.Seed;

namespace Agent.Core.Tests.Wallet;

/// <summary>The hash over the seed files: committed value, output copy, line endings and sensitivity.</summary>
public class SeedHashTests
{
    [Fact]
    public void SeedHashMatchesCommittedValue()
    {
        var committed = File.ReadAllText(Path.Combine(RepositoryRoot(), "data", "seed", "seed-hash.txt")).Trim();

        Assert.True(
            string.Equals(committed, SeedHash.Compute(SeedSetTests.SeedDirectory), StringComparison.Ordinal),
            "The seed files changed. If that is intended, run `dotnet run --project src/Wallet.MockApi -- seed-hash`, write the value to data/seed/seed-hash.txt and re-check every answer key.");
    }

    [Fact]
    public void OutputCopyHashesTheSame()
        => Assert.Equal(SeedHash.Compute(SeedSetTests.SeedDirectory), SeedHash.Compute(SeedFiles.DefaultDirectory));

    [Fact]
    public void CrlfAndLfHashTheSame()
    {
        var dir = SeedSetTests.CopySeed();
        try
        {
            foreach (var file in SeedFiles.All)
            {
                var path = Path.Combine(dir.FullName, file);
                var lf = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
                File.WriteAllText(path, lf.Replace("\n", "\r\n", StringComparison.Ordinal));
            }

            Assert.Contains((byte)'\r', File.ReadAllBytes(Path.Combine(dir.FullName, SeedFiles.Users)));
            Assert.Equal(SeedHash.Compute(SeedSetTests.SeedDirectory), SeedHash.Compute(dir.FullName));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void OneChangedByteChangesTheHash()
    {
        var dir = SeedSetTests.CopySeed();
        try
        {
            SeedSetTests.ReplaceFirst(dir, SeedFiles.Users, "750000", "750001");

            Assert.NotEqual(SeedHash.Compute(SeedSetTests.SeedDirectory), SeedHash.Compute(dir.FullName));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void MissingFileNamesTheFile()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var ex = Assert.Throws<FileNotFoundException>(() => SeedHash.Compute(dir.FullName));
            Assert.Contains(SeedFiles.Users, ex.Message);
        }
        finally
        {
            dir.Delete(true);
        }
    }

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
