namespace Agent.Eval.Running;

/// <summary>Finds the repository and resolves the runner's directories against it.</summary>
public static class EvalPaths
{
    /// <summary>The file that marks the repository root.</summary>
    public const string SolutionFile = "ZainCashAgent.slnx";

    /// <summary>The nearest directory at or above <paramref name="start"/> (the app's base directory by default) that holds <see cref="SolutionFile"/>.</summary>
    /// <exception cref="DirectoryNotFoundException">No such directory.</exception>
    public static string RepositoryRoot(string? start = null)
    {
        for (var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFile)))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException(SolutionFile + " not found above " + (start ?? AppContext.BaseDirectory));
    }

    /// <summary><paramref name="path"/> itself when absolute, else under the repository root.</summary>
    public static string Resolve(string path, string? repositoryRoot = null)
        => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(repositoryRoot ?? RepositoryRoot(), path));

    /// <summary>The seed directory of a set directory (<c>data</c> gives <c>data/seed</c>).</summary>
    public static string SeedDirectory(EvalOptions options, string? repositoryRoot = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Path.Combine(Resolve(options.SetDirectory, repositoryRoot), "seed");
    }
}
