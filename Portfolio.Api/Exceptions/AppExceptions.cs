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

        public AppException(string message, int statusCode) : base(message)
        {
            StatusCode = statusCode;
        }

        public virtual string Title => GetType().Name.Replace("Exception", "");
    }
}
