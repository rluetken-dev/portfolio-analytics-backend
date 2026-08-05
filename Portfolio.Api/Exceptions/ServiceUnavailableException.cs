using System.Net;

namespace Portfolio.Api.Exceptions;

public sealed class ServiceUnavailableException : AppException
{
    public ServiceUnavailableException(string message)
        : base(message, (int)HttpStatusCode.ServiceUnavailable)
    {
    }
}