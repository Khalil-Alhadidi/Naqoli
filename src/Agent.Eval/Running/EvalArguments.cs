using System.Globalization;
using System.Text.RegularExpressions;

namespace Agent.Eval.Running;

/// <summary>
/// The runner's command line: <c>dev</c> or <c>heldout --milestone-11-run</c>, then <c>--only id,id</c>,
/// <c>--wallet url</c>, <c>--out dir</c>, <c>--parser scripted|claude:&lt;candidate&gt;</c>, <c>--label text</c> and
/// <c>--repeat N</c> in any order.
/// </summary>
/// <param name="Set"><see cref="HeldOutGuard.DevSet"/> or <see cref="HeldOutGuard.HeldOutSet"/>.</param>
/// <param name="Milestone11Run">The held-out flag was given.</param>
/// <param name="Only">The case ids to run, or null for all.</param>
/// <param name="Wallet">The wallet's address, overriding <c>Agent:WalletBaseUrl</c>.</param>
/// <param name="Out">The report directory, overriding <c>Eval:ReportDirectory</c>.</param>
/// <param name="Parser">The parser measured: <c>scripted</c> (the default) or <c>claude:&lt;candidate&gt;</c>.</param>
/// <param name="Label">A tag for the report name (lower-case letters, digits, <c>_</c> and <c>-</c>), or null.</param>
/// <param name="Repeat">How many times the set is played, <see cref="MinRepeat"/> to <see cref="MaxRepeat"/>; 1 by default.</param>
public sealed partial record EvalArguments(
    string Set,
    bool Milestone11Run,
    IReadOnlyList<string>? Only,
    Uri? Wallet,
    string? Out,
    string Parser = EvalArguments.ScriptedParser,
    string? Label = null,
    int Repeat = 1)
{
    /// <summary>The usage line.</summary>
    public const string Usage =
        "Usage: Agent.Eval dev | heldout --milestone-11-run  [--only <id,id,...>] [--wallet <url>] [--out <dir>] [--parser scripted|claude:<candidate>] [--label <a-z0-9_->] [--repeat <1-20>]";

    /// <summary>The fewest runs <c>--repeat</c> takes.</summary>
    public const int MinRepeat = 1;

    /// <summary>The most runs <c>--repeat</c> takes.</summary>
    public const int MaxRepeat = 20;

    /// <summary>The default parser.</summary>
    public const string ScriptedParser = "scripted";

    /// <summary>The prefix of a model parser: <c>claude:</c> and a candidate name of the <c>Model</c> section.</summary>
    public const string ClaudePrefix = "claude:";

    /// <summary>The candidate of a <c>claude:&lt;candidate&gt;</c> parser, or null for the scripted one.</summary>
    public string? Candidate => Parser.StartsWith(ClaudePrefix, StringComparison.Ordinal) ? Parser[ClaudePrefix.Length..] : null;

    /// <summary>Parses <paramref name="args"/>; null with <paramref name="error"/> set when they do not fit <see cref="Usage"/>.</summary>
    public static EvalArguments? Parse(IReadOnlyList<string> args, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        error = null;
        if (args.Count == 0 || args[0] is not (HeldOutGuard.DevSet or HeldOutGuard.HeldOutSet))
        {
            error = "The first argument must be 'dev' or 'heldout'.";
            return null;
        }

        var flag = false;
        IReadOnlyList<string>? only = null;
        Uri? wallet = null;
        string? output = null;
        var parser = ScriptedParser;
        string? label = null;
        var repeat = MinRepeat;
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == HeldOutGuard.Milestone11Flag)
            {
                flag = true;
                continue;
            }

            if (arg is not ("--only" or "--wallet" or "--out" or "--parser" or "--label" or "--repeat"))
            {
                error = "Unknown argument '" + arg + "'.";
                return null;
            }

            if (i + 1 >= args.Count)
            {
                error = arg + " needs a value.";
                return null;
            }

            var value = args[++i];
            switch (arg)
            {
                case "--only":
                    only = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                    break;
                case "--wallet":
                    if (!Uri.TryCreate(value, UriKind.Absolute, out wallet) || wallet.Scheme is not ("http" or "https"))
                    {
                        error = "--wallet needs an http or https address.";
                        return null;
                    }

                    break;
                case "--parser":
                    if (!ParserPattern().IsMatch(value))
                    {
                        error = "--parser needs 'scripted' or 'claude:<candidate>', a candidate of the Model section (haiku, sonnet, opus).";
                        return null;
                    }

                    parser = value;
                    break;
                case "--label":
                    if (!LabelPattern().IsMatch(value))
                    {
                        error = "--label needs lower-case letters, digits, '_' or '-' only.";
                        return null;
                    }

                    label = value;
                    break;
                case "--repeat":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out repeat) || repeat is < MinRepeat or > MaxRepeat)
                    {
                        error = "--repeat needs a whole number from 1 to 20.";
                        return null;
                    }

                    break;
                default:
                    output = value;
                    break;
            }
        }

        if (flag && args[0] != HeldOutGuard.HeldOutSet)
        {
            error = HeldOutGuard.Milestone11Flag + " goes with 'heldout' only.";
            return null;
        }

        return new EvalArguments(args[0], flag, only, wallet, output, parser, label, repeat);
    }

    [GeneratedRegex("^(scripted|claude:[a-z0-9_-]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ParserPattern();

    [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex LabelPattern();
}
