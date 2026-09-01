using System.Linq.Expressions;
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

    /// <summary>
    /// The same match as the EF implementation, reached the only way a list can
    /// reach it: <c>CameraName.NormalizedValue</c>, which is the domain's own
    /// <c>upper</c> of the name and what the generated column mirrors.
    ///
    /// <para>
    /// <b>This is a second implementation of one rule, and that is a real
    /// risk</b> — the two agree today and could stop agreeing. It is
    /// unavoidable here: the seam exists because EF reaches a shadow property
    /// and a list cannot. What guards the pair is the integration test, which
    /// asks the real database the same question through HTTP; a handler test
    /// alone would only prove the fake agrees with itself.
    /// </para>
    ///
    /// <para>
    /// <c>Ordinal</c>, matching the database's byte-wise comparison of an
    /// already-upper-cased column. A culture-sensitive containment would fold
    /// pairs Postgres keeps apart, so the fake would answer questions the real
    /// source answers differently.
    /// </para>
    /// </summary>
    public Expression<Func<Domain.Camera.Camera, bool>> NameContains(string normalizedFragment) =>
        camera => camera.Name.NormalizedValue.Contains(normalizedFragment, StringComparison.Ordinal);
}
