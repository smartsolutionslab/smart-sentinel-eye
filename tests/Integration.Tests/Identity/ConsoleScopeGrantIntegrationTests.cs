using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 200 (issue #2279) — what the operator console's own token holds, and
/// what a policy does with it, on the real stack.
///
/// <para>
/// <b><see cref="A_token_holding_neither_the_bundle_nor_variables_read_is_refused"/>
/// (SC-1) is the fact this spec exists to guard.</b> At the time this spec was
/// written, <c>RequireScopeExtensions</c> carried a grandfather clause that
/// accepted <c>sse.management</c> for every <c>sse.*</c> policy but
/// <c>sse.events.publish</c>, so a token that re-acquired the bundle would have
/// answered 200 on <c>GET /system-variables</c> instead of the 403 asserted
/// here — the exact shape of #2279. That clause was withdrawn (spec 265 /
/// #2486): the 403 now holds even if the bundle is re-granted, which is
/// exactly what makes this SC-1 a durable guard rather than one describing a
/// hole that has since closed.
/// </para>
///
/// <para>
/// <b>Spec 286 (issue #2488) retired the <c>smart-sentinel-eye-web</c> client
/// this fact used to mint from.</b> SC-1 and SC-2 now mint from a throwaway
/// public password-grant client this class plants with exactly that retired
/// client's default scopes (<c>sse-identity</c>, <c>sse-audience</c>,
/// <c>sse-groups</c>, <c>sse.audit.read</c>) and deletes afterwards, the same
/// pattern <c>EventTypeRegistryAuthorizationIntegrationTests</c> uses. The
/// API-answer assertions (403 on <c>/system-variables</c>, 200 on
/// <c>/audit</c>) are unchanged; only how the token is obtained changed. The
/// <c>sse.management</c>-absence check in SC-1 is now a probe-shape control —
/// on a client whose scopes this test itself chose, it checks its own input —
/// kept anyway because a probe that somehow acquired the bundle would still be
/// the wrong probe to reason from.
/// </para>
///
/// <para>
/// Every other fact in this file is a <b>control</b> — each one rules out a
/// specific way SC-1's refusal could be lying:
/// <see cref="The_same_refused_token_still_reads_audit"/> (SC-2) rules out a
/// stack refusing everybody or a broken mint;
/// <see cref="A_management_web_token_is_not_refused_the_same_call"/> (SC-3)
/// rules out the replacement client being mis-provisioned;
/// <see cref="A_management_web_token_names_its_scopes_and_not_the_bundle"/>
/// (SC-4) is the positive shape the console's token is meant to have; and
/// <see cref="An_unauthenticated_caller_is_refused_with_401_not_403"/> (SC-8)
/// pins the unrelated 401 boundary so SC-1's 403 is never mistaken for it. A
/// reviewer seeing five green facts here should read four of them as proof
/// the harness works, not as five separate confirmations of the fix itself.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class ConsoleScopeGrantIntegrationTests(AspireFixture aspire)
{
    private const string Operator = "operator";
    private const string OperatorPassword = SeededCredentials.Operator;
    private const string SystemVariablesResource = "system-variables";
    private const string AuditResource = "audit-observability";
    private const string SystemVariablesRoute = "/system-variables";
    private const string AuditRoute = "/audit?pageSize=1";

    private readonly RealmProbe realm = new(aspire);

    /// <summary>
    /// The retired <c>smart-sentinel-eye-web</c> client's exact default scope
    /// set (spec 286 measurement table), reproduced on a planted client so
    /// SC-1 and SC-2 keep proving the same thing about that scope shape rather
    /// than about any specific realm client.
    /// </summary>
    private static readonly string[] ProbeScopes =
        ["sse-identity", "sse-audience", "sse-groups", "sse.audit.read"];

    /// <summary>SC-1 — the reason this spec exists.</summary>
    [Fact]
    public async Task A_token_holding_neither_the_bundle_nor_variables_read_is_refused()
    {
        string clientId = $"console-scope-probe-{Guid.CreateVersion7():N}";
        await realm.PlantPasswordGrantClientAsync(clientId, ProbeScopes, CancellationToken.None);

        try
        {
            string jwt = await aspire.GetAccessTokenForClientAsync(
                clientId, Operator, OperatorPassword, "openid");

            string[] granted = ScopesOf(jwt);
            granted.ShouldNotContain(
                "sse.management",
                customMessage: "probe-shape control: the planted client must not somehow carry the "
                + $"legacy bundle. Scopes: {string.Join(" ", granted)}");

            using HttpClient client = ClientFor(SystemVariablesResource, jwt);
            HttpResponseMessage response = await client.GetAsync(SystemVariablesRoute);

            response.StatusCode.ShouldBe(
                HttpStatusCode.Forbidden,
                $"GET {SystemVariablesRoute} admitted a token holding neither the bundle nor "
                + $"sse.variables.read. {await Diagnose(response, SystemVariablesResource)}");
        }
        finally
        {
            await realm.DeleteAsync(clientId, CancellationToken.None);
        }
    }

    /// <summary>
    /// SC-2 — the control on SC-1. <c>sse.audit.read</c> is one of the probe's
    /// planted default scopes, so this proves SC-1's 403 is about the missing
    /// scope specifically, not a broken mint, a rejected issuer or audience, or
    /// a stack refusing every caller.
    /// </summary>
    [Fact]
    public async Task The_same_refused_token_still_reads_audit()
    {
        string clientId = $"console-scope-probe-{Guid.CreateVersion7():N}";
        await realm.PlantPasswordGrantClientAsync(clientId, ProbeScopes, CancellationToken.None);

        try
        {
            string jwt = await aspire.GetAccessTokenForClientAsync(
                clientId, Operator, OperatorPassword, "openid");

            using HttpClient client = ClientFor(AuditResource, jwt);
            HttpResponseMessage response = await client.GetAsync(AuditRoute);

            response.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                "the positive control failed: the planted token could not read audit either, so "
                + $"SC-1's refusal would prove nothing. {await Diagnose(response, AuditResource)}");
        }
        finally
        {
            await realm.DeleteAsync(clientId, CancellationToken.None);
        }
    }

    /// <summary>
    /// SC-3 — the control on the console's replacement path. A red result here
    /// is an escalation, not a normal phase-4a red: it would mean
    /// <c>management-web</c> itself is mis-provisioned in the realm, which is
    /// outside what this phase should discover this way.
    /// </summary>
    [Fact]
    public async Task A_management_web_token_is_not_refused_the_same_call()
    {
        string jwt = await aspire.GetAccessTokenForClientAsync(
            "management-web", Operator, OperatorPassword, "openid");

        using HttpClient client = ClientFor(SystemVariablesResource, jwt);
        HttpResponseMessage response = await client.GetAsync(SystemVariablesRoute);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"the console's own replacement client was refused. {await Diagnose(response, SystemVariablesResource)}");
    }

    /// <summary>
    /// SC-4 — what the console's token names, asserted as a set membership plus
    /// an absence: a check that only confirms the console works passes just as
    /// happily with the bundle restored, which is exactly how this weakness
    /// returns (`e2e/kiosk-identity.spec.ts` made the same argument for spec 041).
    /// </summary>
    [Fact]
    public async Task A_management_web_token_names_its_scopes_and_not_the_bundle()
    {
        string jwt = await aspire.GetAccessTokenForClientAsync(
            "management-web", Operator, OperatorPassword, "openid");

        string[] granted = ScopesOf(jwt);

        granted.ShouldContain("sse.cameras.write");
        granted.ShouldContain("sse.layouts.write");
        granted.ShouldContain("sse.overlays.write");
        granted.ShouldContain("sse.variables.write");
        granted.ShouldContain("sse.audit.read");
        granted.ShouldNotContain(
            "sse.management",
            customMessage: $"management-web should never carry the legacy bundle. Scopes: {string.Join(" ", granted)}");
    }

    /// <summary>
    /// SC-8 — pinned so SC-1's new 403 is never mistaken for the unrelated 401
    /// boundary: 403 exactly when a scope is missing, 401 exactly when there is
    /// no caller at all.
    /// </summary>
    [Fact]
    public async Task An_unauthenticated_caller_is_refused_with_401_not_403()
    {
        using HttpClient client = aspire.CreateServiceClient(SystemVariablesResource);

        HttpResponseMessage response = await client.GetAsync(SystemVariablesRoute);

        response.StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized,
            $"an anonymous caller must be refused with 401, never 403. {await Diagnose(response, SystemVariablesResource)}");
    }

    /// <summary>
    /// SC-9 — spec 286 (issue #2488). Posts the password grant directly to the
    /// token endpoint via <see cref="AspireFixture.CreateKeycloakClient"/>
    /// rather than <see cref="AspireFixture.GetAccessTokenForClientAsync"/>,
    /// which throws on a non-success response and would hide the status this
    /// fact needs to observe. The positive control mints for
    /// <c>management-web</c> with the same <c>operator</c> credentials through
    /// the identical raw call shape, so a wrong password
    /// (<c>invalid_grant</c>) can never be mistaken for the retirement working.
    /// </summary>
    [Fact]
    public async Task The_retired_client_cannot_mint_a_token()
    {
        (HttpStatusCode status, JsonElement body) = await RawPasswordGrantAsync(
            "smart-sentinel-eye-web", Operator, OperatorPassword);

        status.ShouldBe(
            HttpStatusCode.Unauthorized,
            "smart-sentinel-eye-web is retired (spec 286 / #2488) and must no longer mint a token. "
            + $"Observed: {(int)status} {body}");
        body.TryGetProperty("error", out JsonElement error).ShouldBeTrue(
            $"expected an 'error' field on the refusal. Observed: {(int)status} {body}");
        error.GetString().ShouldBe(
            "invalid_client",
            $"the refusal must be about the client, not the credentials. Observed: {(int)status} {body}");

        (HttpStatusCode controlStatus, JsonElement controlBody) = await RawPasswordGrantAsync(
            "management-web", Operator, OperatorPassword);

        controlStatus.ShouldBe(
            HttpStatusCode.OK,
            "the positive control failed: the same operator credentials against management-web, "
            + $"through the identical raw call shape, must still mint a token. Observed: "
            + $"{(int)controlStatus} {controlBody}");
        controlBody.TryGetProperty("access_token", out _).ShouldBeTrue(
            $"the control response did not carry an access_token. Observed: {controlBody}");
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> RawPasswordGrantAsync(
        string clientId, string username, string password)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid",
        };

        HttpResponseMessage response = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token",
            new FormUrlEncodedContent(form));

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response.StatusCode, body);
    }

    private HttpClient ClientFor(string resourceName, string jwt)
    {
        HttpClient client = aspire.CreateServiceClient(resourceName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }

    private async Task<string> Diagnose(HttpResponseMessage response, string resourceName) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}"
        + $"{resourceName} log:{Environment.NewLine}{aspire.RecentLogs(resourceName)}";

    /// <summary>
    /// The <c>scope</c> claim of a JWT, read without validating it. Re-spelt
    /// here rather than shared from
    /// <c>EventTypeRegistryAuthorizationIntegrationTests</c> — that class keeps
    /// its private helper private, and a second call site is not yet a reason to
    /// widen it (ADR-0036).
    /// </summary>
    private static string[] ScopesOf(string jwt)
    {
        string[] parts = jwt.Split('.');
        parts.Length.ShouldBe(3, "a bearer token should have three segments");

        string payload = parts[1].Replace('-', '+').Replace('_', '/');
        string padded = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        using JsonDocument document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));

        return document.RootElement.TryGetProperty("scope", out JsonElement scope)
            ? scope.GetString()?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []
            : [];
    }
}
