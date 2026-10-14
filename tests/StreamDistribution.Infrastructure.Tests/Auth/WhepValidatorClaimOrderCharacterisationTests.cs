using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Revocation;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Infrastructure.Auth;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Auth;

/// <summary>
/// Characterisation (spec 351, ADR-0144), captured green on the unmodified
/// <c>WhepAuthValidator</c> before the claim reading is moved out of
/// <c>ValidateAsync</c> into private methods. The validator decides which bearer
/// token may open a stream, so what is pinned is the order it reads a validated
/// token's claims in (<c>sub</c> before the revocation registry, the registry
/// before anything is parsed), what it does with the <c>scope</c> and
/// <c>groups</c> claims, the extent of its <c>try</c> (claim reading and the
/// registry call stay inside it, with the same two catch filters) and the text
/// a log reader sees. Existing tests pin outcomes through a registry that
/// ignores order; none of them can see a call that should not have happened.
///
/// <para>
/// Counterfactual this file was seen to catch: moving the revocation check above
/// the <c>sub</c> check turns
/// <see cref="A_token_without_a_sub_is_rejected_before_the_revocation_registry_is_asked"/>
/// red (the registry is called once and the revoked-token line is logged);
/// widening the metadata <c>InvalidOperationException</c> catch to
/// <see cref="Exception"/> turns
/// <see cref="A_metadata_failure_that_is_not_an_invalid_operation_escapes_and_logs_nothing"/>
/// red.
/// </para>
///
/// <para>
/// Expected values are literals written from reading the unrefactored
/// validator. Tokens are signed with a throwaway in-memory key.
/// </para>
/// </summary>
public sealed class WhepValidatorClaimOrderCharacterisationTests : IDisposable
{
    private const string RealmIssuer = "https://keycloak.fab.example/realms/smart-sentinel-eye";

    private const string SigningKeyIdentifier = "whep-claim-order-key";

    private static readonly DateTime IssuedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly RSA signingKey = RSA.Create(2048);

    private readonly CapturingLogger<WhepAuthValidator> logs = new();

    public void Dispose() => signingKey.Dispose();

    [Fact]
    public async Task A_token_without_a_sub_is_rejected_before_the_revocation_registry_is_asked()
    {
        RecordingRegistry registry = new(refuses: true);
        var validator = ValidatorOver(new FixedMetadata(ConfigurationWithKey()), registry);
        var token = TokenWith(
            new Claim("azp", "client-x9"),
            new Claim("scope", "sse.streams.read"));

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(WhepAuthFailure.TokenRejected);
        registry.Calls.ShouldBeEmpty();
        logs.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_revoked_azp_is_rejected_after_one_registry_call_and_logged_at_debug_naming_the_client()
    {
        RecordingRegistry registry = new(refuses: true);
        var validator = ValidatorOver(new FixedMetadata(ConfigurationWithKey()), registry);
        var token = TokenWith(
            new Claim("sub", "subject-7"),
            new Claim("azp", "client-x9"),
            new Claim("scope", "sse.streams.read"));

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(WhepAuthFailure.TokenRejected);
        registry.Calls.ShouldBe([("client-x9", new DateTimeOffset(IssuedAt))]);

        var entry = logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Debug);
        entry.Message.ShouldBe(
            "Refused a token for 'client-x9': its client was disabled after the token was minted (ADR-0160).");
        entry.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task A_revoked_token_with_no_azp_is_logged_with_an_empty_client_and_asked_about_with_null()
    {
        RecordingRegistry registry = new(refuses: true);
        var validator = ValidatorOver(new FixedMetadata(ConfigurationWithKey()), registry);
        var token = TokenWith(
            new Claim("sub", "subject-7"),
            new Claim("scope", "sse.streams.read"));

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        result.Error.ShouldBe(WhepAuthFailure.TokenRejected);
        registry.Calls.ShouldBe([(null, new DateTimeOffset(IssuedAt))]);
        logs.Entries.ShouldHaveSingleItem().Message.ShouldBe(
            "Refused a token for '': its client was disabled after the token was minted (ADR-0160).");
    }

    [Fact]
    public async Task An_admitted_token_yields_its_sub_its_split_scopes_in_order_and_its_fabs_in_claim_order()
    {
        RecordingRegistry registry = new(refuses: false);
        var validator = ValidatorOver(new FixedMetadata(ConfigurationWithKey()), registry);
        var token = TokenWith(
            new Claim("sub", "subject-7"),
            new Claim("azp", "client-x9"),
            new Claim("scope", "  a  b c"),
            new Claim("groups", "/fabs/munich"),
            new Claim("groups", "/other/area-9"),
            new Claim("groups", "/fabs/berlin"),
            new Claim("groups", "/fabs/munich"));

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Subject.ShouldBe("subject-7");
        result.Value.Scopes.ShouldBe(["a", "b", "c"]);
        result.Value.Fabs.ShouldBe(["munich", "berlin"]);
        registry.Calls.ShouldBe([("client-x9", new DateTimeOffset(IssuedAt))]);
        logs.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_admitted_token_without_scope_or_group_claims_yields_empty_scopes_and_fabs()
    {
        var validator = ValidatorOver(new FixedMetadata(ConfigurationWithKey()), new RecordingRegistry(refuses: false));
        var token = TokenWith(new Claim("sub", "subject-8"));

        var result = await validator.ValidateAsync(token, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Subject.ShouldBe("subject-8");
        result.Value.Scopes.ShouldBeEmpty();
        result.Value.Fabs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_registry_that_throws_an_argument_exception_is_answered_as_a_rejected_token()
    {
        var validator = ValidatorOver(
            new FixedMetadata(ConfigurationWithKey()),
            new RecordingRegistry(refuses: false, throws: new ArgumentException("registry-boom-1")));

        var result = await validator.ValidateAsync(
            TokenWith(new Claim("sub", "subject-7"), new Claim("azp", "client-x9")),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(WhepAuthFailure.TokenRejected);
    }

    [Fact]
    public async Task A_registry_that_throws_a_stale_key_exception_is_rejected_and_requests_one_metadata_refresh()
    {
        FixedMetadata metadata = new(ConfigurationWithKey());
        var validator = ValidatorOver(
            metadata,
            new RecordingRegistry(refuses: false, throws: new SecurityTokenSignatureKeyNotFoundException("kid-unknown-2")));

        var result = await validator.ValidateAsync(
            TokenWith(new Claim("sub", "subject-7"), new Claim("azp", "client-x9")),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(WhepAuthFailure.TokenRejected);
        metadata.RefreshRequests.ShouldBe(1);
    }

    [Fact]
    public async Task A_registry_that_throws_an_invalid_operation_exception_escapes_the_validator()
    {
        var validator = ValidatorOver(
            new FixedMetadata(ConfigurationWithKey()),
            new RecordingRegistry(refuses: false, throws: new InvalidOperationException("registry-boom-3")));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => validator.ValidateAsync(
                TokenWith(new Claim("sub", "subject-7"), new Claim("azp", "client-x9")),
                CancellationToken.None));

        thrown.Message.ShouldBe("registry-boom-3");
    }

    [Fact]
    public async Task A_metadata_failure_that_is_not_an_invalid_operation_escapes_and_logs_nothing()
    {
        var validator = ValidatorOver(
            new ThrowingMetadata(new NotSupportedException("metadata-boom-2")),
            new RecordingRegistry(refuses: false));

        var thrown = await Should.ThrowAsync<NotSupportedException>(
            () => validator.ValidateAsync(
                TokenWith(new Claim("sub", "subject-7")),
                CancellationToken.None));

        thrown.Message.ShouldBe("metadata-boom-2");
        logs.Entries.ShouldBeEmpty();
    }

    private WhepAuthValidator ValidatorOver(
        IConfigurationManager<OpenIdConnectConfiguration> metadata,
        IRevokedClientRegistry registry) =>
        new(metadata, registry, logs);

    private OpenIdConnectConfiguration ConfigurationWithKey()
    {
        var publicKey = signingKey.ExportParameters(includePrivateParameters: false);
        OpenIdConnectConfiguration configuration = new() { Issuer = RealmIssuer };
        configuration.SigningKeys.Add(new RsaSecurityKey(publicKey) { KeyId = SigningKeyIdentifier });
        return configuration;
    }

    private string TokenWith(params Claim[] claims)
    {
        SigningCredentials credentials = new(
            new RsaSecurityKey(signingKey) { KeyId = SigningKeyIdentifier },
            SecurityAlgorithms.RsaSha256);

        List<Claim> all =
        [
            .. claims,
            new Claim(
                JwtRegisteredClaimNames.Iat,
                EpochTime.GetIntDate(IssuedAt).ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
        ];

        JwtSecurityToken token = new(
            issuer: RealmIssuer,
            audience: AuthenticationDefaults.ApiAudience,
            claims: all,
            notBefore: IssuedAt,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class RecordingRegistry(bool refuses, Exception? throws = null) : IRevokedClientRegistry
    {
        public List<(string? Azp, DateTimeOffset? IssuedAt)> Calls { get; } = [];

        public bool Refuses(string? azp, DateTimeOffset? issuedAt)
        {
            Calls.Add((azp, issuedAt));

            if (throws is not null)
            {
                throw throws;
            }

            return refuses;
        }
    }

    private sealed class FixedMetadata(OpenIdConnectConfiguration configuration)
        : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int RefreshRequests { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            Task.FromResult(configuration);

        public void RequestRefresh() => RefreshRequests++;
    }

    private sealed class ThrowingMetadata(Exception failure) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            Task.FromException<OpenIdConnectConfiguration>(failure);

        public void RequestRefresh()
        {
        }
    }
}
