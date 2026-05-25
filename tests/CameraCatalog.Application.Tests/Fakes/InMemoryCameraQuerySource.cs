using SmartSentinelEye.CameraCatalog.Application.Queries;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;

/// <summary>
/// In-memory ICameraQuerySource for handler tests (ADR-0052). The list is
/// exposed through TestAsyncEnumerable so EF Core's CountAsync / ToListAsync
/// extensions resolve against an IAsyncQueryProvider — no DbContext, no
/// Postgres. The real implementation wraps DbContext.Cameras.
/// </summary>
public sealed class InMemoryCameraQuerySource(List<Domain.Camera.Camera> cameras) : ICameraQuerySource
{
    public IQueryable<Domain.Camera.Camera> Cameras => new TestAsyncEnumerable<Domain.Camera.Camera>(cameras);
}
