using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Where the run-mode stack is, supplied by the operator (spec 054).
///
/// <para>
/// <b>Nothing in this repository has a stable address.</b> Every service is
/// composed with <c>WithHttpEndpoint()</c> and no port, so ports are assigned per
/// boot; the gateway is the same and additionally runs two or more replicas. The
/// e2e script resolves the gateway by scraping a TypeScript module off the Vite
/// dev server, which is a fair trick for a smoke check and a bad foundation for a
/// measurement — it depends on a frontend being up, which has nothing to do with
/// the audit pipeline.
/// </para>
///
/// <para>
/// <b>So the operator says where the stack is, and the run reports what it
/// actually reached.</b> The address genuinely varies per boot; pretending
/// otherwise is how a figure gets attributed to the wrong stack.
/// </para>
/// </summary>
public sealed record RunModeStackAddress(string SystemVariables, string Keycloak, string AuditDb)
{
    public const string SystemVariablesVariable = "SSE_RUNMODE_SYSTEM_VARIABLES";
    public const string KeycloakVariable = "SSE_RUNMODE_KEYCLOAK";
    public const string AuditDbVariable = "SSE_RUNMODE_AUDIT_DB";

    private const string Realm = "smart-sentinel-eye";
    private const string ClientId = "smart-sentinel-eye-web";
    private const string AdminUsername = "admin";
    private const string AdminPassword = "Admin1234";

    /// <summary>
    /// Reads the address from the environment, or explains what is missing.
    ///
    /// <para>
    /// <b>Absent configuration is a refusal, never a default.</b> A fallback
    /// address would let this run measure some other stack and label the result
    /// "run mode" — a complete, well-formed, confidently wrong answer, which is
    /// the worst outcome this feature can produce.
    /// </para>
    /// </summary>
    public static Option<RunModeStackAddress> FromEnvironment() =>
        From(
            Environment.GetEnvironmentVariable(SystemVariablesVariable),
            Environment.GetEnvironmentVariable(KeycloakVariable),
            Environment.GetEnvironmentVariable(AuditDbVariable));

    /// <summary>
    /// The decision, separated from where the values came from — so it can be
    /// tested without mutating process-wide state that a concurrently running
    /// measurement reads.
    /// </summary>
    public static Option<RunModeStackAddress> From(
        string? systemVariables, string? keycloak, string? auditDb)
    {
        if (string.IsNullOrWhiteSpace(systemVariables)
            || string.IsNullOrWhiteSpace(keycloak)
            || string.IsNullOrWhiteSpace(auditDb))
        {
            return Option<RunModeStackAddress>.None;
        }

        return Option<RunModeStackAddress>.Some(new RunModeStackAddress(systemVariables, keycloak, auditDb));
    }

    /// <summary>What to tell someone who ran this without a stack configured.</summary>
    public static string Missing =>
        "No run-mode stack configured. This run targets a stack it did not start and will not "
        + $"start one. Set {SystemVariablesVariable}, {KeycloakVariable} and {AuditDbVariable} to the "
        + "addresses of a running run-mode stack. For Keycloak, use whatever the realm names as "
        + "its `issuer` — ask its .well-known/openid-configuration rather than assuming the "
        + "proxied or the mapped port. See specs/054-divide-the-recorded-85ms/quickstart.md.";

    /// <summary>
    /// Mints an admin token against the <b>proxied</b> Keycloak address and
    /// returns a client for system-variables.
    ///
    /// <para>
    /// <b>The proxied address, not the container's mapped port.</b> A token minted
    /// against the container port carries an issuer the services do not accept,
    /// so every call returns 401 and nothing in the failure names the cause. This
    /// has cost this repository time before.
    /// </para>
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(CancellationToken cancellationToken)
    {
        using HttpClientHandler handler = new()
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        using HttpClient keycloak = new(handler, disposeHandler: false) { BaseAddress = new Uri(Keycloak) };

        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["client_id"] = ClientId,
            ["username"] = AdminUsername,
            ["password"] = AdminPassword,
            ["scope"] = "openid sse.management",
        };

        using HttpResponseMessage response = await keycloak.PostAsync(
            $"/realms/{Realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(form),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Keycloak password grant failed against '{Keycloak}': {response.StatusCode} {body}. "
                + "If this is a 401 or the issuer looks wrong, check the address is Aspire's proxied "
                + "endpoint rather than the container's mapped port.");
        }

        JsonElement json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        string token = json.GetProperty("access_token").GetString()!;

        // The same certificate handling as the Keycloak call above. The dashboard
        // shows both an http and an https endpoint for the service, and an
        // operator who copies the https one on a host where the dev certificate
        // is untrusted would otherwise get an opaque HttpRequestException from
        // inside the drive rather than a measurement.
        HttpClientHandler serviceHandler = new()
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        HttpClient client = new(serviceHandler, disposeHandler: true)
        {
            BaseAddress = new Uri(SystemVariables),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    /// <summary>Opens a context on the run-mode audit store.</summary>
    public AuditObservabilityDbContext CreateAuditContext()
    {
        DbContextOptionsBuilder<AuditObservabilityDbContext> options = new();
        options.UseNpgsql(AuditDb);

        return new AuditObservabilityDbContext(options.Options);
    }

    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"system-variables {SystemVariables}, keycloak {Keycloak}");
}
