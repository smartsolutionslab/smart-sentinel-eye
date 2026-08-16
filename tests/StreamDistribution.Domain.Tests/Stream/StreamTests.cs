using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Stream.Events;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

public class StreamTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Provision_creates_a_provisioning_stream_and_raises_the_provisioned_event()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());

        Domain.Stream.Stream stream = new StreamBuilder()
            .ForCamera(camera)
            .At(FixedMoment)
            .Build();

        stream.State.ShouldBe(StreamState.Provisioning);
        stream.Camera.ShouldBe(camera);
        stream.Path.ShouldBe(MediaMtxPath.For(camera));
        stream.TranscodeMode.ShouldBe(TranscodeMode.Unknown);
        stream.LastSuccessAt.ShouldBeNull();
        stream.LastError.ShouldBeNull();
        stream.ProvisionedAt.ShouldBe(FixedMoment);

        stream.PendingEvents.Count.ShouldBe(1);
        stream.PendingEvents.Single().ShouldBeOfType<StreamProvisionedDomainEvent>();
    }

    [Fact]
    public void Provision_persists_the_source_url_so_the_reconciler_can_re_add_the_path()
    {
        StreamSourceUrl source = StreamSourceUrl.From("rtsp://camera-sim:8554/station-4");

        Domain.Stream.Stream stream = new StreamBuilder()
            .WithSourceUrl(source)
            .Build();

        stream.SourceUrl.ShouldBe(source);
    }

    [Fact]
    public void Provision_requires_a_source_url()
    {
        Should.Throw<ArgumentException>(() => Domain.Stream.Stream.Provision(
            FabIdentifier.From("munich"),
            CameraIdentifier.From(Guid.CreateVersion7()),
            null,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FixedClock(FixedMoment)));
    }

    [Fact]
    public void Provision_requires_a_fab()
    {
        Should.Throw<ArgumentException>(() => Domain.Stream.Stream.Provision(
            null,
            CameraIdentifier.From(Guid.CreateVersion7()),
            StreamSourceUrl.From("rtsp://camera-sim:8554/station-4"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new FixedClock(FixedMoment)));
    }

    [Fact]
    public void Provision_records_the_fab_the_camera_belongs_to()
    {
        Domain.Stream.Stream stream = new StreamBuilder()
            .WithFab(FabIdentifier.From("dresden"))
            .Build();

        stream.Fab.ShouldBe(FabIdentifier.From("dresden"));
    }

    private sealed class FixedClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }

    [Fact]
    public void Report_healthy_from_provisioning_transitions_and_raises_HealthChanged()
    {
        Domain.Stream.Stream stream = new StreamBuilder().Build();
        stream.ClearPendingEvents();

        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment));

        stream.State.ShouldBe(StreamState.Healthy);
        stream.TranscodeMode.ShouldBe(TranscodeMode.Passthrough);
        stream.LastSuccessAt.ShouldBe(FixedMoment);
        stream.LastError.ShouldBeNull();

        StreamHealthChangedDomainEvent transition =
            stream.PendingEvents.Single().ShouldBeOfType<StreamHealthChangedDomainEvent>();
        transition.FromState.ShouldBe(StreamState.Provisioning);
        transition.ToState.ShouldBe(StreamState.Healthy);
        transition.Error.ShouldBeNull();
    }

    [Fact]
    public void Report_healthy_when_already_healthy_does_not_raise_a_second_event()
    {
        Domain.Stream.Stream stream = new StreamBuilder().Build();
        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment));
        stream.ClearPendingEvents();

        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment.AddSeconds(5)));

        stream.State.ShouldBe(StreamState.Healthy);
        stream.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Report_degraded_from_healthy_raises_HealthChanged_with_the_error()
    {
        Domain.Stream.Stream stream = new StreamBuilder().Build();
        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment));
        stream.ClearPendingEvents();

        stream.ReportDegraded("source unreachable", new TestClock(FixedMoment.AddSeconds(15)));

        stream.State.ShouldBe(StreamState.Degraded);
        stream.LastError.ShouldBe("source unreachable");

        StreamHealthChangedDomainEvent transition =
            stream.PendingEvents.Single().ShouldBeOfType<StreamHealthChangedDomainEvent>();
        transition.FromState.ShouldBe(StreamState.Healthy);
        transition.ToState.ShouldBe(StreamState.Degraded);
        transition.Error.ShouldBe("source unreachable");
    }

    [Fact]
    public void Report_degraded_when_already_degraded_updates_LastError_but_does_not_raise_an_event()
    {
        Domain.Stream.Stream stream = new StreamBuilder().Build();
        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment));
        stream.ReportDegraded("first failure", new TestClock(FixedMoment.AddSeconds(15)));
        stream.ClearPendingEvents();

        stream.ReportDegraded("retry failed", new TestClock(FixedMoment.AddSeconds(20)));

        stream.LastError.ShouldBe("retry failed");
        stream.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Report_offline_from_degraded_raises_HealthChanged()
    {
        Domain.Stream.Stream stream = new StreamBuilder().Build();
        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment));
        stream.ReportDegraded("first failure", new TestClock(FixedMoment.AddSeconds(15)));
        stream.ClearPendingEvents();

        stream.ReportOffline("retry exhausted", new TestClock(FixedMoment.AddMinutes(5)));

        stream.State.ShouldBe(StreamState.Offline);
        StreamHealthChangedDomainEvent transition =
            stream.PendingEvents.Single().ShouldBeOfType<StreamHealthChangedDomainEvent>();
        transition.FromState.ShouldBe(StreamState.Degraded);
        transition.ToState.ShouldBe(StreamState.Offline);
    }

    [Fact]
    public void Report_offline_directly_from_healthy_throws()
    {
        Domain.Stream.Stream stream = new StreamBuilder().Build();
        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment));

        Action act = () => stream.ReportOffline("can't happen", new TestClock(FixedMoment.AddSeconds(10)));

        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void Report_healthy_from_degraded_transitions_back()
    {
        Domain.Stream.Stream stream = new StreamBuilder().Build();
        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment));
        stream.ReportDegraded("first failure", new TestClock(FixedMoment.AddSeconds(15)));
        stream.ClearPendingEvents();

        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment.AddMinutes(1)));

        stream.State.ShouldBe(StreamState.Healthy);
        stream.LastError.ShouldBeNull();
        StreamHealthChangedDomainEvent transition =
            stream.PendingEvents.Single().ShouldBeOfType<StreamHealthChangedDomainEvent>();
        transition.FromState.ShouldBe(StreamState.Degraded);
        transition.ToState.ShouldBe(StreamState.Healthy);
    }

    [Fact]
    public void Report_healthy_from_offline_transitions_back()
    {
        Domain.Stream.Stream stream = new StreamBuilder().Build();
        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment));
        stream.ReportDegraded("failure", new TestClock(FixedMoment.AddSeconds(15)));
        stream.ReportOffline("exhausted", new TestClock(FixedMoment.AddMinutes(5)));
        stream.ClearPendingEvents();

        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment.AddMinutes(10)));

        stream.State.ShouldBe(StreamState.Healthy);
        StreamHealthChangedDomainEvent transition =
            stream.PendingEvents.Single().ShouldBeOfType<StreamHealthChangedDomainEvent>();
        transition.FromState.ShouldBe(StreamState.Offline);
        transition.ToState.ShouldBe(StreamState.Healthy);
    }

    /// <summary>
    /// FR-002: a stream's fab must equal its camera's, and a camera cannot move
    /// fab. Every transition the aggregate has must therefore leave it alone.
    /// The three below are all of them — <c>ReportHealthy</c>,
    /// <c>ReportDegraded</c> and <c>ReportOffline</c>; there is no
    /// decommission.
    /// </summary>
    [Fact]
    public void The_fab_survives_every_state_transition()
    {
        FabIdentifier dresden = FabIdentifier.From("dresden");
        Domain.Stream.Stream stream = new StreamBuilder().WithFab(dresden).Build();

        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment));
        stream.Fab.ShouldBe(dresden);

        stream.ReportDegraded("source unreachable", new TestClock(FixedMoment.AddSeconds(15)));
        stream.Fab.ShouldBe(dresden);

        stream.ReportOffline("retry exhausted", new TestClock(FixedMoment.AddMinutes(5)));
        stream.Fab.ShouldBe(dresden);

        stream.ReportHealthy(TranscodeMode.Software, new TestClock(FixedMoment.AddMinutes(10)));
        stream.Fab.ShouldBe(dresden);
    }

    /// <summary>
    /// Part of the FR-002 guarantee is structural rather than behavioural: no
    /// setter exists, so nothing outside the aggregate can assign the fab. A
    /// behavioural test would not catch someone adding one.
    /// </summary>
    [Fact]
    public void The_fab_has_no_setter()
    {
        typeof(Domain.Stream.Stream)
            .GetProperty(nameof(Domain.Stream.Stream.Fab))
            .GetSetMethod(nonPublic: false)
            .ShouldBeNull();
    }

    /// <summary>
    /// The other part, and the one a setter would break: <c>AttributeToFab</c>
    /// exists for FR-008 and moves a stream from "fab unknown" to "fab known"
    /// only. A stream that already has a fab took it from its camera, and a
    /// camera cannot change fab, so a second call has no legitimate meaning.
    /// </summary>
    [Fact]
    public void A_stream_that_already_has_a_fab_cannot_be_reattributed()
    {
        Domain.Stream.Stream stream = new StreamBuilder().WithFab(FabIdentifier.From("munich")).Build();

        Action act = () => stream.AttributeToFab(FabIdentifier.From("dresden"));

        act.ShouldThrow<InvalidOperationException>();
        stream.Fab.ShouldBe(FabIdentifier.From("munich"));
    }

    private sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
