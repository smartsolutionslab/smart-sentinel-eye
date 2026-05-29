using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace SmartSentinelEye.ServiceDefaults.Authorization;

/// <summary>
/// Policy + endpoint helpers that translate
/// <see cref="Scope.SSE"/> constants into ASP.NET Core
/// authorization policies (spec 008 FR-018).
///
/// <para>
/// Keycloak emits the OIDC <c>scope</c> claim as a single
/// space-separated string (e.g.
/// <c>"openid profile sse.cameras.read sse.rules.write"</c>),
/// so each policy splits and looks for the target token.
/// </para>
/// </summary>
public static class RequireScopeExtensions
{
    /// <summary>
    /// Registers one policy per <paramref name="scopes"/> entry.
    /// Each policy name equals the scope string itself, so
    /// endpoints declare authorization with
    /// <c>.RequireAuthorization(Scope.SSE.Rules.Write)</c>.
    /// </summary>
    public static AuthorizationBuilder AddScopePolicies(
        this AuthorizationBuilder builder, IEnumerable<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scopes);

        foreach (string scope in scopes)
        {
            builder.AddPolicy(scope, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                {
                    foreach (System.Security.Claims.Claim claim in context.User.FindAll("scope"))
                    {
                        string[] tokens = claim.Value.Split(
                            ' ', StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Contains(scope, StringComparer.Ordinal))
                        {
                            return true;
                        }
                    }
                    return false;
                });
            });
        }
        return builder;
    }

    /// <summary>
    /// Convenience extension on the endpoint builder so the call
    /// site reads naturally:
    /// <code>group.MapPost("/", Create).RequireScope(Scope.SSE.Rules.Write);</code>
    /// </summary>
    public static RouteHandlerBuilder RequireScope(
        this RouteHandlerBuilder builder, string scope)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return builder.RequireAuthorization(scope);
    }
}
