using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.ServiceDefaults.Revocation;

namespace SmartSentinelEye.ServiceDefaults.Tests.Revocation;

/// <summary>
/// Phase-6 review of spec 270 (ADR-0160), S7: <c>AddRevocationCheck</c> wires
/// <c>RevokedClientRefresher</c> as <c>TryAddSingleton</c> and then adds it as
/// a hosted service by resolving that same singleton
/// (<c>AddHostedService(sp => sp.GetRequiredService&lt;RevokedClientRefresher&gt;())</c>),
/// so the background loop and <c>RevokedClientSnapshotHealthCheck</c> (which
/// also resolves <see cref="RevokedClientRefresher"/> directly for
/// <c>LastSuccessAt</c>) all observe one instance. A future "simplification"
/// to <c>AddHostedService&lt;RevokedClientRefresher&gt;()</c> would silently
/// create a second, never-run instance whose <c>LastSuccessAt</c> stays
/// <see langword="null"/> forever — this test fails loudly if that happens.
/// </summary>
public sealed class RevokedClientRefresherWiringTests
{
    [Fact]
    public void The_hosted_refresher_is_the_same_instance_the_health_check_reads()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid";

        builder.AddBearerAuthentication();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        RevokedClientRefresher resolvedDirectly = provider.GetRequiredService<RevokedClientRefresher>();
        IHostedService hostedRefresher = provider.GetServices<IHostedService>()
            .OfType<RevokedClientRefresher>()
            .ShouldHaveSingleItem(
                "AddRevocationCheck must register RevokedClientRefresher as a hosted service exactly "
                + "once, driving both the background loop and the health check.");

        hostedRefresher.ShouldBeSameAs(
            resolvedDirectly,
            "the hosted service and the singleton the health check resolves must be the same "
            + "instance; AddHostedService<RevokedClientRefresher>() instead of resolving the "
            + "existing singleton would silently create a second, never-run refresher.");
    }
}
