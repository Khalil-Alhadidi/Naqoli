using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Wallet.MockApi;

namespace Agent.Core.Tests.Wallet;

/// <summary>The OpenAPI document and the Scalar page over it, Development only (Khalil, 22 Oct 2026).</summary>
public class ApiReferenceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDocumentDeclaresBothKeyHeaders()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var document = await ReadDocumentAsync(client);
        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");

        AssertHeaderScheme(schemes.GetProperty(OpenApiSecurity.ClientKeySchemeName), ClientKeyFilter.HeaderName);
        AssertHeaderScheme(schemes.GetProperty(OpenApiSecurity.AdminKeySchemeName), AdminKeyFilter.HeaderName);
    }

    [Fact]
    public async Task TheDocumentSaysWhichRoutesNeedWhichKey()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var document = await ReadDocumentAsync(client);
        var paths = document.RootElement.GetProperty("paths");

        Assert.Empty(SchemesOf(paths.GetProperty("/health").GetProperty("get")));
        Assert.Equal([OpenApiSecurity.ClientKeySchemeName], SchemesOf(paths.GetProperty("/users/{id}").GetProperty("get")));
        Assert.Equal([OpenApiSecurity.ClientKeySchemeName], SchemesOf(paths.GetProperty("/transactions").GetProperty("post")));
        Assert.Equal(
            [OpenApiSecurity.ClientKeySchemeName, OpenApiSecurity.AdminKeySchemeName],
            SchemesOf(paths.GetProperty("/admin/reset").GetProperty("post")));
    }

    [Fact]
    public async Task TheReferencePageIsServedInDevelopment()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/scalar", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync(Token);
        Assert.Contains("ZainCash mock wallet", html);

        // The page loads nothing from outside the machine (README Disclosure). Scalar writes these three settings into
        // the configuration it embeds in the page: no fonts from Scalar's CDN, no AI agent, no link to the hosted client.
        Assert.Contains("\"withDefaultFonts\":false", html);
        Assert.Contains("\"agent\":{\"disabled\":true}", html);
        Assert.Contains("\"hideClientButton\":true", html);
    }

    [Fact]
    public async Task OutsideDevelopmentNeitherRouteExists()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            // Never opened: no route in this test touches the database. The start only needs a non-empty value.
            host.UseSetting("ConnectionStrings:Wallet", "Server=(none);Database=ZainCashWallet_Test");
        });
        using var client = factory.CreateClient();

        using var page = await client.GetAsync("/scalar", Token);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        using var document = await client.GetAsync("/openapi/v1.json", Token);
        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/openapi/v1.json", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
    }

    private static void AssertHeaderScheme(JsonElement scheme, string headerName)
    {
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("header", scheme.GetProperty("in").GetString());
        Assert.Equal(headerName, scheme.GetProperty("name").GetString());
    }

    // The scheme names of an operation's security requirements, in document order; empty when the operation has none.
    private static string[] SchemesOf(JsonElement operation)
        => operation.TryGetProperty("security", out var security)
            ? [.. security.EnumerateArray().SelectMany(r => r.EnumerateObject().Select(p => p.Name))]
            : [];
}
