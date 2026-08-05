using System.Net;

namespace Portfolio.Api.Exceptions;

/// <summary>
/// Represents an HTTP 404 Not Found error.
/// </summary>
public sealed class NotFoundException : AppException
{
    public NotFoundException(string message)
        : base(message, (int)HttpStatusCode.NotFound)
    {
    }
}