using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Domain.RegisteredClient;

namespace SmartSentinelEye.Identity.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Published DeviceRegisteredV1 for {ClientId}.")]
    public static partial void PublishedDeviceRegisteredV1(ILogger logger, ClientId clientId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Published KioskEnrolledV1 for {ClientId}.")]
    public static partial void PublishedKioskEnrolledV1(ILogger logger, ClientId clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disabled device {Identifier} '{ClientId}'.")]
    public static partial void DisabledDevice(ILogger logger, RegisteredClientIdentifier identifier, ClientId clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disabled kiosk {Identifier} '{ClientId}'.")]
    public static partial void DisabledKiosk(ILogger logger, RegisteredClientIdentifier identifier, ClientId clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rotated webhook integration '{IntegrationName}' to clientId '{ClientId}'.")]
    public static partial void RotatedWebhookIntegration(ILogger logger, string integrationName, ClientId clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Enrolled kiosk {Identifier} '{ClientId}' for fab {Fab}.")]
    public static partial void EnrolledKiosk(ILogger logger, RegisteredClientIdentifier identifier, ClientId clientId, FabIdentifier fab);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered device {Identifier} '{ClientId}' ({DeviceType}/{DeviceIdentifier}) for fab {Fab}.")]
    public static partial void RegisteredDevice(ILogger logger, RegisteredClientIdentifier identifier, ClientId clientId, string deviceType, string deviceIdentifier, FabIdentifier fab);
}
