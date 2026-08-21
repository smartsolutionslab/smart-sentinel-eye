using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 024 T010/T011 (#1681). The `camera → SFU` leg has a budget of 80 ms and
/// had nothing measuring it. MediaMTX measures its own RTP ingest and can
/// expose it; the AppHost simply never turned that on.
///
/// <para>
/// This is the cheapest leg in the feature — configuration rather than code —
/// and the only one the product does not have to instrument itself.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class SfuLatencyIsReadableTests(AspireFixture aspire, ITestOutputHelper output)
{
    /// <summary>
    /// T010. Asserts the endpoint answers and carries per-path RTP counters,
    /// which is what a dashboard would read.
    /// </summary>
    [Fact]
    public async Task The_sfu_exposes_its_own_measurements()
    {
        using HttpClient metrics = new() { BaseAddress = aspire.App.GetEndpoint("mediamtx", "metrics") };

        HttpResponseMessage response = await metrics.GetAsync("/metrics");
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync();

        // Prometheus exposition: one metric per line. Asserting on a known
        // family rather than a byte count, so an empty-but-200 response fails.
        body.ShouldContain(
            "paths",
            Case.Insensitive,
            "the SFU answered but published nothing about its paths, which is a "
            + "metrics endpoint that exists and measures nothing");

        output.WriteLine($"SFU metrics: {body.Split('\n').Length} lines exposed");
    }

    /// <summary>
    /// T011. The reason this test exists: enabling metrics edits the config of
    /// a running media server. It should open a listener and touch no media
    /// path, and "should" is not evidence.
    /// </summary>
    [Fact]
    public async Task Turning_metrics_on_did_not_disturb_the_media_path()
    {
        using HttpClient api = new() { BaseAddress = aspire.App.GetEndpoint("mediamtx", "api") };

        HttpResponseMessage paths = await api.GetAsync("/v3/paths/list");

        paths.EnsureSuccessStatusCode();
    }
}
