using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

public class AuditingMessageHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:14:33Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    private static V1Envelope Envelope(CameraRegisteredV1 evt, Guid? eventIdentifier = null) =>
        new(
            EventTypeName: nameof(CameraRegisteredV1),
            OccurredAt: evt.RegisteredAt,
            Fab: Option<FabIdentifier>.None,
            Actor: ActorIdentifier.System,
            ActorUsername: Option<string>.None,
            EventIdentifier: EventIdentifier.From(eventIdentifier ?? Guid.CreateVersion7()),
            Payload: System.Text.Json.JsonSerializer.Serialize(evt));

    [Fact]
    public async Task Writes_a_row_for_a_mapped_V1()
    {
        InMemoryAuditEventRepository repo = new();
        AuditingMessageHandler handler = new(
            repo, V1ResourceMap.Default, new FakeClock(Now),
            NullLogger<AuditingMessageHandler>.Instance);

        CameraRegisteredV1 evt = new(
            Guid.CreateVersion7(), "north-gate", "rtsp://example/cam", Now, Guid.CreateVersion7(), Metadata: TestMetadata);

        await handler.HandleAsync(typeof(CameraRegisteredV1), evt, Envelope(evt), default);

        repo.Committed.Count.ShouldBe(1);
        repo.Committed[0].EventKind.Value.ShouldBe("CameraRegisteredV1");
        repo.Committed[0].ResourceKind.ShouldBe(ResourceKind.Camera);
        repo.Committed[0].ResourceIdentifier!.Value.ShouldBe(evt.Camera.ToString());
    }

    [Fact]
    public async Task Duplicate_event_identifier_is_absorbed_idempotently()
    {
        InMemoryAuditEventRepository repo = new();
        AuditingMessageHandler handler = new(
            repo, V1ResourceMap.Default, new FakeClock(Now),
            NullLogger<AuditingMessageHandler>.Instance);

        CameraRegisteredV1 evt = new(
            Guid.CreateVersion7(), "north-gate", "rtsp://example/cam", Now, Guid.CreateVersion7(), Metadata: TestMetadata);
        Guid eventId = Guid.CreateVersion7();

        await handler.HandleAsync(typeof(CameraRegisteredV1), evt, Envelope(evt, eventId), default);
        await handler.HandleAsync(typeof(CameraRegisteredV1), evt, Envelope(evt, eventId), default);

        repo.Committed.Count.ShouldBe(1);
        repo.SaveAsyncCallCount.ShouldBe(2);
    }

    [Fact]
    public async Task Unmapped_V1_still_audits_with_null_resource_fields()
    {
        InMemoryAuditEventRepository repo = new();
        // A type that's deliberately NOT in V1ResourceMap (string
        // is a stand-in for a future V1 the registry has not yet
        // learned about).
        AuditingMessageHandler handler = new(
            repo, V1ResourceMap.Default, new FakeClock(Now),
            NullLogger<AuditingMessageHandler>.Instance);

        V1Envelope envelope = new(
            EventTypeName: "FutureV1",
            OccurredAt: Now,
            Fab: Option<FabIdentifier>.None,
            Actor: ActorIdentifier.System,
            ActorUsername: Option<string>.None,
            EventIdentifier: EventIdentifier.From(Guid.CreateVersion7()),
            Payload: "{}");

        await handler.HandleAsync(typeof(string), "irrelevant", envelope, default);

        repo.Committed.Count.ShouldBe(1);
        repo.Committed[0].ResourceKind.ShouldBeNull();
        repo.Committed[0].ResourceIdentifier.ShouldBeNull();
        repo.Committed[0].EventKind.Value.ShouldBe("FutureV1");
    }

    /// <summary>
    /// ADR-0127. The batch path exists for exactly one reason — a batch is
    /// <b>one</b> transaction — so that is what is asserted, not the row count.
    /// Rows landing while <c>SaveAsync</c> is called once per message would be
    /// indistinguishable by any assertion on <c>Committed</c>, and is precisely
    /// the shape the change is meant to replace.
    /// </summary>
    [Fact]
    public async Task A_batch_commits_every_row_in_one_transaction()
    {
        InMemoryAuditEventRepository repo = new();
        AuditingMessageHandler handler = new(
            repo, V1ResourceMap.Default, new FakeClock(Now),
            NullLogger<AuditingMessageHandler>.Instance);

        List<(object Payload, V1Envelope Envelope)> batch = [];
        for (int i = 0; i < 25; i++)
        {
            CameraRegisteredV1 evt = new(
                Guid.CreateVersion7(), $"cam-{i}", "rtsp://example/cam", Now, Guid.CreateVersion7(), Metadata: TestMetadata);
            batch.Add((evt, Envelope(evt)));
        }

        await handler.HandleBatchAsync(typeof(CameraRegisteredV1), batch, default);

        repo.Committed.Count.ShouldBe(25);
        repo.SaveAsyncCallCount.ShouldBe(
            1,
            "a batch of 25 must cost one transaction; one save per message is the cost ADR-0127 removes");
    }

    /// <summary>
    /// An empty batch must not open a transaction. Wolverine can hand a batch
    /// through on a trigger tick with nothing in it, and a commit per tick would
    /// be a steady trickle of empty transactions nobody would look for.
    /// </summary>
    [Fact]
    public async Task An_empty_batch_opens_no_transaction()
    {
        InMemoryAuditEventRepository repo = new();
        AuditingMessageHandler handler = new(
            repo, V1ResourceMap.Default, new FakeClock(Now),
            NullLogger<AuditingMessageHandler>.Instance);

        await handler.HandleBatchAsync(typeof(CameraRegisteredV1), [], default);

        repo.SaveAsyncCallCount.ShouldBe(0);
        repo.Committed.ShouldBeEmpty();
    }

    /// <summary>
    /// The batch path must produce the same row as the singular one — it is a
    /// different commit boundary, not a different audit record. A drift here
    /// would give one event kind subtly different rows from every other.
    /// </summary>
    [Fact]
    public async Task A_batched_row_matches_what_the_singular_path_writes()
    {
        CameraRegisteredV1 evt = new(
            Guid.CreateVersion7(), "north-gate", "rtsp://example/cam", Now, Guid.CreateVersion7(), Metadata: TestMetadata);
        V1Envelope envelope = Envelope(evt);

        InMemoryAuditEventRepository singularRepo = new();
        await new AuditingMessageHandler(
            singularRepo, V1ResourceMap.Default, new FakeClock(Now),
            NullLogger<AuditingMessageHandler>.Instance)
            .HandleAsync(typeof(CameraRegisteredV1), evt, envelope, default);

        InMemoryAuditEventRepository batchRepo = new();
        await new AuditingMessageHandler(
            batchRepo, V1ResourceMap.Default, new FakeClock(Now),
            NullLogger<AuditingMessageHandler>.Instance)
            .HandleBatchAsync(typeof(CameraRegisteredV1), [(evt, envelope)], default);

        AuditEvent singular = singularRepo.Committed.ShouldHaveSingleItem();
        AuditEvent batched = batchRepo.Committed.ShouldHaveSingleItem();

        batched.EventKind.Value.ShouldBe(singular.EventKind.Value);
        batched.EventIdentifier.ShouldBe(singular.EventIdentifier);
        batched.OccurredAt.ShouldBe(singular.OccurredAt);
        batched.ResourceKind?.Value.ShouldBe(singular.ResourceKind?.Value);
        batched.ResourceIdentifier?.Value.ShouldBe(singular.ResourceIdentifier?.Value);
        batched.Payload.ShouldBe(singular.Payload);
    }
}
