using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 320 (#2181) plan §8.4 — <b>declaration only</b>, exactly as
/// <see cref="KioskPrivilegeSweepRegistrationTests"/>'s own doc labels itself.
///
/// <para>
/// <b>What this proves: that the registration exists. Nothing else.</b> It
/// reads a service collection and performs one scoped resolution. It does not
/// show that the Identity API started, that a pass ran, or that any client
/// was disabled. <c>KioskPrivilegeSweep</c> shipped in spec 052 and was
/// registered nowhere for the whole of its life (#2132) — nothing here would
/// have caught that on its own either, which is why phase 5's independent
/// end-to-end procedure (spec 320 §9) is what actually proves the pass runs
/// at boot, not this file.
/// </para>
///
/// <para>
/// Red today: <c>OrphanedClientSweepHostedService</c> is declared (T001) but
/// not registered by <c>AddIdentityInfrastructure</c>. Green at T012.
/// </para>
/// </summary>
public class OrphanedClientSweepRegistrationTests
{
    /// <summary>
    /// The registration <c>Identity/Api/Program.cs</c> calls, and the only
    /// line in that file that decides what starts.
    /// </summary>
    private static IServiceCollection IdentityInfrastructure()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // Nothing here is dialled: the collection is inspected and, below, one
        // scoped resolution is performed. Both connection strings exist only
        // because AddIdentityInfrastructure reads them while registering.
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

    [Fact]
    public void The_identity_api_registers_a_startup_service_that_drives_the_orphan_sweep()
    {
        Type[] startupServices = IdentityInfrastructure()
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType is not null
                && descriptor.ImplementationType.Assembly == typeof(IdentityInfrastructureModule).Assembly)
            .Select(descriptor => descriptor.ImplementationType!)
            .ToArray();

        // ShouldContain, not ShouldNotBeEmpty: the message below is about
        // OrphanedClientSweepHostedService specifically, and a count-only
        // assertion stays green when a second, unrelated startup service is
        // added to this assembly and this registration is deleted.
        startupServices.ShouldContain(
            typeof(OrphanedClientSweepHostedService),
            "AddIdentityInfrastructure registers no startup service that drives "
            + "OrphanedClientSweep when the Identity API starts, so a client an aborted "
            + "registration or enrolment left behind (spec 320, #2181) is never disabled.");
    }

    [Fact]
    public void The_sweep_can_be_resolved_from_the_container_that_registration_builds()
    {
        using ServiceProvider provider = IdentityInfrastructure()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();

        scope.ServiceProvider.GetService<OrphanedClientSweep>().ShouldNotBeNull(
            "a startup service can only drive the pass if the container it resolves from "
            + "knows how to build it; OrphanedClientSweep takes the scoped IKeycloakAdminClient "
            + "and IRegisteredClientRepository, so it is scoped too and must be resolved through "
            + "a scope rather than injected into a singleton.");
    }
}
