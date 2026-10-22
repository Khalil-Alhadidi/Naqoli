# Scalar API reference for the mock wallet (Khalil, 22 Oct 2026)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (one implementer, Opus; the controller reviews each task's diff) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Decision (Khalil, 22 Oct 2026):** the mock wallet gets a Scalar page over the OpenAPI document it already serves, Development only. His words: "it will look nicer than nothing and at the end of the day it is a mockup". The architecture does not name a docs UI; this is tooling, not a feature, and changes no wallet behaviour, no route under the client key, no test of the contract.

**Goal:** a judge who opens http://localhost:5276/scalar on the running wallet sees every route, its fields, its error codes, and can try a call with the two key headers filled in.

**Architecture:** `Scalar.AspNetCore` is mapped in the same `IsDevelopment` block that maps `/openapi/v1.json`, outside the client-key group, as that document is. Two OpenAPI security schemes (`ClientKey` and `AdminKey`, both `apiKey` in a header) are added to the document by transformers in one new file, so the document states the contract and Scalar can offer the header fields; the page pre-fills both with `dev`, the value the README's reset example uses, which in Development with no key configured is accepted like any non-empty value.

**Tech Stack:** .NET 10, `Microsoft.AspNetCore.OpenApi` 10.0.13 (already present), `Scalar.AspNetCore` 2.18.2 (new; published 20 Oct 2026; if it fails to build or to serve the page on .NET 10, pin 2.17.14 instead and say so).

**Spec:** this file; `docs/wallet-api.md` (sections "Conventions" and "Client key", which already describe `/openapi/v1.json`); `CLAUDE.md` rules 8 and 9 and the disclosure habit.

## Global Constraints

- The page and the document are served only when `app.Environment.IsDevelopment()`; in any other environment both routes are 404.
- No new route under the client-key group. `ClientKeyTests.NoHeaderIs403OnEveryRouteExceptHealth` stays as it is.
- No Arabic in C#. No real names, numbers or keys. The pre-filled value `dev` is the README's documented Development placeholder, not a secret.
- `docs/disclosure.md` gets the package line in the same commit as the package reference.
- Build with 0 warnings. `dotnet test tests/Agent.Core.Tests` green (the database collections skip when SQL Server is absent, as today).
- Commits in Khalil's name only, no Co-Authored-By line (memory: commits-no-claude-attribution).

## Review Focus

Inputs the spec implies that a person will meet, each pinned to a test in Task 1 or Task 2:

1. Opening `/scalar` outside Development must be a plain 404, not a page and not a 500 (Task 2, `OutsideDevelopmentNeitherRouteExists`).
2. The document must say which routes need which header, so "try it" on `/admin/reset` asks for both keys and `/health` for none (Task 1, `TheDocumentSaysWhichRoutesNeedWhichKey`).
3. The document must still generate on a host with no database (the page is the first thing a judge opens, maybe before `migrate`); the Task 1 tests run on a factory with no SQL Server, as `HealthTests` does.
4. A page whose "try it" sends the pre-filled `dev` must work against `dotnet run` with no key configured; a configured key (user secrets) makes `dev` wrong and the person types the real one. Documented in Task 3, not tested (it is the existing Development rule).
5. The `.http` file stays the route for Visual Studio users; the page does not replace it. Documented in Task 3.

---

### Task 1: The document declares the two key headers

**Files:**
- Create: `src/Wallet.MockApi/OpenApiSecurity.cs`
- Modify: `src/Wallet.MockApi/Program.cs:29` (`builder.Services.AddOpenApi();`)
- Test: `tests/Agent.Core.Tests/Wallet/ApiReferenceTests.cs` (new; no collection, no database, like `HealthTests`)

**Interfaces:**
- Consumes: `ClientKeyFilter.HeaderName` (`X-Wallet-Client-Key`), `AdminKeyFilter.HeaderName` (`X-Wallet-Admin-Key`).
- Produces: `OpenApiSecurity.ClientKeySchemeName = "ClientKey"`, `OpenApiSecurity.AdminKeySchemeName = "AdminKey"`, and `public static OpenApiOptions AddWalletSecurity(this OpenApiOptions options)` in `Wallet.MockApi` namespace. Task 2 passes the two scheme names to Scalar.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Text.Json;
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

    private static async Task<JsonDocument> ReadDocumentAsync(HttpClient client)
    {
        var response = await client.GetAsync("/openapi/v1.json", Token);
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
```

If the generated path key for `/users/{id:int}` is not `/users/{id}`, read it from the document in the first failing run and correct the test (the OpenAPI generator strips route constraints; the key should be `/users/{id}`).

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Wallet.ApiReferenceTests"`
Expected: build error, `OpenApiSecurity` does not exist.

- [ ] **Step 3: Create `src/Wallet.MockApi/OpenApiSecurity.cs`**

`public static class OpenApiSecurity` with the two constants and `AddWalletSecurity(this OpenApiOptions options)`, which registers:

- a document transformer that adds the two schemes to `document.Components.SecuritySchemes`: each `OpenApiSecurityScheme` with `Type = SecuritySchemeType.ApiKey`, `In = ParameterLocation.Header`, `Name` the filter's `HeaderName` constant, and a one-line `Description` in English saying what the header is (`ClientKey`: "Every route except /health. In Development with no Wallet:ClientKey configured, any non-empty value is accepted."; `AdminKey`: "The /admin routes, with the client key. In Development with no Wallet:AdminKey configured, any non-empty value is accepted.");
- an operation transformer that sets `operation.Security` from the route: `/health` gets an empty list; a path starting with `/admin` gets one requirement naming `ClientKey` and `AdminKey` in that order; every other path gets one requirement naming `ClientKey`. Read the path from `context.Description.RelativePath`. A requirement references a scheme by name (`OpenApiSecuritySchemeReference` in Microsoft.OpenApi 2.x, the version `Microsoft.AspNetCore.OpenApi` 10.0.13 brings; check the type name in the restored package rather than from memory).

In `Program.cs`, `builder.Services.AddOpenApi(o => o.AddWalletSecurity());` replaces the bare call. Keep the template's comment line.

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Wallet.ApiReferenceTests"`
Expected: 2 passed. Then `dotnet build` with 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Wallet.MockApi/OpenApiSecurity.cs src/Wallet.MockApi/Program.cs tests/Agent.Core.Tests/Wallet/ApiReferenceTests.cs
git commit -m "feat(wallet): the OpenAPI document declares the two key headers as security schemes (ClientKey on every route but /health, AdminKey as well under /admin), OpenApiSecurity.AddWalletSecurity; tests ApiReferenceTests"
```

---

### Task 2: The Scalar page, Development only

**Files:**
- Modify: `src/Wallet.MockApi/Wallet.MockApi.csproj` (add `<PackageReference Include="Scalar.AspNetCore" Version="2.18.2" />`)
- Modify: `src/Wallet.MockApi/Program.cs:90-93` (the `IsDevelopment` block)
- Modify: `docs/disclosure.md:19` (one line after the OpenAPI package line)
- Test: `tests/Agent.Core.Tests/Wallet/ApiReferenceTests.cs`

**Interfaces:**
- Consumes: `OpenApiSecurity.ClientKeySchemeName`, `OpenApiSecurity.AdminKeySchemeName` (Task 1).
- Produces: route `GET /scalar` (Scalar's default), 200 `text/html` in Development.

- [ ] **Step 1: Add the failing tests to `ApiReferenceTests`**

```csharp
    [Fact]
    public async Task TheReferencePageIsServedInDevelopment()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/scalar", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
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

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/scalar", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json", Token)).StatusCode);
    }
```

The factory's client follows redirects, so the first test holds whether Scalar answers `/scalar` directly or redirects to `/scalar/v1`. Add `using Microsoft.AspNetCore.Hosting;` for `UseEnvironment`.

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Wallet.ApiReferenceTests"`
Expected: `TheReferencePageIsServedInDevelopment` fails with 404; `OutsideDevelopmentNeitherRouteExists` passes already (both routes are absent) and stays as the guard.

- [ ] **Step 3: Add the package and map the page**

In the csproj, the Scalar reference next to the OpenAPI one. In `Program.cs`, inside the existing `if (app.Environment.IsDevelopment())`, after `app.MapOpenApi();`:

```csharp
    // The reference page over the document, at /scalar. Outside the client-key group, like the document. The two
    // header fields are pre-filled with the README's Development placeholder; with a key configured, type the real one.
    app.MapScalarApiReference(options => options
        .WithTitle("ZainCash mock wallet")
        .AddPreferredSecuritySchemes(OpenApiSecurity.ClientKeySchemeName, OpenApiSecurity.AdminKeySchemeName)
        .AddApiKeyAuthentication(OpenApiSecurity.ClientKeySchemeName, key => key.Value = "dev")
        .AddApiKeyAuthentication(OpenApiSecurity.AdminKeySchemeName, key => key.Value = "dev"));
```

`using Scalar.AspNetCore;` at the top. If `AddPreferredSecuritySchemes` takes one name in this version, pass `ClientKeySchemeName` only.

- [ ] **Step 4: Run the tests and the build**

Run: `dotnet test tests/Agent.Core.Tests -- --filter-class "Agent.Core.Tests.Wallet.ApiReferenceTests"`
Expected: 4 passed. `dotnet build`: 0 warnings (a NuGet audit warning on the new package would be one; if it appears, report it and stop).

- [ ] **Step 5: Look at the page once**

Run the wallet (`dotnet run --project src/Wallet.MockApi`), open http://localhost:5276/scalar, check: the title, the routes grouped, `POST /admin/reset` shows both key fields pre-filled, `GET /health` shows none, "try it" on `GET /health` returns `ok`. Do not run a payment from the page. Stop the wallet. Report what you saw in the summary; no screenshot is committed.

- [ ] **Step 6: Disclosure line and commit**

In `docs/disclosure.md`, after the `Microsoft.AspNetCore.OpenApi` line:

`- Scalar.AspNetCore 2.18.2 (the API reference page over the mock wallet's OpenAPI document at /scalar, Development only; Khalil, 22 Oct 2026).`

```bash
git add src/Wallet.MockApi/Wallet.MockApi.csproj src/Wallet.MockApi/Program.cs docs/disclosure.md tests/Agent.Core.Tests/Wallet/ApiReferenceTests.cs
git commit -m "feat(wallet): Scalar API reference at /scalar in Development, over the OpenAPI document, with the two key headers pre-filled with the README's placeholder (Khalil, 22 Oct 2026); Scalar.AspNetCore 2.18.2 disclosed; tests TheReferencePageIsServedInDevelopment and OutsideDevelopmentNeitherRouteExists"
```

---

### Task 3: Documentation

**Files:**
- Modify: `docs/wallet-api.md:16, 26, 54, 453, 628` (every mention of `/openapi/v1.json` in Development)
- Modify: `README.md:126`
- Modify: `CLAUDE.md` (the "Run API" command line)

**Interfaces:** none; text only.

- [ ] **Step 1: wallet-api.md**

Line 16: after the sentence on the OpenAPI document, add: "The reference page over it is at `/scalar` (Scalar; Khalil, 22 Oct 2026), mapped in the same Development block and outside the client-key group. The document declares the two headers as security schemes, `ClientKey` on every route but `/health` and `AdminKey` as well under `/admin`, so the page offers both fields; it pre-fills them with `dev`, which the Development rule accepts, and with a key configured the person types the real one. The `.http` file remains the route for Visual Studio and VS Code. Tests: `ApiReferenceTests` (the schemes, the routes' requirements, the page in Development, both routes 404 outside it)."

Lines 26, 54, 453, 628: change "(and `/openapi/v1.json` in Development)" to "(and `/openapi/v1.json` and `/scalar` in Development)".

- [ ] **Step 2: README.md and CLAUDE.md**

README line 126: "..., the OpenAPI document is at `/openapi/v1.json` and its reference page at `/scalar`, both in Development." README line 188's library list: add `Scalar.AspNetCore 2.18.2` after the OpenApi entry.

CLAUDE.md "Run API" line: `(http://localhost:5276, /health; in Development the OpenAPI document at /openapi/v1.json and the Scalar reference page at /scalar)`.

- [ ] **Step 3: Check and commit**

Run: `grep -rn "openapi/v1.json" docs README.md CLAUDE.md` and confirm every Development mention names `/scalar` too.

```bash
git add docs/wallet-api.md README.md CLAUDE.md
git commit -m "docs(wallet): the Scalar reference page at /scalar documented beside the OpenAPI document in wallet-api.md, the README and CLAUDE.md"
```

---

### Task 4: Close

- [ ] **Step 1: Full test run**

Run: `dotnet build` then `dotnet test tests/Agent.Core.Tests`.
Expected: 0 warnings; the count before this work (run once on c1660b9 to know it) plus 4, all passed but the usual skips. Record the numbers in the summary.

- [ ] **Step 2: Security review**

Run the `dotnet-security-reviewer` agent on the three commits (`git diff c1660b9..HEAD`). Points for it: the page and the document are absent outside Development; nothing under the client-key group changed; the pre-filled `dev` is the documented placeholder and no secret is in the repository; the new package's licence (MIT) and its transitive dependencies. Fix what it finds in the usual `fix(wallet)` commit, or report why not.

- [ ] **Step 3: Summary for Khalil**

State: the page's URL; what was seen in Task 2 step 5; the test counts; which Scalar version was pinned and why if it is not 2.18.2; that `docs/failure-modes.md` is unchanged (no new way to fail: the page is a viewer) unless the review found one.
