using System.Net;

namespace Portfolio.Api.Exceptions;

/// <summary>
/// Represents an HTTP 503 Service Unavailable error.
/// </summary>
public sealed class ServiceUnavailableException : AppException
{
    public ServiceUnavailableException(string message)
        : base(message, (int)HttpStatusCode.ServiceUnavailable)
    {
    }
}