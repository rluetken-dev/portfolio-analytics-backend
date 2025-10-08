using Polly;
using Polly.Extensions.Http;
using Portfolio.Api.Services;
using Portfolio.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Swashbuckle.AspNetCore.Annotations;
using Portfolio.Api.Seed;
using Newtonsoft.Json;
using Portfolio.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Portfolio.Api.Middleware;


var builder = WebApplication.CreateBuilder(args);

// // 🧠 Enable detailed model binding + validation logs
// builder.Logging.AddFilter("Microsoft.AspNetCore.Mvc.Infrastructure", LogLevel.Debug);

// --- Rate Limit & Retry Policies ---

// AlphaVantage Policies
var alphaVantageRateLimit = Policy.RateLimitAsync<HttpResponseMessage>(
    4, // max 5 calls
    TimeSpan.FromMinutes(1),
    4
);

var alphaVantageRetry = HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
    .WaitAndRetryAsync(
        5,
        attempt => TimeSpan.FromSeconds(5 * attempt)
    );

// FMP Policies
var fmpRateLimit = Policy.RateLimitAsync<HttpResponseMessage>(
    5,
    TimeSpan.FromMinutes(1),
    5
);

var fmpRetry = HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
    .WaitAndRetryAsync(
        3,
        attempt => TimeSpan.FromSeconds(5 * attempt)
    );

// --- Fallback read JSON ---
var fallbackPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "companies-fallback.json");
var fallbackJson = File.ReadAllText(fallbackPath);
var fallbackData = JsonConvert.DeserializeObject<FallbackData>(fallbackJson) ?? new FallbackData();

builder.Services.AddSingleton(fallbackData);
builder.Services.AddSingleton(fallbackPath);

// --- existing DbContext registration ---
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// --- HttpClients with Policies ---

// AlphaVantage client
builder.Services.AddHttpClient<AlphaVantageClient>(client =>
{
    client.BaseAddress = new Uri("https://www.alphavantage.co/");
    client.Timeout = TimeSpan.FromSeconds(10); // keep calls bounded
})
.AddPolicyHandler(alphaVantageRateLimit)
.AddPolicyHandler(alphaVantageRetry);

// FMP client
builder.Services.AddHttpClient<FmpClient>(client =>
{
    client.BaseAddress = new Uri("https://financialmodelingprep.com/");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Portfolio.Api (+https://github.com/rluetken-dev)");
})
.AddPolicyHandler(fmpRateLimit)
.AddPolicyHandler(fmpRetry);

// Swagger / Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;        
    });

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

    // --- JWT Auth config for Swagger ---
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // --- Grouping logic (fixed & null-safe) ---
    c.TagActionsBy(api =>
    {
        // 1️⃣ Prefer explicit [SwaggerOperation(Tags = ...)] attribute
        var op = api.ActionDescriptor.EndpointMetadata
            .OfType<SwaggerOperationAttribute>()
            .FirstOrDefault();

        if (op?.Tags is { Length: > 0 })
            return op.Tags;

        // 2️⃣ Fallback: group all Admin endpoints together
        var controller = api.GroupName ?? api.ActionDescriptor.RouteValues["controller"];
        if (!string.IsNullOrEmpty(controller) &&
            controller.Contains("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "Admin Management" };
        }

        // 3️⃣ Default group
        return new[] { controller ?? "Misc" };
    });

    // Include all APIs unless explicitly hidden
    c.DocInclusionPredicate((name, api) => true);
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

// --- JWT Authentication ---
var secretKey = "my_ultra_secure_secret_key_1234567890!@#$"; // gleiche wie in JwtService
var key = Encoding.UTF8.GetBytes(secretKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireClaim("isAdmin", "true"));
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

app.UseAuthentication();

// --- Global error handling middleware ---
app.UseMiddleware<Portfolio.Api.Middleware.ErrorHandlingMiddleware>();

app.UseAuthorization();


// ----- NEW: Minimal health endpoint for quick checks -----
// Returns: { "status": "ok" }
app.MapGet("/health", () => Results.Json(new { status = "ok" }));
// ---------------------------------------------------------

app.MapControllers();

app.Run();
