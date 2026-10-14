namespace Agent.Eval.Running;

/// <summary>
/// The runner's command line: <c>dev</c> or <c>heldout --milestone-11-run</c>, then <c>--only id,id</c>,
/// <c>--wallet url</c> and <c>--out dir</c> in any order.
/// </summary>
/// <param name="Set"><see cref="HeldOutGuard.DevSet"/> or <see cref="HeldOutGuard.HeldOutSet"/>.</param>
/// <param name="Milestone11Run">The held-out flag was given.</param>
/// <param name="Only">The case ids to run, or null for all.</param>
/// <param name="Wallet">The wallet's address, overriding <c>Agent:WalletBaseUrl</c>.</param>
/// <param name="Out">The report directory, overriding <c>Eval:ReportDirectory</c>.</param>
public sealed record EvalArguments(string Set, bool Milestone11Run, IReadOnlyList<string>? Only, Uri? Wallet, string? Out)
{
    /// <summary>The usage line.</summary>
    public const string Usage =
        "Usage: Agent.Eval dev | heldout --milestone-11-run  [--only <id,id,...>] [--wallet <url>] [--out <dir>]";

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
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == HeldOutGuard.Milestone11Flag)
            {
                flag = true;
                continue;
            }

            if (arg is not ("--only" or "--wallet" or "--out"))
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

        return new EvalArguments(args[0], flag, only, wallet, output);
    }
}
