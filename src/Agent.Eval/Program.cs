using Agent.Eval.Running;

// Agent.Eval: plays a request set through the agent's conversation logic and writes the report (milestone 5).
// Exit codes: 0 the run completed (whatever the pass rate), 2 the held-out guard refused or the command line is wrong,
// 1 the wallet is unreachable or the set file is missing. It never prints case text.
return await EvalCommand.RunAsync(args, Console.Out, Console.Error, CancellationToken.None);
