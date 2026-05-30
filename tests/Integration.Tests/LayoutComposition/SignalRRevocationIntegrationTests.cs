using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// Spec 003 T078 — drives the US3 force-disconnect path through the
/// real SignalR hub. Two clients connect, the admin archives a
/// published revision, both clients receive the
/// <c>LayoutRevisionArchived</c> push within 1 s.
/// </summary>
[Collection(AspireCollection.Name)]
public class SignalRRevocationIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const int RevocationBudgetMilliseconds = 1000;

    public async Task InitializeAsync()
    {
        await aspire.ResetLayoutCompositionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Archive_force_disconnects_connected_kiosks_within_one_second()
    {
        using HttpClient admin = await aspire.CreateAdminClientAsync("layout-composition");
        string accessToken = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        // Create + publish a layout so there's something to archive.
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/layouts",
            new { name = $"Live-{Guid.NewGuid():N}".Substring(0, 16), cameraIdentifier = Guid.CreateVersion7() });
        created.EnsureSuccessStatusCode();
        Guid layoutIdentifier = await created.Content.ReadFromJsonAsync<Guid>();
        HttpResponseMessage publish = await admin.PostAsync(
            $"/layouts/{layoutIdentifier}/revisions/1/publish", content: null);
        publish.EnsureSuccessStatusCode();

        Uri hubUri = new(aspire.App.GetEndpoint("layout-composition").ToString().TrimEnd('/') + LayoutLifecycleHub.Path);

        await using HubConnection alpha = BuildClient(hubUri, accessToken);
        await using HubConnection beta = BuildClient(hubUri, accessToken);

        TaskCompletionSource<LayoutRevisionArchivedHubMessage> alphaSeen = new();
        TaskCompletionSource<LayoutRevisionArchivedHubMessage> betaSeen = new();
        alpha.On<JsonElement>(nameof(ILayoutLifecycleClient.LayoutRevisionArchived),
            payload => alphaSeen.TrySetResult(Parse(payload)));
        beta.On<JsonElement>(nameof(ILayoutLifecycleClient.LayoutRevisionArchived),
            payload => betaSeen.TrySetResult(Parse(payload)));

        await alpha.StartAsync();
        await beta.StartAsync();

        Stopwatch sw = Stopwatch.StartNew();
        HttpResponseMessage archived = await admin.PostAsync(
            $"/layouts/{layoutIdentifier}/revisions/1/archive", content: null);
        archived.EnsureSuccessStatusCode();

        using CancellationTokenSource budget = new(TimeSpan.FromSeconds(5));
        LayoutRevisionArchivedHubMessage[] both =
            await Task.WhenAll(alphaSeen.Task.WaitAsync(budget.Token), betaSeen.Task.WaitAsync(budget.Token));
        sw.Stop();

        sw.Elapsed.TotalMilliseconds.ShouldBeLessThan(
            RevocationBudgetMilliseconds,
            $"archive→push took {sw.Elapsed.TotalMilliseconds:F0} ms");

        both[0].Layout.ShouldBe(layoutIdentifier);
        both[0].RevisionNumber.ShouldBe(1);
        both[1].Layout.ShouldBe(layoutIdentifier);
        both[1].RevisionNumber.ShouldBe(1);
    }

    private static HubConnection BuildClient(Uri hubUri, string accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .Build();

    private static LayoutRevisionArchivedHubMessage Parse(JsonElement payload) =>
        new(
            Layout: payload.GetProperty("layout").GetGuid(),
            RevisionNumber: payload.GetProperty("revisionNumber").GetInt32(),
            ArchivedAt: payload.GetProperty("archivedAt").GetDateTimeOffset());
}
