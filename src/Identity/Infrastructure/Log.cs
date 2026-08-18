using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.Identity.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Minted Identity admin token; valid for {Lifetime}s.")]
    public static partial void MintedAdminToken(this ILogger logger, int lifetime);

    [LoggerMessage(Level = LogLevel.Debug, Message = "DisableClientAsync('{ClientId}'): no such Keycloak client; treating as no-op.")]
    public static partial void DisableClientNoOp(this ILogger logger, string clientId);

    // Spec 019. Not an error here — the caller decides what an absent parent
    // means. It means something for '/fabs' (no fab can be provisioned) and
    // nothing for a path nobody has created yet.
    [LoggerMessage(Level = LogLevel.Warning, Message = "No group at path '{ParentPath}' in realm '{Realm}'; no sub-groups to report.")]
    public static partial void FabGroupParentMissing(this ILogger logger, string parentPath, string realm);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying Identity EF Core migrations.")]
    public static partial void ApplyingMigrations(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Identity migrations applied.")]
    public static partial void MigrationsApplied(this ILogger logger);
}
