using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.SystemVariables.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "ReverseIndex seed: overlay-designer returned {Status}; the index is empty. Attempt {Attempt} failed, retrying in {RetryDelay}.")]
    public static partial void SeedNonSuccessStatus(this ILogger logger, HttpStatusCode status, int attempt, TimeSpan retryDelay);

    /// <summary>
    /// Error, not Warning: a refused credential is not an outage. No republish
    /// repairs it, because the overlays that were already published raise no
    /// event, so only a later attempt can fill the index. The seeder retries it
    /// (spec 465, FR-008), so a corrected credential takes effect without a
    /// restart; until then every variable change resolves against nothing.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "ReverseIndex seed: overlay-designer refused the system-variables-seeder credential ({Status}). The index is empty. Attempt {Attempt} failed, retrying in {RetryDelay}. Check the client's secret and its sse.overlays.read scope.")]
    public static partial void SeedRefused(this ILogger logger, HttpStatusCode status, int attempt, TimeSpan retryDelay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ReverseIndex seed: response missing 'published' key; the index is empty. Attempt {Attempt} failed, retrying in {RetryDelay}.")]
    public static partial void SeedMissingPublishedKey(this ILogger logger, int attempt, TimeSpan retryDelay);

    [LoggerMessage(Level = LogLevel.Information, Message = "ReverseIndex seeded with {Count} published overlays.")]
    public static partial void SeededOverlays(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ReverseIndex seed failed; the index is empty. Attempt {Attempt} failed, retrying in {RetryDelay}.")]
    public static partial void SeedFailed(this ILogger logger, Exception exception, int attempt, TimeSpan retryDelay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying SystemVariables EF Core migrations.")]
    public static partial void ApplyingMigrations(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SystemVariables migrations applied.")]
    public static partial void MigrationsApplied(this ILogger logger);
}
