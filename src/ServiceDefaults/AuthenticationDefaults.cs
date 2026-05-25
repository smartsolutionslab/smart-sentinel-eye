using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    public const string AdminPolicy = "admin";

    public static IHostApplicationBuilder AddBearerAuthentication(
        this IHostApplicationBuilder builder,
        string keycloakResourceName = "keycloak",
        string realm = "smart-sentinel-eye")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keycloakResourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(realm);

        string keycloakBaseUrl =
            builder.Configuration.GetConnectionString(keycloakResourceName)
            ?? throw new InvalidOperationException(
                $"Connection string '{keycloakResourceName}' is required for JWT bearer auth.");

        string authority = $"{keycloakBaseUrl.TrimEnd('/')}/realms/{realm}";

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = "account";
                options.RequireHttpsMetadata = false; // dev/test; Helm overlay enforces in prod
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("scope", "sse.management");
            });

        return builder;
    }
}
