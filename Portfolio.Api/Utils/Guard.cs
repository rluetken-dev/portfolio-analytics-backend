using Portfolio.Api.Exceptions;

namespace Portfolio.Api.Utils;

public static class Guard
{
    public static void NotFoundIfNull(object? value, string message)
    {
        if (value is null)
        {
            throw new NotFoundException(message);
        }
    }

    public static void ForbidIf(bool condition, string message)
    {
        if (condition)
        {
            throw new ForbiddenException(message);
        }
    }

    public static void UnauthorizedIf(bool condition, string message)
    {
        if (condition)
        {
            throw new UnauthorizedException(message);
        }
    }

    public static void BadRequestIf(bool condition, string message)
    {
        if (condition)
        {
            throw new BadRequestException(message);
        }
    }
}