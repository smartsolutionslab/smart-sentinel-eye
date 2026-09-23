using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 223 (#2441) — the seam at <c>EventsEndpoints.Writes.cs:344-351</c> that
/// turns a refused lease into a 429 is covered at unit level (spec 104's
/// <c>IngestWriteLimiterTests</c>) but unreachable from outside the process
/// until the fixture pins <c>EventIngestion:IngestWrite:Concurrency</c> to 1
/// (the AppHost's <c>isE2ETests</c> override, spec §5.1). This is not a load
/// test and is not formally deterministic (spec §7): eight requests dispatched
/// through one <c>Task.WhenAll</c> overlap overwhelmingly at concurrency 1, but
/// not by guarantee. If it ever flakes, raise the request count — never loop
/// until a 429 appears, which would turn a real regression (the limiter no
/// longer shedding) into a slow pass.
/// </summary>
[Collection(AspireCollection.Name)]
public class IngestBackpressureIntegrationTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string Operator = "op-hamburg@hamburg.test";
    private const string OperatorPassword = "Operator1234";

    /// <summary>
    /// AS-1. Both clauses are load-bearing (spec §2.1): at least one 429 proves
    /// the limiter sheds, and at least one 201 proves it is a limiter rather
    /// than a wall stuck refusing everything — precisely the shape a
    /// <c>concurrency: 0</c> misconfiguration would produce, invisible from
    /// outside the process anywhere else.
    /// </summary>
    [Fact]
    public async Task A_burst_past_the_write_limit_is_shed_with_the_documented_problem_title()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", Operator, OperatorPassword);

        string kind = $"Backpressure{Guid.CreateVersion7():N}"[..20];

        Task<HttpResponseMessage>[] posts =
        [
            .. Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync("/events/manual", new
            {
                deviceId = "backpressure-device",
                kind,
                occurredAt = DateTimeOffset.UtcNow,
                payload = new { note = "spec 223 T001" },
            })),
        ];

        HttpResponseMessage[] responses = await Task.WhenAll(posts);

        string distribution = DescribeDistribution(responses);
        output.WriteLine($"POST /events/manual x8 (concurrency 1) -> {distribution}");

        string diagnostics = $"observed {distribution}. Recent event-ingestion logs:\n{aspire.RecentLogs("event-ingestion")}";

        HttpResponseMessage? refused = responses.FirstOrDefault(response => response.StatusCode == HttpStatusCode.TooManyRequests);
        refused.ShouldNotBeNull($"expected at least one 429; {diagnostics}");

        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe(
            "EVENT_INGEST_BACKPRESSURE",
            $"the 429's problem title did not match the documented contract; {diagnostics}");

        responses.ShouldContain(
            response => response.StatusCode == HttpStatusCode.Created,
            $"expected at least one 201; a limiter refusing every request is not a limiter; {diagnostics}");
    }

    private static string DescribeDistribution(IReadOnlyCollection<HttpResponseMessage> responses)
    {
        return string.Join(", ", responses
            .GroupBy(response => (int)response.StatusCode)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key}x{group.Count()}"));
    }
}
