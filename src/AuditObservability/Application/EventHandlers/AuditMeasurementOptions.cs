namespace SmartSentinelEye.AuditObservability.Application.EventHandlers;

/// <summary>
/// Whether the audit write path records where its own time goes (spec 053).
///
/// <para>
/// <b>Off by default, and the default is the important part.</b> These stamps
/// exist to answer one question about a requirement, not to serve the product,
/// and turning them on puts measurement apparatus on a path every change in the
/// system passes through. That is a real cost, so it is opt-in and its price is
/// measured rather than argued.
/// </para>
/// </summary>
public sealed class AuditMeasurementOptions
{
    public const string SectionName = "AuditObservability:Measurement";

    /// <summary>
    /// <b>False unless something deliberately turns it on.</b> A test asserts
    /// this rather than trusting it, because a default nobody checks is a
    /// default that drifts.
    /// </summary>
    public bool RecordIngestBreakdown { get; set; }
}
