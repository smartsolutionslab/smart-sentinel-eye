using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// Stream repository contract (ADR-0041). Implementation lives in
/// StreamDistribution.Infrastructure; the Domain layer has no persistence
/// dependency.
/// </summary>
public interface IStreamRepository
{
    Task<Option<Stream>> GetByIdentifierAsync(StreamIdentifier stream, CancellationToken cancellationToken);

    Task<Option<Stream>> GetByCameraAsync(CameraIdentifier camera, CancellationToken cancellationToken);

    Task<Option<Stream>> GetByPathAsync(MediaMtxPath path, CancellationToken cancellationToken);

    void Add(Stream stream);

    Task SaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reports whether the stream's row still carries the version this unit of work loaded or
    /// inserted, without changing it. A conditional no-op UPDATE: it waits on the row lock of any
    /// uncommitted writer and re-evaluates after that writer commits. Writes nothing, so it can
    /// never make a concurrent writer lose (spec 318 §1.3).
    /// </summary>
    Task<bool> IsUnchangedSinceLoadAsync(Stream stream, CancellationToken cancellationToken);

    /// <summary>
    /// The row's committed state, read past the change tracker — a tracked query would hand back
    /// the instance this unit of work already holds, with the values it loaded.
    /// </summary>
    Task<StreamState> ReadCommittedStateAsync(StreamIdentifier stream, CancellationToken cancellationToken);
}
