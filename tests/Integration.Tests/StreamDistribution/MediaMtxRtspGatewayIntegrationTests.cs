using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 309 FR-002, against real MediaMTX: <c>AddPathAsync</c> must be
/// idempotent on the path name — a path MediaMTX already has is success, so
/// a redelivery or a lost response's retry does not throw. Only the
/// "already exists" branch of a 400 is swallowed; a 400 for any other
/// reason still throws, which is what the second fact proves (memory:
/// prove a guard by counterfactual).
/// </summary>
[Collection(AspireCollection.Name)]
public class MediaMtxRtspGatewayIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Adding_a_path_that_already_exists_succeeds()
    {
        MediaMtxRtspGateway gateway = NewGateway();
        MediaMtxPath path = MediaMtxPath.For(CameraIdentifier.From(Guid.CreateVersion7()));
        const string source = "rtsp://10.0.9.1/h264";

        await gateway.AddPathAsync(path, source, CancellationToken.None);

        await Should.NotThrowAsync(() => gateway.AddPathAsync(path, source, CancellationToken.None));

        IReadOnlyList<string> names = await ListMediaMtxPathNamesAsync();
        names.Count(name => name == path.Value).ShouldBe(1);
    }

    [Fact]
    public async Task Adding_a_path_MediaMTX_rejects_for_another_reason_still_throws()
    {
        MediaMtxRtspGateway gateway = NewGateway();
        MediaMtxPath path = MediaMtxPath.For(CameraIdentifier.From(Guid.CreateVersion7()));

        // A source MediaMTX cannot parse as an RTSP URL — a 400 that is not
        // "the path already exists" (it doesn't). FR-002 must not swallow
        // this one: it is the counterfactual proving the fix is scoped to
        // the conflict case, not to every 400 this endpoint can answer.
        await Should.ThrowAsync<HttpRequestException>(
            () => gateway.AddPathAsync(path, "not-a-source", CancellationToken.None));

        IReadOnlyList<string> names = await ListMediaMtxPathNamesAsync();
        names.ShouldNotContain(path.Value);
    }

    private MediaMtxRtspGateway NewGateway() =>
        new(aspire.App.CreateHttpClient("mediamtx", "api"), NullLogger<MediaMtxRtspGateway>.Instance);

    private async Task<IReadOnlyList<string>> ListMediaMtxPathNamesAsync()
    {
        using HttpClient mediaMtx = aspire.App.CreateHttpClient("mediamtx", "api");
        HttpResponseMessage response = await mediaMtx.GetAsync("/v3/config/paths/list");
        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (!payload.TryGetProperty("items", out JsonElement items))
        {
            return Array.Empty<string>();
        }

        return items.EnumerateArray()
            .Select(item => item.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? string.Empty : string.Empty)
            .Where(name => name.Length > 0)
            .ToArray();
    }
}
