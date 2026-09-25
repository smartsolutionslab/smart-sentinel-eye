using System.Reflection;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
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
    /// Spec 247 / #2469. This is the premise the outbox guard's one exemption
    /// (<c>OutboxCommitTests.PermittedDirectCommits</c>) depends on:
    /// <see cref="StreamFabAttributionService"/> commits its own
    /// <c>DbContext</c> directly, outside <see cref="ITransactionalCommit"/>,
    /// which is only safe because the pass it runs never has an announcement
    /// to lose. <c>Stream.AttributeToFab</c> raises no domain event, and the
    /// service holds no <see cref="IDomainEventDispatcher"/> to drain one
    /// anyway — so if a pending event ever appeared here, committing through
    /// the seam would not save it either, because nothing drains it.
    ///
    /// <para>
    /// <b>If this test ever fails</b> — because <c>AttributeToFab</c> starts
    /// raising an event — the fix is to route the pass through
    /// <see cref="IStreamRepository"/>.SaveAsync (which does drain and
    /// dispatch <see cref="AggregateRoot{TIdentifier}.PendingEvents"/>) and
    /// delete the <c>PermittedDirectCommits</c> entry, not to add more
    /// permitted types or to adjust this assertion.
    /// </para>
    ///
    /// <para>
    /// Both streams are asserted attributed first: an <c>Attribute</c> that
    /// did nothing would also raise nothing, and would pass the "no pending
    /// events" half vacuously.
    /// </para>
    /// </summary>
    [Fact]
    public void The_pass_raises_nothing_a_direct_commit_would_drop()
    {
        StreamAggregate munich = Unattributed();
        StreamAggregate dresden = Unattributed();
        munich.ClearPendingEvents();
        dresden.ClearPendingEvents();

        int attributed = StreamFabAttributionService.Attribute(
            [munich, dresden],
            new Dictionary<Guid, string>
            {
                [munich.Camera.Value] = "munich",
                [dresden.Camera.Value] = "dresden",
            });

        attributed.ShouldBe(2);
        munich.PendingEvents.ShouldBeEmpty();
        dresden.PendingEvents.ShouldBeEmpty();
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
