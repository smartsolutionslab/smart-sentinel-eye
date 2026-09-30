using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// US2. Observed failing before the Aspire.Npgsql wiring's metrics switch
/// was flipped on, green once <c>DisableMetrics</c> was set to <c>false</c>
/// in <c>PostgresTelemetry</c>. Same shape as
/// <c>IngestVolumeRegistrationTests</c>: a capturing
/// <see cref="BaseExporter{T}"/> attached to a real <see cref="MeterProvider"/>,
/// and a probe instrument recorded through a <see cref="Meter"/> sharing
/// Npgsql's meter name.
///
/// <para>
/// <b>Why this was red, and for the right reason.</b> No context called
/// <c>AddOpenTelemetry().WithMetrics(...).AddMeter("Npgsql")</c> anywhere.
/// This test supplies the reader itself (via
/// <see cref="PostgresComposition.Compose(string, Action{IHostApplicationBuilder}?)"/>'s
/// <c>configureMore</c> hook) so <c>MeterProvider</c> resolves either way; what
/// it cannot supply is the <c>AddMeter("Npgsql")</c> call that makes the
/// provider actually collect from that meter. Before that switch, the probe
/// instrument was recorded into a <see cref="MeterProvider"/> that was not
/// listening for it, so the exporter saw nothing — a missing registration,
/// not a connectivity failure or a compile error.
/// </para>
///
/// <para>
/// The meter name is <c>"Npgsql"</c>, read directly off the restored 10.0.3
/// assembly's <c>Npgsql.MetricsReporter.Meter.Name</c> (internal type,
/// observed via reflection — T001's fact 4).
/// </para>
/// </summary>
public class PostgresMetricsRegistrationTests
{
    private const string NpgsqlMeterName = "Npgsql";
    private const string ProbeInstrumentName = "spec282.postgres-metrics-registration-probe";

    [Theory]
    [MemberData(nameof(PostgresComposition.AllContexts), MemberType = typeof(PostgresComposition))]
    public void The_composed_meter_provider_exports_npgsqls_meter(string contextName)
    {
        CapturingExporter exporter = new();

        using ServiceProvider provider = PostgresComposition.Compose(contextName, builder =>
            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics => metrics.AddReader(new BaseExportingMetricReader(exporter))));

        MeterProvider meterProvider = provider.GetRequiredService<MeterProvider>();

        using Meter probe = new(NpgsqlMeterName);
        Counter<long> counter = probe.CreateCounter<long>(ProbeInstrumentName);
        counter.Add(1);
        meterProvider.ForceFlush();

        exporter.Names.ShouldContain(
            ProbeInstrumentName,
            $"{contextName}'s MeterProvider does not export Npgsql's meter (\"{NpgsqlMeterName}\"). "
            + "The connection-pool metrics that would make an ADR-0125 cap running at its ceiling "
            + "visible on the dashboard never leave the process (FR-009).");
    }

    /// <summary>
    /// Records the names of the instruments the provider exports, mirroring
    /// <c>IngestVolumeRegistrationTests.CapturingExporter</c>.
    /// </summary>
    private sealed class CapturingExporter : BaseExporter<Metric>
    {
        public List<string> Names { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (Metric metric in batch)
            {
                Names.Add(metric.Name);
            }

            return ExportResult.Success;
        }
    }
}
