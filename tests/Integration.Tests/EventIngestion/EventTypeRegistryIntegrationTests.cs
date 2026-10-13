using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Phase 4a (spec 143 T003e) — the registry's HTTP surface, against the real
/// stack. Every case here is red on arrival for the same reason: the
/// <c>/event-types</c> group is not mapped yet, so every request answers
/// <c>404 Not Found</c> regardless of what the test expects — except
/// <see cref="An_unknown_kind_is_still_ingested"/>, which exercises the
/// existing, untouched <c>/events/manual</c> endpoint and is documented
/// green on arrival (spec.md §4's quiet case, FR-013).
///
/// <para>
/// Run-unique kinds built from <c>Guid.NewGuid():N</c> — the database is
/// shared across runs (tests/Integration.Tests house convention).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class EventTypeRegistryIntegrationTests(AspireFixture aspire)
{
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string DresdenOperatorPassword = SeededCredentials.OpDresden;
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string MultiFabOperatorPassword = SeededCredentials.OpMulti;

    [Fact]
    public async Task A_registered_event_type_is_listed_back()
    {
        var kind = UniqueKind();
        using var dresden = await ClientFor(DresdenOperator);

        var created = await RegisterAsync(dresden, kind);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));
        created.Headers.Location?.ToString().ShouldBe($"/event-types/{kind}");

        var listed = await ListAsync(dresden);
        var row = listed.EnumerateArray().Single(r => r.GetProperty("kind").GetString() == kind);
        row.GetProperty("fab").GetString().ShouldBe("dresden");
        row.GetProperty("state").GetString().ShouldBe("Registered");
    }

    [Fact]
    public async Task The_same_kind_twice_in_one_fab_is_refused_with_409()
    {
        var kind = UniqueKind();
        using var dresden = await ClientFor(DresdenOperator);
        (await RegisterAsync(dresden, kind)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var refused = await RegisterAsync(dresden, kind);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await BodyAsync(refused));
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("EVENT_TYPE_ALREADY_REGISTERED");

        var listed = await ListAsync(dresden);
        listed.EnumerateArray().Count(r => r.GetProperty("kind").GetString() == kind).ShouldBe(1);
    }

    [Fact]
    public async Task The_same_kind_in_two_fabs_is_two_entries()
    {
        var kind = UniqueKind();
        using var multi = await ClientFor(MultiFabOperator);

        (await RegisterAsync(multi, kind, fabId: "dresden")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await RegisterAsync(multi, kind, fabId: "munich")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var listed = await ListAsync(multi);
        listed.EnumerateArray().Count(r => r.GetProperty("kind").GetString() == kind).ShouldBe(2);
    }

    [Fact]
    public async Task A_malformed_kind_is_refused_with_400()
    {
        using var dresden = await ClientFor(DresdenOperator);

        var badBody = await dresden.PostAsJsonAsync(
            "/event-types", new { kind = "person in restricted zone" });
        badBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(badBody));
        (await badBody.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("title").GetString().ShouldBe("EVENT_TYPE_INVALID_INPUT");

        var badRoute = await SendDeleteAsync(dresden, "not%20a%20kind", expectedVersion: 0);
        badRoute.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(badRoute));
        (await badRoute.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("title").GetString().ShouldBe("EVENT_TYPE_INVALID_INPUT");
    }

    [Fact]
    public async Task A_multi_fab_caller_that_names_no_fab_is_refused_with_400()
    {
        using var multi = await ClientFor(MultiFabOperator);

        var refused = await RegisterAsync(multi, UniqueKind());

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("title").GetString().ShouldBe("EVENT_FAB_REQUIRED");
    }

    /// <summary>
    /// A refusal must be shown to have written nothing, not merely to have
    /// answered — the other fab's list is asserted unchanged.
    /// </summary>
    [Fact]
    public async Task Naming_a_fab_the_caller_does_not_hold_registers_nothing()
    {
        var kind = UniqueKind();
        using var dresden = await ClientFor(DresdenOperator);

        var refused = await RegisterAsync(dresden, kind, fabId: "munich");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(refused));

        using var multi = await ClientFor(MultiFabOperator);
        var listed = await ListAsync(multi);
        listed.EnumerateArray().Count(r => r.GetProperty("kind").GetString() == kind).ShouldBe(0);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_with_401()
    {
        using var anonymous = aspire.CreateServiceClient("event-ingestion");

        var refused = await anonymous.PostAsJsonAsync(
            "/event-types", new { kind = UniqueKind() });

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // FR-010's whole reason for existing -- a caller holding the general
    // ingest-write scope but not the registry-write scope must not be able to
    // declare which event types are legitimate -- moved to
    // EventTypeRegistryAuthorizationIntegrationTests's
    // An_event_source_token_can_neither_declare_nor_retire_an_event_type.
    // Every token this repo's test clients mint (management-web included)
    // carries the full sse.* bundle as *default* client scopes regardless of
    // the `scope` requested on the grant, so a test minted this way can never
    // demonstrate the negative case once T008 grants this scope to
    // management-web -- it was structurally unable to fail either way. The
    // replacement plants a narrow-scoped event-source-shaped client instead
    // (spec 143 FR-010's testing-gotcha note, plan.md §9 counterfactual 4).

    [Fact]
    public async Task Retiring_without_If_Match_is_refused_with_428()
    {
        var kind = UniqueKind();
        using var dresden = await ClientFor(DresdenOperator);
        (await RegisterAsync(dresden, kind)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var refused = await dresden.DeleteAsync($"/event-types/{kind}");

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired, await BodyAsync(refused));

        var listed = await ListAsync(dresden);
        listed.EnumerateArray().Single(r => r.GetProperty("kind").GetString() == kind)
            .GetProperty("state").GetString().ShouldBe("Registered");
    }

    [Fact]
    public async Task Retiring_with_a_stale_If_Match_is_refused_with_409()
    {
        var kind = UniqueKind();
        using var dresden = await ClientFor(DresdenOperator);
        (await RegisterAsync(dresden, kind)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var refused = await SendDeleteAsync(dresden, kind, expectedVersion: 99);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await BodyAsync(refused));
        // ADR-0119 (StaleCodeConventionTests): the wire code ends "_STALE", not
        // "_STALE_VERSION" — the shared client keys on that exact suffix to
        // tell an operator their edit conflicts rather than to retry.
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("title").GetString().ShouldBe("EVENT_TYPE_STALE");
    }

    [Fact]
    public async Task Retiring_a_type_of_another_fab_is_reported_as_404()
    {
        var kind = UniqueKind();
        using var multi = await ClientFor(MultiFabOperator);
        (await RegisterAsync(multi, kind, fabId: "munich")).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var dresden = await ClientFor(DresdenOperator);
        var refused = await SendDeleteAsync(dresden, kind, expectedVersion: 0);

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(refused));

        var listed = await ListAsync(multi);
        listed.EnumerateArray().Single(r => r.GetProperty("kind").GetString() == kind)
            .GetProperty("state").GetString().ShouldBe("Registered");
    }

    [Fact]
    public async Task A_retired_kind_can_be_registered_again()
    {
        var kind = UniqueKind();
        using var dresden = await ClientFor(DresdenOperator);
        (await RegisterAsync(dresden, kind)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await SendDeleteAsync(dresden, kind, expectedVersion: 0))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var reregistered = await RegisterAsync(dresden, kind);

        reregistered.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(reregistered));
        var listed = await ListAsync(dresden);
        listed.EnumerateArray()
            .Count(r => r.GetProperty("kind").GetString() == kind && r.GetProperty("state").GetString() == "Registered")
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_repeated_register_with_the_same_idempotency_key_replays_the_same_201()
    {
        var kind = UniqueKind();
        var key = $"key-{Guid.CreateVersion7():N}";
        using var dresden = await ClientFor(DresdenOperator);

        var first = await RegisterWithKeyAsync(dresden, kind, key);
        var second = await RegisterWithKeyAsync(dresden, kind, key);

        second.ShouldBe(first, "a replay must return the identifier the first attempt created, not a second one");
    }

    /// <summary>
    /// Green on arrival (spec.md §4's quiet case, FR-013): this exercises
    /// the existing, untouched <c>/events/manual</c> endpoint, which this
    /// spec does not modify — it is the guard that would notice a phase-4
    /// engineer wiring the registry into ingest, not a registry behaviour.
    /// </summary>
    [Fact]
    public async Task An_unknown_kind_is_still_ingested()
    {
        var kind = $"NothingDeclared{Guid.NewGuid():N}"[..32];
        using var dresden = await ClientFor(DresdenOperator);

        var created = await dresden.PostAsJsonAsync("/events/manual", new
        {
            deviceId = "registry-quiet-check",
            kind,
            occurredAt = DateTimeOffset.UtcNow,
            payload = new { note = "spec 143 quiet case" },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        (await WaitForCountAsync(dresden, kind)).ShouldBe(1, "an unknown kind must still be ingested (FR-013)");
    }

    private static async Task<int> WaitForCountAsync(HttpClient reader, string kind)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var listed = await reader.GetAsync($"/events?fabId=dresden&kind={kind}");
            listed.StatusCode.ShouldBe(HttpStatusCode.OK);
            var page = await listed.Content.ReadFromJsonAsync<JsonElement>();
            var count = page.GetProperty("items").GetArrayLength();
            if (count > 0 || DateTime.UtcNow >= deadline)
            {
                return count;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string kind, string? fabId = null) =>
        client.PostAsJsonAsync(fabId is null ? "/event-types" : $"/event-types?fabId={fabId}", new { kind });

    private async Task<Guid> RegisterWithKeyAsync(HttpClient client, string kind, string key)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/event-types")
        {
            Content = JsonContent.Create(new { kind }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(response));

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetGuid();
    }

    private static Task<HttpResponseMessage> SendDeleteAsync(HttpClient client, string kind, int expectedVersion)
    {
        HttpRequestMessage request = new(HttpMethod.Delete, $"/event-types/{kind}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedVersion}\"");
        return client.SendAsync(request);
    }

    private async Task<JsonElement> ListAsync(HttpClient client)
    {
        var listed = await client.GetAsync("/event-types");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(listed));
        return await listed.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<HttpClient> ClientFor(string username) =>
        aspire.CreateAuthenticatedClientAsync("event-ingestion", username, PasswordFor(username));

    private static string PasswordFor(string username) => username switch
    {
        DresdenOperator => DresdenOperatorPassword,
        MultiFabOperator => MultiFabOperatorPassword,
        _ => throw new ArgumentOutOfRangeException(nameof(username), username, "no seeded password for this username"),
    };

    private static string UniqueKind() => $"EventType{Guid.NewGuid():N}";

    private async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}" +
        $"event-ingestion log:{Environment.NewLine}{aspire.RecentLogs("event-ingestion")}";
}
