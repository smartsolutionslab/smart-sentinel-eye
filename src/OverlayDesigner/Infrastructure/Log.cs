using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.OverlayDesigner.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Applying OverlayDesigner EF Core migrations.")]
    public static partial void ApplyingMigrations(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "OverlayDesigner migrations applied.")]
    public static partial void MigrationsApplied(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Darkened an element colour over the luminance cap: revision {RevisionIdentifier} element {Ordinal}, {PreviousColour} to {MigratedColour}.")]
    public static partial void ColourDarkened(
        this ILogger logger,
        Guid revisionIdentifier,
        int ordinal,
        string previousColour,
        string migratedColour);

    [LoggerMessage(Level = LogLevel.Information, Message = "OverlayDesigner colour cap applied; {Count} element colours darkened.")]
    public static partial void ColourCapApplied(this ILogger logger, int count);
}
