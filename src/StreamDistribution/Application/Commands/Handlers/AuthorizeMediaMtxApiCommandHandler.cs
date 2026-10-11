using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;

namespace SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;

public sealed class AuthorizeMediaMtxApiCommandHandler(
    IMediaMtxApiAuthenticator authenticator, ILogger<AuthorizeMediaMtxApiCommandHandler> logger)
    : ICommandHandler<AuthorizeMediaMtxApiCommand, Result<bool, AuthorizeMediaMtxApiError>>
{
    public Task<Result<bool, AuthorizeMediaMtxApiError>> HandleAsync(
        AuthorizeMediaMtxApiCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        if (authenticator.Authorizes(command.Password))
        {
            return Task.FromResult<Result<bool, AuthorizeMediaMtxApiError>>(Success(true));
        }

        logger.RefusedMediaMtxApiCall();
        return Task.FromResult<Result<bool, AuthorizeMediaMtxApiError>>(
            Failure(AuthorizeMediaMtxApiFailures.Unauthorized()));
    }
}
