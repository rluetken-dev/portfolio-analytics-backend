using System.Net;

namespace Portfolio.Api.Exceptions;

/// <summary>
/// Represents an HTTP 401 Unauthorized error.
/// </summary>
public sealed class UnauthorizedException : AppException
{
    public UnauthorizedException(string message)
        : base(message, (int)HttpStatusCode.Unauthorized)
    {
    }
}