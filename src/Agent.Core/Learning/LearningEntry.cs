namespace Agent.Core.Learning;

/// <summary>
/// One learning-log entry: a reason from <c>settings.json</c> <c>learning_log.logged</c> and its detail (a code, the
/// typed recipient, or what was asked for). Milestone 6 persists exactly what <see cref="LearningLogRule"/> returns.
/// </summary>
public sealed record LearningEntry(string Reason, string? Detail);
