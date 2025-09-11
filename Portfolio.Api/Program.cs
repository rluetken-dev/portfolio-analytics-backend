using Polly;
using Polly.Extensions.Http;
using Portfolio.Api.Services;
using Portfolio.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Swashbuckle.AspNetCore.Annotations;


var builder = WebApplication.CreateBuilder(args);

// --- existing DbContext registration ---
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHttpClient<AlphaVantageClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10); // keep calls bounded
});

// --- rest stays the same ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Enable [SwaggerOperation]/[SwaggerResponse] attributes
    c.EnableAnnotations();

    // Include XML comments from this assembly
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);
});

// Register maintenance utilities for admin endpoints (DI).
builder.Services.AddScoped<Portfolio.Api.Services.MaintenanceService>();

// Use the root base address so we can call /stable/... endpoints cleanly.
// IMPORTANT: Leave the trailing slash to avoid bad relative-URL joins.
builder.Services.AddHttpClient<Portfolio.Api.Services.FmpClient>(client =>
{
    client.BaseAddress = new Uri("https://financialmodelingprep.com/");
});

// Registers the ingest service used to upsert income statements into the DB.
// English: Scoped lifetime is fine (1 per request).
builder.Services.AddScoped<IncomeIngestService>();

// Registers the balance-sheet ingest service (scoped = one per request).
builder.Services.AddScoped<BalanceSheetIngestService>();

// Registers the cash-flow ingest service (scoped = one per request).
builder.Services.AddScoped<CashFlowIngestService>();

// Register demo-data seeding helpers (kept separate so controllers stay thin and testable).
builder.Services.AddScoped<ISeedService, SeedService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        // Preferred: applies EF Core migrations to create/update scheme
        db.Database.Migrate();
    }
    catch
    {
        // Fallback: quick-and-dirty if you don't have migrations yet
        db.Database.EnsureCreated();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
