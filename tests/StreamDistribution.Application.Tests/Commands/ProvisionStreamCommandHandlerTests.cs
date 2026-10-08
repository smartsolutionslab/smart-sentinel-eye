using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Commands;

public class ProvisionStreamCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.CreateVersion7());

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    [Fact]
    public async Task Provision_for_a_new_camera_creates_the_stream_and_registers_the_path()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        ProvisionStreamCommand command = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);

        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        streams.Streams.Count.ShouldBe(1);
        streams.Streams.Single().Camera.ShouldBe(camera);
        streams.SaveCallCount.ShouldBe(1);
        gateway.AddCalls.Count.ShouldBe(1);
        gateway.AddCalls.Single().Path.Value.ShouldBe($"cam-{camera.Value}");
        gateway.AddCalls.Single().Source.ShouldBe("rtsp://10.0.5.1/h264");
    }

    /// <summary>
    /// Spec 309 FR-004 (declared edit — <c>AddCalls.Count</c> was 1 and the
    /// name did not say "re_asserts"). Re-provisioning an existing,
    /// non-retired stream now re-asserts its MediaMTX path instead of
    /// skipping the gateway call, so a redelivery can heal a row that was
    /// saved but never got its path added.
    /// </summary>
    [Fact]
    public async Task Provision_for_an_existing_camera_returns_the_existing_identifier_and_re_asserts_its_path()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        ProvisionStreamCommand first = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        Result<StreamIdentifier, ProvisionStreamError> firstResult =
            await handler.HandleAsync(first, CancellationToken.None);

        ProvisionStreamCommand redelivery = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        Result<StreamIdentifier, ProvisionStreamError> secondResult =
            await handler.HandleAsync(redelivery, CancellationToken.None);

        secondResult.IsSuccess.ShouldBeTrue();
        secondResult.Value.ShouldBe(firstResult.Value);
        streams.Streams.Count.ShouldBe(1);
        gateway.AddCalls.Count.ShouldBe(2);
    }

    /// <summary>
    /// Spec 309 FR-003 (declared edit — <c>SaveCallCount</c> was asserted 0).
    /// The row now saves before the gateway is asked to add its path, so a
    /// gateway failure after the save no longer means zero saves; the row
    /// must survive so a redelivery can find it and retry the add.
    /// </summary>
    [Fact]
    public async Task Provision_when_the_RTSP_gateway_is_unreachable_returns_RtspGatewayUnavailable()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new()
        {
            OnAddPath = (_, _) => throw new HttpRequestException("connection refused"),
        };
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        ProvisionStreamCommand command = new(
            Munich,
            CameraIdentifier.From(Guid.CreateVersion7()),
            "rtsp://10.0.5.1/h264",
            AnAdmin);

        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        ProvisionStreamError.RtspGatewayUnavailable err =
            result.Error.ShouldBeOfType<ProvisionStreamError.RtspGatewayUnavailable>();
        err.Status.ShouldBe(HttpStatusCode.ServiceUnavailable);
        streams.SaveCallCount.ShouldBe(1);
        streams.Streams.Count.ShouldBe(1);
    }

    /// <summary>
    /// Spec 309 FR-001. A throw from <c>SaveAsync</c> must propagate before
    /// the gateway is ever asked to add the path — today the order is
    /// reversed (add, then save), so a save failure after a successful add
    /// strands a live MediaMTX path with no row behind it.
    /// </summary>
    [Fact]
    public async Task Provision_when_the_save_fails_registers_no_MediaMTX_path()
    {
        InMemoryStreamRepository streams = new()
        {
            OnSave = () => throw new InvalidOperationException("save failed"),
        };
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        ProvisionStreamCommand command = new(
            Munich,
            CameraIdentifier.From(Guid.CreateVersion7()),
            "rtsp://10.0.5.1/h264",
            AnAdmin);

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(command, CancellationToken.None));

        gateway.AddCalls.ShouldBeEmpty();
    }

    /// <summary>
    /// Spec 309 FR-001 (ordering). Captured from inside the gateway call, so
    /// the assertion observes the repository's state at the moment
    /// <c>AddPathAsync</c> runs, not merely afterwards.
    /// </summary>
    [Fact]
    public async Task Provision_saves_the_stream_before_registering_its_path()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        int? streamCountWhenPathWasAdded = null;
        gateway.OnAddPath = (_, _) => streamCountWhenPathWasAdded = streams.Streams.Count;
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        ProvisionStreamCommand command = new(
            Munich,
            CameraIdentifier.From(Guid.CreateVersion7()),
            "rtsp://10.0.5.1/h264",
            AnAdmin);

        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        streamCountWhenPathWasAdded.ShouldBe(1);
    }

    /// <summary>
    /// Spec 309 FR-004 / §1.3. Save-first alone creates a new partial state:
    /// "row saved, path not added". A redelivery onto that row must re-add
    /// the path rather than short-circuit, or the camera stays unprovisioned
    /// until the next restart.
    /// </summary>
    [Fact]
    public async Task A_redelivery_after_a_failed_path_registration_registers_the_path()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new()
        {
            OnAddPath = (_, _) => throw new HttpRequestException("connection refused"),
        };
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        ProvisionStreamCommand command = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);

        Result<StreamIdentifier, ProvisionStreamError> firstResult =
            await handler.HandleAsync(command, CancellationToken.None);

        firstResult.IsFailure.ShouldBeTrue();
        firstResult.Error.ShouldBeOfType<ProvisionStreamError.RtspGatewayUnavailable>();
        streams.Streams.Count.ShouldBe(1);

        gateway.OnAddPath = (_, _) => { };

        ProvisionStreamCommand redelivery = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        Result<StreamIdentifier, ProvisionStreamError> secondResult =
            await handler.HandleAsync(redelivery, CancellationToken.None);

        secondResult.IsSuccess.ShouldBeTrue();
        secondResult.Value.ShouldBe(streams.Streams.Single().Id);
        gateway.AddCalls.Count.ShouldBe(1);
        streams.Streams.Count.ShouldBe(1);
    }

    /// <summary>
    /// Spec 309 FR-004 guard — characterisation, green today and after: a
    /// Retired row never gets its path re-asserted, because re-registering
    /// hardware that was pulled off the wall would resurrect a path nobody
    /// wants reachable.
    /// </summary>
    [Fact]
    public async Task Provision_for_a_retired_stream_does_not_re_register_its_path()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        ProvisionStreamCommand first = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        Result<StreamIdentifier, ProvisionStreamError> firstResult =
            await handler.HandleAsync(first, CancellationToken.None);

        streams.Streams.Single().Retire(new FixedClock(FixedMoment));

        ProvisionStreamCommand redelivery = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        Result<StreamIdentifier, ProvisionStreamError> secondResult =
            await handler.HandleAsync(redelivery, CancellationToken.None);

        secondResult.IsSuccess.ShouldBeTrue();
        secondResult.Value.ShouldBe(firstResult.Value);
        gateway.AddCalls.Count.ShouldBe(1);
    }

    /// <summary>
    /// Spec 318 §1.1/§6.1 (A1) — the issue's race, provoked deterministically:
    /// the retire runs from inside <c>AddPathAsync</c>, which is the window by
    /// construction, not a timing accident. Today the provision is past its
    /// only read by the time the retire commits and removes the path, so it
    /// re-adds the path the retire just took away — the camera is briefly
    /// watchable again by its own fab.
    /// </summary>
    [Fact]
    public async Task A_retire_that_lands_while_a_redelivery_registers_the_path_keeps_the_path_removed()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        ProvisionStreamCommand first = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        await handler.HandleAsync(first, CancellationToken.None);

        Domain.Stream.Stream stream = streams.Streams.Single();
        stream.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
        await streams.SaveAsync(CancellationToken.None);
        stream.ClearPendingEvents();

        RetireStreamCommandHandler retireHandler = new(
            streams, gateway, new FixedClock(FixedMoment), NullLogger<RetireStreamCommandHandler>.Instance);

        // The race window by construction: a retire commits and removes the
        // path from inside the provision's own AddPathAsync call (spec 318
        // §1.3), not by sleeping and hoping for an interleaving.
        gateway.OnAddPath = (_, _) =>
            retireHandler.HandleAsync(new RetireStreamCommand(camera), CancellationToken.None)
                .GetAwaiter().GetResult();

        ProvisionStreamCommand redelivery = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(redelivery, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(stream.Id);
        (await gateway.ListConfiguredPathsAsync(CancellationToken.None)).ShouldNotContain(stream.Path);
        gateway.RemoveCalls.ShouldBe([stream.Path, stream.Path], "the retire's removal, then the provision's compensation");
        stream.State.ShouldBe(StreamState.Retired);
    }

    /// <summary>
    /// Spec 318 §1.1 (A2) — the same window on the <em>first</em> delivery: no
    /// row exists yet, so the provision's insert commits before the retire
    /// lands, and the retire removes a path the provision is about to add
    /// right behind it.
    /// </summary>
    [Fact]
    public async Task A_retire_that_lands_between_the_insert_and_the_path_registration_keeps_the_path_removed()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());

        RetireStreamCommandHandler retireHandler = new(
            streams, gateway, new FixedClock(FixedMoment), NullLogger<RetireStreamCommandHandler>.Instance);

        gateway.OnAddPath = (_, _) =>
            retireHandler.HandleAsync(new RetireStreamCommand(camera), CancellationToken.None)
                .GetAwaiter().GetResult();

        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);
        ProvisionStreamCommand command = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);

        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(command, CancellationToken.None);

        Domain.Stream.Stream stream = streams.Streams.Single();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(stream.Id);
        (await gateway.ListConfiguredPathsAsync(CancellationToken.None)).ShouldNotContain(stream.Path);
        gateway.RemoveCalls.ShouldBe([stream.Path, stream.Path], "the retire's removal, then the provision's compensation");
        stream.State.ShouldBe(StreamState.Retired);
    }

    /// <summary>
    /// Spec 318 §9 A3 (A3) — the guard the mismatch branch needs: the version
    /// also moves for a health report, which is not a retirement. Must stay
    /// green before and after the fix — the counterfactual that turns it red
    /// is "compensate on any mismatch" rather than only on a committed
    /// <c>Retired</c> read (plan §6.3, applied in T022).
    /// </summary>
    [Fact]
    public async Task A_health_report_during_path_registration_does_not_remove_the_path()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        ProvisionStreamCommand first = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        await handler.HandleAsync(first, CancellationToken.None);

        Domain.Stream.Stream stream = streams.Streams.Single();
        stream.ReportHealthy(TranscodeMode.Passthrough, new FixedClock(FixedMoment));
        await streams.SaveAsync(CancellationToken.None);
        stream.ClearPendingEvents();

        ReportStreamHealthCommandHandler healthHandler = new(
            streams, new FixedClock(FixedMoment), NullLogger<ReportStreamHealthCommandHandler>.Instance);
        RtspPathHealth observation = new(
            IsReady: true, LastError: null, LastFrameAt: null, DetectedMode: TranscodeMode.Passthrough);

        // A health sweep commits between the redelivery's read and its path
        // registration, moving the version without retiring the stream.
        gateway.OnAddPath = (_, _) =>
            healthHandler.HandleAsync(
                    new ReportStreamHealthCommand(camera, observation, DeclareOffline: false),
                    CancellationToken.None)
                .GetAwaiter().GetResult();

        ProvisionStreamCommand redelivery = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(redelivery, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        gateway.RemoveCalls.ShouldBeEmpty();
        (await gateway.ListConfiguredPathsAsync(CancellationToken.None)).ShouldContain(stream.Path);
    }

    /// <summary>
    /// Spec 318 §1.4 row 3 (A4) — the compensation itself can fail. The retire's
    /// own removal must succeed (it is not what this fact is about), only the
    /// provision's own undo of its own add.
    /// </summary>
    [Fact]
    public async Task A_failed_compensation_returns_RtspGatewayUnavailable()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        ProvisionStreamCommand first = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        await handler.HandleAsync(first, CancellationToken.None);

        RetireStreamCommandHandler retireHandler = new(
            streams, gateway, new FixedClock(FixedMoment), NullLogger<RetireStreamCommandHandler>.Instance);

        int removeCallCount = 0;
        gateway.OnRemovePath = _ =>
        {
            removeCallCount++;
            if (removeCallCount == 2)
            {
                throw new HttpRequestException("MediaMTX unreachable");
            }
        };
        gateway.OnAddPath = (_, _) =>
            retireHandler.HandleAsync(new RetireStreamCommand(camera), CancellationToken.None)
                .GetAwaiter().GetResult();

        ProvisionStreamCommand redelivery = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(redelivery, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        ProvisionStreamError.RtspGatewayUnavailable err =
            result.Error.ShouldBeOfType<ProvisionStreamError.RtspGatewayUnavailable>();
        err.Status.ShouldBe(HttpStatusCode.ServiceUnavailable);
        gateway.RemoveCalls.Count.ShouldBe(1, "the retire's own removal succeeded; only the compensation failed");
    }

    /// <summary>
    /// Spec 318 §1.3 happy path (A5) — characterisation of the new check under
    /// no concurrent writer: the assertion matches, so nothing is undone.
    /// </summary>
    [Fact]
    public async Task Provision_with_no_concurrent_writer_removes_nothing()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        ProvisionStreamCommand command = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);

        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        gateway.RemoveCalls.ShouldBeEmpty();
    }

    /// <summary>
    /// Spec 318 US2 (B1) — a redelivery that lands on an already-retired row
    /// finishes the teardown a failed (or crashed) compensation left behind,
    /// instead of leaving a leftover MediaMTX path for a restart to find.
    /// </summary>
    [Fact]
    public async Task A_redelivery_for_a_retired_stream_removes_its_leftover_path()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        ProvisionStreamCommand first = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        await handler.HandleAsync(first, CancellationToken.None);

        Domain.Stream.Stream stream = streams.Streams.Single();
        stream.Retire(new FixedClock(FixedMoment));
        stream.ClearPendingEvents();

        // A leftover path MediaMTX still holds for the now-retired stream —
        // e.g. a compensation that failed on an earlier delivery (spec 318 §1.4).
        await gateway.AddPathAsync(stream.Path, stream.SourceUrl.Value, CancellationToken.None);
        int addCallsBeforeRedelivery = gateway.AddCalls.Count;

        ProvisionStreamCommand redelivery = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(redelivery, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(stream.Id);
        gateway.RemoveCalls.ShouldBe([stream.Path]);
        gateway.AddCalls.Count.ShouldBe(addCallsBeforeRedelivery, "a retired stream's path is never re-added");
    }

    /// <summary>
    /// Spec 318 US2 (B2) — the teardown's own gateway failure is reported so
    /// the outbox redelivers, mirroring the retire's own compensation-failure
    /// handling (RetireStreamCommandHandler).
    /// </summary>
    [Fact]
    public async Task A_redelivery_for_a_retired_stream_when_MediaMTX_is_down_returns_RtspGatewayUnavailable()
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        ProvisionStreamCommand first = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        await handler.HandleAsync(first, CancellationToken.None);

        Domain.Stream.Stream stream = streams.Streams.Single();
        stream.Retire(new FixedClock(FixedMoment));
        stream.ClearPendingEvents();

        gateway.OnRemovePath = _ => throw new HttpRequestException("MediaMTX unreachable");

        ProvisionStreamCommand redelivery = new(Munich, camera, "rtsp://10.0.5.1/h264", AnAdmin);
        Result<StreamIdentifier, ProvisionStreamError> result =
            await handler.HandleAsync(redelivery, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        ProvisionStreamError.RtspGatewayUnavailable err =
            result.Error.ShouldBeOfType<ProvisionStreamError.RtspGatewayUnavailable>();
        err.Status.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Provision_with_blank_RTSP_source_returns_InvalidRtspSource(string source)
    {
        InMemoryStreamRepository streams = new();
        FakeRtspGateway gateway = new();
        ProvisionStreamCommandHandler handler = NewHandler(streams, gateway);

        Result<StreamIdentifier, ProvisionStreamError> result = await handler.HandleAsync(
            new ProvisionStreamCommand(
                Munich, CameraIdentifier.From(Guid.CreateVersion7()), source, AnAdmin),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ProvisionStreamError.InvalidRtspSource>();
        gateway.AddCalls.ShouldBeEmpty();
    }

    private static ProvisionStreamCommandHandler NewHandler(InMemoryStreamRepository streams, FakeRtspGateway gateway) =>
        new(
            streams,
            gateway,
            new FixedClock(FixedMoment),
            NullLogger<ProvisionStreamCommandHandler>.Instance);
}
