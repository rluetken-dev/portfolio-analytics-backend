using System.Net;

namespace Portfolio.Api.Exceptions
{
    /// <summary>
    /// Exception for invalid client requests (returns HTTP 400).
    /// </summary>
    public class BadRequestException : AppException
    {
        public BadRequestException(string message)
            : base(message, (int)HttpStatusCode.BadRequest)
        {
        }
    }
}
