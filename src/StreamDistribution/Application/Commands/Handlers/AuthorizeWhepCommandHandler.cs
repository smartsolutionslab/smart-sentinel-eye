using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Auth;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;

public sealed class AuthorizeWhepCommandHandler(
    IWhepAuthValidator whepAuth,
    IStreamRepository streams)
    : ICommandHandler<AuthorizeWhepCommand, Result<MediaMtxPath, AuthorizeWhepError>>
{
    /// <summary>
    /// Watching a stream is a read, so this gate asks for the read scope —
    /// still accepting the grandfathered management bundle, which is the rule
    /// <c>RequireScopeExtensions</c> applies to every other endpoint through its
    /// authorization policy. The two strings are repeated rather than referenced
    /// because Application stays ASP.NET-free (ADR-0051).
    ///
    /// <para>
    /// Spec 041: this asked for the management bundle <em>alone</em>, so no
    /// kiosk could pass it — not the browser kiosk, and not an enrolled device,
    /// whose bundle has never carried it. Constitution §VIII says a kiosk holds
    /// view-only scopes, and watching video is the view.
    /// </para>
    /// </summary>
    private const string RequiredScope = "sse.streams.read";

    private const string LegacyManagementBundle = "sse.management";

    public async Task<Result<MediaMtxPath, AuthorizeWhepError>> HandleAsync(
        AuthorizeWhepCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        (MediaMtxPath? path, string? bearerToken, _) = command;

        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return Failure(AuthorizeWhepFailures.Unauthorized());
        }

        Option<WhepAuthSubject> subject = await whepAuth.ValidateAsync(bearerToken, cancellationToken);

        if (!subject.HasValue)
        {
            return Failure(AuthorizeWhepFailures.Unauthorized());
        }

        if (!subject.Value.Scopes.Contains(RequiredScope, StringComparer.Ordinal)
            && !subject.Value.Scopes.Contains(LegacyManagementBundle, StringComparer.Ordinal))
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
