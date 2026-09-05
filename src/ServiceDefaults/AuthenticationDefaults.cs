using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Configures JWT bearer authentication against the Keycloak realm exposed
/// by Aspire (ADR-0007 + ADR-0008 + ADR-0023). Adds the "admin" authorisation
/// policy that gates management endpoints; full Identity context lands in
/// a follow-up spec.
///
/// The Keycloak base URL is read from the Aspire-injected connection
/// string for the named keycloak resource. The realm path is appended.
/// </summary>
public static class AuthenticationDefaults
{
    /// <summary>
    /// Legacy bundle policy from spec 005/006/007 era. Carries
    /// every <c>*.write</c> scope; new endpoints should use the
    /// resource-shaped <see cref="Scope"/> catalogue instead.
    /// Scheduled for removal in spec 009 once every endpoint has
    /// migrated.
    /// </summary>
#pragma warning disable S1133 // tracked for removal in spec 009 via issue #844
    [Obsolete("Use the sse.* scope policies via Scope catalogue + RequireScope instead. Removed in spec 009.")]
    public const string AdminPolicy = "admin";
#pragma warning restore S1133

    public static IHostApplicationBuilder AddBearerAuthentication(
        this IHostApplicationBuilder builder,
        string keycloakResourceName = "keycloak",
        string realm = "smart-sentinel-eye")
    {
        Ensure.That(keycloakResourceName).IsNotNull().IsNotNullOrWhiteSpace();
        Ensure.That(realm).IsNotNull().IsNotNullOrWhiteSpace();

        // Aspire publishes Keycloak under one of three keys depending on
        // the dev-cert / HTTPS-upgrade configuration. Accept any of them.
        string keycloakBaseUrl =
            builder.Configuration.GetConnectionString(keycloakResourceName)
            ?? builder.Configuration[$"services:{keycloakResourceName}:http:0"]
            ?? builder.Configuration[$"services:{keycloakResourceName}:https:0"]
            ?? throw new InvalidOperationException(
                $"Keycloak base URL not found. Looked for ConnectionStrings:{keycloakResourceName}, " +
                $"services:{keycloakResourceName}:http:0, services:{keycloakResourceName}:https:0.");

        string authority = $"{keycloakBaseUrl.TrimEnd('/')}/realms/{realm}";

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.RequireHttpsMetadata = false; // dev/test; Helm overlay enforces in prod
                // The audience arrives on the sse-audience client scope, which
                // every client in the realm carries as a default scope; clients
                // created at runtime get it from
                // KeycloakScopeBundles.AudienceScope, the only route open to
                // them because their representation has no mapper field.
                // Set on the collection, not on options.Audience: that one only
                // seeds the singular ValidAudience, and every later reader of
                // these options asks ValidAudiences.
                options.TokenValidationParameters.ValidAudiences = [ApiAudience];
                // Preserve original JWT claim types (`sub`, `scope`, …) instead
                // of remapping them to legacy WS-* URIs. Endpoints read `sub`
                // directly to build OperatorIdentifier.
                options.MapInboundClaims = false;
            });

        builder.Services.AddSingleton<IFabAuthorizationGuard, DefaultFabAuthorizationGuard>();
        // First: a request the caller got wrong is not a server failure, and
        // saying 500 sends them away to retry something that cannot succeed.
        builder.Services.AddExceptionHandler<BadHttpRequestExceptionHandler>();
        builder.Services.AddExceptionHandler<FabAuthorizationExceptionHandler>();
        builder.Services.AddExceptionHandler<UnattributableOperatorExceptionHandler>();
        builder.Services.AddExceptionHandler<ConcurrencyConflictExceptionHandler>();
        // After the concurrency handler, and the reason is worth more than the
        // rule: DbUpdateConcurrencyException derives from DbUpdateException, so
        // a handler matching the base type ahead of it would swallow every lost
        // update and report it as a name collision. UniqueConstraintExceptionHandler
        // matches the SQLSTATE instead, so it cannot — this ordering is the
        // second defence, kept because a later edit might widen that match.
        builder.Services.AddExceptionHandler<UniqueConstraintExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthorizationBuilder()
            .AddScopePolicies(Scope.All)
#pragma warning disable CS0618 // legacy policy registered for the spec 005-007 endpoints during the spec 008 PR-F migration
            .AddPolicy(AdminPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                // Keycloak emits "scope" as a single space-separated claim
                // ("openid profile sse.management"), so RequireClaim with a
                // value never matches the substring. Split and search.
                policy.RequireAssertion(context =>
                    context.User.FindAll("scope").Any(claim =>
                        claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Contains(ManagementScope, StringComparer.Ordinal)));
            });
#pragma warning restore CS0618

        return builder;
    }

    public const string ManagementScope = "sse.management";

    /// <summary>
    /// The realm client a browser kiosk authenticates as, carried in the token's
    /// <c>azp</c> claim.
    /// </summary>
    /// <remarks>
    /// Hard-coded rather than configured, and pinned by
    /// <c>KioskScopeParityTests</c> against the realm JSON — the same
    /// answer it already gives for the kiosk's scopes.
    /// A constant that cannot silently drift is cheaper here than plumbing an
    /// option through a context that needs nothing else from configuration.
    /// </remarks>
    public const string KioskClientId = "kiosk-web";

    /// <summary>
    /// The API every token in this realm is minted for, checked against the
    /// access token's <c>aud</c> claim.
    /// </summary>
    /// <remarks>
    /// The realm emits it from the <c>sse-audience</c> client scope. The literal
    /// is spelt out a second time in <c>RealmAudienceTests</c>, which reads the
    /// realm file, and a third in <c>BearerAudienceTests</c>, which reads these
    /// options — so the realm and the services cannot drift apart in silence
    /// (spec 069 FR-009).
    /// </remarks>
    public const string ApiAudience = "smart-sentinel-eye-api";
}
