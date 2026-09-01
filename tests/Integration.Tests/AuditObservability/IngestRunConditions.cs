using System.Globalization;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// What a measurement run reports about itself (spec 054 US2).
///
/// <para>
/// <b>A breakdown without these is not comparable with anything.</b> The figures
/// this feature produces exist to sit beside figures from another stack, and a
/// number whose provenance lives in somebody's memory of which shell they ran it
/// in cannot be set beside anything honestly.
/// </para>
///
/// <para>
/// <b><see cref="Endpoint"/> is the one that matters most</b>, and it is the one
/// no automated check can replace. An endpoint is an endpoint: nothing in a run
/// can tell the stack it was aimed at from another that happened to answer. What
/// establishes it is a person reading this line against the stack they started —
/// so the line has to be there, and has to say what was actually reached rather
/// than what was configured.
/// </para>
/// </summary>
public sealed record IngestRunConditions(
    string Environment,
    string Endpoint,
    double IntendedRatePerSecond,
    double AchievedRatePerSecond,
    string LogLevel,
    bool MeasurementSwitchOn,
    int RowsMeasured,
    int RowsMissingStamps)
{
    /// <summary>
    /// Whether the services were logging verbosely enough to be the bottleneck.
    ///
    /// <para>
    /// Development pins Debug, where this stack sustains 60–83 ev/s — below the
    /// rate the requirement names. A breakdown taken there measures the logging
    /// as much as the pipeline.
    /// </para>
    /// </summary>
    public bool LoggingIsVerbose =>
        LogLevel.StartsWith("Debug", StringComparison.OrdinalIgnoreCase)
        || LogLevel.StartsWith("Trace", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the achieved rate lands close enough to the intended one.</summary>
    public bool RateWasMet =>
        AchievedRatePerSecond >= IngestRunShape.MinimumAcceptableRate
        && AchievedRatePerSecond <= IngestRunShape.MaximumAcceptableRate;

    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"""
         environment                           : {Environment}
         endpoint reached                      : {Endpoint}
         rate, intended → achieved             : {IntendedRatePerSecond:F0} → {AchievedRatePerSecond:F1} ev/s
         service log level                     : {LogLevel}
         measurement switch                    : {(MeasurementSwitchOn ? "ON" : "OFF")}
         rows measured                         : {RowsMeasured} ({RowsMissingStamps} missing stamps)
         """);
}
