using Stream = SmartSentinelEye.StreamDistribution.Domain.Stream.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.DTOs;

internal static class StreamMappings
{
    public static StreamHealthDto ToDto(this Stream stream, string whepUrl) =>
        new(
            CameraIdentifier: stream.Camera.Value,
            Fab: stream.Fab.Value,
            State: stream.State.Value,
            WhepUrl: whepUrl,
            TranscodeMode: stream.TranscodeMode.Value,
            LastSuccessAt: stream.LastSuccessAt?.Value,
            Error: stream.LastError?.Value);
}
