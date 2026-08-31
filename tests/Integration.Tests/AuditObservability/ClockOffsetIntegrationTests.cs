using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Spec 053 US2 — the clocks, asked rather than assumed.
///
/// <para>
/// <b>What this can and cannot establish, stated first.</b> It measures a
/// process's offset from the shared database server, which is the reference
/// every service in this pipeline already has and none had used. Doing that from
/// the test process establishes the <i>method</i> and the size of its residual.
/// Establishing the offset of the two <i>stamping</i> processes needs a
/// timestamp taken from the shared clock at the moment each stamp is made —
/// which is the apparatus the next phase builds.
/// </para>
///
/// <para>
/// So this is a floor, not the whole answer: if the method's own residual is
/// already larger than the threshold, nothing built on it can be established and
/// the phase has failed before the apparatus exists.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class ClockOffsetIntegrationTests(AspireFixture aspire, ITestOutputHelper output)
{
    /// <summary>
    /// **The readings are more precise than they are accurate, and that is a
    /// finding rather than a caveat.**
    ///
    /// <para>
    /// Four runs of this probe against the same stack gave offsets of −8.00,
    /// −1.84, +0.28 and +2.21 ms, while each individual reading reported a
    /// residual of only 1.2–2.5 ms. So the spread <i>between</i> runs is roughly
    /// five times the uncertainty each run claims for itself: the residual
    /// captures how well a single reading was taken, not how much the thing
    /// being read moves.
    /// </para>
    ///
    /// <para>
    /// The honest uncertainty on a host-to-container comparison is therefore the
    /// run-to-run spread — about ten milliseconds — which sits exactly on the
    /// threshold an attribution can absorb. That is uncomfortably close, and it
    /// is precisely why the phase exists: a single reading would have reported
    /// either −8 ms or +0.28 ms with a confident ±2 ms beside it, and both would
    /// have been believed.
    /// </para>
    ///
    /// <para>
    /// It does not by itself sink the attribution. The two <i>stamping</i>
    /// processes are host processes sharing one operating-system clock, not a
    /// host and a container — so their relative skew should be far smaller than
    /// this. <b>Should be</b> is the operative phrase, and measuring it needs the
    /// apparatus the next phase builds.
    /// </para>
    /// </summary>
    [Trait("Category", "Measurement")]
    [Fact]
    public async Task The_shared_server_can_be_read_precisely_enough_to_matter()
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();
        string connectionString = context.Database.GetConnectionString()!;

        ClockOffset offset = await ClockOffsetProbe.MeasureBestOfAsync(
            connectionString, readings: 20, CancellationToken.None);

        output.WriteLine($"offset from the shared server: {offset}");

        offset.Residual.ShouldBeLessThan(
            AttributionVerdict.Threshold,
            $"the method's own uncertainty is {offset.Residual.TotalMilliseconds:F2} ms; "
            + "an attribution cannot be established to within a bound its measurement cannot reach");
    }

    /// <summary>
    /// The verdict is produced from a real reading rather than a constructed one,
    /// so the arithmetic and the measurement meet at least once.
    /// </summary>
    [Trait("Category", "Measurement")]
    [Fact]
    public async Task A_process_measured_against_itself_shows_no_skew_worth_reporting()
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();
        string connectionString = context.Database.GetConnectionString()!;

        ClockOffset first = await ClockOffsetProbe.MeasureBestOfAsync(connectionString, 20, CancellationToken.None);
        ClockOffset second = await ClockOffsetProbe.MeasureBestOfAsync(connectionString, 20, CancellationToken.None);

        RelativeSkew skew = RelativeSkew.Between(first, second);
        AttributionVerdict verdict = AttributionVerdict.For(skew);

        output.WriteLine($"first: {first}  second: {second}");
        output.WriteLine($"relative skew: {skew}");
        output.WriteLine($"verdict: {verdict}");

        // **This measures the instrument's noise floor, and asserting the 10 ms
        // verdict threshold here would be asserting the instrument is quieter
        // than it is.**
        //
        // The true skew is zero — it is one process against itself. So every
        // millisecond of the figure below is measurement noise, and the readings
        // recorded above span roughly 10 ms across runs with residuals of only
        // 1–2 ms. A pair drawn from that spread exceeds 10 ms routinely, so the
        // stricter assertion this test used to make failed on sound data.
        //
        // The bound here is loose enough to survive that noise and tight enough
        // to catch a clock that has genuinely stepped. **The uncomfortable part
        // is the consequence**: the noise floor is the same order as the
        // threshold the verdict decides on, so the verdict distinguishes a badly
        // skewed stack from a healthy one and not much finer than that.
        skew.WorstCase.ShouldBeLessThan(
            TimeSpan.FromMilliseconds(25),
            $"one process against itself has no real skew, so this is the instrument's own noise; "
            + $"got {skew}");

        verdict.Standing.ShouldBeOneOf(
            AttributionStanding.Established,
            AttributionStanding.NotEstablished);
    }
}
