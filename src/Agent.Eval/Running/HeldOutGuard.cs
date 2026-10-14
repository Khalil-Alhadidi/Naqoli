using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Agent.Eval.Running;

/// <summary>The guard's decision.</summary>
/// <param name="Allowed">True when the run may open its set file.</param>
/// <param name="FileHash">The held-out file's SHA-256 (lower-case hex), computed only with the milestone 11 flag.</param>
/// <param name="CommittedHash">The held-out file's SHA-256 recorded in <c>docs/disclosure.md</c>, read only with the flag.</param>
/// <param name="Message">One line for the console (never case text).</param>
public sealed record HeldOutDecision(bool Allowed, string? FileHash, string? CommittedHash, string Message);

/// <summary>
/// Rule 7: the held-out set is opened only by <c>Agent.Eval heldout --milestone-11-run</c>. The guard runs before any set
/// file is opened. Without the flag it refuses at once and touches no file. With the flag it hashes the file and
/// compares the hash with the one committed in <c>docs/disclosure.md</c> (the 64-hex value on the line naming
/// <see cref="HeldOutFileName"/>), and refuses when they differ.
/// </summary>
public static partial class HeldOutGuard
{
    public const string HeldOutSet = "heldout";
    public const string DevSet = "dev";
    public const string Milestone11Flag = "--milestone-11-run";
    public const string HeldOutFileName = "requests.heldout.jsonl";

    /// <summary>The rule-7 sentence printed when the flag is missing.</summary>
    public const string RuleSentence =
        "Rule 7: the held-out set is sealed until the milestone 11 run. Only 'Agent.Eval heldout --milestone-11-run' opens it; tune on the dev set.";

    /// <summary>Decides whether the run of <paramref name="args"/> may open its set file.</summary>
    public static HeldOutDecision Check(IReadOnlyList<string> args, string setDirectory, string disclosurePath)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(setDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(disclosurePath);

        if (args.Count == 0 || args[0] != HeldOutSet)
            return new HeldOutDecision(true, null, null, "Not the held-out set.");

        // Without the flag nothing is opened, not even the disclosure file.
        if (!args.Contains(Milestone11Flag, StringComparer.Ordinal))
            return new HeldOutDecision(false, null, null, RuleSentence);

        var committed = CommittedHash(disclosurePath);
        if (committed is null)
            return new HeldOutDecision(false, null, null, "No SHA-256 for " + HeldOutFileName + " in " + disclosurePath + "; refusing.");

        var path = Path.Combine(setDirectory, HeldOutFileName);
        if (!File.Exists(path))
            return new HeldOutDecision(false, null, committed, "The set file " + path + " is missing.");

        string hash;
        using (var stream = File.OpenRead(path))
            hash = Convert.ToHexStringLower(SHA256.HashData(stream));

        return string.Equals(hash, committed, StringComparison.OrdinalIgnoreCase)
            ? new HeldOutDecision(true, hash, committed, "Held-out file SHA-256 " + hash + " matches the committed " + committed + ".")
            : new HeldOutDecision(false, hash, committed, "Held-out file SHA-256 " + hash + " differs from the committed " + committed + "; refusing.");
    }

    /// <summary>The 64-hex value on the first line of <paramref name="disclosurePath"/> that names the held-out file, or null.</summary>
    public static string? CommittedHash(string disclosurePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(disclosurePath);
        if (!File.Exists(disclosurePath))
            return null;

        foreach (var line in File.ReadLines(disclosurePath))
        {
            if (!line.Contains(HeldOutFileName, StringComparison.Ordinal))
                continue;
            var match = Sha256Hex().Match(line);
            if (match.Success)
                return match.Value.ToLowerInvariant();
        }

        return null;
    }

    /// <summary>The SHA-256 of a file, lower-case hex.</summary>
    public static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    [GeneratedRegex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{64}(?![0-9A-Fa-f])")]
    private static partial Regex Sha256Hex();
}
