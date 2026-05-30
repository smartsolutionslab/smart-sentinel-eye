using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Auth;

/// <summary>
/// Validates a bearer token forwarded by MediaMTX's external auth hook
/// against the same Keycloak realm as the standard JwtBearer pipeline.
/// Issuer + signing keys are fetched from the realm's OIDC discovery
/// document (cached by <see cref="ConfigurationManager{T}"/>).
/// </summary>
public sealed class WhepAuthValidator : IWhepAuthValidator
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _oidc;
    private readonly TokenValidationParameters _parameters;
    private readonly JwtSecurityTokenHandler _handler = new();

    public WhepAuthValidator(IOptions<WhepAuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string authority = options.Value.Authority.TrimEnd('/');

        // Allow an http metadata authority (dev/test/Aspire); production
        // Keycloak is https, enforced by the Helm overlay. Mirrors the standard
        // JwtBearer pipeline's RequireHttpsMetadata = false (AuthenticationDefaults).
        // Without this the default HttpDocumentRetriever requires https and throws
        // IDX20108 on the dev/CI http authority — a 500 on every WHEP authorize.
        _oidc = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = false });

        _parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = false, // mirrors JwtBearerOptions in AuthenticationDefaults
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "preferred_username",
        };

        _handler.MapInboundClaims = false;
    }

    public async Task<Option<WhepAuthSubject>> ValidateAsync(string bearerToken, CancellationToken cancellationToken)
    {
        try
        {
            OpenIdConnectConfiguration configuration =
                await _oidc.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            TokenValidationParameters parameters = _parameters.Clone();
            parameters.IssuerSigningKeys = configuration.SigningKeys;

            System.Security.Claims.ClaimsPrincipal principal = _handler.ValidateToken(
                bearerToken, parameters, out _);

            string? subject = principal.FindFirst("sub")?.Value;
            if (subject is null)
            {
                return Option<WhepAuthSubject>.None;
            }

            string scopeClaim = principal.FindFirst("scope")?.Value ?? string.Empty;
            string[] scopes = scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return Option<WhepAuthSubject>.Some(new WhepAuthSubject(subject, scopes));
        }
        catch (SecurityTokenException)
        {
            return Option<WhepAuthSubject>.None;
        }
        catch (ArgumentException)
        {
            // Some malformed-token paths in JwtSecurityTokenHandler surface
            // as ArgumentException rather than SecurityTokenException. Treat
            // both as anonymous so MediaMTX gets a clean 401.
            return Option<WhepAuthSubject>.None;
        }
    }
}

public sealed class WhepAuthOptions
{
    public const string SectionName = "WhepAuth";
    public string Authority { get; set; } = string.Empty;
}
