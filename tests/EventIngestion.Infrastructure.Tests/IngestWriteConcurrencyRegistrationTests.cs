using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using SmartSentinelEye.EventIngestion.Application.Ingress;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Spec 175 (issue #2212) AS-1 / AS-2 — <c>IngestWriteLimiter</c>'s
/// concurrency must come from configuration, and the shipped default of 64
/// must survive the registration unchanged.
///
/// <para>
/// Modelled on <see cref="IngestVolumeRegistrationTests"/>: composes the real
/// <c>AddEventIngestionInfrastructure</c> module in-process, never starts the
/// host, and dials nothing. Same cost, stated the same way there: this test
/// couples to the five configuration keys the module resolves, so a change to
/// any of them breaks this test in a way that reads as unrelated to
/// backpressure. The trade is a loud, named failure instead of the silent one
/// this spec exists to close.
/// </para>
///
/// <para>
/// AS-1 and AS-2 are deliberately two separate facts, not one. AS-2 alone
/// passes against <em>today's</em> registration too — a parameterless
/// <c>AddSingleton&lt;IngestWriteLimiter&gt;()</c> also yields 64 — so it
/// cannot tell a registration that reads configuration apart from one that
/// ignores it. AS-1 is the assertion that catches that.
/// </para>
/// </summary>
public class IngestWriteConcurrencyRegistrationTests
{
    /// <summary>
    /// AS-1 — new behaviour. Today's registration
    /// (<c>AddSingleton&lt;IngestWriteLimiter&gt;()</c> at
    /// <c>EventIngestionInfrastructureModule.cs:117</c>) ignores configuration
    /// entirely and always grants 64, so the third lease here succeeds and
    /// this test fails.
    /// </summary>
    [Fact]
    public void A_configured_concurrency_bounds_the_registered_limiter()
    {
        Dictionary<string, string?> configuration = Configuration;
        configuration["EventIngestion:IngestWrite:Concurrency"] = "2";

        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration.AddInMemoryCollection(configuration);

        builder.AddEventIngestionInfrastructure();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();
        IngestWriteLimiter limiter = provider.GetRequiredService<IngestWriteLimiter>();

        limiter.TryAcquire().Acquired.ShouldBeTrue();
        limiter.TryAcquire().Acquired.ShouldBeTrue();

        limiter.TryAcquire().Acquired.ShouldBeFalse(
            "a configured concurrency of 2 must bound the registered singleton, not the unconfigured default of 64");
    }

    /// <summary>
    /// AS-2 — characterisation, already true today. No <c>IngestWrite</c> key
    /// is set, so the registration must grant exactly 64 leases and refuse the
    /// 65th. Asserted through the registration rather than the type default,
    /// because the registration is what this spec changes; the property
    /// default alone cannot distinguish a factory that reads it from one that
    /// ignores it.
    /// </summary>
    [Fact]
    public void The_unconfigured_registration_still_bounds_writes_at_sixty_four()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration.AddInMemoryCollection(Configuration);

        builder.AddEventIngestionInfrastructure();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();
        IngestWriteLimiter limiter = provider.GetRequiredService<IngestWriteLimiter>();

        List<bool> granted = [];
        for (int writer = 0; writer < IngestWriteLimiter.DefaultConcurrency; writer++)
        {
            granted.Add(limiter.TryAcquire().Acquired);
        }

        granted.ShouldAllBe(acquired => acquired);
        limiter.TryAcquire().Acquired.ShouldBeFalse();
    }

    /// <summary>
    /// The keys the module resolves before it returns. Values are
    /// syntactically valid and never dialled: the module parses them, it does
    /// not connect. Same shape as <see cref="IngestVolumeRegistrationTests"/>,
    /// copied rather than shared per that file's own precedent.
    /// </summary>
    private static Dictionary<string, string?> Configuration =>
        new()
        {
            ["ConnectionStrings:event-ingestion-db"] = "Host=localhost;Database=x;Username=u;Password=p",
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@localhost:5672",
            ["ConnectionStrings:messaging"] = "amqp://guest:guest@localhost:5672",
            ["ConnectionStrings:keycloak"] = "http://localhost:8080",
            ["Mosquitto:Endpoint"] = "localhost:1883",
        };
}
