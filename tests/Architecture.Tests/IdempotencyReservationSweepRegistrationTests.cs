using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Automation.Infrastructure;
using SmartSentinelEye.Automation.Infrastructure.Persistence;
using SmartSentinelEye.CameraCatalog.Infrastructure;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.EventIngestion.Infrastructure;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Identity.Infrastructure;
using SmartSentinelEye.Identity.Infrastructure.Persistence;
using SmartSentinelEye.LayoutComposition.Infrastructure;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;
using SmartSentinelEye.OverlayDesigner.Infrastructure;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.SystemVariables.Infrastructure;
using SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Issue #2690, split from #2491 at that issue's phase 3 — the opposite
/// phase-4a colour (this is observed <b>green</b>, not red: all seven
/// registrations already exist) guarding a different kind of defect in a
/// different project than #2491's fencing-token race fix.
///
/// <para>
/// Precedent: <see cref="KioskPrivilegeSweepRegistrationTests"/> closed
/// exactly this gap for a different sweep after it shipped registered
/// nowhere (#2132) — its unit tests stayed green because every one of them
/// constructs the class directly, which is precisely what nothing in
/// production does. Startup wiring is invisible from a test that drives the
/// pass itself, so container introspection at this layer is the only place
/// the gap can be asserted.
/// </para>
///
/// <para>
/// <b>What this proves: that the registration exists. Nothing else.</b> A
/// green run here says <c>AddIdempotencyReservationSweep&lt;TDbContext&gt;</c>
/// was called for the matching <c>TDbContext</c>; it says nothing about
/// whether the sweep ever actually runs, reclaims a reservation, or is wired
/// with a sane interval.
/// </para>
///
/// <para>
/// <c>IdempotencyReservationSweepHostedService&lt;TDbContext&gt;</c> is a
/// generic type declared in <c>ServiceDefaults</c>, not in any context's own
/// assembly — unlike <c>KioskPrivilegeSweepHostedService</c>, which lives in
/// <c>Identity.Infrastructure</c> and so can be matched by
/// "<c>IHostedService</c> whose implementation comes from this module's own
/// assembly". That filter cannot distinguish one context's sweep from
/// another's here, because every context's closed generic type is produced
/// by the same open generic in the same foreign assembly. So each context is
/// checked against its own closed generic type instead, built from the
/// concrete <c>TDbContext</c> named at that call site
/// (<c>AutomationDbContext</c>, <c>CameraCatalogDbContext</c>, …) — the
/// registration this guard exists to catch missing or, just as wrong,
/// pointed at a different context's <c>DbContext</c>.
/// </para>
///
/// <para>
/// Seven contexts, not all nine that exist in this solution:
/// <c>audit-observability</c> and <c>stream-distribution</c> have no
/// idempotent write path at all and never call
/// <c>AddIdempotencyReservationSweep</c> — confirmed absent by inspection,
/// not merely omitted from this list by oversight.
/// </para>
/// </summary>
public class IdempotencyReservationSweepRegistrationTests
{
    /// <summary>
    /// Nothing here is dialled: each <c>Add{Context}Infrastructure</c> call
    /// only parses these values while registering (mirrors
    /// <c>KioskPrivilegeSweepRegistrationTests.IdentityInfrastructure</c> and
    /// the shared <c>PostgresComposition</c> harness's per-context
    /// configuration). No host is built and no service is resolved, so
    /// nothing here constructs a hosted service or opens a connection —
    /// this only inspects the registration list Wolverine and every other
    /// call appended to it.
    /// </summary>
    private static IServiceCollection Automation()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@127.0.0.1:5672",
            ["ConnectionStrings:automation-db"] =
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
        });

        builder.AddAutomationInfrastructure();

        return builder.Services;
    }

    private static IServiceCollection CameraCatalog()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@127.0.0.1:5672",
            ["ConnectionStrings:camera-catalog-db"] =
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
        });

        builder.AddCameraCatalogInfrastructure();

        return builder.Services;
    }

    private static IServiceCollection EventIngestion()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@127.0.0.1:5672",
            ["ConnectionStrings:event-ingestion-db"] =
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:keycloak"] = "http://127.0.0.1:8080",
            ["Mosquitto:Endpoint"] = "127.0.0.1:1883",
        });

        builder.AddEventIngestionInfrastructure();

        return builder.Services;
    }

    private static IServiceCollection Identity()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:keycloak"] = "http://127.0.0.1:8080",
            ["ConnectionStrings:identity-db"] =
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:rabbitmq"] = "amqp://unused:unused@127.0.0.1:5672",
        });

        builder.AddIdentityInfrastructure();

        return builder.Services;
    }

    private static IServiceCollection LayoutComposition()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@127.0.0.1:5672",
            ["ConnectionStrings:layout-composition-db"] =
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
        });

        builder.AddLayoutCompositionInfrastructure();

        return builder.Services;
    }

    private static IServiceCollection OverlayDesigner()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@127.0.0.1:5672",
            ["ConnectionStrings:overlay-designer-db"] =
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
        });

        builder.AddOverlayDesignerInfrastructure();

        return builder.Services;
    }

    private static IServiceCollection SystemVariables()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@127.0.0.1:5672",
            ["ConnectionStrings:system-variables-db"] =
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:keycloak"] = "http://127.0.0.1:8080",
        });

        builder.AddSystemVariablesInfrastructure();

        return builder.Services;
    }

    /// <summary>
    /// Asserts <paramref name="services"/> contains the closed generic
    /// <c>IdempotencyReservationSweepHostedService&lt;TDbContext&gt;</c>
    /// registered as an <see cref="IHostedService"/> — the shape
    /// <c>AddIdempotencyReservationSweep&lt;TDbContext&gt;</c> produces.
    /// Checked by descriptor, not by resolving: resolving would construct
    /// every hosted service the module registers, including Wolverine's own,
    /// which is more than this guard needs to touch.
    /// </summary>
    private static void SweepIsRegisteredFor<TDbContext>(IServiceCollection services, string contextName)
        where TDbContext : DbContext
    {
        Type expected = typeof(IdempotencyReservationSweepHostedService<TDbContext>);

        bool registered = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == expected);

        registered.ShouldBeTrue(
            $"{contextName} is expected to register {expected.Name} via "
            + $"AddIdempotencyReservationSweep<{typeof(TDbContext).Name}>() alongside its "
            + "IIdempotencyStore (ADR-0142). Without it, a reservation left behind by an attempt "
            + "that died mid-request is never reclaimed, and a retried request replaying the same "
            + "Idempotency-Key is stuck behind a reservation that will never expire.");
    }

    [Fact]
    public void The_automation_context_registers_its_own_idempotency_reservation_sweep() =>
        SweepIsRegisteredFor<AutomationDbContext>(Automation(), "Automation");

    [Fact]
    public void The_camera_catalog_context_registers_its_own_idempotency_reservation_sweep() =>
        SweepIsRegisteredFor<CameraCatalogDbContext>(CameraCatalog(), "CameraCatalog");

    [Fact]
    public void The_event_ingestion_context_registers_its_own_idempotency_reservation_sweep() =>
        SweepIsRegisteredFor<EventIngestionDbContext>(EventIngestion(), "EventIngestion");

    [Fact]
    public void The_identity_context_registers_its_own_idempotency_reservation_sweep() =>
        SweepIsRegisteredFor<IdentityDbContext>(Identity(), "Identity");

    [Fact]
    public void The_layout_composition_context_registers_its_own_idempotency_reservation_sweep() =>
        SweepIsRegisteredFor<LayoutCompositionDbContext>(LayoutComposition(), "LayoutComposition");

    [Fact]
    public void The_overlay_designer_context_registers_its_own_idempotency_reservation_sweep() =>
        SweepIsRegisteredFor<OverlayDesignerDbContext>(OverlayDesigner(), "OverlayDesigner");

    [Fact]
    public void The_system_variables_context_registers_its_own_idempotency_reservation_sweep() =>
        SweepIsRegisteredFor<SystemVariablesDbContext>(SystemVariables(), "SystemVariables");
}
