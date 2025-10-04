using System;
using System.Net;

namespace Portfolio.Api.Exceptions
{
    /// <summary>
    /// Base class for all custom application exceptions.
    /// Includes an HTTP status code and a clean message for client responses.
    /// </summary>
    public abstract class AppException : Exception
    {
        /// <summary>
        /// The HTTP status code associated with this exception.
        /// </summary>
        public int StatusCode { get; }

        protected AppException(string message, int statusCode = (int)HttpStatusCode.BadRequest)
            : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
