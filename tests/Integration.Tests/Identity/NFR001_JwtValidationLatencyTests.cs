using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Runtime;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 008 NFR-001 — JWT validation overhead per request must
/// stay ≤ 500 µs p99 on the hot path (cached JWKS, no Keycloak
/// round-trip). The test warms the OIDC discovery cache once,
/// then validates the same access token 1 000 times in a tight
/// loop and asserts the resulting p99.
///
/// <para>
/// Runs against the Aspire-booted Keycloak so the test exercises
/// the real signing-key formats + the real
/// <c>ConfigurationManager</c> cache the production
/// <c>WhepAuthValidator</c> uses. No Testcontainers.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class NFR001_JwtValidationLatencyTests(AspireFixture aspire)
{
    private const int WarmupIterations = 100;
    private const int MeasureIterations = 1_000;
    private const double P99BudgetMicroseconds = 500;

    [Fact]
    public async Task Per_request_JWT_validation_p99_stays_under_the_500us_budget()
    {
        string token = await aspire.GetAccessTokenAsync(AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        using HttpClient keycloak = aspire.App.CreateHttpClient("keycloak");
        string authority = $"{keycloak.BaseAddress!.ToString().TrimEnd('/')}/realms/smart-sentinel-eye";

        HttpDocumentRetriever retriever = new() { RequireHttps = false };
        ConfigurationManager<OpenIdConnectConfiguration> oidc = new(
            $"{authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            retriever);

        OpenIdConnectConfiguration config = await oidc.GetConfigurationAsync(CancellationToken.None);

        TokenValidationParameters parameters = new()
        {
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
        };

        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        for (int i = 0; i < WarmupIterations; i++)
        {
            handler.ValidateToken(token, parameters, out _);
        }

        // Stabilise the managed runtime so the p99 reflects JWT-validation
        // overhead rather than GC pauses. On the shared CI runner a mid-loop
        // gen2 collect spiked a sample to ~16 ms while the median was ~96 µs —
        // i.e. the tail was GC, not code. Collect first, then defer gen2 for the
        // measurement window so the p99 captures the hot-path cost the NFR is about.
        double[] elapsedMicroseconds = new double[MeasureIterations];
#pragma warning disable S1215 // Intentional: deterministic benchmark stabilisation, not production code.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
#pragma warning restore S1215
        GCLatencyMode previousLatencyMode = GCSettings.LatencyMode;
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        try
        {
            for (int i = 0; i < MeasureIterations; i++)
            {
                long start = Stopwatch.GetTimestamp();
                handler.ValidateToken(token, parameters, out _);
                elapsedMicroseconds[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }
        }
        finally
        {
            GCSettings.LatencyMode = previousLatencyMode;
        }

        Array.Sort(elapsedMicroseconds);
        double p50 = elapsedMicroseconds[MeasureIterations / 2];
        double p99 = elapsedMicroseconds[(int)Math.Ceiling(MeasureIterations * 0.99) - 1];
        double max = elapsedMicroseconds[^1];

        p99.ShouldBeLessThan(
            P99BudgetMicroseconds,
            $"p50 = {p50:F1} µs, p99 = {p99:F1} µs, max = {max:F1} µs");
    }
}
