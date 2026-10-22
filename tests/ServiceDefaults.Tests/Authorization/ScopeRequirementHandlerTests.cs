using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authorization;

public class ScopeRequirementHandlerTests
{
    private static ServiceProvider BuildProvider(IAuthorizationDecisionPoint decisionPoint)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(decisionPoint);
        services.AddAuthorizationBuilder().AddScopePolicies(Scope.All);
        return services.BuildServiceProvider();
    }

    private static Mock<IAuthorizationDecisionPoint> DecisionPointAnswering(AuthorizationDecision decision)
    {
        Mock<IAuthorizationDecisionPoint> decisionPoint = new();
        decisionPoint
            .Setup(d => d.DecideAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<string>(),
                It.IsAny<Option<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(decision);
        return decisionPoint;
    }

    private static ClaimsPrincipal UserWithScopes(params string[] scopes)
    {
        ClaimsIdentity identity = new("test");
        identity.AddClaim(new Claim("scope", string.Join(' ', scopes)));
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task Policy_fails_when_the_decision_point_denies_a_principal_that_holds_the_scope()
    {
        var decisionPoint = DecisionPointAnswering(
            AuthorizationDecision.Deny(AuthorizationDenyReason.MissingScope, Option<string>.None));
        using var provider = BuildProvider(decisionPoint.Object);
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = UserWithScopes(Scope.Sse.Rules.Write);

        var result = await authorization.AuthorizeAsync(user, null, Scope.Sse.Rules.Write);

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Policy_succeeds_when_the_decision_point_allows_a_principal_that_lacks_the_scope()
    {
        var decisionPoint = DecisionPointAnswering(AuthorizationDecision.Allow());
        using var provider = BuildProvider(decisionPoint.Object);
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = UserWithScopes(Scope.Sse.Cameras.Read);

        var result = await authorization.AuthorizeAsync(user, null, Scope.Sse.Rules.Write);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Policy_asks_the_decision_point_about_its_own_scope_with_no_fab()
    {
        var decisionPoint = DecisionPointAnswering(AuthorizationDecision.Allow());
        using var provider = BuildProvider(decisionPoint.Object);
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = UserWithScopes(Scope.Sse.Rules.Write);

        await authorization.AuthorizeAsync(user, null, Scope.Sse.Rules.Write);

        decisionPoint.Verify(
            d => d.DecideAsync(
                user,
                Scope.Sse.Rules.Write,
                Option<string>.None,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Every_scope_policy_carries_a_scope_requirement_for_its_own_scope()
    {
        using var provider = BuildProvider(DecisionPointAnswering(AuthorizationDecision.Allow()).Object);
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        List<string> withoutRequirement = [];
        foreach (var scope in Scope.All)
        {
            var policy = await policies.GetPolicyAsync(scope);
            var requirements = policy?.Requirements.OfType<ScopeRequirement>() ?? [];
            if (!requirements.Any(requirement => requirement.Scope == scope))
            {
                withoutRequirement.Add(scope);
            }
        }

        withoutRequirement.ShouldBeEmpty(
            $"every scope policy must route through the decision point, but these do not: {string.Join(", ", withoutRequirement)}.");
    }
}
