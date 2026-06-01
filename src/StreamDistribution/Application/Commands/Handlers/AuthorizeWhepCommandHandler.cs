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

        var (path, bearerToken) = command;

        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return Result<MediaMtxPath, AuthorizeWhepError>.Failure(new AuthorizeWhepError.Unauthorized());
        }

        var subject = await whepAuth.ValidateAsync(bearerToken, cancellationToken);

        if (!subject.HasValue)
        {
            return Result<MediaMtxPath, AuthorizeWhepError>.Failure(new AuthorizeWhepError.Unauthorized());
        }

        if (!subject.Value.Scopes.Contains(RequiredScope, StringComparer.Ordinal))
        {
            return Result<MediaMtxPath, AuthorizeWhepError>.Failure(new AuthorizeWhepError.Forbidden());
        }

        var stream = await streams.GetByPathAsync(path, cancellationToken);

        if (stream.HasValue && stream.Value.State == StreamState.Offline)
        {
            return Result<MediaMtxPath, AuthorizeWhepError>.Failure(new AuthorizeWhepError.StreamUnavailable());
        }

        return Result<MediaMtxPath, AuthorizeWhepError>.Success(path);
    }
}
