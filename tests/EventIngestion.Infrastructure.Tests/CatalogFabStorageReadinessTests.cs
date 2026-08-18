using System.Collections.Frozen;

using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Spec 019 T019. The cache is only ever allowed to make a <b>positive</b>
/// answer fast; every other path re-reads or throws.
/// </summary>
public class CatalogFabStorageReadinessTests
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Berlin = FabIdentifier.From("berlin");

    [Fact]
    public async Task Reports_a_provisioned_fab_as_ready()
    {
        (CatalogFabStorageReadiness readiness, _) = Readiness(Catalog("munich"));

        (await readiness.IsReadyAsync(Munich, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task Serves_a_repeat_positive_from_cache_without_reading_again()
    {
        (CatalogFabStorageReadiness readiness, Counter reads) = Readiness(Catalog("munich"));

        await readiness.IsReadyAsync(Munich, CancellationToken.None);
        await readiness.IsReadyAsync(Munich, CancellationToken.None);
        await readiness.IsReadyAsync(Munich, CancellationToken.None);

        reads.Count.ShouldBe(1, "the write path must not query the catalog per request");
    }

    /// <summary>
    /// The asymmetry that matters. A fab provisioned a minute ago must not be
    /// refused because the cached snapshot predates it, so a negative is always
    /// re-read before it is believed.
    /// </summary>
    [Fact]
    public async Task Re_reads_before_answering_that_a_fab_is_not_ready()
    {
        HashSet<string> catalog = new(StringComparer.Ordinal) { "munich" };
        (CatalogFabStorageReadiness readiness, Counter reads) = Readiness(catalog);

        (await readiness.IsReadyAsync(Berlin, CancellationToken.None)).ShouldBeFalse();
        int afterFirstMiss = reads.Count;

        // Provisioning runs. No restart, no cache expiry — the next ask must
        // still see it, because the negative path does not trust the snapshot.
        catalog.Add("berlin");

        (await readiness.IsReadyAsync(Berlin, CancellationToken.None)).ShouldBeTrue();
        reads.Count.ShouldBeGreaterThan(afterFirstMiss);
    }

    /// <summary>
    /// A database failure must not be reported as "this fab has no storage".
    /// That would blame a provisioning gap that does not exist and send someone
    /// to look in entirely the wrong place.
    /// </summary>
    [Fact]
    public async Task Surfaces_a_catalog_failure_rather_than_answering_not_ready()
    {
        CatalogFabStorageReadiness readiness = new(
            _ => throw new InvalidOperationException("connection refused"),
            new FrozenClock());

        await Should.ThrowAsync<InvalidOperationException>(
            () => readiness.IsReadyAsync(Munich, CancellationToken.None));
    }

    /// <summary>
    /// The set is read live rather than copied, so a test can provision a fab
    /// mid-run and see whether the readiness check notices.
    /// </summary>
    private static (CatalogFabStorageReadiness, Counter) Readiness(HashSet<string> catalog)
    {
        Counter reads = new();
        CatalogFabStorageReadiness readiness = new(
            _ =>
            {
                reads.Count++;
                return Task.FromResult(catalog.ToFrozenSet(StringComparer.Ordinal));
            },
            new FrozenClock());

        return (readiness, reads);
    }

    private static HashSet<string> Catalog(params string[] fabs) => new(fabs, StringComparer.Ordinal);

    /// <summary>
    /// Time that never moves, so a cached snapshot never expires by age. That
    /// isolates what these tests are about — whether a miss re-reads — from
    /// whether the TTL happened to elapse mid-test.
    /// </summary>
    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UnixEpoch;
    }

    private sealed class Counter
    {
        public int Count { get; set; }
    }
}
