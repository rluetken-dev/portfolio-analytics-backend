using System.Net;

namespace Portfolio.Api.Exceptions;

/// <summary>
/// Represents an HTTP 403 Forbidden error.
/// </summary>
public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message)
        : base(message, (int)HttpStatusCode.Forbidden)
    {
    }
}