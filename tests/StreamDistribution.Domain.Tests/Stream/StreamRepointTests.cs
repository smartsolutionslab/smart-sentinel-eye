using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Spec 029 T025 — the stream follows its camera's corrected address
/// (FR-013, FR-014).
/// </summary>
public class StreamRepointTests
{
    private static readonly DateTimeOffset ChangedAt =
        DateTimeOffset.Parse("2026-08-24T12:00:00Z", CultureInfo.InvariantCulture);

    private const string OriginalUrl = "rtsp://camera-sim:8554/original";
    private const string CorrectedUrl = "rtsp://camera-sim:8554/corrected";

    public static TheoryData<string> EveryLiveState => new("Provisioning", "Healthy", "Degraded", "Offline");

    [Theory]
    [MemberData(nameof(EveryLiveState))]
    public void A_stream_can_be_repointed_from_any_live_state(string from)
    {
        Domain.Stream.Stream stream = InState(from);

        stream.RepointTo(StreamSourceUrl.From(CorrectedUrl), Clock());

        stream.SourceUrl.Value.ShouldBe(CorrectedUrl);
    }

    /// <summary>
    /// FR-014. The path derives from the camera identifier, which is immutable,
    /// so a correction must move only what the path pulls from. If the path
    /// changed, every kiosk already watching would lose its stream over a
    /// clerical fix.
    /// </summary>
    [Fact]
    public void Repointing_does_not_change_the_path_a_viewer_is_watching()
    {
        Domain.Stream.Stream stream = InState("Healthy");
        MediaMtxPath before = stream.Path;

        stream.RepointTo(StreamSourceUrl.From(CorrectedUrl), Clock());

        stream.Path.ShouldBe(before);
        stream.State.ShouldBe(StreamState.Healthy, "a corrected address is not a health event");
    }

    [Fact]
    public void Repointing_to_the_url_it_already_has_changes_nothing()
    {
        Domain.Stream.Stream stream = InState("Healthy");

        stream.RepointTo(StreamSourceUrl.From(OriginalUrl), Clock());

        stream.SourceUrl.Value.ShouldBe(OriginalUrl);
    }

    /// <summary>
    /// Mirrors the guard spec 028 put on the health reports. Re-pointing
    /// hardware that has been retired changes nothing except the record, and
    /// the watcher no longer sweeps a retired stream anyway.
    /// </summary>
    [Fact]
    public void A_retired_stream_refuses_to_be_repointed()
    {
        Domain.Stream.Stream stream = InState("Healthy");
        stream.Retire(Clock());

        Should.Throw<InvalidOperationException>(() =>
            stream.RepointTo(StreamSourceUrl.From(CorrectedUrl), Clock()));

        stream.SourceUrl.Value.ShouldBe(OriginalUrl);
    }

    private static FixedClock Clock() => new(ChangedAt);

    private static Domain.Stream.Stream InState(string state)
    {
        Domain.Stream.Stream stream = new StreamBuilder()
            .WithSourceUrl(StreamSourceUrl.From(OriginalUrl))
            .Build();

        FixedClock clock = Clock();

        switch (state)
        {
            case "Provisioning":
                break;
            case "Healthy":
                stream.ReportHealthy(TranscodeMode.Passthrough, clock);
                break;
            case "Degraded":
                stream.ReportDegraded(StreamError.From("no frames"), clock);
                break;
            case "Offline":
                stream.ReportDegraded(StreamError.From("no frames"), clock);
                stream.ReportOffline(StreamError.From("still no frames"), clock);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unhandled state.");
        }

        stream.State.Value.ShouldBe(state);

        return stream;
    }

    private sealed class FixedClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
