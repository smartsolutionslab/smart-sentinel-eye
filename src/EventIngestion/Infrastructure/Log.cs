using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Persistence loop started.")]
    public static partial void PersistenceLoopStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Persistence loop stopping (cancellation).")]
    public static partial void PersistenceLoopStopping(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ingest failed for {Identifier} ({Source}/{Device}): {Code}")]
    public static partial void IngestFailed(ILogger logger, EventIdentifier identifier, Source source, DeviceIdentifier device, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "MQTT subscriber started; subscribed to topic '{Topic}' at QoS 1.")]
    public static partial void MqttSubscriberStarted(ILogger logger, string topic);

    [LoggerMessage(Level = LogLevel.Information, Message = "MQTT subscriber stopped.")]
    public static partial void MqttSubscriberStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejecting MQTT delivery on '{Topic}': {Error}")]
    public static partial void RejectingMqttDelivery(ILogger logger, string topic, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to capture dead letter for topic '{Topic}': {Message}")]
    public static partial void DeadLetterCaptureFailed(ILogger logger, Exception exception, string topic, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "No per-fab partitions under 'events' yet; skipping rollover. Add a fab via 'CREATE TABLE events_<fabId> PARTITION OF events FOR VALUES IN (...)'.")]
    public static partial void NoFabPartitions(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ensured partition {Partition} (FROM {From} TO {To}).")]
    public static partial void EnsuredPartition(ILogger logger, string partition, string from, string to);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying EventIngestion EF Core migrations.")]
    public static partial void ApplyingMigrations(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "EventIngestion migrations applied.")]
    public static partial void MigrationsApplied(ILogger logger);
}
