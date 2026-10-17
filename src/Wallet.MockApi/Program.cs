using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wallet.MockApi;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Seed;
using Wallet.MockApi.Endpoints;
using Wallet.MockApi.Services;

// "seed-hash [directory]": print the hash of the seed files and exit. Never touches the database.
if (args is ["seed-hash", ..])
{
    var directory = args.Length > 1 ? args[1] : SeedFiles.DefaultDirectory;
    Console.WriteLine(SeedHash.Compute(directory));
    return 0;
}

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(WalletConnection.ConnectionStringName);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(WalletConnection.MissingConnectionStringMessage);
    return 1;
}

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<WalletDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<WalletOptions>()
    .BindConfiguration(WalletOptions.SectionName)
    // An empty admin or client key means none, as an absent one does; post-configuration runs before validation.
    .PostConfigure(o =>
    {
        o.AdminKey = string.IsNullOrEmpty(o.AdminKey) ? null : o.AdminKey;
        o.ClientKey = string.IsNullOrEmpty(o.ClientKey) ? null : o.ClientKey;
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();
// The seed files, read and validated once; a missing or invalid file fails the start (see below).
builder.Services.AddSingleton(sp => SeedSet.Load(sp.GetRequiredService<IOptions<WalletOptions>>().Value.SeedDirectory));
builder.Services.AddSingleton<FeeCalculator>();
builder.Services.AddScoped<PaymentValidator>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<ExternalDebitService>();
builder.Services.AddSingleton<FaultSwitch>();
builder.Services.AddSingleton<WalletGate>();

var app = builder.Build();

// "migrate": apply migrations, add the synthetic seed if the database is empty, report and exit.
if (args is ["migrate", ..])
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var seed = scope.ServiceProvider.GetRequiredService<SeedSet>();
        db.Database.Migrate();
        SeedData.Apply(db, seed, TimeProvider.System.GetUtcNow());
        Console.WriteLine(
            $"Database ready: {db.Users.Count()} users, {db.Accounts.Count()} accounts, "
            + $"{db.Contacts.Count()} contacts, {db.Transactions.Count()} transactions");
        return 0;
    }
    catch (SqlException ex)
    {
        Console.Error.WriteLine($"Cannot reach SQL Server using connection string '{WalletConnection.ConnectionStringName}': {ex.Message} See README, section Database.");
        return 1;
    }
    catch (SeedException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

// Read the seed files now, so a missing or invalid file stops the start with its message, not the first reset.
app.Services.GetRequiredService<SeedSet>();

// Configure the HTTP request pipeline.
// Errors become RFC 7807 problem details. The API serves plain HTTP on loopback, so there is no HTTPS redirection.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Only /health is open; every other route needs the client key (ClientKeyFilter), and /admin the admin key as well.
app.MapGet("/health", () => "ok");
var api = app.MapGroup(string.Empty)
    .AddEndpointFilter<ClientKeyFilter>()
    .ProducesProblem(StatusCodes.Status403Forbidden);
api.MapReadEndpoints();
api.MapAliasEndpoints();
api.MapTransactionEndpoints();
api.MapAdminEndpoints();

app.Run();
return 0;

public partial class Program { }
