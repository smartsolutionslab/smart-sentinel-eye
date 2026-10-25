using System.Text.Json;
using Npgsql;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 454 (#3096), spec.md §4 end to end on the real stack (ADR-0103). A replay of a rotate key
/// is answered while the client is still at the version that answer carried, and refused with
/// 409 <c>IDEMPOTENCY_KEY_SUPERSEDED</c> once something else has moved the client on, with no
/// secret in the body. A key completed before versions were stored fails closed.
///
/// <para>
/// Every test mints its own integration name and keys: these write real Keycloak clients and
/// nothing here resets Postgres rows between runs. The legacy-row case damages a row the system
/// itself just wrote rather than hand-inserting a synthetic one (spec 201 precedent).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class WebhookRotationReplaySupersededIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";

    private const string Superseded = "IDEMPOTENCY_KEY_SUPERSEDED";

    [Fact]
    public async Task A_replay_with_nothing_in_between_returns_the_same_secret_and_version()
    {
        using var identity = await aspire.CreateAdminClientAsync("identity");
        var name = UniqueName();
        var key = UniqueKey();

        var first = await identity.SendAsync(Create(name, key));
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(first));
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();

        var second = await identity.SendAsync(Create(name, key));

        second.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(second));
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("clientSecret").GetString().ShouldBe(firstBody.GetProperty("clientSecret").GetString());
        secondBody.GetProperty("version").GetInt32().ShouldBe(firstBody.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task A_replay_after_a_later_rotation_is_refused_as_superseded_and_discloses_no_secret()
    {
        using var identity = await aspire.CreateAdminClientAsync("identity");
        var name = UniqueName();
        var firstKey = UniqueKey();
        var secondKey = UniqueKey();

        var created = await identity.SendAsync(Create(name, firstKey));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(created));
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var createdSecret = createdBody.GetProperty("clientSecret").GetString()!;
        var createdVersion = createdBody.GetProperty("version").GetInt32();

        var rotated = await identity.SendAsync(Rotate(name, createdVersion, secondKey));
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(rotated));
        var rotatedSecret = (await rotated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientSecret").GetString()!;
        rotatedSecret.ShouldNotBe(createdSecret, "the rotation must have changed the secret, or this test proves nothing.");

        var replay = await identity.SendAsync(Create(name, firstKey));

        replay.StatusCode.ShouldBe(HttpStatusCode.Conflict, await DiagnoseAsync(replay));
        var text = await replay.Content.ReadAsStringAsync();
        TitleOf(JsonDocument.Parse(text).RootElement).ShouldBe(Superseded);
        text.ShouldNotContain(createdSecret);
        text.ShouldNotContain(rotatedSecret);
        text.ShouldNotContain("clientSecret");
    }

    [Fact]
    public async Task The_newer_key_still_replays_its_own_secret_after_the_older_one_is_superseded()
    {
        using var identity = await aspire.CreateAdminClientAsync("identity");
        var name = UniqueName();
        var firstKey = UniqueKey();
        var secondKey = UniqueKey();

        var created = await identity.SendAsync(Create(name, firstKey));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(created));
        var createdVersion = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32();
        var rotated = await identity.SendAsync(Rotate(name, createdVersion, secondKey));
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(rotated));
        var rotatedBody = await rotated.Content.ReadFromJsonAsync<JsonElement>();

        var replay = await identity.SendAsync(Rotate(name, createdVersion, secondKey));

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(replay));
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        replayBody.GetProperty("clientSecret").GetString().ShouldBe(rotatedBody.GetProperty("clientSecret").GetString());
        replayBody.GetProperty("version").GetInt32().ShouldBe(rotatedBody.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Each_completed_key_records_the_version_its_own_answer_carried()
    {
        using var identity = await aspire.CreateAdminClientAsync("identity");
        var name = UniqueName();
        var firstKey = UniqueKey();
        var secondKey = UniqueKey();

        var created = await identity.SendAsync(Create(name, firstKey));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(created));
        var createdVersion = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32();
        var rotated = await identity.SendAsync(Rotate(name, createdVersion, secondKey));
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(rotated));
        var rotatedVersion = (await rotated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32();

        (await StoredVersionAsync(firstKey)).ShouldBe(createdVersion);
        (await StoredVersionAsync(secondKey)).ShouldBe(rotatedVersion);
    }

    [Fact]
    public async Task A_completed_key_with_no_stored_version_is_refused_as_superseded()
    {
        using var identity = await aspire.CreateAdminClientAsync("identity");
        var name = UniqueName();
        var key = UniqueKey();

        var created = await identity.SendAsync(Create(name, key));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(created));
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientSecret").GetString()!;
        await ClearStoredVersionAsync(key);

        var replay = await identity.SendAsync(Create(name, key));

        replay.StatusCode.ShouldBe(HttpStatusCode.Conflict, await DiagnoseAsync(replay));
        var text = await replay.Content.ReadAsStringAsync();
        TitleOf(JsonDocument.Parse(text).RootElement).ShouldBe(Superseded);
        text.ShouldNotContain(secret);
    }

    [Fact]
    public async Task A_key_reused_for_a_different_request_is_still_refused_422_not_superseded()
    {
        using var identity = await aspire.CreateAdminClientAsync("identity");
        var name = UniqueName();
        var key = UniqueKey();

        var created = await identity.SendAsync(Create(name, key));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(created));
        var createdVersion = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32();
        var rotated = await identity.SendAsync(Rotate(name, createdVersion, UniqueKey()));
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(rotated));

        var reused = await identity.SendAsync(Rotate(name, createdVersion + 1, key));

        reused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await DiagnoseAsync(reused));
        TitleOf(await reused.Content.ReadFromJsonAsync<JsonElement>()).ShouldBe("IDEMPOTENCY_KEY_REUSED");
    }

    private static string UniqueName() => $"t454-{Guid.CreateVersion7():N}";

    private static string UniqueKey() => $"key-{Guid.CreateVersion7():N}";

    private static HttpRequestMessage Create(string name, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = JsonContent.Create(new { fabId = Fab }),
        };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return request;
    }

    private static HttpRequestMessage Rotate(string name, int expectedVersion, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = JsonContent.Create(new { fabId = Fab }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedVersion}\"");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return request;
    }

    private static string? TitleOf(JsonElement problem) =>
        problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("title", out var title)
            ? title.GetString()
            : null;

    private async Task<NpgsqlDataSource> IdentityDatabaseAsync() =>
        NpgsqlDataSource.Create(
            await aspire.App.GetConnectionStringAsync("identity-db")
            ?? throw new InvalidOperationException("Connection string 'identity-db' was not provisioned by Aspire."));

    private async Task<int?> StoredVersionAsync(string key)
    {
        await using var database = await IdentityDatabaseAsync();
        await using var command = database.CreateCommand("SELECT resource_version FROM idempotency_key WHERE key = @key;");
        command.Parameters.AddWithValue("key", key);

        var stored = await command.ExecuteScalarAsync();

        return stored is null or DBNull ? null : Convert.ToInt32(stored, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task ClearStoredVersionAsync(string key)
    {
        await using var database = await IdentityDatabaseAsync();
        await using var command = database.CreateCommand("UPDATE idempotency_key SET resource_version = NULL WHERE key = @key;");
        command.Parameters.AddWithValue("key", key);

        (await command.ExecuteNonQueryAsync()).ShouldBe(1, "exactly the row this test wrote.");
    }

    private async Task<string> DiagnoseAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}"
        + $"identity log:{Environment.NewLine}{aspire.RecentLogs("identity")}";
}
