using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wallet.MockApi;
using Wallet.MockApi.Data;
using Wallet.MockApi.Endpoints;
using Wallet.MockApi.Services;

// "seed-hash": print the hash of the frozen seed and exit. Never touches the database.
if (args is ["seed-hash", ..])
{
    Console.WriteLine(SeedData.ComputeHash());
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
    // An empty admin key means none, as an absent one does; post-configuration runs before validation.
    .PostConfigure(o => o.AdminKey = string.IsNullOrEmpty(o.AdminKey) ? null : o.AdminKey)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<FeeCalculator>();
builder.Services.AddScoped<PaymentValidator>();
builder.Services.AddScoped<TransactionService>();
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
        db.Database.Migrate();
        SeedData.Apply(db);
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
}

// Configure the HTTP request pipeline.
// Errors become RFC 7807 problem details. The API serves plain HTTP on loopback, so there is no HTTPS redirection.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => "ok");
app.MapReadEndpoints();
app.MapTransactionEndpoints();
app.MapAdminEndpoints();

app.Run();
return 0;

public partial class Program { }
