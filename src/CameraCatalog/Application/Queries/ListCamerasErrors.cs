using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Queries;

/// <summary>
/// Failure cases for ListCamerasQuery per spec FR-009. Each case carries
/// Code, Message, and HttpStatusCode for RFC 7807 mapping (ADR-0089).
/// </summary>
public abstract record ListCamerasError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record InvalidSortField(string Requested, IReadOnlyList<string> Allowed)
        : ListCamerasError(
            "CATALOG_INVALID_SORT_FIELD",
            $"Unknown sort field '{Requested}'. Allowed: {string.Join(", ", Allowed)}.",
            HttpStatusCode.BadRequest);

    public sealed record InvalidSortOrder(string Requested)
        : ListCamerasError(
            "CATALOG_INVALID_SORT_ORDER",
            $"Unknown sort order '{Requested}'. Allowed: asc, desc.",
            HttpStatusCode.BadRequest);

    public sealed record LimitExceeded(int Requested, int Maximum)
        : ListCamerasError(
            "CATALOG_LIMIT_EXCEEDED",
            $"Requested limit {Requested} exceeds the maximum allowed ({Maximum}).",
            HttpStatusCode.BadRequest);

    public sealed record InvalidPagination(string Message)
        : ListCamerasError(
            "CATALOG_INVALID_PAGINATION",
            Message,
            HttpStatusCode.BadRequest);
}
