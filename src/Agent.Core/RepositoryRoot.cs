namespace Agent.Core;

/// <summary>Finds the repository a process runs from: the directory that holds the solution file.</summary>
public static class RepositoryRoot
{
    /// <summary>The file that marks the repository root.</summary>
    public const string SolutionFile = "ZainCashAgent.slnx";

    /// <summary>
    /// The nearest directory at or above <paramref name="start"/> (the app's base directory by default) that holds
    /// <see cref="SolutionFile"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No directory at or above <paramref name="start"/> holds it.</exception>
    public static string Find(string? start = null)
    {
        var from = start ?? AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(from); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFile)))
                return dir.FullName;
        }

        throw new InvalidOperationException(SolutionFile + " not found at or above " + from + ".");
    }
}
