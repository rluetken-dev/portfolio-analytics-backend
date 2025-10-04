using System.Net;
using System.Text.Json;
using Portfolio.Api.Exceptions;

namespace Portfolio.Api.Middleware
{
    /// <summary>
    /// Global error handler middleware.
    /// Catches all unhandled exceptions and returns a standardized JSON response.
    /// </summary>
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorHandlingMiddleware> _logger;

        public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (AppException ex)
            {
                _logger.LogWarning("Handled exception: {Message}", ex.Message);
                await WriteProblemDetailsAsync(context, ex.StatusCode, ex.GetType().Name, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception occurred: {Message}", ex.Message);
                await WriteProblemDetailsAsync(
                    context,
                    (int)HttpStatusCode.InternalServerError,
                    "InternalServerError",
                    "An unexpected error occurred. Please try again later."
                );
            }
        }

        private static async Task WriteProblemDetailsAsync(HttpContext context, int status, string title, string detail)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = status;

            var problem = new
            {
                type = $"https://httpstatuses.com/{status}",
                title,
                status,
                detail,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
    }
}
