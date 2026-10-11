namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// Serialises every class that listens to
/// <c>SmartSentinelEye.IngestChannelSaturation</c>, the same reasoning as
/// <see cref="IngestVolumeCollection"/>: <c>Meter</c> is process-wide, so
/// two classes emitting at the same moment would collect each other's
/// measurements without this.
/// </summary>
[CollectionDefinition(Name)]
#pragma warning disable CA1711 // xUnit convention requires the "Collection" suffix
public class IngestChannelSaturationCollection
#pragma warning restore CA1711
{
    public const string Name = "ingest-channel-saturation";

    // xUnit reads the attribute off this type; nothing constructs it.
    protected IngestChannelSaturationCollection()
    {
    }
}
