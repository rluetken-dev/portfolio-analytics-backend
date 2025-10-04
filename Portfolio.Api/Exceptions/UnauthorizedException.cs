using System.Net;

namespace Portfolio.Api.Exceptions
{
    /// <summary>
    /// Exception for unauthorized access (returns HTTP 401).
    /// </summary>
    public class UnauthorizedException : AppException
    {
        public UnauthorizedException(string message)
            : base(message, (int)HttpStatusCode.Unauthorized)
        {
        }
    }
}
