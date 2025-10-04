using System.Net;

namespace Portfolio.Api.Exceptions
{
    /// <summary>
    /// Exception for missing entities (returns HTTP 404).
    /// </summary>
    public class NotFoundException : AppException
    {
        public NotFoundException(string message)
            : base(message, (int)HttpStatusCode.NotFound)
        {
        }
    }
}
