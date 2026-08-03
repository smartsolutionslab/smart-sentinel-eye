using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;

public sealed class AuthorizeWhepCommandHandler(IWhepAuthValidator whepAuth, IStreamRepository streams)
    : ICommandHandler<AuthorizeWhepCommand, Result<MediaMtxPath, AuthorizeWhepError>>
{
    private const string RequiredScope = "sse.management";

    public async Task<Result<MediaMtxPath, AuthorizeWhepError>> HandleAsync(AuthorizeWhepCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        (MediaMtxPath? path, string? bearerToken) = command;

        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return Failure(AuthorizeWhepFailures.Unauthorized());
        }

        Option<WhepAuthSubject> subject = await whepAuth.ValidateAsync(bearerToken, cancellationToken);

        if (!subject.HasValue)
        {
            return Failure(AuthorizeWhepFailures.Unauthorized());
        }

        if (!subject.Value.Scopes.Contains(RequiredScope, StringComparer.Ordinal))
        {
            return Failure(AuthorizeWhepFailures.Forbidden());
        }

        Option<Stream> stream = await streams.GetByPathAsync(path, cancellationToken);

        if (stream.HasValue && stream.Value.State == StreamState.Offline)
        {
            return Failure(AuthorizeWhepFailures.StreamUnavailable());
        }

        return Success(path);
    }
}
