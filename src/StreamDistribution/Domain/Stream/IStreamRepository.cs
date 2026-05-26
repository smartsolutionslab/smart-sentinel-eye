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
}
