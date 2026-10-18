using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.HealthWatcher;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;
using HealthCommandHandler = SmartSentinelEye.Shared.CQRS.ICommandHandler<
    SmartSentinelEye.StreamDistribution.Application.Commands.ReportStreamHealthCommand,
    SmartSentinelEye.Shared.Kernel.Result<
        SmartSentinelEye.StreamDistribution.Domain.Stream.StreamState,
        SmartSentinelEye.StreamDistribution.Application.Commands.ReportStreamHealthError>>;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.HealthWatcher;

/// <summary>
/// #3010 / spec 391 US1. The real <see cref="StreamHealthWatcher"/> over the
/// real <see cref="MediaMtxRtspGateway"/>: a health read that is cut short must
/// fail the tick (one Warning, nothing dispatched), because the watcher reads a
/// path missing from the result as "not registered" and a partial result would
/// announce outages that are not happening. The stub throws on the 1,101st
/// request, so an unbounded read fails fast instead of hanging.
/// </summary>
public class StreamHealthWatcherCutShortReadTests
{
    private const string HugePageCount = "2147483647";

    private static readonly Guid CameraA = Guid.Parse("01a02481-3b04-757e-97f3-c3e7d22a6101");
    private static readonly Guid CameraB = Guid.Parse("01a02481-3b04-757e-97f3-c3e7d22a6102");

    [Theory]
    [InlineData("1.5")]
    [InlineData("3e9")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("1e400")]
    public async Task A_health_read_cut_short_by_an_unreadable_page_count_fails_the_tick_and_dispatches_no_camera(
        string rawPageCount)
    {
        var handler = new PagesHandler(_ => Body(rawPageCount, ReadyItem(CameraA)));

        var sweep = await DispatchAsync(handler);

        handler.RequestedPages.ShouldBe([0]);
        AssertTickFailed(sweep);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"items\":null")]
    [InlineData(",\"items\":{}")]
    [InlineData(",\"items\":\"x\"")]
    public async Task A_health_page_without_a_readable_items_array_fails_the_tick_and_dispatches_no_camera(
        string itemsMember)
    {
        var handler = new PagesHandler(_ => $"{{\"pageCount\":2{itemsMember}}}");

        var sweep = await DispatchAsync(handler);

        handler.RequestedPages.ShouldBe([0]);
        AssertTickFailed(sweep);
    }

    [Fact]
    public async Task A_malformed_page_is_a_lost_tick_logged_as_a_health_sweep_failure()
    {
        var handler = new PagesHandler(_ => "[]");

        var sweep = await DispatchAsync(handler);

        handler.RequestedPages.ShouldBe([0]);
        AssertTickFailed(sweep);
    }

    [Fact]
    public async Task An_undecodable_string_is_a_lost_tick_logged_as_a_health_sweep_failure()
    {
        List<byte> body = [.. Encoding.UTF8.GetBytes($"{{\"pageCount\":1,\"items\":[{{\"name\":\"cam-{CameraA}")];
        body.Add(0xFF);
        body.AddRange(Encoding.UTF8.GetBytes("\",\"ready\":true}]}"));
        var content = new ByteArrayContent([.. body]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var handler = new FixedContentHandler(content);

        var sweep = await DispatchAsync(handler);

        AssertTickFailed(sweep);
        sweep.WatcherLog.Entries.Single(entry => entry.Level == LogLevel.Warning)
            .Exception.ShouldNotBeNull().InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task A_health_read_that_reaches_the_page_cap_fails_the_tick_and_dispatches_no_camera()
    {
        var handler = new PagesHandler(_ => Body(HugePageCount, ReadyItem(Guid.NewGuid())));

        var sweep = await DispatchAsync(handler);

        handler.RequestedPages.Count.ShouldBe(1000);
        AssertTickFailed(sweep);
    }

    [Fact]
    public async Task An_empty_page_ends_the_health_read_and_the_tick_dispatches_every_stream()
    {
        var handler = new PagesHandler(page => page == 0
            ? Body(HugePageCount, ReadyItem(CameraA))
            : Body(HugePageCount));

        var sweep = await DispatchAsync(handler);

        handler.RequestedPages.ShouldBe([0, 1]);
        sweep.Scopes.ShouldBe(2);
        sweep.Handled.Select(command => command.Camera.Value).ShouldBe([CameraA, CameraB]);
        sweep.Handled[0].Observation.IsReady.ShouldBeTrue();
        sweep.Handled[1].Observation.LastError.ShouldBe("path not registered");
        sweep.WatcherLog.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    private static void AssertTickFailed(Sweep sweep)
    {
        sweep.Scopes.ShouldBe(0, "no camera can be dispatched from a health read that never finished");
        sweep.Handled.ShouldBeEmpty();
        var warnings = sweep.WatcherLog.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(1);
        warnings[0].Message.ShouldBe("MediaMTX health sweep failed; skipping this tick for every stream.");
        var failure = warnings[0].Exception.ShouldBeOfType<HttpRequestException>();
        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        sweep.WatcherLog.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
    }

    private static async Task<Sweep> DispatchAsync(HttpMessageHandler mediaMtx)
    {
        Recorder recorder = new();

        ServiceCollection services = new();
        services.AddSingleton(recorder);
        services.AddScoped<HealthCommandHandler, RecordingHandler>();

        await using var provider = services.BuildServiceProvider();

        CountingScopeFactory scopes = new(provider.GetRequiredService<IServiceScopeFactory>());
        CapturingLogger<StreamHealthWatcher> watcherLog = new();
        StreamHealthWatcher watcher = new(scopes, new FixedClock(), watcherLog);

        MediaMtxRtspGateway gateway = new(
            new HttpClient(mediaMtx) { BaseAddress = new Uri("http://mediamtx.test") },
            new CapturingLogger<MediaMtxRtspGateway>());

        (Guid Camera, MediaMtxPath Path, StreamState State)[] streams =
        [
            (CameraA, MediaMtxPath.For(CameraIdentifier.From(CameraA)), StreamState.Healthy),
            (CameraB, MediaMtxPath.For(CameraIdentifier.From(CameraB)), StreamState.Healthy),
        ];

        await watcher.DispatchAsync(streams, gateway, CancellationToken.None);

        return new Sweep(scopes.Count, recorder.Handled, watcherLog);
    }

    private static string ReadyItem(Guid camera) => $"{{\"name\":\"cam-{camera}\",\"ready\":true}}";

    private static string Body(string rawPageCount, params string[] items) =>
        $"{{\"pageCount\":{rawPageCount},\"items\":[{string.Join(",", items)}]}}";

    private sealed record Sweep(
        int Scopes,
        IReadOnlyList<ReportStreamHealthCommand> Handled,
        CapturingLogger<StreamHealthWatcher> WatcherLog);

    private sealed class Recorder
    {
        private readonly List<ReportStreamHealthCommand> handled = [];

        public IReadOnlyList<ReportStreamHealthCommand> Handled => handled;

        public void Record(ReportStreamHealthCommand command) => handled.Add(command);
    }

    private sealed class RecordingHandler(Recorder recorder) : HealthCommandHandler
    {
        public Task<Result<StreamState, ReportStreamHealthError>> HandleAsync(
            ReportStreamHealthCommand command, CancellationToken cancellationToken)
        {
            recorder.Record(command);

            return Task.FromResult(Result<StreamState, ReportStreamHealthError>.Success(StreamState.Degraded));
        }
    }

    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public int Count { get; private set; }

        public IServiceScope CreateScope()
        {
            Count++;

            return inner.CreateScope();
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            DateTimeOffset.Parse("2026-08-23T09:00:00Z", CultureInfo.InvariantCulture);
    }

    private sealed class FixedContentHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class PagesHandler(Func<int, string> bodyForPage) : HttpMessageHandler
    {
        private const int RequestCeiling = 1100;

        private readonly List<int> requestedPages = [];

        public List<int> RequestedPages => requestedPages;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (requestedPages.Count >= RequestCeiling)
            {
                throw new InvalidOperationException("unbounded read");
            }

            var uri = request.RequestUri ?? throw new InvalidOperationException("request had no URI");
            var page = uri.Query.TrimStart('?').Split('&')
                .Where(part => part.StartsWith("page=", StringComparison.Ordinal))
                .Select(part => int.Parse(part["page=".Length..], CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0)
                .First();
            requestedPages.Add(page);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(bodyForPage(page), Encoding.UTF8, "application/json"),
            });
        }
    }
}
