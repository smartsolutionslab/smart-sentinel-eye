using System.Globalization;
using System.Text.Json;

namespace SmartSentinelEye.Shared.Contracts.Tests;

/// <summary>
/// Spec 025 T006 / FR-008 / FR-011. `RootIngestedAt` was added to
/// <see cref="EventMetadata"/> so the `event → overlay state` leg could be
/// measured end to end, and the claim made in the spec was that it is additive
/// in both directions and therefore not breaking under ADR-0073.
///
/// <para>
/// **That claim is demonstrated here rather than reasoned about.** "An optional
/// field is obviously safe" is the kind of statement that is usually true and
/// occasionally expensive, and the cost of being wrong is a versioned duplicate
/// of two contracts discovered after they are in production.
/// </para>
/// </summary>
public class EventMetadataRootIngestedAtTests
{
    private static readonly DateTimeOffset DecisionAt =
        DateTimeOffset.Parse("2026-08-22T09:00:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset Ingested =
        DateTimeOffset.Parse("2026-08-22T08:59:59.200Z", CultureInfo.InvariantCulture);

    /// <summary>
    /// Forward compatibility: a payload written before the field existed still
    /// deserialises, and the absence reads as absence.
    /// </summary>
    [Fact]
    public void Metadata_written_before_the_field_existed_still_deserialises()
    {
        const string beforeTheChange = """
            {
              "EventIdentifier": "00000000-0000-0000-0000-0000000000aa",
              "OccurredAt": "2026-08-22T09:00:00+00:00",
              "Fab": "munich",
              "Actor": null
            }
            """;

        EventMetadata metadata = JsonSerializer.Deserialize<EventMetadata>(beforeTheChange);

        metadata.ShouldNotBeNull();
        metadata.Fab.ShouldBe("munich");
        metadata.RootIngestedAt.ShouldBeNull(
            "a message that predates the field must read as 'no root moment', which "
            + "FR-005 requires to mean 'not measurable' rather than 'instant'");
    }

    /// <summary>
    /// Backward compatibility: a payload carrying the new field is readable by a
    /// consumer that does not know about it. Modelled with a type that has the
    /// old shape, because that is exactly what an un-redeployed service is.
    /// </summary>
    [Fact]
    public void Metadata_carrying_the_field_is_readable_by_a_consumer_that_predates_it()
    {
        EventMetadata current = new(
            Guid.Parse("00000000-0000-0000-0000-0000000000bb"),
            DecisionAt, "munich", null, Ingested);

        string wire = JsonSerializer.Serialize(current);

        MetadataAsItWasBefore old = JsonSerializer.Deserialize<MetadataAsItWasBefore>(wire);

        old.ShouldNotBeNull();
        old.Fab.ShouldBe("munich");
        old.OccurredAt.ShouldBe(DecisionAt);
    }

    /// <summary>
    /// The reason the field exists rather than reusing <c>OccurredAt</c>: the two
    /// mean different things and a downstream event carries both.
    /// </summary>
    [Fact]
    public void The_root_moment_and_the_occurrence_are_not_the_same_field()
    {
        EventMetadata downstream = new(
            Guid.CreateVersion7(), DecisionAt, "munich", null, Ingested);

        downstream.OccurredAt.ShouldBe(
            DecisionAt, "OccurredAt still means when this event's own action happened");
        downstream.RootIngestedAt.ShouldBe(
            Ingested, "RootIngestedAt means when the plant-floor event that caused it was accepted");
        downstream.RootIngestedAt.Value.ShouldBeLessThan(
            downstream.OccurredAt, "the root is upstream in time of the decision it caused");
    }

    /// <summary>
    /// <see cref="EventMetadata"/> as it was before the field — the shape an
    /// un-redeployed consumer still compiles against.
    /// </summary>
    private sealed record MetadataAsItWasBefore(
        Guid EventIdentifier,
        DateTimeOffset OccurredAt,
        string Fab,
        Guid? Actor);
}
