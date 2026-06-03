using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.ScenarioSimulator;

/// <summary>
/// Source-generated log methods for the Scenario Simulator (ADR-0050).
/// </summary>
[ExcludeFromCodeCoverage] // source-generated logging glue, not business logic
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Minted scenario-simulator token (expires in {ExpiresIn}s).")]
    public static partial void MintedSimulatorToken(this ILogger logger, int expiresIn);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding scenario '{Scenario}' with {AssetCount} asset(s).")]
    public static partial void SeedingScenario(this ILogger logger, string scenario, int assetCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scenario '{Scenario}' seeded.")]
    public static partial void ScenarioSeeded(this ILogger logger, string scenario);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Active scenario '{Scenario}' not found in configuration; nothing to seed.")]
    public static partial void ScenarioNotFound(this ILogger logger, string scenario);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered camera '{Name}' -> {RtspUrl}.")]
    public static partial void CameraRegistered(this ILogger logger, string name, string rtspUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera '{Name}' already registered; skipping (idempotent).")]
    public static partial void CameraAlreadyRegistered(this ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provisioned camera-sim loop path '{Path}'.")]
    public static partial void CameraSimPathProvisioned(this ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "camera-sim path '{Path}' already exists; skipping (idempotent).")]
    public static partial void CameraSimPathAlreadyExists(this ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera URL {Url} has no path component; not a simulated camera, skipping.")]
    public static partial void SkippedNonSimulatedCamera(this ILogger logger, string url);
}
