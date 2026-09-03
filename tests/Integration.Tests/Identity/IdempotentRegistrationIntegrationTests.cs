using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// ADR-0142 against the real stack. The unit tests prove the mechanism in
/// isolation; this proves the two claims that only a live Keycloak and a live
/// Postgres can settle — that a replay hands back the <b>same secret</b>, read
/// from Keycloak rather than from anything we stored, and that a request without
/// a key still behaves exactly as spec 008 FR-010 specifies.
///
/// <para>
/// The second test is what keeps the constitution amendment honest. Relaxing
/// single-reveal was justified as applying only to a caller that asks for
/// replay; if a keyless repeat also replayed, the amendment would be far wider
/// than the one that was approved.
/// </para>
///
/// <para>
/// Each test mints its own device identifier, as
/// <c>RegisteredClientConcurrencyIntegrationTests</c> does: registration creates
/// real Keycloak clients, and wiping Postgres rows would leave those behind and
/// desynchronised.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class IdempotentRegistrationIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";

    /// <summary>
    /// The observed failure, inverted into a guarantee: a repeat that carries the
    /// key gets the registration back instead of DEVICE_ALREADY_REGISTERED.
    /// </summary>
    [Fact]
    public async Task A_repeat_carrying_the_same_key_replays_the_original_credentials()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string device = NewDeviceIdentifier();
        string key = $"key-{Guid.CreateVersion7():N}";

        JsonElement first = await RegisterAsync(identity, device, key, HttpStatusCode.Created);
        JsonElement second = await RegisterAsync(identity, device, key, HttpStatusCode.Created);

        Secret(second).ShouldBe(
            Secret(first),
            "a replay must return the credentials the first attempt earned. A different secret would mean "
            + "the replay rotated, silently invalidating the one already delivered.");
        ClientId(second).ShouldBe(ClientId(first));
        Identifier(second).ShouldBe(Identifier(first));
    }

    /// <summary>
    /// Without a key the endpoint is unchanged, which is the whole basis on which
    /// the constitution amendment was kept narrow.
    /// </summary>
    [Fact]
    public async Task A_repeat_without_a_key_is_still_refused_as_a_duplicate()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string device = NewDeviceIdentifier();

        await RegisterAsync(identity, device, key: null, HttpStatusCode.Created);

        using HttpResponseMessage repeat = await SendAsync(identity, device, key: null);

        repeat.StatusCode.ShouldBe(
            HttpStatusCode.Conflict,
            "spec 008 FR-010's 409 is unchanged for a caller that did not ask for replay — a keyless repeat "
            + "is what a genuine second registration looks like.");
    }

    /// <summary>
    /// Two keys, one device: the second is a real duplicate rather than a retry,
    /// so it must be refused. Otherwise the key would be a way to bypass the
    /// duplicate rule rather than a way to survive a retry.
    /// </summary>
    [Fact]
    public async Task A_different_key_for_the_same_device_is_still_a_duplicate()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string device = NewDeviceIdentifier();

        await RegisterAsync(identity, device, $"key-{Guid.CreateVersion7():N}", HttpStatusCode.Created);

        using HttpResponseMessage other = await SendAsync(identity, device, $"key-{Guid.CreateVersion7():N}");

        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_malformed_key_is_refused_rather_than_ignored()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");

        using HttpResponseMessage response = await SendAsync(
            identity, NewDeviceIdentifier(), key: "has spaces and *stars*");

        response.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest,
            "silently ignoring an unusable key would give the caller the at-most-once guarantee it asked "
            + "for without delivering it.");
    }

    private static string NewDeviceIdentifier() => $"t040-{Guid.CreateVersion7():N}";

    private static async Task<JsonElement> RegisterAsync(
        HttpClient identity, string device, string? key, HttpStatusCode expected)
    {
        using HttpResponseMessage response = await SendAsync(identity, device, key);

        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient identity, string device, string? key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/devices/register?fabId={Fab}")
        {
            Content = JsonContent.Create(new { deviceType = "plc", deviceIdentifier = device }),
        };

        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return identity.SendAsync(request);
    }

    private static string Secret(JsonElement body) => body.GetProperty("clientSecret").GetString()!;

    private static string ClientId(JsonElement body) => body.GetProperty("clientId").GetString()!;

    private static Guid Identifier(JsonElement body) => body.GetProperty("registeredClientIdentifier").GetGuid();
}
