using System.Diagnostics;
using System.Net.Http.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Latency budget enforcement (constitution §IV + ADR-0031). The command
/// path POST /cameras must stay within 200 ms p95 against the AspireFixture
/// stack. The first request includes JIT + EF first-query overhead; we drop
/// the first N samples as warmup before measuring p95.
/// </summary>
[Collection(AspireCollection.Name)]
public class CommandLatencyTests(AspireFixture aspire) : IAsyncLifetime
{
    private const int SampleCount = 100;
    private const int WarmupCount = 10;
    private const int BudgetMilliseconds = 200;

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task POST_cameras_p95_stays_within_the_command_path_budget()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync("camera-catalog");

        List<double> latencies = new(capacity: SampleCount);

        for (int index = 0; index < SampleCount + WarmupCount; index++)
        {
            Stopwatch sw = Stopwatch.StartNew();
            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/cameras",
                new { name = $"Cam-Latency-{index:D4}", rtspUrl = $"rtsp://10.0.5.{index % 250}/h264" });
            sw.Stop();

            response.EnsureSuccessStatusCode();

            if (index >= WarmupCount)
            {
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }
        }

        latencies.Sort();
        double p95 = latencies[(int)Math.Ceiling(0.95 * latencies.Count) - 1];
        double median = latencies[latencies.Count / 2];

        // Surface the measurement in test output regardless of pass/fail.
        Console.WriteLine($"POST /cameras: n={latencies.Count} median={median:F1}ms p95={p95:F1}ms budget={BudgetMilliseconds}ms");

        p95.ShouldBeLessThan(
            BudgetMilliseconds,
            customMessage: $"POST /cameras p95 was {p95:F1}ms over {latencies.Count} samples; budget is {BudgetMilliseconds}ms.");
    }
}
