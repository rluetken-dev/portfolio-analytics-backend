using System.Net;

namespace Portfolio.Api.Exceptions
{
    /// <summary>
    /// Exception for forbidden actions (returns HTTP 403).
    /// </summary>
    public class ForbiddenException : AppException
    {
        public ForbiddenException(string message)
            : base(message, (int)HttpStatusCode.Forbidden)
        {
        }
    }
}
