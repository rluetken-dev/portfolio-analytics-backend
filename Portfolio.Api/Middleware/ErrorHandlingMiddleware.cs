using System.Net;
using Portfolio.Api.Exceptions;

namespace Portfolio.Api.Middleware;

public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            await HandleAppExceptionAsync(context, ex);
        }
        catch (Exception ex)
        {
            await HandleUnexpectedExceptionAsync(context, ex);
        }
    }

    private async Task HandleAppExceptionAsync(HttpContext context, AppException ex)
    {
        if (ex.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(ex, "Application exception occurred.");
        }
        else
        {
            _logger.LogWarning("Application exception occurred: {Message}", ex.Message);
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = ex.StatusCode;

        var problem = new
        {
            type = $"https://httpstatuses.com/{ex.StatusCode}",
            title = ex.Title,
            status = ex.StatusCode,
            detail = ex.Message,
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsJsonAsync(problem);
    }

    private async Task HandleUnexpectedExceptionAsync(HttpContext context, Exception ex)
    {
        _logger.LogError(ex, "Unexpected server error occurred.");

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problem = new
        {
            type = $"https://httpstatuses.com/{(int)HttpStatusCode.InternalServerError}",
            title = "Internal Server Error",
            status = StatusCodes.Status500InternalServerError,
            detail = "An unexpected error occurred.",
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsJsonAsync(problem);
    }
}