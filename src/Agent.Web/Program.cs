using Agent.Core;
using Agent.Core.Data;
using Agent.Web.Components;
using Agent.Web.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddAgentWebServices(builder.Configuration);

var app = builder.Build();

// "migrate": apply the agent database's migrations, report and exit.
if (args is ["migrate", ..])
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        db.Database.Migrate();
        Console.WriteLine("Agent database ready");
        return 0;
    }
    catch (SqlException ex)
    {
        Console.Error.WriteLine($"Cannot reach SQL Server using connection string '{AgentConnection.ConnectionStringName}': {ex.Message} See README, section Database.");
        return 1;
    }
    catch (InvalidOperationException ex)
    {
        // A missing connection string (AgentConnection.MissingConnectionStringMessage).
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
return 0;
