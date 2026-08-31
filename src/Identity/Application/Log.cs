using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Domain.RegisteredClient;

namespace SmartSentinelEye.Identity.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    // Spec 052. Reported at Information because "how many kiosk accounts still
    // hold a privilege they should not" is the sort of thing worth being able to
    // read back after the fact.
    [LoggerMessage(Level = LogLevel.Information, Message = "Stripped inherited realm privileges from {StrippedCount} of {KioskCount} enrolled kiosk accounts.")]
    public static partial void SweptKioskPrivileges(this ILogger logger, int strippedCount, int kioskCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not strip inherited realm privileges from kiosk '{ClientId}'; it still holds them.")]
    public static partial void CouldNotSweepKiosk(this ILogger logger, string clientId, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Published DeviceRegisteredV1 for {ClientId}.")]
    public static partial void PublishedDeviceRegisteredV1(this ILogger logger, ClientId clientId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Published KioskEnrolledV1 for {ClientId}.")]
    public static partial void PublishedKioskEnrolledV1(this ILogger logger, ClientId clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disabled device {Identifier} '{ClientId}'.")]
    public static partial void DisabledDevice(this ILogger logger, RegisteredClientIdentifier identifier, ClientId clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disabled kiosk {Identifier} '{ClientId}'.")]
    public static partial void DisabledKiosk(this ILogger logger, RegisteredClientIdentifier identifier, ClientId clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rotated webhook integration '{IntegrationName}' to clientId '{ClientId}'.")]
    public static partial void RotatedWebhookIntegration(this ILogger logger, string integrationName, ClientId clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Enrolled kiosk {Identifier} '{ClientId}' for fab {Fab}.")]
    public static partial void EnrolledKiosk(this ILogger logger, RegisteredClientIdentifier identifier, ClientId clientId, FabIdentifier fab);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered device {Identifier} '{ClientId}' ({DeviceType}/{DeviceIdentifier}) for fab {Fab}.")]
    public static partial void RegisteredDevice(this ILogger logger, RegisteredClientIdentifier identifier, ClientId clientId, string deviceType, string deviceIdentifier, FabIdentifier fab);
}
