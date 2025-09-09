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

// === NEW: HttpClient with a simple transient error retry policy ===
static IAsyncPolicy<HttpResponseMessage> RetryPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()             // 5xx, 408, network errors
        .WaitAndRetryAsync(                     // simple backoff
            new[]
            {
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromSeconds(1)
            }
        );

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
