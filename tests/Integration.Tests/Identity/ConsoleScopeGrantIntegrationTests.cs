using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 200 (issue #2279) — what the operator console's own token holds, and
/// what a policy does with it, on the real stack.
///
/// <para>
/// <b><see cref="A_smart_sentinel_eye_web_token_does_not_carry_the_bundle_and_is_refused"/>
/// (SC-1) is the fact this spec exists to guard.</b> At the time this spec was
/// written, <c>RequireScopeExtensions</c> carried a grandfather clause that
/// accepted <c>sse.management</c> for every <c>sse.*</c> policy but
/// <c>sse.events.publish</c>, so a <c>smart-sentinel-eye-web</c> token that
/// re-acquired the bundle would have answered 200 on <c>GET /system-variables</c>
/// instead of the 403 asserted here — the exact shape of #2279. That clause was
/// withdrawn (spec 265 / #2486): the 403 now holds even if the bundle is
/// re-granted, which is exactly what makes this SC-1 a durable guard rather
/// than one describing a hole that has since closed.
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
    private const string OperatorPassword = "Operator1234";
    private const string SystemVariablesResource = "system-variables";
    private const string AuditResource = "audit-observability";
    private const string SystemVariablesRoute = "/system-variables";
    private const string AuditRoute = "/audit?pageSize=1";

    /// <summary>SC-1 — the reason this spec exists.</summary>
    [Fact]
    public async Task A_smart_sentinel_eye_web_token_does_not_carry_the_bundle_and_is_refused()
    {
        string jwt = await aspire.GetAccessTokenForClientAsync(
            "smart-sentinel-eye-web", Operator, OperatorPassword, "openid");

        string[] granted = ScopesOf(jwt);
        granted.ShouldNotContain(
            "sse.management",
            customMessage: "smart-sentinel-eye-web should no longer default-grant the legacy bundle. "
            + $"Scopes: {string.Join(" ", granted)}");

        using HttpClient client = ClientFor(SystemVariablesResource, jwt);
        HttpResponseMessage response = await client.GetAsync(SystemVariablesRoute);

        response.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            $"GET {SystemVariablesRoute} admitted a smart-sentinel-eye-web token that should hold no "
            + $"sse.variables.read. {await Diagnose(response, SystemVariablesResource)}");
    }

    /// <summary>
    /// SC-2 — the control on SC-1. <c>sse.audit.read</c> stays a default scope of
    /// <c>smart-sentinel-eye-web</c>, so this proves SC-1's 403 is about the
    /// missing scope specifically, not a broken mint, a rejected issuer or
    /// audience, or a stack refusing every caller.
    /// </summary>
    [Fact]
    public async Task The_same_refused_token_still_reads_audit()
    {
        string jwt = await aspire.GetAccessTokenForClientAsync(
            "smart-sentinel-eye-web", Operator, OperatorPassword, "openid");

        using HttpClient client = ClientFor(AuditResource, jwt);
        HttpResponseMessage response = await client.GetAsync(AuditRoute);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "the positive control failed: a smart-sentinel-eye-web token could not read audit either, "
            + $"so SC-1's refusal would prove nothing. {await Diagnose(response, AuditResource)}");
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
