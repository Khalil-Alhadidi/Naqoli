# Security review: milestone 1 (skeleton), HEAD 5023da2

Produced by the dotnet-security-reviewer agent on 14 Oct 2026; saved by the controller because the agent had no write tool. Verdict: conditional pass. Critical 0, Important 2, Minor 7, Recommendations 6.

## Approved
- No credentials anywhere (code, config, launch settings, migration, docs). README uses placeholders. UserSecretsId is not a secret.
- The committed default connection string (Windows auth, no credential) is acceptable for development per https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets and https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-strings. Rule 9 wording ("connection strings go in user secrets or environment variables") should be confirmed by Khalil as meaning strings that carry credentials, or the default moved (M1).
- OpenAPI only in Development (Program.cs:32-35).
- Agent.Web uses the standard secure pipeline: exception handler + HSTS outside Development, UseAntiforgery, MapStaticAssets, no DetailedErrors; Error page static, shows only a request ID.
- Logging: Microsoft.EntityFrameworkCore at Warning; no EnableSensitiveDataLogging.
- EF Core: no raw SQL; migration generated, reversible, schema only; unique Phone index guards the seed's Any() check.
- migrate fails cleanly without a connection string.
- Supply chain: NuGet audit active (auditMode all, auditLevel low), no audit log entries; Pkcs 10.0.13 pin resolves; direct reference is Microsoft's documented remedy (https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages); dotnet-ef pinned with rollForward false; every package disclosed.
- Async: HealthTests uses await using and TestContext.Current.CancellationToken; Assert.SkipUnless gates the SQL test.
- .gitignore excludes .env, *.pfx, *.publishsettings, *.pubxml, *.user, .superpowers/, SQL data files.

## Important
**I1. Seed phone numbers sit in a live Iraqi mobile range (rule 8).** SeedData.cs:11-13, SeedDataTests.cs:30 (regex ^0770000\d{4}$). 07700001001..1003 are well-formed numbers under 0770, an in-service operator prefix, and may belong to real subscribers. The seed freezes in milestone 2, so this is the last cheap moment. Fix: Khalil picks a documented synthetic pattern that the milestone 6 masker still treats as a phone number, records it in data/seed/README.md and the regex test is updated; or Khalil explicitly accepts this range and records the decision.

**I2. The mock wallet API accepts any Host header and has no caller check (rule 1, forward-looking).** appsettings.json:12 ("AllowedHosts": "*"), Program.cs (no authentication). DNS rebinding from a malicious page could reach a localhost service that does not filter hosts; once POST /transactions and /admin/* exist, anything reaching the port could move mock money without the Confirm tap. Fix: "AllowedHosts": "localhost" now (https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/host-filtering); before milestone 2, a shared-secret header on POST /transactions and /admin/*, kept in user secrets for both apps; map /admin/* only in Development; never a permissive CORS policy.

## Minor
**M1.** Default connection string with TrustServerCertificate=True applies in every environment (appsettings.json:3). Move to appsettings.Development.json; update README. (https://learn.microsoft.com/en-us/sql/connect/ado-net/connection-string-syntax)
**M2.** Design-time factory does not match the app's environment or sources (WalletDbContextFactory.cs:8-9, 13-22): ignores args (--environment), reads only DOTNET_ENVIRONMENT (app prefers ASPNETCORE_ENVIRONMENT), adds user secrets in every environment, base path is the current directory; the doc comment claims "same sources, same order". Fix: parse --environment, then ASPNETCORE_ENVIRONMENT, then DOTNET_ENVIRONMENT, then Development; user secrets only in Development; AppContext.BaseDirectory; correct the comment. (https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation)
**M3.** API uses HTTPS redirection with an HTTP default profile (Program.cs:37, launchSettings.json:4-8). Microsoft: APIs should not rely on redirection (https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl). Fix: remove UseHttpsRedirection from the API; either make https the default profile when the wallet client arrives in milestone 3, or document loopback HTTP as a choice.
**M4.** No ProblemDetails or exception handler; migrate throws an unhandled SqlException when the server is unreachable; failure-modes row 14 overclaims ("Database missing" is handled only for a missing connection string). Fix: AddProblemDetails + UseExceptionHandler + UseStatusCodePages (https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api); catch SqlException in migrate, one line, exit 1; reword row 14 and add the unreachable-server row.
**M5.** Supply-chain hardening incomplete: TreatWarningsAsErrors false so new NU1902/NU1903/NU1904 only warn; OpenApi at 10.0.12 vs 10.0.13 elsewhere; no lock file, no central package management, no source mapping; global.json does not pin the SDK. Fix: WarningsAsErrors for NU1902;NU1903;NU1904; align OpenApi; optionally RestorePackagesWithLockFile, CPM with transitive pinning, nuget.config with source mapping; pin sdk version with rollForward latestFeature.
**M6.** Opt-in SQL test drops a database named in code without a guard and cleanup is not guaranteed (SeedApplyTests.cs:16-26). Fix: optional ConnectionStrings__WalletTest override with the current default; assert the name ends in _Test before dropping; try/finally or IAsyncLifetime. (https://learn.microsoft.com/en-us/ef/core/testing/testing-with-the-database)
**M7.** Agent.Web loads Roboto from Google Fonts (App.razor:9), a third-party request without SRI/CSP that also makes the demo depend on the network; Error.razor:17-26 tells users how to enable Development. Fix: self-host or drop the font; plan a CSP; trim the Development paragraph.

## Areas with no findings
Area 1 (secrets) and area 6 (async/API patterns). Areas 2, 3, 4, 5, 7 have the findings above.

## Recommendations for later milestones
R1 (rule 1): wallet client execute method internal to Agent.Core with exactly one caller; architecture test; Confirm handler loads the pending record by ID from server state and does Pending→Executing atomically (conditional UPDATE or concurrency token).
R2 (rule 6): unique index on the idempotency key in the transactions table.
R3 (rule 5): keep EF at Warning, never EnableSensitiveDataLogging; System.Net.Http.HttpClient at Warning in Agent.Web and Agent.Eval; never a PIN in a URL; no HttpLogging request bodies; Microsoft.Extensions.Compliance.Redaction for PIN, purchase code, balance.
R4 (rule 9): user-secrets init on Agent.Web and Agent.Eval when model keys arrive; options validation with ValidateOnStart.
R5: AddHealthChecks/MapHealthChecks; TypedResults; MigrateAsync; consider UseSeeding/UseAsyncSeeding so reset and migrate share one path.
R6: EF Design package brings Roslyn/MSBuild/Newtonsoft/Humanizer into the API output; move migrations out or strip if the API is ever hosted.

## Verdict
Conditional pass. Before tagging milestone 1: I1 fixed or explicitly decided by Khalil. Before milestone 2 endpoints: I2. M1 to M7 not blocking; M1, M2, M5 worth doing in the same pass.
