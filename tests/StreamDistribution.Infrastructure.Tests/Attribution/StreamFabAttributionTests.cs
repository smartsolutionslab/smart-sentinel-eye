using System.Reflection;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;
using SmartSentinelEye.StreamDistribution.Infrastructure.Attribution;
using StreamAggregate = SmartSentinelEye.StreamDistribution.Domain.Stream.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Attribution;

/// <summary>
/// Spec 016 T024 — the matching step of the startup attribution pass
/// (FR-008, FR-010).
///
/// <para>
/// The database half is left to the integration suite (ADR-0103: no
/// in-memory provider, no Testcontainers). What is worth isolating here is
/// the decision the pass makes per stream, and above all the one it refuses
/// to make: a stream whose camera cannot be resolved keeps its null fab.
/// </para>
/// </summary>
public class StreamFabAttributionTests
{
    [Fact]
    public void A_stream_takes_the_fab_of_its_own_camera()
    {
        StreamAggregate munich = Unattributed();
        StreamAggregate dresden = Unattributed();

        int attributed = StreamFabAttributionService.Attribute(
            [munich, dresden],
            new Dictionary<Guid, string>
            {
                [munich.Camera.Value] = "munich",
                [dresden.Camera.Value] = "dresden",
            });

        attributed.ShouldBe(2);
        munich.Fab.ShouldBe(FabIdentifier.From("munich"));
        // dresden, not munich: a pass that filled everything from the first
        // entry, or from a default, would pass the munich assertion alone.
        dresden.Fab.ShouldBe(FabIdentifier.From("dresden"));
    }

    /// <summary>
    /// FR-010. The stream stays unattributed and is counted as unresolved —
    /// never defaulted to whichever fab happened to be in the map.
    /// </summary>
    [Fact]
    public void A_stream_whose_camera_cannot_be_resolved_stays_unattributed()
    {
        StreamAggregate known = Unattributed();
        StreamAggregate orphan = Unattributed();

        int attributed = StreamFabAttributionService.Attribute(
            [known, orphan],
            new Dictionary<Guid, string> { [known.Camera.Value] = "munich" });

        attributed.ShouldBe(1);
        orphan.Fab.ShouldBeNull();
    }

    [Fact]
    public void Nothing_to_attribute_attributes_nothing()
    {
        StreamFabAttributionService.Attribute(
            [],
            new Dictionary<Guid, string> { [Guid.CreateVersion7()] = "munich" })
            .ShouldBe(0);
    }

    /// <summary>
    /// A stream with no fab cannot be built through <c>Provision</c>, which
    /// requires one — by design, so no placeholder is ever written. The state
    /// exists only in rows that predate the column, so the test reaches it the
    /// same way EF does: by writing the property directly on a materialised
    /// aggregate.
    /// </summary>
    private static StreamAggregate Unattributed()
    {
        StreamAggregate stream = new StreamBuilder().Build();

        typeof(StreamAggregate)
            .GetProperty(nameof(StreamAggregate.Fab), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(stream, null);

        return stream;
    }
}
