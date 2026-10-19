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
/// #3013 / spec 420 US1. A health sweep that keeps failing the same way warns
/// once, then logs Debug, and warns again after ten minutes, on a different
/// failure kind, or after a successful sweep. The real
/// <see cref="StreamHealthWatcher"/> over the real
/// <see cref="MediaMtxRtspGateway"/> and a scripted MediaMTX; ticks are repeated
/// <c>DispatchAsync</c> calls on one watcher.
/// </summary>
public class StreamHealthWatcherSweepFailureThrottleTests
{
    private const string WarningMessage = "MediaMTX health sweep failed; skipping this tick for every stream.";
    private const string RepeatNotice = "repeats at most every 10 minutes";

    private static readonly Guid CameraA = Guid.Parse("01a02481-3b04-757e-97f3-c3e7d22a6101");
    private static readonly Guid CameraB = Guid.Parse("01a02481-3b04-757e-97f3-c3e7d22a6102");

    [Theory]
    [InlineData(503)]
    [InlineData(401)]
    public async Task A_health_sweep_that_keeps_failing_the_same_way_warns_once_then_logs_debug(int status)
    {
        await using var harness = Harness.Create(Reply.Failing((HttpStatusCode)status));

        await harness.TickAsync();
        await harness.TickAsync();
        await harness.TickAsync();

        var warnings = harness.Warnings;
        warnings.Count.ShouldBe(1);
        warnings[0].Message.ShouldBe(WarningMessage);
        warnings[0].Exception.ShouldBeOfType<HttpRequestException>().StatusCode.ShouldBe((HttpStatusCode)status);
        var repeats = harness.Repeats;
        repeats.Count.ShouldBe(2);
        foreach (var repeat in repeats)
        {
            repeat.Message.ShouldContain("HttpRequestException");
            repeat.Message.ShouldContain(status.ToString(CultureInfo.InvariantCulture));
            repeat.Exception.ShouldBeNull();
        }

        harness.Log.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
        harness.Handled.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_same_failure_warns_again_once_ten_minutes_have_passed()
    {
        await using var harness = Harness.Create(Reply.Failing(HttpStatusCode.ServiceUnavailable));

        await harness.TickAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
        await harness.TickAsync();
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await harness.TickAsync();

        harness.Warnings.Count.ShouldBe(2);
        harness.Repeats.Count.ShouldBe(1);
        var levels = harness.Log.Entries
            .Where(entry => entry.Level == LogLevel.Warning || Harness.IsRepeat(entry))
            .Select(entry => entry.Level)
            .ToList();
        levels.ShouldBe([LogLevel.Warning, LogLevel.Debug, LogLevel.Warning]);
    }

    [Fact]
    public async Task A_different_failure_kind_warns_at_once()
    {
        await using var harness = Harness.Create(
            Reply.Failing(HttpStatusCode.ServiceUnavailable),
            Reply.Failing(HttpStatusCode.ServiceUnavailable),
            Reply.Failing(HttpStatusCode.InternalServerError),
            Reply.Page("[]"));

        await harness.TickAsync();
        await harness.TickAsync();
        await harness.TickAsync();
        await harness.TickAsync();

        var warnings = harness.Warnings;
        warnings.Count.ShouldBe(3);
        var first = warnings[0].Exception.ShouldBeOfType<HttpRequestException>();
        first.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var second = warnings[1].Exception.ShouldBeOfType<HttpRequestException>();
        second.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var third = warnings[2].Exception.ShouldBeOfType<HttpRequestException>();
        third.StatusCode.ShouldBeNull();
        third.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        harness.Repeats.Count.ShouldBe(1);
        var levels = harness.Log.Entries
            .Where(entry => entry.Level == LogLevel.Warning || Harness.IsRepeat(entry))
            .Select(entry => entry.Level)
            .ToList();
        levels.ShouldBe([LogLevel.Warning, LogLevel.Debug, LogLevel.Warning, LogLevel.Warning]);
    }

    [Fact]
    public async Task A_failure_with_the_same_exception_type_and_no_status_but_another_error_warns_at_once()
    {
        await using var harness = Harness.Create(
            Reply.Throwing(new HttpRequestException(HttpRequestError.ConnectionError, "connection refused")),
            Reply.Page("[]"));

        await harness.TickAsync();
        await harness.TickAsync();

        var warnings = harness.Warnings;
        warnings.Count.ShouldBe(2);
        warnings[0].Exception.ShouldBeOfType<HttpRequestException>()
            .HttpRequestError.ShouldBe(HttpRequestError.ConnectionError);
        warnings[1].Exception.ShouldBeOfType<HttpRequestException>()
            .HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        harness.Repeats.Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_successful_sweep_re_arms_the_warning()
    {
        await using var harness = Harness.Create(
            Reply.Failing(HttpStatusCode.ServiceUnavailable),
            Reply.Failing(HttpStatusCode.ServiceUnavailable),
            Reply.Page("{\"pageCount\":1,\"items\":[]}"),
            Reply.Failing(HttpStatusCode.ServiceUnavailable));

        await harness.TickAsync();
        await harness.TickAsync();
        await harness.TickAsync();
        await harness.TickAsync();

        harness.Warnings.Count.ShouldBe(2);
        harness.Repeats.Count.ShouldBe(1);
        harness.Handled.Count.ShouldBe(2);
    }

    private sealed record Reply(HttpStatusCode Status, string Body, Exception? Failure = null)
    {
        public static Reply Throwing(Exception failure) => new(HttpStatusCode.OK, string.Empty, failure);

        public static Reply Failing(HttpStatusCode status) => new(status, string.Empty);

        public static Reply Page(string body) => new(HttpStatusCode.OK, body);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly StreamHealthWatcher watcher;
        private readonly MediaMtxRtspGateway gateway;
        private readonly Recorder recorder;

        private Harness(
            ServiceProvider provider,
            StreamHealthWatcher watcher,
            MediaMtxRtspGateway gateway,
            Recorder recorder,
            ManualTimeProvider time,
            CapturingLogger<StreamHealthWatcher> log)
        {
            this.provider = provider;
            this.watcher = watcher;
            this.gateway = gateway;
            this.recorder = recorder;
            Time = time;
            Log = log;
        }

        public ManualTimeProvider Time { get; }

        public CapturingLogger<StreamHealthWatcher> Log { get; }

        public IReadOnlyList<ReportStreamHealthCommand> Handled => recorder.Handled;

        public List<(LogLevel Level, string Message, Exception? Exception)> Warnings =>
            [.. Log.Entries.Where(entry => entry.Level == LogLevel.Warning)];

        public List<(LogLevel Level, string Message, Exception? Exception)> Repeats =>
            [.. Log.Entries.Where(IsRepeat)];

        public static bool IsRepeat((LogLevel Level, string Message, Exception? Exception) entry) =>
            entry.Level == LogLevel.Debug && entry.Message.Contains(RepeatNotice, StringComparison.Ordinal);

        public static Harness Create(params Reply[] script)
        {
            Recorder recorder = new();
            ManualTimeProvider time = new(DateTimeOffset.Parse("2026-10-19T09:00:00Z", CultureInfo.InvariantCulture));
            CapturingLogger<StreamHealthWatcher> log = new();

            ServiceCollection services = new();
            services.AddSingleton(recorder);
            services.AddSingleton<IClock>(new FixedClock());
            services.AddSingleton<ILogger<StreamHealthWatcher>>(log);
            services.AddSingleton<TimeProvider>(time);
            services.AddScoped<HealthCommandHandler, RecordingHandler>();

            var provider = services.BuildServiceProvider();
            var watcher = ActivatorUtilities.CreateInstance<StreamHealthWatcher>(provider);
            MediaMtxRtspGateway gateway = new(
                new HttpClient(new ScriptedHandler(script)) { BaseAddress = new Uri("http://mediamtx.test") },
                new CapturingLogger<MediaMtxRtspGateway>());

            return new Harness(provider, watcher, gateway, recorder, time, log);
        }

        public Task TickAsync()
        {
            (Guid Camera, MediaMtxPath Path, StreamState State)[] streams =
            [
                (CameraA, MediaMtxPath.For(CameraIdentifier.From(CameraA)), StreamState.Healthy),
                (CameraB, MediaMtxPath.For(CameraIdentifier.From(CameraB)), StreamState.Healthy),
            ];

            return watcher.DispatchAsync(streams, gateway, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            watcher.Dispose();
            await provider.DisposeAsync();
        }
    }

    private sealed class ScriptedHandler(Reply[] script) : HttpMessageHandler
    {
        private int next;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var reply = script[Math.Min(next, script.Length - 1)];
            next++;

            if (reply.Failure is not null)
            {
                throw reply.Failure;
            }

            return Task.FromResult(new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(reply.Body, Encoding.UTF8, "application/json"),
            });
        }
    }

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

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            DateTimeOffset.Parse("2026-10-19T09:00:00Z", CultureInfo.InvariantCulture);
    }
}
