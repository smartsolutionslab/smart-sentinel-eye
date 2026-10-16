using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Infrastructure;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests;

/// <summary>
/// Spec 385 (#2991) addendum, C8: <c>StreamRepoint:ChangedAtTolerance</c> is
/// refused at startup when negative or above one day, accepted from 0 to one day
/// inclusive, and defaults to five minutes. Untracked until
/// <c>StreamRepointOptions</c> exists; the engineer renames this to <c>.cs</c>.
/// Assumed shape: <c>StreamRepointOptions</c> (SectionName "StreamRepoint",
/// property <c>ChangedAtTolerance</c>) in
/// <c>SmartSentinelEye.StreamDistribution.Application.Commands</c>, bound by a
/// private static <c>BindStreamRepoint(IHostApplicationBuilder)</c> on
/// <c>StreamDistributionInfrastructureModule</c>, reached by name as
/// <c>MediaMtxSourceRepairIntervalValidationTests</c> does. The engineer may
/// adapt namespace and binder name here, never an assertion.
/// </summary>
public class StreamRepointToleranceValidationTests
{
    [Theory]
    [InlineData("-00:00:00.0000001")]
    [InlineData("-00:05:00")]
    [InlineData("1.00:00:00.0000001")]
    [InlineData("2.00:00:00")]
    public void C8_A_tolerance_below_zero_or_above_one_day_is_refused_at_startup(string tolerance)
    {
        using var provider = OptionsProvider(tolerance);

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<StreamRepointOptions>>().Value);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:05:00")]
    [InlineData("1.00:00:00")]
    public void C8_A_tolerance_from_zero_to_one_day_is_accepted(string tolerance)
    {
        using var provider = OptionsProvider(tolerance);

        provider.GetRequiredService<IOptions<StreamRepointOptions>>().Value.ChangedAtTolerance
            .ShouldBe(TimeSpan.Parse(tolerance, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void C8_Without_configuration_the_tolerance_is_five_minutes()
    {
        using var provider = OptionsProvider(null);

        provider.GetRequiredService<IOptions<StreamRepointOptions>>().Value.ChangedAtTolerance
            .ShouldBe(TimeSpan.FromMinutes(5));
    }

    private static ServiceProvider OptionsProvider(string? tolerance)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        if (tolerance is not null)
        {
            builder.Configuration["StreamRepoint:ChangedAtTolerance"] = tolerance;
        }

        typeof(StreamDistributionInfrastructureModule)
            .GetMethod("BindStreamRepoint", BindingFlags.NonPublic | BindingFlags.Static)
            .ShouldNotBeNull()
            .Invoke(null, [builder]);

        return builder.Services.BuildServiceProvider();
    }
}
