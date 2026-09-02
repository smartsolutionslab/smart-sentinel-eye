using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Observes what a second <c>AddStandardResilienceHandler</c> call actually does
/// to a client, so <c>ResilienceRegistrationTests</c> rests on a measurement
/// rather than on a claim about a library.
///
/// <para>
/// The intuition a reader brings to the duplicate call is that it is redundant —
/// that the second registration overwrites, or is deduplicated by name, and the
/// worst case is noise. It is not: the handlers nest, and a nested retry
/// multiplies rather than repeats. That is the whole reason the duplicates were
/// worth removing, and it is not something the registration code shows.
/// </para>
///
/// <para>
/// Walking <c>InnerHandler</c> by reflection is deliberate. The pipeline is
/// assembled inside the factory and exposed nowhere; the shape of the chain is
/// the thing under test, and there is no public surface that reports it.
/// </para>
/// </summary>
public class ResilienceHandlerNestingTests
{
    private const string Client = "probe";

    [Fact]
    public void One_registration_yields_one_resilience_handler()
    {
        ServiceCollection services = new();
        services.AddHttpClient(Client).AddStandardResilienceHandler();

        CountResilienceHandlers(services).ShouldBe(1);
    }

    [Fact]
    public void A_client_that_asks_again_for_the_defaults_handler_gets_a_second_one_nested_inside_the_first()
    {
        ServiceCollection services = new();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddHttpClient(Client).AddStandardResilienceHandler();

        CountResilienceHandlers(services).ShouldBe(
            2,
            "this is the shape AddServiceDefaults plus a per-client call produced. The second handler is "
            + "appended, not merged: AddHttpMessageHandler has no notion of a pipeline already being "
            + "present. Nested, the outer handler's 3 retries each drive the inner handler's 4 attempts, "
            + "so one logical request can reach the server 16 times.");
    }

    /// <summary>
    /// The numbers the guard's reasoning quotes, read off the options rather than
    /// recited. They are the library's defaults, so they can move under a package
    /// bump — and if they do, the "4 attempts become 16" arithmetic in
    /// <c>ResilienceRegistrationTests</c> needs rewriting rather than trusting.
    /// </summary>
    [Fact]
    public void The_standard_handler_still_carries_the_budget_the_guards_describe()
    {
        ServiceCollection services = new();
        services.AddHttpClient(Client).AddStandardResilienceHandler();

        HttpStandardResilienceOptions options = services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{Client}-standard");

        options.Retry.MaxRetryAttempts.ShouldBe(3, "one attempt plus three retries is the 'four attempts' figure.");
        options.AttemptTimeout.Timeout.ShouldBe(TimeSpan.FromSeconds(10));
        options.TotalRequestTimeout.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    private static int CountResilienceHandlers(IServiceCollection services)
    {
        ServiceProvider provider = services.BuildServiceProvider();
        HttpMessageHandler handler = provider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(Client);

        int found = 0;
        object? current = handler;
        while (current is not null)
        {
            if (current.GetType().Name.Contains("ResilienceHandler", StringComparison.Ordinal))
            {
                found++;
            }

            PropertyInfo? inner = current.GetType().GetProperty(
                "InnerHandler", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            current = inner?.GetValue(current);
        }

        return found;
    }
}
