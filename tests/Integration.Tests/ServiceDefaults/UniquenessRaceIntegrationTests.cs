using System.Net;
using System.Net.Http.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.ServiceDefaults;

/// <summary>
/// Spec 034 T011 — SC-002.
///
/// <para>
/// This test asserts an <b>invariant</b>, not an occurrence. Concurrent writers
/// asking for the same name must produce exactly one success and never a server
/// fault. Whether the interleaving that reaches
/// <c>UniqueConstraintExceptionHandler</c> actually happens on a given run is
/// deliberately <b>not</b> asserted.
/// </para>
///
/// <para>
/// The spec's "How this is tested" section records why. A test that demanded the
/// race occur would fail intermittently for reasons unrelated to the code, and a
/// flaky test in this repository has already cost a merge. This one can fail to
/// add information — on a run where every writer's application-level check wins
/// cleanly — but it cannot go green while the bug is present, because a 500 is a
/// 500 however the race resolved.
/// </para>
///
/// <para>
/// Do not replace it with a forcing test.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class UniquenessRaceIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MunichOperator = "op-3@munich.test";
    private const string OperatorPassword = "Operator1234";

    /// <summary>
    /// Enough writers that the race is likely without being so many that the
    /// test becomes a load test.
    /// </summary>
    private const int Writers = 12;

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Concurrent_writers_for_one_name_yield_one_success_and_never_a_fault()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);

        string contested = $"race-{Guid.CreateVersion7():N}"[..20];

        IEnumerable<Task<HttpResponseMessage>> attempts = Enumerable
            .Range(0, Writers)
            .Select(_ => cameras.PostAsJsonAsync("/cameras", new
            {
                name = contested,
                rtspUrl = "rtsp://10.0.7.12/h264",
            }));

        HttpResponseMessage[] responses = await Task.WhenAll(attempts);

        try
        {
            HttpStatusCode[] statuses = [.. responses.Select(response => response.StatusCode)];

            // The invariant, and the whole assertion. A caller who lost the race
            // is refused; nobody is told the server broke.
            statuses.ShouldNotContain(
                HttpStatusCode.InternalServerError,
                $"a writer losing the uniqueness race was told the server failed. Statuses: {string.Join(", ", statuses)}");

            statuses.Count(status => status == HttpStatusCode.Created)
                .ShouldBe(1, $"exactly one writer may take the name. Statuses: {string.Join(", ", statuses)}");

            statuses.Where(status => status != HttpStatusCode.Created)
                .ShouldAllBe(status => status == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    private async Task<HttpClient> ClientFor(string username) =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", username, OperatorPassword);
}
