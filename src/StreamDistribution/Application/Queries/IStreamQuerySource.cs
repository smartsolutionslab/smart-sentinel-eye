namespace SmartSentinelEye.StreamDistribution.Application.Queries;

/// <summary>
/// Read-side seam exposing <c>IQueryable&lt;Stream&gt;</c> so query handlers
/// stay in the Application layer while EF Core's translation runs in
/// Infrastructure. Mirrors the pattern from <c>ICameraQuerySource</c>.
/// </summary>
public interface IStreamQuerySource
{
    IQueryable<Domain.Stream.Stream> Streams { get; }
}
