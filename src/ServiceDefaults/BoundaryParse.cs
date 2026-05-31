using Microsoft.AspNetCore.Http;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Parses a primitive request value into a value object at an API trust
/// boundary, turning the <see cref="ArgumentException"/> a value object's
/// <c>From(...)</c> throws on invalid input into an RFC-7807 400 result.
/// Endpoints share this instead of repeating the
/// <c>try { x = X.From(...); } catch (ArgumentException ex) { return Results.Problem(...); }</c>
/// block at each route that binds a raw <c>Guid</c>, <c>int</c>, or <c>string</c>.
/// </summary>
public static class BoundaryParse
{
    /// <summary>
    /// Runs <paramref name="parse"/>. On success, yields the parsed
    /// <paramref name="value"/> and returns <c>true</c>. If it throws
    /// <see cref="ArgumentException"/>, yields a 400 Problem Details result
    /// titled <paramref name="errorCode"/> with the exception message as the
    /// detail, and returns <c>false</c>.
    /// </summary>
    public static bool TryParse<T>(Func<T> parse, string errorCode, out T value, out IResult problem)
    {
        ArgumentNullException.ThrowIfNull(parse);
        try
        {
            value = parse();
            problem = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            value = default;
            problem = Results.Problem(
                title: errorCode,
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }
    }
}
