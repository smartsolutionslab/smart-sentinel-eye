using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

// Decorators over the existing fakes that append one word per port call to a
// single shared, ordered list. The characterisation tests of spec 351 assert
// that list verbatim: a refactor that moves a token check below the validator
// call, a stream lookup above a scope check, or a save below the gateway call
// cannot pass unnoticed, which no existing fake can express because each keeps
// its own private record. Everything is delegated; nothing here decides
// behaviour.

/// <summary>Words the decorators below write into the shared call log.</summary>
public static class PortCalls
{
    public const string Validate = "validate";
    public const string LookupPath = "lookup-path";
    public const string LookupCamera = "lookup-camera";
    public const string Save = "save";
    public const string Repoint = "repoint";
}

public sealed class RecordingWhepAuthValidator(
    IWhepAuthValidator inner,
    List<string> log) : IWhepAuthValidator
{
    public Task<Result<WhepAuthSubject, WhepAuthFailure>> ValidateAsync(
        string bearerToken,
        CancellationToken cancellationToken)
    {
        log.Add(PortCalls.Validate);
        return inner.ValidateAsync(bearerToken, cancellationToken);
    }
}

public sealed class RecordingStreamRepository(
    IStreamRepository inner,
    List<string> log) : IStreamRepository
{
    public Task<Option<Domain.Stream.Stream>> GetByIdentifierAsync(
        StreamIdentifier stream,
        CancellationToken cancellationToken) =>
        inner.GetByIdentifierAsync(stream, cancellationToken);

    public Task<Option<Domain.Stream.Stream>> GetByCameraAsync(
        CameraIdentifier camera,
        CancellationToken cancellationToken)
    {
        log.Add(PortCalls.LookupCamera);
        return inner.GetByCameraAsync(camera, cancellationToken);
    }

    public Task<Option<Domain.Stream.Stream>> GetByPathAsync(
        MediaMtxPath path,
        CancellationToken cancellationToken)
    {
        log.Add(PortCalls.LookupPath);
        return inner.GetByPathAsync(path, cancellationToken);
    }

    public void Add(Domain.Stream.Stream stream) => inner.Add(stream);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        log.Add(PortCalls.Save);
        return inner.SaveAsync(cancellationToken);
    }

    public Task<bool> IsUnchangedSinceLoadAsync(
        Domain.Stream.Stream stream,
        CancellationToken cancellationToken) =>
        inner.IsUnchangedSinceLoadAsync(stream, cancellationToken);

    public Task<StreamState> ReadCommittedStateAsync(
        StreamIdentifier stream,
        CancellationToken cancellationToken) =>
        inner.ReadCommittedStateAsync(stream, cancellationToken);
}

public sealed class RecordingRtspGateway(
    IRtspGateway inner,
    List<string> log) : IRtspGateway
{
    public Task AddPathAsync(
        MediaMtxPath path,
        string rtspSourceUrl,
        CancellationToken cancellationToken) =>
        inner.AddPathAsync(path, rtspSourceUrl, cancellationToken);

    public Task RemovePathAsync(
        MediaMtxPath path,
        CancellationToken cancellationToken) =>
        inner.RemovePathAsync(path, cancellationToken);

    public Task RepointPathAsync(
        MediaMtxPath path,
        string rtspSourceUrl,
        CancellationToken cancellationToken)
    {
        log.Add(PortCalls.Repoint);
        return inner.RepointPathAsync(path, rtspSourceUrl, cancellationToken);
    }

    public Task<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>> GetAllPathsHealthAsync(
        CancellationToken cancellationToken) =>
        inner.GetAllPathsHealthAsync(cancellationToken);

    public Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(
        CancellationToken cancellationToken) =>
        inner.ListConfiguredPathsAsync(cancellationToken);

    public Task<IReadOnlyDictionary<MediaMtxPath, string>> ListConfiguredPathSourcesAsync(
        CancellationToken cancellationToken) =>
        inner.ListConfiguredPathSourcesAsync(cancellationToken);
}
