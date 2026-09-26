using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// #2575 against the real stack. <c>RegisterDeviceCommandHandler</c> checks
/// <c>deviceType</c> but never <c>deviceIdentifier</c> before building
/// <c>ClientId.From($"{deviceType}-{deviceIdentifier}")</c> — a
/// <see langword="null"/> or empty identifier still produces a grammatically
/// valid clientId ("plc-"), so today the request succeeds with 201 and
/// registers a real Keycloak client literally named "plc-".
///
/// <para>
/// The unit tests (<c>RegisterDeviceCommandHandlerTests</c>) prove the same
/// gap against the handler in isolation; this proves it travels unchanged
/// through <c>RegisterDeviceRequest</c>'s bare-string binding and the wire,
/// which no Application-layer fake can show.
/// </para>
///
/// <para>
/// Each of a-c is expected to observe today's bug (201, or 409 on the
/// repeat) rather than the 400 the fix will return — this is the red
/// evidence ADR-0139 requires, not an assertion of desired behaviour. Row d
/// is a green guard: a unique valid identifier already returns 201 today and
/// must keep doing so after the fix.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class AbsentDeviceIdentifierIsRefusedIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string Fab = "munich";

    private readonly List<string> createdClientIds = [];

    public async Task InitializeAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("identity", KnownResourceStates.Running, cts.Token);
    }

    /// <summary>
    /// Disables every "plc-" (or "inference-") client this test run actually
    /// created — the red run of rows a-c creates one for real, since today
    /// nothing stops the request before Keycloak.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (createdClientIds.Count == 0)
        {
            return;
        }

        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        foreach (string clientId in createdClientIds)
        {
            await identity.DeleteAsync($"/devices/{clientId}?fabId={Fab}");
        }
    }

    [Fact]
    public async Task An_omitted_device_identifier_field_is_refused_with_DEVICE_INVALID_IDENTIFIER()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        using StringContent body = new("{\"deviceType\":\"plc\"}", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await identity.PostAsync(
            $"/devices/register?fabId={Fab}", body);

        await RecordIfCreatedAsync(response);
        response.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest, await aspire.DiagnoseAsync("identity", response));
        (await ProblemCodeAsync(response)).ShouldBe("DEVICE_INVALID_IDENTIFIER");
    }

    [Fact]
    public async Task A_null_device_identifier_is_refused_with_DEVICE_INVALID_IDENTIFIER()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        using StringContent body = new(
            "{\"deviceType\":\"plc\",\"deviceIdentifier\":null}", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await identity.PostAsync(
            $"/devices/register?fabId={Fab}", body);

        await RecordIfCreatedAsync(response);
        response.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest, await aspire.DiagnoseAsync("identity", response));
        (await ProblemCodeAsync(response)).ShouldBe("DEVICE_INVALID_IDENTIFIER");
    }

    [Fact]
    public async Task An_empty_device_identifier_is_refused_with_DEVICE_INVALID_IDENTIFIER()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");

        using HttpResponseMessage response = await identity.PostAsJsonAsync(
            $"/devices/register?fabId={Fab}",
            new { deviceType = "plc", deviceIdentifier = "" });

        await RecordIfCreatedAsync(response);
        response.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest, await aspire.DiagnoseAsync("identity", response));
        (await ProblemCodeAsync(response)).ShouldBe("DEVICE_INVALID_IDENTIFIER");
    }

    /// <summary>
    /// Once the null-guard fix lands, the identifier check runs before the
    /// duplicate-clientId check (#2575's own designed invariant), so a
    /// repeated empty identifier never reaches the repository or Keycloak on
    /// either attempt — both are refused identically, not just the first.
    /// Before the fix this collided as 409 DEVICE_ALREADY_REGISTERED on the
    /// second call; that shape is now unreachable by design, not merely
    /// untested.
    /// </summary>
    [Fact]
    public async Task A_repeated_empty_device_identifier_is_refused_both_times()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        object emptyIdentifierBody = new { deviceType = "plc", deviceIdentifier = "" };

        using HttpResponseMessage first = await identity.PostAsJsonAsync(
            $"/devices/register?fabId={Fab}", emptyIdentifierBody);
        await RecordIfCreatedAsync(first);

        using HttpResponseMessage second = await identity.PostAsJsonAsync(
            $"/devices/register?fabId={Fab}", emptyIdentifierBody);
        await RecordIfCreatedAsync(second);

        first.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest, await aspire.DiagnoseAsync("identity", first));
        (await ProblemCodeAsync(first)).ShouldBe("DEVICE_INVALID_IDENTIFIER");
        second.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest, await aspire.DiagnoseAsync("identity", second));
        (await ProblemCodeAsync(second)).ShouldBe("DEVICE_INVALID_IDENTIFIER");
    }

    /// <summary>
    /// Green guard, not red evidence: a unique valid identifier already
    /// returns 201 today, and must keep doing so once the null-guard fix
    /// lands (#2575).
    /// </summary>
    [Fact]
    public async Task A_valid_unique_device_identifier_still_registers_successfully()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string device = $"t2575-{Guid.CreateVersion7():N}";

        using HttpResponseMessage response = await identity.PostAsJsonAsync(
            $"/devices/register?fabId={Fab}",
            new { deviceType = "plc", deviceIdentifier = device });

        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await aspire.DiagnoseAsync("identity", response));
        await RecordIfCreatedAsync(response);
    }

    private async Task RecordIfCreatedAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Created)
        {
            return;
        }

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        createdClientIds.Add(body.GetProperty("clientId").GetString()!);
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;
}
