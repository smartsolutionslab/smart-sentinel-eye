using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 042 — a write made with a real token is attributed to a real operator.
///
/// <para>
/// <b>Nothing asserted this before.</b> Every other test involving an
/// <c>OperatorIdentifier</c> fabricates one — <c>OperatorIdentifier.From(Guid
/// .CreateVersion7())</c> — and hands it to a handler directly, which is exactly
/// how a client that could not be attributed sat in the realm unnoticed. Only a
/// minted token exercises the path that was broken.
/// </para>
///
/// <para>
/// <b>Success is the assertion.</b> A token with no <c>sub</c> makes
/// <c>ClaimsPrincipalExtensions.ToOperatorIdentifier</c> throw
/// <c>UnattributableOperatorException</c>, mapped to a <b>401</b> — it does not
/// produce a wrong operator, because attributing a change to a fabricated person
/// would corrupt the audit trail. So the failure mode is refusal, and a created
/// resource is proof the subject arrived.
/// </para>
///
/// <para>
/// This is also the only check that would notice the <c>sse-identity</c> mapper
/// being present but not firing. <c>RealmIdentityTests</c> reads names; it would
/// stay green.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class TokenAttributionIntegrationTests(AspireFixture aspire)
{
    [Fact]
    public async Task A_minted_token_carries_a_subject()
    {
        string token = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        SubjectOf(token).ShouldNotBeNullOrWhiteSpace(
            "an access token with no sub cannot be attributed, so every write it makes is refused "
            + "(spec 042 FR-003).");
    }

    [Fact]
    public async Task A_write_made_with_a_minted_token_is_attributed_rather_than_refused()
    {
        string token = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        using HttpClient overlays = aspire.CreateServiceClient("overlay-designer");
        overlays.DefaultRequestHeaders.Authorization = new("Bearer", token);

        HttpResponseMessage created = await overlays.PostAsJsonAsync("/overlays", new
        {
            name = $"attribution-{Guid.NewGuid():N}"[..24],
            label = new
            {
                text = "spec 042",
                normalizedX = 0.1m,
                normalizedY = 0.1m,
                normalizedWidth = 0.2m,
                normalizedHeight = 0.1m,
                fontSizePx = 24,
            },
        });

        // Not 401. The body has to be valid to get this far — the endpoint parses
        // the label before it calls ToOperatorIdentifier — so a 400 here would
        // mean the payload is wrong rather than the subject missing, and a 401
        // means the subject never arrived.
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync(created));
    }

    /// <summary>
    /// The subject is a Guid, because <c>OperatorIdentifier.From</c> parses it as
    /// one and an unparseable subject fails closed exactly like a missing one.
    /// </summary>
    [Fact]
    public async Task The_subject_is_shaped_like_an_operator_identifier()
    {
        string token = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        Guid.TryParse(SubjectOf(token), out Guid subject).ShouldBeTrue(
            "ToOperatorIdentifier parses sub as a Guid and throws when it cannot.");
        subject.ShouldNotBe(Guid.Empty);
    }

    private static string? SubjectOf(string token)
    {
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        return handler.ReadJwtToken(token).Claims
            .FirstOrDefault(claim => string.Equals(claim.Type, "sub", StringComparison.Ordinal))
            ?.Value;
    }

    private async Task<string> DiagnoseAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        return $"body: {body}{Environment.NewLine}overlay-designer log:{Environment.NewLine}"
            + aspire.RecentLogs("overlay-designer");
    }
}
