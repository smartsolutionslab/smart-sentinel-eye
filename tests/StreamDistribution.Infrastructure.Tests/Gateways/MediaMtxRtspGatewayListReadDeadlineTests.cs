using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #3021 item 3 / spec 418 US2. All three list reads share one whole-read
/// deadline of five seconds, measured on the gateway's <see cref="TimeProvider"/>.
/// A read still running when it fires fails as an
/// <see cref="HttpRequestError.InvalidResponse"/> with no status code, no inner
/// exception and no text from MediaMTX; a caller's own cancellation stays an
/// <see cref="OperationCanceledException"/>.
///
/// <para>
/// The gateway is resolved through <c>AddMediaMtxGateway</c> with the fake
/// registered as the <see cref="TimeProvider"/> first, so this is also the
/// proof that the deadline reaches the gateway through the registration. No
/// resilience handler is added: a retry or an attempt timeout would interfere.
/// </para>
///
/// <para>
/// <b>Why the fake is trusted:</b>
/// <see cref="Without_advancing_the_clock_the_held_read_stays_pending"/> shows
/// the held read does not end by itself, so a read that ends after
/// <see cref="SchedulingTimeProvider.Advance"/> was ended by the deadline.
/// </para>
/// </summary>
public class MediaMtxRtspGatewayListReadDeadlineTests
{
    private const string HealthEndpoint = "/v3/paths/list";
    private const string ConfiguredEndpoint = "/v3/config/paths/list";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RealTimeBound = TimeSpan.FromSeconds(10);

    private readonly SchedulingTimeProvider time = new(DateTimeOffset.Parse("2026-10-19T09:00:00Z", CultureInfo.InvariantCulture));
    private readonly TaskCompletionSource secondPageRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> secondPageBody = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData("health", HealthEndpoint)]
    [InlineData("configured", ConfiguredEndpoint)]
    [InlineData("sources", ConfiguredEndpoint)]
    public async Task A_read_still_running_after_five_seconds_fails_as_an_invalid_response(
        string reader,
        string endpoint)
    {
        var (gateway, _) = Gateway((page, token) => page == 0 ? Task.FromResult(Page(reader, 3)) : HoldSecondPage(token));

        var read = ReadAsync(gateway, reader, CancellationToken.None);
        await secondPageRequested.Task.WaitAsync(RealTimeBound);
        time.Advance(Deadline);
        var failure = await EndedWithFailureAsync(read);

        var invalid = failure.ShouldBeOfType<HttpRequestException>();
        invalid.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        invalid.StatusCode.ShouldBeNull();
        invalid.InnerException.ShouldBeNull();
        invalid.Message.ShouldContain(endpoint);
        invalid.Message.ShouldContain("5 s");
    }

    [Theory]
    [InlineData("health")]
    [InlineData("configured")]
    [InlineData("sources")]
    public async Task The_deadline_covers_the_whole_read_not_each_page(string reader)
    {
        var (gateway, handler) = Gateway((_, _) =>
        {
            time.Advance(TimeSpan.FromSeconds(2));

            return Task.FromResult(Page(reader, 10));
        });

        var failure = await Record.ExceptionAsync(() => ReadAsync(gateway, reader, CancellationToken.None));

        var invalid = failure.ShouldBeOfType<HttpRequestException>();
        invalid.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        handler.RequestedPages.Count.ShouldBeLessThanOrEqualTo(4);
    }

    [Theory]
    [InlineData("health")]
    [InlineData("configured")]
    [InlineData("sources")]
    public async Task A_read_that_finishes_before_five_seconds_returns_every_page(string reader)
    {
        var (gateway, handler) = Gateway((page, token) => page == 0 ? Task.FromResult(Page(reader, 2)) : HoldSecondPage(token));

        var read = ReadAsync(gateway, reader, CancellationToken.None);
        await secondPageRequested.Task.WaitAsync(RealTimeBound);
        time.Advance(TimeSpan.FromSeconds(4.9));
        secondPageBody.SetResult(Page(reader, 2));
        var count = await read.WaitAsync(RealTimeBound);

        count.ShouldBe(2);
        handler.RequestedPages.ShouldBe([0, 1]);
    }

    [Theory]
    [InlineData("health")]
    [InlineData("configured")]
    [InlineData("sources")]
    public async Task A_caller_cancellation_before_the_deadline_stays_an_operation_cancellation(string reader)
    {
        using CancellationTokenSource caller = new();
        var (gateway, _) = Gateway((page, token) => page == 0 ? Task.FromResult(Page(reader, 3)) : HoldSecondPage(token));

        var read = ReadAsync(gateway, reader, caller.Token);
        await secondPageRequested.Task.WaitAsync(RealTimeBound);
        await caller.CancelAsync();
        time.Advance(Deadline);
        var failure = await EndedWithFailureAsync(read);

        failure.ShouldBeAssignableTo<OperationCanceledException>();
        failure.ShouldNotBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Without_advancing_the_clock_the_held_read_stays_pending()
    {
        var (gateway, _) = Gateway((page, token) => page == 0 ? Task.FromResult(Page("health", 3)) : HoldSecondPage(token));
        using CancellationTokenSource caller = new();

        var read = ReadAsync(gateway, "health", caller.Token);
        await secondPageRequested.Task.WaitAsync(RealTimeBound);
        await Task.WhenAny(read, Task.Delay(TimeSpan.FromMilliseconds(500)));
        var stillPending = !read.IsCompleted;
        await caller.CancelAsync();
        await Record.ExceptionAsync(() => read);

        stillPending.ShouldBeTrue("a read held open ends only when the deadline fires, never by itself");
    }

    private static async Task<int> ReadAsync(IRtspGateway gateway, string reader, CancellationToken cancellationToken) =>
        reader switch
        {
            "health" => (await gateway.GetAllPathsHealthAsync(cancellationToken)).Count,
            "configured" => (await gateway.ListConfiguredPathsAsync(cancellationToken)).Count,
            _ => (await gateway.ListConfiguredPathSourcesAsync(cancellationToken)).Count,
        };

    private static async Task<Exception> EndedWithFailureAsync(Task<int> read)
    {
        await Task.WhenAny(read, Task.Delay(RealTimeBound));
        read.IsCompleted.ShouldBeTrue(
            "the read was still running after the clock reached the five-second deadline");
        var failure = await Record.ExceptionAsync(() => read);

        return failure ?? throw new InvalidOperationException("the read completed without a failure");
    }

    private Task<string> HoldSecondPage(CancellationToken cancellationToken)
    {
        secondPageRequested.TrySetResult();

        return secondPageBody.Task.WaitAsync(cancellationToken);
    }

    private static string Page(string reader, int pageCount)
    {
        var name = $"cam-{Guid.NewGuid()}";
        var item = reader == "health"
            ? $"{{\"name\":\"{name}\",\"ready\":true,\"source\":{{\"type\":\"rtspSource\",\"id\":\"rtsp://10.0.8.1/x\"}}}}"
            : $"{{\"name\":\"{name}\",\"source\":\"rtsp://10.0.8.1/h264\"}}";

        return $"{{\"pageCount\":{pageCount.ToString(CultureInfo.InvariantCulture)},\"items\":[{item}]}}";
    }

    private (IRtspGateway Gateway, PagesHandler Handler) Gateway(
        Func<int, CancellationToken, Task<string>> bodyForPage)
    {
        PagesHandler handler = new(bodyForPage);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.Configure<MediaMtxOptions>(options => options.ManagementUrl = "http://mediamtx.test");
        services.AddMediaMtxGateway().ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<IRtspGateway>(), handler);
    }

    private sealed class PagesHandler(Func<int, CancellationToken, Task<string>> bodyForPage) : HttpMessageHandler
    {
        private readonly object gate = new();
        private readonly List<int> requestedPages = [];

        public IReadOnlyList<int> RequestedPages
        {
            get
            {
                lock (gate)
                {
                    return [.. requestedPages];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri ?? throw new InvalidOperationException("request had no URI");
            var page = uri.Query.TrimStart('?').Split('&')
                .Where(part => part.StartsWith("page=", StringComparison.Ordinal))
                .Select(part => int.Parse(part["page=".Length..], CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0)
                .First();
            lock (gate)
            {
                requestedPages.Add(page);
            }

            var body = await bodyForPage(page, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
