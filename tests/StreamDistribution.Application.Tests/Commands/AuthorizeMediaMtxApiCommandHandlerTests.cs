using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Commands;

/// <summary>
/// ADR-0171. <see cref="AuthorizeMediaMtxApiCommandHandler"/> has exactly one
/// decision to make: does the presented password match the configured
/// secret. <see cref="IMediaMtxApiAuthenticator"/> owns the actual
/// comparison (constant-time, in Infrastructure); these tests only verify
/// the handler wires its answer to the right <c>Result</c>.
/// </summary>
public class AuthorizeMediaMtxApiCommandHandlerTests
{
    [Fact]
    public async Task Authorize_with_a_matching_secret_returns_success()
    {
        FakeMediaMtxApiAuthenticator authenticator = new() { Result = true };
        AuthorizeMediaMtxApiCommandHandler handler = new(authenticator);

        Result<bool, AuthorizeMediaMtxApiError> result = await handler.HandleAsync(
            new AuthorizeMediaMtxApiCommand("correct-secret"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task Authorize_with_a_non_matching_secret_returns_Unauthorized()
    {
        FakeMediaMtxApiAuthenticator authenticator = new() { Result = false };
        AuthorizeMediaMtxApiCommandHandler handler = new(authenticator);

        Result<bool, AuthorizeMediaMtxApiError> result = await handler.HandleAsync(
            new AuthorizeMediaMtxApiCommand("wrong-secret"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<AuthorizeMediaMtxApiError.Unauthorized>();
    }

    /// <summary>
    /// A WHEP viewer's bearer token has no reason to also be the control-API
    /// secret, and this handler never looks at <see cref="IWhepAuthValidator"/>
    /// at all — it is wired to <see cref="IMediaMtxApiAuthenticator"/> only.
    /// This is the control for that separation: a password that happens to
    /// look like a bearer token still goes through the same secret check and
    /// is refused exactly like any other non-matching value.
    /// </summary>
    [Fact]
    public async Task Authorize_with_a_whep_bearer_token_as_the_password_returns_Unauthorized()
    {
        FakeMediaMtxApiAuthenticator authenticator = new() { Result = false };
        AuthorizeMediaMtxApiCommandHandler handler = new(authenticator);

        Result<bool, AuthorizeMediaMtxApiError> result = await handler.HandleAsync(
            new AuthorizeMediaMtxApiCommand("Bearer.xyz"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<AuthorizeMediaMtxApiError.Unauthorized>();
    }
}
