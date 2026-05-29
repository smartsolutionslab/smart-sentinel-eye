using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
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

    private readonly AspireFixture _aspire = aspire;

    [Fact]
    public async Task Per_request_JWT_validation_p99_stays_under_the_500us_budget()
    {
        string token = await _aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        using HttpClient keycloak = _aspire.App.CreateHttpClient("keycloak");
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

        double[] elapsedMicroseconds = new double[MeasureIterations];
        for (int i = 0; i < MeasureIterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            handler.ValidateToken(token, parameters, out _);
            elapsedMicroseconds[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
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
