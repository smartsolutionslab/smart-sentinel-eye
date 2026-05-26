using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Application.Queries;

/// <summary>
/// Read-side seam: exposes an IQueryable&lt;Camera&gt; so the query handler can
/// push sort + pagination into SQL. Implementation in Infrastructure wraps
/// the EF Core DbContext; the in-memory fake in tests wraps a list.
/// </summary>
public interface ICameraQuerySource
{
    IQueryable<Camera> Cameras { get; }
}
