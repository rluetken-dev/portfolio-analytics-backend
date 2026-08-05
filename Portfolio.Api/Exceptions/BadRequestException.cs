using System.Net;

namespace Portfolio.Api.Exceptions;

/// <summary>
/// Represents an HTTP 400 Bad Request error.
/// </summary>
public sealed class BadRequestException : AppException
{
    public BadRequestException(string message)
        : base(message, (int)HttpStatusCode.BadRequest)
    {
    }
}