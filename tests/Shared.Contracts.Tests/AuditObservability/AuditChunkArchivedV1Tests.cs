using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.AuditObservability;

namespace SmartSentinelEye.Shared.Contracts.Tests.AuditObservability;

public class AuditChunkArchivedV1Tests
{
    private static readonly DateTimeOffset From =
        DateTimeOffset.Parse("2026-02-01T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset Until =
        DateTimeOffset.Parse("2026-03-01T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset ArchivedAt =
        DateTimeOffset.Parse("2026-05-29T02:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Exposes_every_field_via_the_positional_constructor()
    {
        Guid chunkId = Guid.CreateVersion7();
        AuditChunkArchivedV1 evt = new(
            chunkId, "munich", 8_640_000, From, Until, ArchivedAt,
            "fab=munich/year=2026/month=02/chunk.ndjson.gz",
            "deadbeefcafef00d1234567890abcdef");

        evt.ChunkIdentifier.ShouldBe(chunkId);
        evt.FabId.ShouldBe("munich");
        evt.RowCount.ShouldBe(8_640_000);
        evt.OccurredFrom.ShouldBe(From);
        evt.OccurredUntil.ShouldBe(Until);
        evt.ArchivedAt.ShouldBe(ArchivedAt);
        evt.MinioObjectKey.ShouldBe("fab=munich/year=2026/month=02/chunk.ndjson.gz");
        evt.ContentMd5.ShouldBe("deadbeefcafef00d1234567890abcdef");
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it() =>
        new AuditChunkArchivedV1(
            Guid.CreateVersion7(), null, 0, From, Until, ArchivedAt, "k", "m")
            .ShouldBeAssignableTo<IIntegrationEvent>();

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid id = Guid.CreateVersion7();
        AuditChunkArchivedV1 a = new(id, "munich", 1, From, Until, ArchivedAt, "k", "m");
        AuditChunkArchivedV1 b = new(id, "munich", 1, From, Until, ArchivedAt, "k", "m");
        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        AuditChunkArchivedV1 original = new(
            Guid.CreateVersion7(), "munich", 42, From, Until, ArchivedAt,
            "fab=munich/year=2026/month=02/chunk.ndjson.gz",
            "deadbeefcafef00d1234567890abcdef");
        string json = JsonSerializer.Serialize(original);
        JsonSerializer.Deserialize<AuditChunkArchivedV1>(json).ShouldBe(original);
    }

    [Fact]
    public void Fab_id_is_nullable_for_cross_fab_chunks()
    {
        AuditChunkArchivedV1 evt = new(
            Guid.CreateVersion7(), null, 0, From, Until, ArchivedAt, "k", "m");
        evt.FabId.ShouldBeNull();
    }
}
