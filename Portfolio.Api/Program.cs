using Polly;
using Polly.Extensions.Http;
using Portfolio.Api.Services;
using Portfolio.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Swashbuckle.AspNetCore.Annotations;
using Portfolio.Api.Seed;

var builder = WebApplication.CreateBuilder(args);

// --- existing DbContext registration ---
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// Register HttpClients
builder.Services.AddHttpClient<AlphaVantageClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10); // keep calls bounded
});

// Swagger / Controllers
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

// ----- NEW: CORS for Vite dev server (http://localhost:5173) -----
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowViteDev", policy =>
        policy
            .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")  // Vite dev server origin
            .AllowAnyMethod()                                               // GET/POST/PUT/DELETE...
            .AllowAnyHeader()                                               // Content-Type, etc.
            .AllowCredentials()                                             // keep if you might use cookies/auth later
    );
});
// ---------------------------------------------------------------

// FMP client
builder.Services.AddHttpClient<Portfolio.Api.Services.FmpClient>(client =>
{
    // IMPORTANT: Leave the trailing slash to avoid bad relative-URL joins.
    client.BaseAddress = new Uri("https://financialmodelingprep.com/");
});

// Domain services
builder.Services.AddScoped<IncomeIngestService>();
builder.Services.AddScoped<BalanceSheetIngestService>();
builder.Services.AddScoped<CashFlowIngestService>();
builder.Services.AddScoped<ISeedService, SeedService>();

builder.Services.AddScoped<MaintenanceService>();

// English: lightweight client pointing to this API itself (same dev host/port)
builder.Services.AddHttpClient("self", c =>
{
    c.BaseAddress = new Uri("http://localhost:5046");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

// English: register loader (stateless → singleton ok)
builder.Services.AddSingleton<ISeedFileService, SeedFileService>();

// English: ensure your DB seeder is available (if not already)
builder.Services.AddScoped<Portfolio.Api.Services.ISeedService, Portfolio.Api.Services.SeedService>();
var app = builder.Build();

// Ensure DB ready (migrate or create)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.Migrate();
    }
    catch
    {
        db.Database.EnsureCreated();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ----- NEW: enable CORS BEFORE mapping endpoints -----
app.UseCors("AllowViteDev");
// -----------------------------------------------------

// (Optional) If you force HTTPS in production, keep this. For local dev over http you can comment out.
// app.UseHttpsRedirection();

app.UseAuthorization();

// ----- NEW: Minimal health endpoint for quick checks -----
// Returns: { "status": "ok" }
app.MapGet("/health", () => Results.Json(new { status = "ok" }));
// ---------------------------------------------------------

app.MapControllers();

app.Run();
