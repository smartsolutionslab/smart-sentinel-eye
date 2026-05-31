using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.Identity.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Minted Identity admin token; valid for {Lifetime}s.")]
    public static partial void MintedAdminToken(ILogger logger, int lifetime);

    [LoggerMessage(Level = LogLevel.Debug, Message = "DisableClientAsync('{ClientId}'): no such Keycloak client; treating as no-op.")]
    public static partial void DisableClientNoOp(ILogger logger, string clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying Identity EF Core migrations.")]
    public static partial void ApplyingMigrations(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Identity migrations applied.")]
    public static partial void MigrationsApplied(ILogger logger);
}
