using Microsoft.AspNetCore.Mvc.Testing;

namespace Agent.Core.Tests.Wallet;

public class HealthTests
{
    [Fact]
    public async Task HealthReturnsOk()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
