using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Core.Tests.Wallet;

/// <summary>Reads the ProblemDetails the wallet returns and its extensions.</summary>
internal static class WalletProblemReader
{
    public static async Task<ProblemDetails> ReadProblemAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        return problem;
    }

    public static string? Code(this ProblemDetails problem)
        => problem.Extensions.TryGetValue("code", out var value) && value is JsonElement element ? element.GetString() : null;

    public static long Long(this ProblemDetails problem, string name)
    {
        Assert.True(problem.Extensions.TryGetValue(name, out var value), $"Extension '{name}' is missing.");
        return Assert.IsType<JsonElement>(value).GetInt64();
    }
}
