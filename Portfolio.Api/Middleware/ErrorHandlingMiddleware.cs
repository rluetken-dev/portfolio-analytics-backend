using System.Net;
using System.Text.Json;
using Portfolio.Api.Exceptions;

namespace Portfolio.Api.Middleware
{
    public class ErrorHandlingMiddleware
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
            // Business errors (400–499) are not “real” exceptions
            if (ex.StatusCode >= 500)
                _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            else
                _logger.LogWarning("Handled exception: {Message}", ex.Message);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = ex.StatusCode;

            var problem = new
            {
                type = $"https://httpstatuses.com/{ex.StatusCode}",
                title = ex.GetType().Name.Replace("Exception", ""),
                status = ex.StatusCode,
                detail = ex.Message,
                message = ex.Message, 
                traceId = context.TraceIdentifier
            };
            _logger.LogWarning("🚨 Writing error response: {Json}", JsonSerializer.Serialize(problem));

            await context.Response.WriteAsJsonAsync(problem);

        }

        private async Task HandleUnexpectedExceptionAsync(HttpContext context, Exception ex)
        {
            _logger.LogError(ex, "Unexpected server error: {Message}", ex.Message);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var problem = new
            {
                type = "https://httpstatuses.com/500",
                title = "Internal Server Error",
                status = 500,
                detail = "An unexpected error occurred. Please contact support.",
                message = "An unexpected error occurred. Please contact support.",
                traceId = context.TraceIdentifier
            };
            _logger.LogWarning("🚨 Writing error response: {Json}", JsonSerializer.Serialize(problem));

            await context.Response.WriteAsJsonAsync(problem); 
        }
    }
}
