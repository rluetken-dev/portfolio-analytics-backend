using Portfolio.Api.Exceptions;

namespace Portfolio.Api.Utils
{
    /// <summary>
    /// Static guard helper for clean and expressive validation and error throwing.
    /// </summary>
    public static class Guard
    {
        /// <summary>
        /// Throws NotFoundException if the given object is null.
        /// </summary>
        public static void NotFoundIfNull(object? value, string message)
        {
            if (value == null)
                throw new NotFoundException(message);
        }

        /// <summary>
        /// Throws ForbiddenException if condition is true.
        /// </summary>
        public static void ForbidIf(bool condition, string message)
        {
            if (condition)
                throw new ForbiddenException(message);
        }

        /// <summary>
        /// Throws UnauthorizedException if condition is true.
        /// </summary>
        public static void UnauthorizedIf(bool condition, string message)
        {
            if (condition)
                throw new UnauthorizedException(message);
        }

        /// <summary>
        /// Throws AppException (400 Bad Request) if condition is true.
        /// </summary>
        public static void BadRequestIf(bool condition, string message)
        {
            if (condition)
                throw new BadRequestException(message);
        }
    }
}
