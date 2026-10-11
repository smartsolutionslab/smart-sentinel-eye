using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;

namespace SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;

public sealed class AuthorizeMediaMtxApiCommandHandler(IMediaMtxApiAuthenticator authenticator)
    : ICommandHandler<AuthorizeMediaMtxApiCommand, Result<bool, AuthorizeMediaMtxApiError>>
{
    public Task<Result<bool, AuthorizeMediaMtxApiError>> HandleAsync(
        AuthorizeMediaMtxApiCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        Result<bool, AuthorizeMediaMtxApiError> result = authenticator.Authorizes(command.Password)
            ? Success(true)
            : Failure(AuthorizeMediaMtxApiFailures.Unauthorized());

        return Task.FromResult(result);
    }
}
