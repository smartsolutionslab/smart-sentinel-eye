using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Spec 403 (#3008), adversarial pass on <c>Stream.RepointTo</c>.
///
/// <para>
/// The fuzz facts drive thousands of seeded sequences through the aggregate and compare it, step by step, with
/// a reference model written from spec 403 FR-004 rather than from the implementation: versioned against
/// versioned compares the version (equal applies), versioned over a version-less record applies, version-less
/// is ordered by <c>ChangedAt</c> (equal and null-recorded apply), and an apply records all three fields with a
/// version-less apply clearing the version. Seeds are fixed so a failure names its sequence.
/// </para>
///
/// <para>
/// The <c>Residual_</c> facts are green on purpose: they pin sequences in which the stream ends on a change the
/// catalogue has already superseded, under the rule the spec decided (plan §2.3). They exist so the residuals
/// are executable rather than prose, and so a change to the rule shows up as a changed outcome here.
/// </para>
/// </summary>
public class StreamRepointVersionOrderingAdversarialTests
{
    private static readonly DateTimeOffset Moment = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private const string OriginalUrl = "rtsp://camera-sim:8554/original";

    private const int Sequences = 4000;

    private sealed record Change(string Url, DateTimeOffset ChangedAt, int? Version);

    private sealed record Recorded(string Url, DateTimeOffset? ChangedAt, int? Version);

    private static (bool Applied, Recorded Next) Model(Recorded recorded, Change incoming)
    {
        bool older;
        if (incoming.Version is { } version)
        {
            older = recorded.Version is { } recordedVersion && version < recordedVersion;
        }
        else
        {
            older = recorded.ChangedAt is { } recordedAt && incoming.ChangedAt < recordedAt;
        }

        return older
            ? (false, recorded)
            : (true, new Recorded(incoming.Url, incoming.ChangedAt, incoming.Version));
    }

    [Fact]
    public void Versioned_only_sequences_with_arbitrary_stamps_and_redeliveries_end_on_the_highest_version()
    {
        for (var seed = 0; seed < Sequences; seed++)
        {
            var random = new Random(seed);
            var changes = CatalogueOfVersionedChanges(random);
            var arrivals = ArrivalsWithRedeliveries(random, changes);
            var stream = NewStream();
            int? highestSeen = null;

            foreach (var change in arrivals)
            {
                var before = stream.SourceCameraVersion?.Value;
                var applied = Apply(stream, change);

                applied.ShouldBe(before is null || change.Version >= before, $"seed {seed}: apply decision for {change}");
                stream.SourceCameraVersion.ShouldNotBeNull($"seed {seed}");
                if (before is { } previous)
                {
                    stream.SourceCameraVersion.Value.ShouldBeGreaterThanOrEqualTo(previous, $"seed {seed}: version went down");
                }

                highestSeen = highestSeen is null ? change.Version : Math.Max(highestSeen.Value, change.Version!.Value);
            }

            var latest = changes.Single(change => change.Version == highestSeen);
            stream.SourceUrl.Value.ShouldBe(latest.Url, $"seed {seed}: arrivals {Describe(arrivals)}");
            stream.SourceCameraVersion!.Value.ShouldBe(latest.Version!.Value, $"seed {seed}");
            stream.SourceChangedAt!.Value.ShouldBe(latest.ChangedAt, $"seed {seed}");
        }
    }

    [Fact]
    public void Mixed_sequences_of_versioned_and_version_less_changes_follow_the_spec_rule_at_every_step()
    {
        for (var seed = 0; seed < Sequences; seed++)
        {
            var random = new Random(10_000 + seed);
            var stream = NewStream();
            Recorded recorded = new(OriginalUrl, null, null);
            var length = random.Next(1, 14);

            for (var step = 0; step < length; step++)
            {
                var incoming = RandomChange(random, step);
                var (expectedApplied, expected) = Model(recorded, incoming);

                var applied = Apply(stream, incoming);

                var where = $"seed {10_000 + seed}, step {step}, incoming {incoming}, recorded {recorded}";
                applied.ShouldBe(expectedApplied, where);
                stream.SourceUrl.Value.ShouldBe(expected.Url, where);
                var recordedAt = stream.SourceChangedAt?.Value;
                var recordedVersion = stream.SourceCameraVersion?.Value;
                recordedAt.ShouldBe(expected.ChangedAt, where);
                recordedVersion.ShouldBe(expected.Version, where);

                if (recorded.Version is { } before && (recordedVersion ?? -1) < before)
                {
                    incoming.Version.ShouldBeNull($"{where}: only a version-less apply may lower or clear the version");
                }

                recorded = expected;
            }
        }
    }

    [Fact]
    public void Version_zero_and_int_max_are_ordered_without_overflow()
    {
        var stream = NewStream();

        Apply(stream, new Change("rtsp://camera-sim:8554/max", Moment, int.MaxValue)).ShouldBeTrue();
        Apply(stream, new Change("rtsp://camera-sim:8554/zero", Moment.AddDays(1), 0)).ShouldBeFalse();
        Apply(stream, new Change("rtsp://camera-sim:8554/max-again", Moment.AddDays(-1), int.MaxValue)).ShouldBeTrue();

        stream.SourceUrl.Value.ShouldBe("rtsp://camera-sim:8554/max-again");
        stream.SourceCameraVersion.ShouldBe(SourceCameraVersion.From(int.MaxValue));
    }

    [Fact]
    public void Extreme_stamps_do_not_matter_once_both_sides_are_versioned()
    {
        var stream = NewStream();

        Apply(stream, new Change("rtsp://camera-sim:8554/a", DateTimeOffset.MaxValue, 1)).ShouldBeTrue();
        Apply(stream, new Change("rtsp://camera-sim:8554/b", DateTimeOffset.MinValue, 2)).ShouldBeTrue();
        Apply(stream, new Change("rtsp://camera-sim:8554/a", DateTimeOffset.MaxValue, 1)).ShouldBeFalse();

        stream.SourceUrl.Value.ShouldBe("rtsp://camera-sim:8554/b");
        stream.SourceChangedAt.ShouldBe(SourceChangedAt.From(DateTimeOffset.MinValue));
    }

    [Fact]
    public void An_ignored_change_leaves_every_recorded_field_and_raises_nothing()
    {
        var stream = NewStream();
        Apply(stream, new Change("rtsp://camera-sim:8554/b", Moment, 4));
        stream.ClearPendingEvents();

        Apply(stream, new Change("rtsp://camera-sim:8554/a", Moment.AddHours(1), 3)).ShouldBeFalse();
        Apply(stream, new Change("rtsp://camera-sim:8554/c", Moment.AddHours(-1), null)).ShouldBeFalse();

        stream.SourceUrl.Value.ShouldBe("rtsp://camera-sim:8554/b");
        stream.SourceChangedAt.ShouldBe(SourceChangedAt.From(Moment));
        stream.SourceCameraVersion.ShouldBe(SourceCameraVersion.From(4));
        stream.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Stamps_that_name_the_same_instant_in_different_offsets_are_equal_and_apply()
    {
        var stream = NewStream();
        Apply(stream, new Change("rtsp://camera-sim:8554/a", Moment, null));

        var sameInstantElsewhere = Moment.ToOffset(TimeSpan.FromHours(2));

        Apply(stream, new Change("rtsp://camera-sim:8554/b", sameInstantElsewhere, null)).ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe("rtsp://camera-sim:8554/b");
    }

    /// <summary>
    /// Residual, the downgrade (spec 403 §8 R-V2, plan §2.3 "null on a version-less apply"). Catalogue order:
    /// A made against v3, B against v4, then C by a CameraCatalog replica still on the old code (no version).
    /// Arrival: B, C, then a redelivery of A. C clears the recorded version, so A's redelivery meets a record
    /// without a version and applies under rule 2: the stream ends on A, two changes behind the catalogue.
    /// </summary>
    [Fact]
    public void Residual_a_version_less_apply_clears_the_version_and_lets_an_older_versioned_redelivery_win()
    {
        var stream = NewStream();

        Apply(stream, new Change("rtsp://camera-sim:8554/b", Moment, 4)).ShouldBeTrue();
        Apply(stream, new Change("rtsp://camera-sim:8554/c", Moment.AddSeconds(5), null)).ShouldBeTrue();
        stream.SourceCameraVersion.ShouldBeNull();

        var staleApplied = Apply(stream, new Change("rtsp://camera-sim:8554/a", Moment.AddSeconds(-30), 3));

        staleApplied.ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe("rtsp://camera-sim:8554/a");
        stream.SourceCameraVersion.ShouldBe(SourceCameraVersion.From(3));
    }

    /// <summary>
    /// Residual, a late version-less message after a clock correction, with no replica overlap at all. Catalogue
    /// order: L made by the old CameraCatalog while its clock ran ten minutes fast (stamped 09:10), deploy, clock
    /// corrected, V made against v7 (stamped 09:05). L is delivered after V — the outbox backlog drained late, or
    /// L was dead-lettered (Wolverine moves a failed message to the error queue on its first failure,
    /// <c>CameraEventFailurePolicyCharacterisationTests</c>) and replayed. Rule 3 compares L's stamp with V's,
    /// L wins, and the record loses V's version. This is #3008's own defect, reached through the legacy path, and
    /// R-V2 as written ("replica overlap within seconds") does not describe it.
    /// </summary>
    [Fact]
    public void Residual_a_late_version_less_change_stamped_by_a_fast_clock_overrides_a_newer_versioned_change()
    {
        var stream = NewStream();

        Apply(stream, new Change("rtsp://camera-sim:8554/v", Moment.AddMinutes(5), 7)).ShouldBeTrue();

        var lateLegacyApplied = Apply(stream, new Change("rtsp://camera-sim:8554/l", Moment.AddMinutes(10), null));

        lateLegacyApplied.ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe("rtsp://camera-sim:8554/l");
        stream.SourceCameraVersion.ShouldBeNull();
    }

    /// <summary>
    /// Residual, equal stamps across the deploy boundary: the last version-less change and the first versioned
    /// one carry the same instant. A redelivery of the version-less one applies (rule 3: equal applies) and puts
    /// the stream back on the older address.
    /// </summary>
    [Fact]
    public void Residual_a_version_less_redelivery_with_the_same_stamp_as_the_first_versioned_change_reapplies_the_older_address()
    {
        var stream = NewStream();

        Apply(stream, new Change("rtsp://camera-sim:8554/l", Moment, null)).ShouldBeTrue();
        Apply(stream, new Change("rtsp://camera-sim:8554/v", Moment, 0)).ShouldBeTrue();

        Apply(stream, new Change("rtsp://camera-sim:8554/l", Moment, null)).ShouldBeTrue();

        stream.SourceUrl.Value.ShouldBe("rtsp://camera-sim:8554/l");
    }

    /// <summary>
    /// Residual, R-V3 is narrower than it reads. A version-less message stamped <c>MaxValue</c> pins against later
    /// version-less changes only, as R-V3 says, but it also applies over a record that a versioned change wrote.
    /// The next genuine versioned change does clear it.
    /// </summary>
    [Fact]
    public void Residual_a_far_future_version_less_stamp_overrides_a_versioned_record_until_the_next_versioned_change()
    {
        var stream = NewStream();
        Apply(stream, new Change("rtsp://camera-sim:8554/v", Moment, 9)).ShouldBeTrue();

        Apply(stream, new Change("rtsp://camera-sim:8554/far", DateTimeOffset.MaxValue, null)).ShouldBeTrue();
        Apply(stream, new Change("rtsp://camera-sim:8554/legacy-later", Moment.AddDays(1), null)).ShouldBeFalse();
        stream.SourceUrl.Value.ShouldBe("rtsp://camera-sim:8554/far");

        Apply(stream, new Change("rtsp://camera-sim:8554/next", Moment.AddDays(1), 10)).ShouldBeTrue();
        stream.SourceUrl.Value.ShouldBe("rtsp://camera-sim:8554/next");
    }

    private static List<Change> CatalogueOfVersionedChanges(Random random)
    {
        var count = random.Next(1, 7);
        var version = random.Next(0, 3);
        List<Change> changes = [];

        for (var index = 0; index < count; index++)
        {
            changes.Add(new Change($"rtsp://camera-sim:8554/v{version}", RandomStamp(random), version));
            version += random.Next(1, 4);
        }

        return changes;
    }

    private static List<Change> ArrivalsWithRedeliveries(Random random, List<Change> changes)
    {
        List<Change> arrivals = [.. changes];
        var redeliveries = random.Next(0, 6);

        for (var index = 0; index < redeliveries; index++)
        {
            arrivals.Add(changes[random.Next(changes.Count)]);
        }

        return [.. arrivals.OrderBy(_ => random.Next())];
    }

    private static Change RandomChange(Random random, int step)
    {
        var version = random.Next(4) == 0 ? (int?)null : random.Next(0, 6);
        var url = $"rtsp://camera-sim:8554/s{step}";
        return new Change(url, RandomStamp(random), version);
    }

    private static DateTimeOffset RandomStamp(Random random) => random.Next(12) switch
    {
        0 => DateTimeOffset.MinValue,
        1 => DateTimeOffset.MaxValue,
        _ => Moment.AddMinutes(random.Next(-6, 7)),
    };

    private static bool Apply(Domain.Stream.Stream stream, Change change) =>
        stream.RepointTo(
            StreamSourceUrl.From(change.Url),
            SourceChangedAt.From(change.ChangedAt),
            change.Version is { } version
                ? Option<SourceCameraVersion>.Some(SourceCameraVersion.From(version))
                : Option<SourceCameraVersion>.None,
            new FixedClock(Moment));

    private static string Describe(IEnumerable<Change> arrivals) =>
        string.Join(" ; ", arrivals.Select(change => $"v{change.Version}@{change.ChangedAt:O}"));

    private static Domain.Stream.Stream NewStream() =>
        new StreamBuilder().WithSourceUrl(StreamSourceUrl.From(OriginalUrl)).Build();

    private sealed class FixedClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
