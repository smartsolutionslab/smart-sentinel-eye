using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.ServiceDefaults.Authorization;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(keycloakResourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(realm);

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

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.RequireHttpsMetadata = false; // dev/test; Helm overlay enforces in prod
                // Audience validation is delegated to the scope policy below.
                // A dedicated bearer-only Keycloak client + audience mapper
                // lands when the Identity context is built out (spec TBD).
                options.TokenValidationParameters.ValidateAudience = false;
                // Preserve original JWT claim types (`sub`, `scope`, …) instead
                // of remapping them to legacy WS-* URIs. Endpoints read `sub`
                // directly to build OperatorIdentifier.
                options.MapInboundClaims = false;
            });

        builder.Services.AddSingleton<IFabAuthorizationGuard, DefaultFabAuthorizationGuard>();
        builder.Services.AddExceptionHandler<FabAuthorizationExceptionHandler>();
        builder.Services.AddExceptionHandler<UnattributableOperatorExceptionHandler>();
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
}
