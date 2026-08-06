using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Polly;
using Polly.Extensions.Http;
using Portfolio.Api.Data;
using Portfolio.Api.Middleware;
using Portfolio.Api.Models;
using Portfolio.Api.Seed;
using Portfolio.Api.Services;
using Swashbuckle.AspNetCore.Annotations;

var builder = WebApplication.CreateBuilder(args);

// External provider resilience policies
var alphaVantageRateLimit = Policy.RateLimitAsync<HttpResponseMessage>(
    4,
    TimeSpan.FromMinutes(1),
    4);

var alphaVantageRetry = HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(response => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
    .WaitAndRetryAsync(5, attempt => TimeSpan.FromSeconds(5 * attempt));

var fmpRateLimit = Policy.RateLimitAsync<HttpResponseMessage>(
    5,
    TimeSpan.FromMinutes(1),
    5);

var fmpRetry = HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(response => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(5 * attempt));

// Local fallback company data
string fallbackPath = Path.Combine(
    builder.Environment.ContentRootPath,
    "Data",
    "companies-fallback.json");

FallbackData fallbackData = File.Exists(fallbackPath)
    ? JsonConvert.DeserializeObject<FallbackData>(File.ReadAllText(fallbackPath)) ?? new FallbackData()
    : new FallbackData();

builder.Services.AddSingleton(fallbackData);

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// External provider HTTP clients
builder.Services.AddHttpClient<AlphaVantageClient>(client =>
{
    client.BaseAddress = new Uri("https://www.alphavantage.co/");
    client.Timeout = TimeSpan.FromSeconds(10);
})
.AddPolicyHandler(alphaVantageRateLimit)
.AddPolicyHandler(alphaVantageRetry);

builder.Services.AddHttpClient<FmpClient>(client =>
{
    client.BaseAddress = new Uri("https://financialmodelingprep.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Portfolio.Api (+https://github.com/rluetken-dev/portfolio-analytics-backend)");
})
.AddPolicyHandler(fmpRateLimit)
.AddPolicyHandler(fmpRetry);

// MVC and Swagger
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();

    string xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
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

    options.TagActionsBy(api =>
    {
        SwaggerOperationAttribute? operation = api.ActionDescriptor.EndpointMetadata
            .OfType<SwaggerOperationAttribute>()
            .FirstOrDefault();

        if (operation?.Tags is { Length: > 0 })
        {
            return operation.Tags;
        }

        string? controller = api.GroupName ?? api.ActionDescriptor.RouteValues["controller"];

        return [controller ?? "Misc"];
    });

    options.DocInclusionPredicate((_, _) => true);
});

// Local frontend development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowViteDev", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://localhost:5174",
                "http://127.0.0.1:5173",
                "http://127.0.0.1:5174")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Authentication
string secretKey = builder.Configuration["Jwt:Secret"] ?? string.Empty;

if (string.IsNullOrWhiteSpace(secretKey))
{
    throw new InvalidOperationException("JWT secret is not configured.");
}

byte[] signingKey = Encoding.UTF8.GetBytes(secretKey);

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
        IssuerSigningKey = new SymmetricSecurityKey(signingKey),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireClaim("isAdmin", "true"));
});

// Application services
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<IncomeIngestService>();
builder.Services.AddScoped<BalanceSheetIngestService>();
builder.Services.AddScoped<CashFlowIngestService>();
builder.Services.AddScoped<MaintenanceService>();
builder.Services.AddScoped<PortfolioAnalyticsService>();
builder.Services.AddScoped<ISeedService, SeedService>();
builder.Services.AddSingleton<ISeedFileService, SeedFileService>();

builder.Services.AddHttpClient("self", client =>
{
    client.BaseAddress = new Uri("http://localhost:5046");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

var app = builder.Build();

// Database startup
using (IServiceScope scope = app.Services.CreateScope())
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

    if (app.Configuration.GetValue<bool>("DemoMode"))
    {
        await SeedLocalDemoDataAsync(scope.ServiceProvider, app.Environment, app.Logger);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ErrorHandlingMiddleware>();

app.UseCors("AllowViteDev");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

app.MapControllers();

app.Run();

static async Task SeedLocalDemoDataAsync(
    IServiceProvider services,
    IWebHostEnvironment environment,
    ILogger logger)
{
    var seedFileService = services.GetRequiredService<ISeedFileService>();
    var seedService = services.GetRequiredService<ISeedService>();

    string companiesPath = Path.Combine(environment.ContentRootPath, "SeedData", "companies");

    if (!Directory.Exists(companiesPath))
    {
        logger.LogWarning("Demo seed skipped. Directory not found: {Path}", companiesPath);
        return;
    }

    string[] files = Directory.GetFiles(companiesPath, "*.json");

    foreach (string file in files)
    {
        string symbol = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
        var result = await seedFileService.LoadCompanyAsync(symbol);

        if (!result.Success || result.Data is null)
        {
            logger.LogWarning("Demo seed skipped for {Symbol}: {Error}", symbol, result.Error);
            continue;
        }

        var company = result.Data;

        await seedService.SeedTickerProfileAsync(
            company.Symbol,
            company.Profile.Name,
            company.Profile.Sector,
            CancellationToken.None);

        foreach (var quote in company.Quotes.Rows)
        {
            if (!DateOnly.TryParse(quote.Date, out var date))
            {
                continue;
            }

            await seedService.SeedFullPriceAsync(
                company.Symbol,
                date,
                quote.Open,
                quote.High,
                quote.Low,
                quote.Close,
                quote.Volume,
                CancellationToken.None);
        }

        foreach (var annual in company.Fundamentals?.Annual ?? [])
        {
            if (annual.NetIncome.HasValue && annual.Equity.HasValue)
            {
                await seedService.SeedAnnualAsync(
                    company.Symbol,
                    annual.Year,
                    annual.NetIncome.Value,
                    annual.Equity.Value,
                    CancellationToken.None);
            }

            if (annual.Revenue.HasValue)
            {
                await seedService.SeedRevenueAsync(
                    company.Symbol,
                    annual.Year,
                    annual.Revenue.Value,
                    CancellationToken.None);
            }

            if (annual.TotalAssets.HasValue)
            {
                await seedService.SeedAssetsAsync(
                    company.Symbol,
                    annual.Year,
                    annual.TotalAssets.Value,
                    CancellationToken.None);
            }

            if (annual.TotalLiabilities.HasValue)
            {
                await seedService.SeedLiabilitiesAsync(
                    company.Symbol,
                    annual.Year,
                    annual.TotalLiabilities.Value,
                    CancellationToken.None);
            }

            if (annual.Shares.HasValue)
            {
                await seedService.SeedSharesAsync(
                    company.Symbol,
                    annual.Year,
                    annual.Shares.Value,
                    CancellationToken.None);
            }

            if (annual.OperatingCashFlow.HasValue)
            {
                await seedService.SeedOperatingCashFlowAsync(
                    company.Symbol,
                    annual.Year,
                    annual.OperatingCashFlow.Value,
                    CancellationToken.None);
            }

            if (annual.CapitalExpenditures.HasValue)
            {
                await seedService.SeedCapitalExpendituresAsync(
                    company.Symbol,
                    annual.Year,
                    annual.CapitalExpenditures.Value,
                    CancellationToken.None);
            }
        }
    }

    logger.LogInformation("Local demo seed completed. Files processed: {Count}", files.Length);
}