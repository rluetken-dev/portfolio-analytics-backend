namespace Portfolio.Api.Exceptions;

/// <summary>
/// Base type for application exceptions that map to HTTP error responses.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }

    public virtual string Title => GetType().Name.Replace("Exception", string.Empty);
}