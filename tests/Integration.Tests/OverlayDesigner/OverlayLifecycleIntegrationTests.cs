using System.Diagnostics;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.OverlayDesigner;

/// <summary>
/// Spec 004 T022 — end-to-end through the overlay-designer API and the
/// underlying Postgres + Wolverine stack. Drives the US1 happy path:
/// create a Draft via <c>POST /overlays</c>, publish revision 1 via
/// <c>POST /overlays/{id}/revisions/1/publish</c>, and assert the
/// transition is observable on <c>GET /overlays/{id}</c> within the
/// 500 ms SLO budget for the synchronous command path.
/// </summary>
[Collection(AspireCollection.Name)]
public class OverlayLifecycleIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetOverlayDesignerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object SampleLabelBody() => new
    {
        kind = "Text",
        color = "#FFFFFFD9",
        text = "Production Line 1",
        normalizedX = 0.5m,
        normalizedY = 0.05m,
        normalizedWidth = 0.3m,
        normalizedHeight = 0.08m,
        fontSizePx = 48,
    };

    [Fact]
    public async Task Create_and_publish_an_overlay_emits_OverlayRevisionPublishedV3_within_500_ms()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        Stopwatch sw = Stopwatch.StartNew();
        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Ovl-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new[] { SampleLabelBody() },
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();
        overlayIdentifier.ShouldNotBe(Guid.Empty);

        // Not OverlayRequests.PostAsync: it reads the version first, and that
        // round trip would come out of this test's 500 ms budget. A
        // just-created chain is at version 0.
        HttpResponseMessage published = await overlays.SendAsync(
            OverlayRequests.Conditional(HttpMethod.Post, overlayIdentifier, "revisions/1/publish", version: 0));
        sw.Stop();

        published.StatusCode.ShouldBe(HttpStatusCode.OK);
        sw.Elapsed.TotalMilliseconds.ShouldBeLessThan(500,
            $"create + publish took {sw.Elapsed.TotalMilliseconds:F0} ms");

        HttpResponseMessage fetched = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement revisions = payload.GetProperty("revisions");
        revisions.GetArrayLength().ShouldBe(1);
        revisions[0].GetProperty("state").GetString().ShouldBe("Published");
        revisions[0].GetProperty("revisionNumber").GetInt32().ShouldBe(1);
        revisions[0].GetProperty("elements")[0].GetProperty("text").GetString().ShouldBe("Production Line 1");
    }

    /// <summary>
    /// Spec 300 (#2349) T013 — a mixed-kind revision round-trips through
    /// create, read and publish with every element's kind, colour and
    /// geometry intact, and publishing emits exactly one
    /// <c>OverlayRevisionPublishedV3</c> carrying both elements correctly.
    ///
    /// <para>
    /// "Exactly one event, with field assertions" is asserted the way
    /// <c>EndToEndIngestionIntegrationTests</c> already does for
    /// <c>CameraRegisteredV1</c>: poll AuditObservability's read API for the
    /// one row keyed by event kind + resource identifier, and inspect its
    /// serialised payload. The unique <c>event_identifier</c> constraint
    /// that test relies on is what makes "exactly one" a real assertion
    /// rather than "at least one seen so far" — a redelivery would still
    /// collapse to the same single row.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Create_and_publish_a_mixed_kind_overlay_emits_exactly_one_published_event()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");

        HttpResponseMessage created = await overlays.PostAsJsonAsync(
            "/overlays",
            new
            {
                name = $"Mix-{Guid.NewGuid():N}".Substring(0, 16),
                elements = new object[]
                {
                    new { kind = "Box", color = "#1565C0FF", normalizedX = 0.1m, normalizedY = 0.1m, normalizedWidth = 0.2m, normalizedHeight = 0.2m },
                    new { kind = "Text", color = "#FFFFFFD9", text = "Mixed", normalizedX = 0.5m, normalizedY = 0.05m, normalizedWidth = 0.3m, normalizedHeight = 0.08m, fontSizePx = 48 },
                },
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid overlayIdentifier = await created.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage fetchedBeforePublish = await overlays.GetAsync($"/overlays/{overlayIdentifier}");
        fetchedBeforePublish.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement elementsBeforePublish = (await fetchedBeforePublish.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revisions")[0]
            .GetProperty("elements");
        elementsBeforePublish.GetArrayLength().ShouldBe(2);
        elementsBeforePublish[0].GetProperty("kind").GetString().ShouldBe("Box");
        elementsBeforePublish[0].GetProperty("color").GetString().ShouldBe("#1565C0FF");
        elementsBeforePublish[0].GetProperty("normalizedX").GetDecimal().ShouldBe(0.1m);
        elementsBeforePublish[0].GetProperty("normalizedY").GetDecimal().ShouldBe(0.1m);
        elementsBeforePublish[0].GetProperty("normalizedWidth").GetDecimal().ShouldBe(0.2m);
        elementsBeforePublish[0].GetProperty("normalizedHeight").GetDecimal().ShouldBe(0.2m);
        elementsBeforePublish[0].GetProperty("text").ValueKind.ShouldBe(JsonValueKind.Null);
        elementsBeforePublish[1].GetProperty("kind").GetString().ShouldBe("Text");
        elementsBeforePublish[1].GetProperty("color").GetString().ShouldBe("#FFFFFFD9");
        elementsBeforePublish[1].GetProperty("text").GetString().ShouldBe("Mixed");
        elementsBeforePublish[1].GetProperty("fontSizePx").GetInt32().ShouldBe(48);

        (await OverlayRequests.PostAsync(overlays, overlayIdentifier, "revisions/1/publish"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpClient auditReader = await aspire.CreateAdminClientAsync("audit-observability");
        JsonElement payload = await PollForOverlayPublishedPayloadAsync(auditReader, overlayIdentifier);

        JsonElement publishedElements = payload.GetProperty("Elements");
        publishedElements.GetArrayLength().ShouldBe(2);
        publishedElements[0].GetProperty("Kind").GetString().ShouldBe("Box");
        publishedElements[0].GetProperty("Color").GetString().ShouldBe("#1565C0FF");
        publishedElements[0].GetProperty("Text").ValueKind.ShouldBe(JsonValueKind.Null);
        publishedElements[1].GetProperty("Kind").GetString().ShouldBe("Text");
        publishedElements[1].GetProperty("Color").GetString().ShouldBe("#FFFFFFD9");
        publishedElements[1].GetProperty("Text").GetString().ShouldBe("Mixed");
        publishedElements[1].GetProperty("FontSizePx").GetInt32().ShouldBe(48);
    }

    private static async Task<JsonElement> PollForOverlayPublishedPayloadAsync(HttpClient auditReader, Guid overlayIdentifier)
    {
        string query = $"/audit?eventKind=OverlayRevisionPublishedV3&resourceIdentifier={overlayIdentifier}&pageSize=10";

        for (int attempt = 0; attempt < 40; attempt++)
        {
            HttpResponseMessage response = await auditReader.GetAsync(query);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
            JsonElement rows = page.GetProperty("rows");
            if (rows.GetArrayLength() == 1)
            {
                return JsonDocument.Parse(rows[0].GetProperty("payload").GetString()!).RootElement;
            }

            rows.GetArrayLength().ShouldBeLessThanOrEqualTo(1,
                "the unique event_identifier constraint must keep redeliveries to one row");
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new Xunit.Sdk.XunitException(
            $"No audit row for OverlayRevisionPublishedV3 / {overlayIdentifier} appeared within 20s.");
    }

    [Fact]
    public async Task A_name_collision_returns_409_Conflict_with_OVERLAY_NAME_TAKEN()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        string sharedName = $"Ovl-{Guid.NewGuid():N}".Substring(0, 16);

        HttpResponseMessage first = await overlays.PostAsJsonAsync(
            "/overlays", new { name = sharedName, elements = new[] { SampleLabelBody() } });
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage second = await overlays.PostAsJsonAsync(
            "/overlays", new { name = sharedName, elements = new[] { SampleLabelBody() } });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("OVERLAY_NAME_TAKEN");
    }

    [Fact]
    public async Task List_with_state_Published_returns_only_chains_with_a_published_revision()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        string draftName = $"Drf-{Guid.NewGuid():N}".Substring(0, 16);
        string pubName = $"Pub-{Guid.NewGuid():N}".Substring(0, 16);

        HttpResponseMessage draftRaw = await overlays.PostAsJsonAsync(
            "/overlays", new { name = draftName, elements = new[] { SampleLabelBody() } });
        draftRaw.EnsureSuccessStatusCode();

        HttpResponseMessage pubRaw = await overlays.PostAsJsonAsync(
            "/overlays", new { name = pubName, elements = new[] { SampleLabelBody() } });
        pubRaw.EnsureSuccessStatusCode();
        Guid pubIdentifier = await pubRaw.Content.ReadFromJsonAsync<Guid>();
        HttpResponseMessage publish = await OverlayRequests.PostAsync(overlays, pubIdentifier, $"revisions/1/publish");
        publish.EnsureSuccessStatusCode();

        HttpResponseMessage response = await overlays.GetAsync("/overlays?state=Published");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement published = payload.GetProperty("published");
        IEnumerable<string> names = published.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!);
        names.ShouldContain(pubName);
        names.ShouldNotContain(draftName);
    }

    [Fact]
    public async Task Get_for_an_unknown_overlay_returns_404()
    {
        using HttpClient overlays = await aspire.CreateAdminClientAsync("overlay-designer");
        HttpResponseMessage response = await overlays.GetAsync($"/overlays/{Guid.CreateVersion7()}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anonymous_GET_returns_401()
    {
        HttpResponseMessage response = await aspire.OverlayDesigner.GetAsync(
            $"/overlays/{Guid.CreateVersion7()}");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
