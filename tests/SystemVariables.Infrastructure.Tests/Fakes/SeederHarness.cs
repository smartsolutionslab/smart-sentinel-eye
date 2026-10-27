using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Infrastructure.Resolution;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Tests.Fakes;

/// <summary>
/// Spec 465 (#3182): builds the seeder the way the plan says it is built —
/// <c>new ReverseIndexSeederHostedService(httpClientFactory, reverseIndex,
/// logger, timeProvider)</c> — over a scripted transport and a virtual clock,
/// and drives it through <see cref="IHostedService"/> only
/// (<c>StartAsync</c>, then the clock, then <c>StopAsync</c>).
/// </summary>
internal sealed class SeederHarness
{
    private static readonly TimeSpan RealWait = TimeSpan.FromSeconds(5);

    private SeederHarness(ScriptedOverlayDesigner transport, IReverseIndex index)
    {
        Transport = transport;
        Index = index;
        Clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero));
        Logger = new CapturingLogger<ReverseIndexSeederHostedService>();
        Seeder = new ReverseIndexSeederHostedService(
            new FixedClientFactory(
                new HttpClient(transport) { BaseAddress = new Uri("http://overlay-designer") }),
            index,
            Logger,
            Clock);
    }

    public ScriptedOverlayDesigner Transport { get; }

    public IReverseIndex Index { get; }

    public ManualTimeProvider Clock { get; }

    public CapturingLogger<ReverseIndexSeederHostedService> Logger { get; }

    public IHostedService Seeder { get; }

    public static SeederHarness Create(ScriptedOverlayDesigner transport, IReverseIndex? index = null) =>
        new(transport, index ?? new InMemoryReverseIndex());

    /// <summary>Starts the host service; fails the test rather than hang if it blocks on the seed.</summary>
    public async Task StartAsync() =>
        await Seeder.StartAsync(CancellationToken.None).WaitAsync(RealWait);

    public Task StopAsync() => Seeder.StopAsync(CancellationToken.None).WaitAsync(RealWait);

    /// <summary>Real-time wait for the loop to reach its delay between two attempts.</summary>
    public Task<bool> WaitForDelayAsync() => Clock.WaitForPendingTimersAsync(1, RealWait);

    public Task<bool> WaitForRequestsAsync(int count) => Transport.WaitForRequestsAsync(count, RealWait);

    /// <summary>
    /// For scaffolding that runs the seeder to its first outcome: the loop is a
    /// background task, so "StartAsync returned" no longer means "attempted".
    /// </summary>
    public static async Task WaitForFirstLogAsync(CapturingLogger<ReverseIndexSeederHostedService> logger)
    {
        var deadline = DateTime.UtcNow + RealWait;
        while (DateTime.UtcNow < deadline && logger.Entries.Count == 0)
        {
            await Task.Delay(10);
        }
    }

    public async Task<bool> WaitForLogAsync(Func<string, bool> predicate)
    {
        var deadline = DateTime.UtcNow + RealWait;
        while (DateTime.UtcNow < deadline)
        {
            if (Logger.Entries.Any(entry => predicate(entry.Message)))
            {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }

    public string Transcript() =>
        "logged: " + string.Join(" | ", Logger.Entries.Select(entry => $"[{entry.Level}] {entry.Message}"))
        + $" ; requests made: {Transport.RequestCount}";
}

/// <summary>
/// A transport that answers a script, one step per request; the last step
/// repeats. Hand-written at the transport seam (ADR-0054).
/// </summary>
internal sealed class ScriptedOverlayDesigner(params ScriptedOverlayDesigner.Step[] steps) : HttpMessageHandler
{
    private readonly object gate = new();
    private int requestCount;

    public delegate Task<HttpResponseMessage> Step(HttpRequestMessage request, CancellationToken cancellationToken);

    public int RequestCount
    {
        get
        {
            lock (gate)
            {
                return requestCount;
            }
        }
    }

    public static Step Respond(HttpStatusCode status, string body = "{}") =>
        (_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

    public static Step Throw(Exception failure) => (_, _) => Task.FromException<HttpResponseMessage>(failure);

    /// <summary>Never answers until the request is cancelled.</summary>
    public static Step Hang() => async (_, cancellationToken) =>
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("unreachable");
    };

    /// <summary>Answers only once <paramref name="release"/> completes.</summary>
    public static Step HangUntil(Task release, HttpStatusCode status, string body = "{}") =>
        async (request, cancellationToken) =>
        {
            await release.WaitAsync(cancellationToken);
            return await Respond(status, body)(request, cancellationToken);
        };

    /// <summary>Runs <paramref name="whileInFlight"/> (an event being handled) before the listing is answered.</summary>
    public static Step RespondAfter(Action whileInFlight, HttpStatusCode status, string body) =>
        (request, cancellationToken) =>
        {
            whileInFlight();
            return Respond(status, body)(request, cancellationToken);
        };

    public async Task<bool> WaitForRequestsAsync(int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (RequestCount >= count)
            {
                return true;
            }

            await Task.Delay(10);
        }

        return RequestCount >= count;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        int index;
        lock (gate)
        {
            index = requestCount++;
        }

        return steps[Math.Min(index, steps.Length - 1)](request, cancellationToken);
    }
}

internal sealed class FixedClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
