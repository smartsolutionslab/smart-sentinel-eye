using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Revocation;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Infrastructure.Auth;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Auth;

/// <summary>
/// Spec 270 (ADR-0160), plan.md §4.4 — <c>WhepAuthValidator</c> refuses a
/// signature-valid, unexpired token when the registry says its client was
/// revoked. Through the internal metadata-source constructor seam (#2099),
/// the same seam <see cref="WhepAuthValidatorResolutionTests"/> and
/// <see cref="WhepValidatorRefreshRestraintTests"/> use, so no realm and no
/// network call is needed.
///
/// <para>
/// <b>Red today: compile.</b> The internal constructor takes two parameters
/// on <c>develop</c> (metadata, logger); plan.md §4.4 adds
/// <c>IRevokedClientRegistry</c>, so every construction below fails to
/// compile until T016 lands. This file also depends on
/// <c>SmartSentinelEye.ServiceDefaults.Revocation.IRevokedClientRegistry</c>,
/// which does not exist until T010.
/// </para>
///
/// <para>
/// <b>A judgment call.</b> Plan.md §4.4 says both constructors "take
/// <c>IRevokedClientRegistry</c>" but does not fix the parameter's position.
/// This file places it between the metadata source and the logger —
/// <c>WhepAuthValidator(metadata, registry, logger)</c> — mirroring where the
/// public constructor would insert it between <c>IOptions&lt;WhepAuthOptions&gt;</c>
/// and <c>ILogger</c>.
/// </para>
///
/// <para>
/// <b>Phase-6 review, S4.</b> <see cref="CapturingRegistry"/> wraps a real
/// <see cref="RevokedClientSnapshot"/> rather than answering a fixed
/// true/false, so these tests exercise the actual §2 rule through
/// <c>WhepAuthValidator</c>'s own <c>azp</c>/<c>IssuedAtOf</c> reads. A stub
/// that ignores its arguments cannot tell a correct read from
/// <c>IssuedAtOf</c> always returning <see langword="null"/> — every listed
/// client would then be refused forever, and the old stub-based tests would
/// still pass.
/// </para>
/// </summary>
public sealed class WhepRevocationTests : IDisposable
{
    private const string RealmIssuer = "https://keycloak.fab.example/realms/smart-sentinel-eye";
    private const string SigningKeyIdentifier = "whep-revocation-tests-key";
    private const string RevokedClientId = "kiosk-269";

    private readonly RSA signingKey = RSA.Create(2048);

    public void Dispose() => signingKey.Dispose();

    [Fact]
    public async Task A_registry_that_refuses_the_azp_rejects_the_token()
    {
        DateTime issuedAt = DateTime.UtcNow.AddMinutes(-1);
        CapturingRegistry registry = RegistryDisabledAt(RevokedClientId, issuedAt);
        CountingRefreshMetadata metadata = MetadataFor(RealmIssuer);
        WhepAuthValidator validator = new(metadata, registry, NullLogger<WhepAuthValidator>.Instance);
        string token = TokenFor(RevokedClientId, issuedAt);

        Result<WhepAuthSubject, WhepAuthFailure> result =
            await validator.ValidateAsync(token, CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(
            WhepAuthFailure.TokenRejected,
            "a revoked client's otherwise-valid token must be refused exactly like any other "
            + "rejected token (FR-002) — no new failure case.");
    }

    /// <summary>
    /// The control: the same token, the same metadata, only the registry's
    /// answer differs. Without this, the refusal above could pass because the
    /// token itself was wrong (memory: an assertion must not check its own
    /// input).
    /// </summary>
    [Fact]
    public async Task A_registry_that_admits_the_azp_leaves_validation_unchanged()
    {
        CapturingRegistry registry = new(RevokedClientSnapshot.From([]));
        CountingRefreshMetadata metadata = MetadataFor(RealmIssuer);
        WhepAuthValidator validator = new(metadata, registry, NullLogger<WhepAuthValidator>.Instance);
        string token = TokenFor(RevokedClientId, DateTime.UtcNow.AddMinutes(-1));

        Result<WhepAuthSubject, WhepAuthFailure> result =
            await validator.ValidateAsync(token, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Subject.ShouldBe(RevokedClientId);
        result.Value.Scopes.ShouldBe(["sse.streams.read"]);
    }

    /// <summary>
    /// Proves FR-001's re-registration rule at the WHEP enforcement point
    /// itself (phase-6 review, S4), not only at <c>RevokedClientSnapshotTests</c>'
    /// unit level: <see cref="CapturingRegistry"/> wraps the real
    /// <see cref="RevokedClientSnapshot"/> rule, so a broken <c>azp</c>/<c>iat</c>
    /// read inside <c>WhepAuthValidator</c> (e.g. <c>IssuedAtOf</c> always
    /// returning <see langword="null"/>) would fail this test rather than pass
    /// it silently.
    /// </summary>
    [Fact]
    public async Task A_later_issued_token_from_a_re_registered_client_is_admitted()
    {
        DateTimeOffset disabledAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTime laterIssuedAt = disabledAt.AddMinutes(6).UtcDateTime;
        CapturingRegistry registry = new(RevokedClientSnapshot.From(
            [new RevokedClientEntry(RevokedClientId, disabledAt)]));
        CountingRefreshMetadata metadata = MetadataFor(RealmIssuer);
        WhepAuthValidator validator = new(metadata, registry, NullLogger<WhepAuthValidator>.Instance);
        string token = TokenFor(RevokedClientId, laterIssuedAt);

        Result<WhepAuthSubject, WhepAuthFailure> result =
            await validator.ValidateAsync(token, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(
            "a client re-registered under the same id mints tokens after the old DisabledAt, and "
            + "FR-001 must admit them rather than refuse them forever for their predecessor's "
            + "revocation.");
    }

    /// <summary>
    /// Proves <c>WhepAuthValidator</c> passes the token's own <c>azp</c> and
    /// <c>iat</c> to the registry (phase-6 review, S4) — the exact silent
    /// break a stub that ignores its arguments cannot catch.
    /// </summary>
    [Fact]
    public async Task The_validator_passes_the_tokens_own_azp_and_issued_at_to_the_registry()
    {
        DateTime issuedAt = DateTime.UtcNow.AddMinutes(-1);
        CapturingRegistry registry = new(RevokedClientSnapshot.From([]));
        CountingRefreshMetadata metadata = MetadataFor(RealmIssuer);
        WhepAuthValidator validator = new(metadata, registry, NullLogger<WhepAuthValidator>.Instance);
        string token = TokenFor(RevokedClientId, issuedAt);

        await validator.ValidateAsync(token, CancellationToken.None);

        registry.LastAzp.ShouldBe(RevokedClientId);
        registry.LastIssuedAt.ShouldBe(TruncatedToSeconds(issuedAt));
    }

    /// <summary>
    /// A revocation refusal is not a stale-document problem — a fresher
    /// discovery document cannot cure it (plan.md §4.4) — so it must not join
    /// <c>WhepValidatorRefreshRestraintTests</c>' population of failures that
    /// provoke a re-read.
    /// </summary>
    [Fact]
    public async Task A_revocation_refusal_does_not_request_a_metadata_refresh()
    {
        DateTime issuedAt = DateTime.UtcNow.AddMinutes(-1);
        CapturingRegistry registry = RegistryDisabledAt(RevokedClientId, issuedAt);
        CountingRefreshMetadata metadata = MetadataFor(RealmIssuer);
        WhepAuthValidator validator = new(metadata, registry, NullLogger<WhepAuthValidator>.Instance);
        string token = TokenFor(RevokedClientId, issuedAt);

        await validator.ValidateAsync(token, CancellationToken.None);

        metadata.RefreshRequests.ShouldBe(
            0, "no fresher discovery document can cure a revocation; requesting one on every "
            + "revoked-token retry would turn a reconnect loop into a needless JWKS re-read.");
    }

    private static CapturingRegistry RegistryDisabledAt(string clientId, DateTime issuedAt) =>
        new(RevokedClientSnapshot.From([new RevokedClientEntry(clientId, new DateTimeOffset(issuedAt, TimeSpan.Zero))]));

    private static DateTimeOffset TruncatedToSeconds(DateTime value) =>
        new(EpochTime.DateTime(EpochTime.GetIntDate(value)), TimeSpan.Zero);

    private CountingRefreshMetadata MetadataFor(string issuer)
    {
        RSAParameters publicKey = signingKey.ExportParameters(includePrivateParameters: false);
        OpenIdConnectConfiguration configuration = new() { Issuer = issuer };
        configuration.SigningKeys.Add(new RsaSecurityKey(publicKey) { KeyId = SigningKeyIdentifier });
        return new CountingRefreshMetadata(configuration);
    }

    private string TokenFor(string azp, DateTime issuedAt)
    {
        SigningCredentials credentials = new(
            new RsaSecurityKey(signingKey) { KeyId = SigningKeyIdentifier },
            SecurityAlgorithms.RsaSha256);

        JwtSecurityToken token = new(
            issuer: RealmIssuer,
            audience: AuthenticationDefaults.ApiAudience,
            claims:
            [
                new Claim("sub", azp),
                new Claim("scope", "sse.streams.read"),
                new Claim("azp", azp),
                new Claim(
                    JwtRegisteredClaimNames.Iat,
                    EpochTime.GetIntDate(issuedAt).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64),
            ],
            notBefore: issuedAt.AddMinutes(-1),
            expires: issuedAt.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Wraps a real <see cref="RevokedClientSnapshot"/> — the actual §2 rule —
    /// rather than answering a fixed true/false, and records the arguments it
    /// was last called with (phase-6 review, S4).
    /// </summary>
    private sealed class CapturingRegistry(RevokedClientSnapshot snapshot) : IRevokedClientRegistry
    {
        public string? LastAzp { get; private set; }

        public DateTimeOffset? LastIssuedAt { get; private set; }

        public bool Refuses(string? azp, DateTimeOffset? issuedAt)
        {
            LastAzp = azp;
            LastIssuedAt = issuedAt;
            return snapshot.Refuses(azp, issuedAt);
        }
    }

    /// <summary>
    /// Implements the metadata-source interface directly, the same way
    /// <see cref="WhepAuthValidatorResolutionTests.UnusedMetadata"/> does,
    /// rather than wrapping a real <see cref="ConfigurationManager{T}"/>: it
    /// serves a fixed, already-built <see cref="OpenIdConnectConfiguration"/>
    /// and counts refresh requests directly, so no discovery-document JSON
    /// needs parsing at all.
    /// </summary>
    private sealed class CountingRefreshMetadata(OpenIdConnectConfiguration configuration)
        : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int RefreshRequests { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            Task.FromResult(configuration);

        public void RequestRefresh() => RefreshRequests++;
    }
}
