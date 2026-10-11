using Microsoft.Extensions.Logging.Abstractions;
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
        AuthorizeMediaMtxApiCommandHandler handler = new(authenticator, NullLogger<AuthorizeMediaMtxApiCommandHandler>.Instance);

        Result<bool, AuthorizeMediaMtxApiError> result = await handler.HandleAsync(
            new AuthorizeMediaMtxApiCommand("correct-secret"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task Authorize_with_a_non_matching_secret_returns_Unauthorized()
    {
        FakeMediaMtxApiAuthenticator authenticator = new() { Result = false };
        AuthorizeMediaMtxApiCommandHandler handler = new(authenticator, NullLogger<AuthorizeMediaMtxApiCommandHandler>.Instance);

        Result<bool, AuthorizeMediaMtxApiError> result = await handler.HandleAsync(
            new AuthorizeMediaMtxApiCommand("wrong-secret"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<AuthorizeMediaMtxApiError.Unauthorized>();
    }

    /// <summary>
    /// The nit the security review raised: a refused api call should log,
    /// mirroring <c>RefusedWhepAction</c> on the WHEP side — and the one thing
    /// that log line must never do is carry the password it refused.
    /// </summary>
    [Fact]
    public async Task Authorize_with_a_non_matching_secret_logs_the_refusal_without_the_password()
    {
        FakeMediaMtxApiAuthenticator authenticator = new() { Result = false };
        CapturingLogger<AuthorizeMediaMtxApiCommandHandler> logger = new();
        AuthorizeMediaMtxApiCommandHandler handler = new(authenticator, logger);

        await handler.HandleAsync(new AuthorizeMediaMtxApiCommand("wrong-secret"), CancellationToken.None);

        string message = logger.Entries.ShouldHaveSingleItem().Message;
        message.ShouldNotContain("wrong-secret");
    }

    /// <summary>The control: a matching secret logs nothing.</summary>
    [Fact]
    public async Task Authorize_with_a_matching_secret_logs_nothing()
    {
        FakeMediaMtxApiAuthenticator authenticator = new() { Result = true };
        CapturingLogger<AuthorizeMediaMtxApiCommandHandler> logger = new();
        AuthorizeMediaMtxApiCommandHandler handler = new(authenticator, logger);

        await handler.HandleAsync(new AuthorizeMediaMtxApiCommand("correct-secret"), CancellationToken.None);

        logger.Entries.ShouldBeEmpty();
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
        AuthorizeMediaMtxApiCommandHandler handler = new(authenticator, NullLogger<AuthorizeMediaMtxApiCommandHandler>.Instance);

        Result<bool, AuthorizeMediaMtxApiError> result = await handler.HandleAsync(
            new AuthorizeMediaMtxApiCommand("Bearer.xyz"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<AuthorizeMediaMtxApiError.Unauthorized>();
    }
}
