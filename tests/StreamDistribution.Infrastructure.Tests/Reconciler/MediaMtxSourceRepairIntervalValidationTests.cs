using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #2960 / spec 378, second adversarial pass. <c>PeriodicTimer</c> accepts a
/// period of 1 ms up to 4294967294 ms; the options validation must accept no
/// more and no less, or the sweep faults after ApplicationStarted and the
/// default BackgroundServiceExceptionBehavior stops the host. The module's
/// binding is private, so it is reached by name.
/// </summary>
public class MediaMtxSourceRepairIntervalValidationTests
{
    private const string LargestAcceptedByTimer = "49.17:02:47.2940000";

    [Theory]
    [InlineData("00:00:00.0005000")]
    [InlineData("00:00:00.0010000")]
    [InlineData("00:00:00.0100000")]
    [InlineData("00:00:00.9990000")]
    [InlineData("50.00:00:00")]
    [InlineData("49.17:02:47.2950000")]
    public void An_interval_the_periodic_timer_rejects_is_refused_at_startup(string interval)
    {
        using var provider = OptionsProvider(interval);

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<MediaMtxSourceRepairOptions>>().Value);
    }

    [Theory]
    [InlineData("00:00:01")]
    [InlineData("00:00:10")]
    [InlineData(LargestAcceptedByTimer)]
    public void An_interval_the_periodic_timer_accepts_is_not_refused(string interval)
    {
        using var provider = OptionsProvider(interval);

        provider.GetRequiredService<IOptions<MediaMtxSourceRepairOptions>>().Value.Interval
            .ShouldBe(TimeSpan.Parse(interval, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_largest_interval_the_timer_accepts_renders_as_the_string_the_integration_lane_emits()
    {
        TimeSpan.FromMilliseconds(uint.MaxValue - 1).ToString().ShouldBe(LargestAcceptedByTimer);
    }

    [Theory]
    [InlineData("00:00:00.0005000")]
    [InlineData("50.00:00:00")]
    [InlineData(LargestAcceptedByTimer)]
    public async Task A_sweep_built_from_validated_options_never_faults_after_the_application_started(string interval)
    {
        using var provider = OptionsProvider(interval);
        IOptions<MediaMtxSourceRepairOptions> options;
        try
        {
            options = provider.GetRequiredService<IOptions<MediaMtxSourceRepairOptions>>();
            _ = options.Value;
        }
        catch (OptionsValidationException)
        {
            return; // refused at startup: the sweep is never built
        }

        await using var sweepServices = new ServiceCollection()
            .AddSingleton<IRtspGateway>(new SourceRepairGateway())
            .BuildServiceProvider();
        using LifetimeStub lifetime = new();
        using MediaMtxSourceRepairSweep sweep = new(
            sweepServices.GetRequiredService<IServiceScopeFactory>(),
            lifetime,
            options,
            TimeProvider.System,
            new CapturingLogger<MediaMtxSourceRepairSweep>());

        await sweep.StartAsync(CancellationToken.None);
        lifetime.FireApplicationStarted();
        await Task.WhenAny(sweep.ExecuteTask!, Task.Delay(TimeSpan.FromMilliseconds(500)));
        var faulted = sweep.ExecuteTask!.IsFaulted;
        await sweep.StopAsync(CancellationToken.None);

        faulted.ShouldBeFalse("a value the validation accepts must be a value PeriodicTimer accepts");
    }

    private static ServiceProvider OptionsProvider(string interval)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration["MediaMtxSourceRepair:Interval"] = interval;

        typeof(StreamDistributionInfrastructureModule)
            .GetMethod("BindMediaMtxSourceRepair", BindingFlags.NonPublic | BindingFlags.Static)
            .ShouldNotBeNull()
            .Invoke(null, [builder]);

        return builder.Services.BuildServiceProvider();
    }

    private sealed class LifetimeStub : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource started = new();
        private readonly CancellationTokenSource stopping = new();
        private readonly CancellationTokenSource stopped = new();

        public CancellationToken ApplicationStarted => started.Token;

        public CancellationToken ApplicationStopping => stopping.Token;

        public CancellationToken ApplicationStopped => stopped.Token;

        public void FireApplicationStarted() => started.Cancel();

        public void StopApplication() => stopping.Cancel();

        public void Dispose()
        {
            started.Dispose();
            stopping.Dispose();
            stopped.Dispose();
        }
    }
}
