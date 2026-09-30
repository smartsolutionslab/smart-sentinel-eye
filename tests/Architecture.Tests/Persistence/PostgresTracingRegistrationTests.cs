using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// US2. Observed red before the Aspire.Npgsql wiring's tracing switch was
/// flipped on (and after the initial wiring, which left tracing disabled);
/// green once <c>DisableTracing</c> was set to <c>false</c> in
/// <c>PostgresTelemetry</c>.
///
/// <para>
/// <b>Why this was red, and for the right reason.</b> Tracing was wired
/// only in <c>ServiceDefaults.ConfigureOpenTelemetry</c>
/// (<c>Extensions.cs</c>), which this harness deliberately does not compose
/// (plan §5.2, same scope note as <c>DependencyHealthCheckGuardTests</c>).
/// Nothing inside any of the nine <c>Add{Context}Infrastructure</c> methods
/// called <c>AddOpenTelemetry().WithTracing(...).AddNpgsql()</c> (or
/// equivalent), so the composed <c>TracerProvider</c> — which this test's own
/// <c>configureMore</c> hook always registers, to make the provider and its
/// capturing exporter resolvable either way — was never subscribed to
/// Npgsql's activity source. The probe activity below therefore went
/// unsampled and the exporter captured nothing: a missing subscription, not
/// a stray compile error or a connectivity failure.
/// </para>
///
/// <para>
/// The source name is <c>"Npgsql"</c>, read directly off the restored 10.0.3
/// assembly's <c>Npgsql.NpgsqlActivitySource.Source.Name</c> (internal type,
/// observed via reflection rather than guessed — T001's fact 4 in the
/// engineer's brief) rather than assumed from a naming convention.
/// </para>
///
/// <para>
/// <b>Why a probe <see cref="ActivitySource"/> and not Npgsql's own.</b>
/// <c>Npgsql.NpgsqlActivitySource</c> is internal, so this test cannot ask it
/// directly whether it has a listener. .NET's diagnostic source matches
/// listeners against any source sharing the subscribed name — so a second,
/// independently-constructed <see cref="ActivitySource"/> of the same name
/// observes exactly what Npgsql's own source would.
/// </para>
///
/// <para>
/// <b>Why this asserts an exported activity rather than
/// <see cref="ActivitySource.HasListeners"/>.</b>
/// <c>HasListeners()</c> answers process-wide, by source name: it is true
/// the moment <b>anything</b> in the process subscribes an
/// <see cref="ActivityListener"/> to <c>"Npgsql"</c>, regardless of which
/// context's (or which concurrently-running test's) <c>TracerProvider</c>
/// did the subscribing. It cannot prove that <em>this</em> context's own
/// composition is what listens. Instead, this test attaches a capturing
/// exporter to <em>this</em> context's own <c>TracerProvider</c> via
/// <c>PostgresComposition.Compose</c>'s <c>configureMore</c> hook — the same
/// per-provider shape <c>PostgresMetricsRegistrationTests</c> uses — starts
/// an activity from the probe source, and asserts that this specific
/// provider's own export pipeline captured it. A listener belonging to some
/// unrelated provider cannot make this pass, because export only flows
/// through processors registered on the provider under test.
/// </para>
/// </summary>
public class PostgresTracingRegistrationTests
{
    private const string NpgsqlActivitySourceName = "Npgsql";
    private const string ProbeActivityName = "spec282.postgres-tracing-registration-probe";

    [Theory]
    [MemberData(nameof(PostgresComposition.AllContexts), MemberType = typeof(PostgresComposition))]
    public void The_composed_tracer_provider_listens_to_npgsqls_activity_source(string contextName)
    {
        CapturingExporter exporter = new();

        using ServiceProvider provider = PostgresComposition.Compose(contextName, builder =>
            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter))));

        TracerProvider tracerProvider = provider.GetRequiredService<TracerProvider>();

        using ActivitySource probe = new(NpgsqlActivitySourceName);
        using Activity? activity = probe.StartActivity(ProbeActivityName);
        activity?.Stop();

        tracerProvider.ForceFlush();

        exporter.Captured.ShouldContain(
            captured => captured.OperationName == ProbeActivityName,
            $"{contextName}'s TracerProvider does not export activities from Npgsql's activity "
            + $"source (\"{NpgsqlActivitySourceName}\"). A database command's time stays a gap in "
            + "the request's trace instead of a span underneath it.");
    }

    /// <summary>
    /// Records every activity the processor sees, mirroring
    /// <c>PostgresSpanIntegrationTests.CapturingExporter</c>'s shape.
    /// </summary>
    private sealed class CapturingExporter : BaseExporter<Activity>
    {
        public List<Activity> Captured { get; } = [];

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (Activity activity in batch)
            {
                Captured.Add(activity);
            }

            return ExportResult.Success;
        }
    }
}
