using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Data;

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
builder.Services.AddDbContext<WalletDbContext>(o => o.UseSqlServer(connectionString));

var app = builder.Build();

// "migrate": apply migrations, add the synthetic seed if the database is empty, report and exit.
if (args is ["migrate", ..])
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
    db.Database.Migrate();
    SeedData.Apply(db);
    Console.WriteLine($"Database ready: {db.Users.Count()} users, {db.Accounts.Count()} accounts");
    return 0;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => "ok");

app.Run();
return 0;

public partial class Program { }
