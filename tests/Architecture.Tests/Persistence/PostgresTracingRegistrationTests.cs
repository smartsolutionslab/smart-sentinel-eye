using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 282 (#1140), T020 / FR-008. US2, red: observed failing on the tree as
/// it stands before T040 (and after T030, which leaves tracing disabled),
/// green once T040 flips <c>DisableTracing</c> to <c>false</c> in
/// <c>PostgresTelemetry</c>.
///
/// <para>
/// <b>Why this is red today, and for the right reason.</b> Tracing is wired
/// only in <c>ServiceDefaults.ConfigureOpenTelemetry</c>
/// (<c>Extensions.cs</c>), which this harness deliberately does not compose
/// (plan §5.2, same scope note as <c>DependencyHealthCheckGuardTests</c>).
/// Nothing inside any of the nine <c>Add{Context}Infrastructure</c> methods
/// calls <c>AddOpenTelemetry().WithTracing(...)</c> today, so
/// <c>TracerProvider</c> does not resolve at all — a missing registration,
/// not a stray compile error or a connectivity failure.
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
/// directly whether it has a listener. <see cref="ActivitySource.HasListeners"/>
/// answers by <b>name</b>, not by instance — .NET's diagnostic source
/// matches listeners against any source sharing the subscribed name — so a
/// second, independently-constructed <see cref="ActivitySource"/> of the same
/// name observes exactly what Npgsql's own source would.
/// </para>
/// </summary>
public class PostgresTracingRegistrationTests
{
    private const string NpgsqlActivitySourceName = "Npgsql";

    [Theory]
    [MemberData(nameof(PostgresComposition.AllContexts), MemberType = typeof(PostgresComposition))]
    public void The_composed_tracer_provider_listens_to_npgsqls_activity_source(string contextName)
    {
        using ServiceProvider provider = PostgresComposition.Compose(contextName);

        TracerProvider? tracerProvider = provider.GetService<TracerProvider>();
        tracerProvider.ShouldNotBeNull(
            $"{contextName}'s AddXInfrastructure registers no TracerProvider, so no database "
            + "command it issues can ever produce a span (FR-008).");

        using ActivitySource probe = new(NpgsqlActivitySourceName);
        probe.HasListeners().ShouldBeTrue(
            $"{contextName}'s TracerProvider does not listen to Npgsql's activity source "
            + $"(\"{NpgsqlActivitySourceName}\"). A database command's time stays a gap in the "
            + "request's trace instead of a span underneath it.");
    }
}
