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
        stream.LastSuccessAt.HasValue.ShouldBeFalse();
        stream.LastError.HasValue.ShouldBeFalse();
        stream.ProvisionedAt.ShouldBe(FixedMoment);

        stream.PendingEvents.Count.ShouldBe(1);
        stream.PendingEvents.Single().ShouldBeOfType<StreamProvisionedDomainEvent>();
    }

    [Fact]
    public void Report_healthy_from_provisioning_transitions_and_raises_HealthChanged()
    {
        Domain.Stream.Stream stream = new StreamBuilder().Build();
        stream.ClearPendingEvents();

        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment));

        stream.State.ShouldBe(StreamState.Healthy);
        stream.TranscodeMode.ShouldBe(TranscodeMode.Passthrough);
        stream.LastSuccessAt.HasValue.ShouldBeTrue();
        stream.LastSuccessAt.Value.ShouldBe(FixedMoment);
        stream.LastError.HasValue.ShouldBeFalse();

        StreamHealthChangedDomainEvent transition =
            stream.PendingEvents.Single().ShouldBeOfType<StreamHealthChangedDomainEvent>();
        transition.FromState.ShouldBe(StreamState.Provisioning);
        transition.ToState.ShouldBe(StreamState.Healthy);
        transition.Error.HasValue.ShouldBeFalse();
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
        stream.LastError.HasValue.ShouldBeTrue();
        stream.LastError.Value.ShouldBe("source unreachable");

        StreamHealthChangedDomainEvent transition =
            stream.PendingEvents.Single().ShouldBeOfType<StreamHealthChangedDomainEvent>();
        transition.FromState.ShouldBe(StreamState.Healthy);
        transition.ToState.ShouldBe(StreamState.Degraded);
        transition.Error.HasValue.ShouldBeTrue();
        transition.Error.Value.ShouldBe("source unreachable");
    }

    [Fact]
    public void Report_degraded_when_already_degraded_updates_LastError_but_does_not_raise_an_event()
    {
        Domain.Stream.Stream stream = new StreamBuilder().Build();
        stream.ReportHealthy(TranscodeMode.Passthrough, new TestClock(FixedMoment));
        stream.ReportDegraded("first failure", new TestClock(FixedMoment.AddSeconds(15)));
        stream.ClearPendingEvents();

        stream.ReportDegraded("retry failed", new TestClock(FixedMoment.AddSeconds(20)));

        stream.LastError.Value.ShouldBe("retry failed");
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
        stream.LastError.HasValue.ShouldBeFalse();
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

    private sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
