using System.Security.Claims;
using Moq;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authorization;

public class DefaultAuthorizationDecisionPointTests
{
    private const string Fab = "munich";

    private readonly Mock<IFabAuthorizationGuard> guard = new(MockBehavior.Strict);

    private DefaultAuthorizationDecisionPoint CreateDecisionPoint() => new(guard.Object);

    private static ClaimsPrincipal UserWithScopes(params string[] scopeClaims)
    {
        ClaimsIdentity identity = new("test");
        foreach (var scopeClaim in scopeClaims)
        {
            identity.AddClaim(new Claim("scope", scopeClaim));
        }
        return new ClaimsPrincipal(identity);
    }

    private void GuardAllows() =>
        guard
            .Setup(g => g.EnsureAccessAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

    [Fact]
    public async Task Scope_held_and_no_fab_is_allowed_without_consulting_the_fab_guard()
    {
        var decisionPoint = CreateDecisionPoint();
        var user = UserWithScopes(Scope.Sse.Cameras.Read);

        var decision = await decisionPoint.DecideAsync(
            user,
            Scope.Sse.Cameras.Read,
            Option<string>.None,
            CancellationToken.None);

        decision.IsAllowed.ShouldBeTrue();
        guard.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Scope_absent_is_denied_as_missing_scope_without_throwing()
    {
        var decisionPoint = CreateDecisionPoint();
        var user = UserWithScopes(Scope.Sse.Cameras.Read);

        var decision = await decisionPoint.DecideAsync(
            user,
            Scope.Sse.Rules.Write,
            Option<string>.None,
            CancellationToken.None);

        decision.IsAllowed.ShouldBeFalse();
        decision.Reason.ShouldBe(AuthorizationDenyReason.MissingScope);
    }

    [Fact]
    public async Task Scope_inside_a_space_separated_claim_is_found()
    {
        var decisionPoint = CreateDecisionPoint();
        var user = UserWithScopes("openid profile sse.cameras.read sse.rules.write");

        var decision = await decisionPoint.DecideAsync(
            user,
            Scope.Sse.Rules.Write,
            Option<string>.None,
            CancellationToken.None);

        decision.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Scope_spread_across_repeated_scope_claims_is_found()
    {
        var decisionPoint = CreateDecisionPoint();
        var user = UserWithScopes("openid profile", Scope.Sse.Rules.Write);

        var decision = await decisionPoint.DecideAsync(
            user,
            Scope.Sse.Rules.Write,
            Option<string>.None,
            CancellationToken.None);

        decision.IsAllowed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("sse.cameras.rea")]
    [InlineData("SSE.CAMERAS.READ")]
    public async Task Near_miss_of_the_scope_is_denied_as_missing_scope(string heldScope)
    {
        var decisionPoint = CreateDecisionPoint();
        var user = UserWithScopes(heldScope);

        var decision = await decisionPoint.DecideAsync(
            user,
            Scope.Sse.Cameras.Read,
            Option<string>.None,
            CancellationToken.None);

        decision.IsAllowed.ShouldBeFalse();
        decision.Reason.ShouldBe(AuthorizationDenyReason.MissingScope);
    }

    [Fact]
    public async Task Scope_held_and_fab_authorized_is_allowed_and_the_guard_is_asked_once_with_that_fab()
    {
        GuardAllows();
        var decisionPoint = CreateDecisionPoint();
        var user = UserWithScopes(Scope.Sse.Cameras.Read);

        var decision = await decisionPoint.DecideAsync(
            user,
            Scope.Sse.Cameras.Read,
            Option<string>.Some(Fab),
            CancellationToken.None);

        decision.IsAllowed.ShouldBeTrue();
        guard.Verify(
            g => g.EnsureAccessAsync(user, Fab, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Allowed_decision_on_a_fab_carries_the_fab()
    {
        GuardAllows();
        var decisionPoint = CreateDecisionPoint();
        var user = UserWithScopes(Scope.Sse.Cameras.Read);

        var decision = await decisionPoint.DecideAsync(
            user,
            Scope.Sse.Cameras.Read,
            Option<string>.Some(Fab),
            CancellationToken.None);

        decision.IsAllowed.ShouldBeTrue();
        decision.Fab.ShouldBe(Option<string>.Some(Fab));
    }

    [Fact]
    public async Task Fab_refused_by_the_guard_is_denied_as_fab_not_authorized_carrying_the_fab()
    {
        guard
            .Setup(g => g.EnsureAccessAsync(It.IsAny<ClaimsPrincipal>(), Fab, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FabAuthorizationException(Fab));
        var decisionPoint = CreateDecisionPoint();
        var user = UserWithScopes(Scope.Sse.Cameras.Read);

        var decision = await decisionPoint.DecideAsync(
            user,
            Scope.Sse.Cameras.Read,
            Option<string>.Some(Fab),
            CancellationToken.None);

        decision.IsAllowed.ShouldBeFalse();
        decision.Reason.ShouldBe(AuthorizationDenyReason.FabNotAuthorized);
        decision.Fab.ShouldBe(Option<string>.Some(Fab));
    }

    [Fact]
    public async Task Scope_absent_with_a_fab_is_denied_as_missing_scope_without_asking_the_guard()
    {
        var decisionPoint = CreateDecisionPoint();
        var user = UserWithScopes(Scope.Sse.Cameras.Read);

        var decision = await decisionPoint.DecideAsync(
            user,
            Scope.Sse.Rules.Write,
            Option<string>.Some(Fab),
            CancellationToken.None);

        decision.IsAllowed.ShouldBeFalse();
        decision.Reason.ShouldBe(AuthorizationDenyReason.MissingScope);
        guard.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_exception_other_than_fab_authorization_from_the_guard_propagates()
    {
        guard
            .Setup(g => g.EnsureAccessAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("guard failure"));
        var decisionPoint = CreateDecisionPoint();
        var user = UserWithScopes(Scope.Sse.Cameras.Read);

        var act = () => decisionPoint.DecideAsync(
            user,
            Scope.Sse.Cameras.Read,
            Option<string>.Some(Fab),
            CancellationToken.None);

        var exception = await Should.ThrowAsync<InvalidOperationException>(act);
        exception.Message.ShouldBe("guard failure");
    }

    [Fact]
    public async Task Null_principal_is_refused_with_a_guard_exception()
    {
        var decisionPoint = CreateDecisionPoint();

        var act = () => decisionPoint.DecideAsync(
            null!,
            Scope.Sse.Cameras.Read,
            Option<string>.None,
            CancellationToken.None);

        await Should.ThrowAsync<ArgumentNullException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_scope_is_refused_with_a_guard_exception(string scope)
    {
        var decisionPoint = CreateDecisionPoint();
        var user = UserWithScopes(Scope.Sse.Cameras.Read);

        var act = () => decisionPoint.DecideAsync(
            user,
            scope,
            Option<string>.None,
            CancellationToken.None);

        await Should.ThrowAsync<ArgumentException>(act);
    }
}
