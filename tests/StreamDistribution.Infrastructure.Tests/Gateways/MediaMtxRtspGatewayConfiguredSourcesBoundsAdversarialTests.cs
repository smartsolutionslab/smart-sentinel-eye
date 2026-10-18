using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #2994 / spec 388 US2, adversarial pass (T006). The bounded configured-sources
/// read at its cap boundaries, on malformed <c>items</c>, on a <c>pageCount</c>
/// literal no number type holds, under cancellation, and as the repair pass sees
/// it when one read is cut short and the next is whole. The stub throws past
/// 1,100 requests so an unbounded read fails fast.
/// </summary>
public class MediaMtxRtspGatewayConfiguredSourcesBoundsAdversarialTests
{
    private readonly CapturingLogger<MediaMtxRtspGateway> logger = new();

    [Theory]
    [InlineData(999, 999, 0)]
    [InlineData(1000, 1000, 0)]
    [InlineData(1001, 1000, 1)]
    public async Task The_page_cap_warns_only_when_mediamtx_still_reports_more_pages(
        int pageCount,
        int expectedRequests,
        int expectedWarnings)
    {
        var raw = pageCount.ToString(CultureInfo.InvariantCulture);
        var handler = new PagesHandler(_ => Body(raw, Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264")));

        var sources = await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.Count.ShouldBe(expectedRequests);
        handler.RequestedPages.ShouldBe(Enumerable.Range(0, expectedRequests));
        sources.Count.ShouldBe(expectedRequests);
        Warnings().Count.ShouldBe(expectedWarnings);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("-2147483648")]
    public async Task A_zero_or_negative_page_count_reads_one_page_without_a_warning(string rawPageCount)
    {
        var handler = new PagesHandler(_ => Body(rawPageCount, Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264")));

        var sources = await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0]);
        sources.Count.ShouldBe(1);
        Warnings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("{\"pageCount\":5}")]
    [InlineData("{\"pageCount\":5,\"items\":null}")]
    [InlineData("{\"pageCount\":5,\"items\":{\"name\":\"x\"}}")]
    [InlineData("{\"pageCount\":5,\"items\":\"[]\"}")]
    public async Task Items_that_are_absent_or_not_an_array_end_the_read_at_that_page(string malformedPage)
    {
        var first = $"cam-{Guid.NewGuid()}";
        var handler = new PagesHandler(page => page == 0
            ? Body("5", Item(first, "rtsp://10.0.8.1/h264"))
            : malformedPage);

        var sources = await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0, 1]);
        sources.Keys.Select(path => path.Value).ShouldBe([first]);
    }

    [Fact]
    public async Task An_empty_page_ends_the_read_even_when_a_later_page_would_have_items()
    {
        var first = $"cam-{Guid.NewGuid()}";
        var handler = new PagesHandler(page => page switch
        {
            0 => Body("3", Item(first, "rtsp://10.0.8.1/h264")),
            1 => Body("3"),
            _ => Body("3", Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.2/h264")),
        });

        var sources = await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0, 1]);
        sources.Count.ShouldBe(1);
        Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_page_holding_only_names_already_read_is_not_an_empty_page()
    {
        var shared = $"cam-{Guid.NewGuid()}";
        var last = $"cam-{Guid.NewGuid()}";
        var handler = new PagesHandler(page => page switch
        {
            0 => Body("3", Item(shared, "rtsp://10.0.8.1/a")),
            1 => Body("3", Item(shared, "rtsp://10.0.8.1/b")),
            _ => Body("3", Item(last, "rtsp://10.0.8.1/c")),
        });

        var sources = await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0, 1, 2]);
        sources.Count.ShouldBe(2);
        sources[MediaMtxPath.From(shared)].ShouldBe("rtsp://10.0.8.1/b");
    }

    [Fact]
    public async Task A_ten_thousand_digit_page_count_ends_the_read_with_one_short_warning_and_no_throw()
    {
        var rawPageCount = new string('9', 10_000);
        var handler = new PagesHandler(_ => Body(rawPageCount, Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264")));

        var sources = await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0]);
        sources.Count.ShouldBe(1);
        var warnings = Warnings();
        warnings.Count.ShouldBe(1);
        warnings[0].Message.ShouldNotContain("99999");
        warnings[0].Message.Length.ShouldBeLessThan(300);
        warnings[0].Exception.ShouldBeNull();
    }

    [Fact]
    public async Task A_cancellation_mid_read_propagates_and_logs_nothing()
    {
        using CancellationTokenSource shutdown = new();
        var handler = new PagesHandler(page =>
        {
            if (page == 3)
            {
                shutdown.Cancel();
            }

            return Body("2147483647", Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264"));
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => GatewayOver(handler).ListConfiguredPathSourcesAsync(shutdown.Token));

        handler.RequestedPages.Count.ShouldBeLessThanOrEqualTo(5);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_read_cut_short_by_an_unreadable_page_count_does_not_poison_the_next_full_read()
    {
        var first = $"cam-{Guid.NewGuid()}";
        var second = $"cam-{Guid.NewGuid()}";
        var call = 0;
        var handler = new PagesHandler(page =>
        {
            if (page == 0)
            {
                call++;
            }

            var pageCount = call == 1 ? "1.5" : "2";
            return page == 0
                ? Body(pageCount, Item(first, "rtsp://10.0.8.1/a"))
                : Body(pageCount, Item(second, "rtsp://10.0.8.1/b"));
        });
        var gateway = GatewayOver(handler);

        var cut = await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        var whole = await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        cut.Count.ShouldBe(1);
        whole.Count.ShouldBe(2);
        handler.RequestedPages.ShouldBe([0, 0, 1]);
        Warnings().Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_persistently_unreadable_page_count_warns_once_then_logs_debug()
    {
        var handler = new PagesHandler(_ => Body("1.5", Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264")));
        var gateway = GatewayOver(handler);

        for (var read = 0; read < 3; read++)
        {
            await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        }

        Warnings().Count.ShouldBe(1, "one Warning, then Debug: at a 10 s interval a Warning per read is 8,640 a day (spec 409)");
        RepeatDebugLines().Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_persistently_reached_page_cap_warns_once_then_logs_debug()
    {
        EventCapturingLogger eventLogger = new();
        var handler = new UnboundedPagesHandler(Body("2147483647", Item("cam-0197a1b2-0000-7000-8000-000000000b01", "rtsp://10.0.8.1/h264")));
        MediaMtxRtspGateway gateway = new(
            new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") },
            eventLogger);

        for (var read = 0; read < 3; read++)
        {
            await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        }

        var warnings = eventLogger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(1);
        warnings[0].EventName.ShouldBe("MediaMtxConfiguredSourcesPageCapReached");
    }

    [Fact]
    public async Task A_complete_read_resets_the_unreadable_page_count_warning()
    {
        string[] pageCounts = ["1.5", "1.5", "1", "1.5", "1.5"];
        var call = -1;
        var handler = new PagesHandler(page =>
        {
            if (page == 0)
            {
                call++;
            }

            return Body(pageCounts[call], Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264"));
        });
        var gateway = GatewayOver(handler);

        for (var read = 0; read < pageCounts.Length; read++)
        {
            await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        }

        Warnings().Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_change_from_unreadable_to_page_cap_warns_at_once()
    {
        var call = 0;
        var handler = new PagesHandler(page =>
        {
            if (page == 0)
            {
                call++;
            }

            return Body(call == 1 ? "1.5" : "2147483647", Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264"));
        });
        var gateway = GatewayOver(handler);

        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        Warnings().Count.ShouldBe(2);
    }

    [Fact]
    public async Task Gateways_resolved_from_the_registration_in_two_scopes_share_one_warning_state()
    {
        var handler = new PagesHandler(_ => Body("1.5", Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264")));
        ServiceCollection services = new();
        services.Configure<MediaMtxOptions>(options => options.ManagementUrl = "http://mediamtx.test");
        services.Configure<MediaMtxApiOptions>(options => options.Secret = "test-secret");
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILogger<MediaMtxRtspGateway>>(logger);
        services.AddMediaMtxGateway().ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        for (var scopeNumber = 0; scopeNumber < 2; scopeNumber++)
        {
            using var scope = provider.CreateScope();
            var gateway = scope.ServiceProvider.GetRequiredService<IRtspGateway>();
            await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        }

        Warnings().Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_gateway_registration_resolves_without_a_caller_registered_time_provider()
    {
        var handler = new PagesHandler(_ => Body("1", Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264")));
        ServiceCollection services = new();
        services.Configure<MediaMtxOptions>(options => options.ManagementUrl = "http://mediamtx.test");
        services.Configure<MediaMtxApiOptions>(options => options.Secret = "test-secret");
        services.AddSingleton<ILogger<MediaMtxRtspGateway>>(logger);
        services.AddMediaMtxGateway().ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IRtspGateway>();
        var sources = await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        sources.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_path_on_a_page_a_short_read_skipped_warns_again_on_the_next_full_read()
    {
        var onPageZero = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        var onPageOne = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        var shortRead = false;
        var handler = new PagesHandler(page =>
        {
            var pageCount = shortRead ? "1.5" : "2";
            return page == 0
                ? Body(pageCount, Item(onPageZero.Value, "rtsp://10.0.8.1/h264"))
                : Body(pageCount, Item(onPageOne.Value, "rtsp://10.0.8.2/drifted"));
        })
        {
            PatchStatus = HttpStatusCode.BadRequest,
        };
        var gateway = GatewayOver(handler);
        SourceRepairSnapshot snapshot = new();
        snapshot.Commit(onPageZero, "rtsp://10.0.8.1/h264");
        snapshot.Commit(onPageOne, "rtsp://10.0.8.1/h264");
        CapturingLogger<MediaMtxReconciler> repairLogger = new();
        MediaMtxSourceRepair repair = new(repairLogger);

        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        shortRead = true;
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        shortRead = false;
        await repair.RepairAsync(gateway, snapshot, CancellationToken.None);

        repairLogger.Entries
            .Count(entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains(onPageOne.ToString(), StringComparison.Ordinal))
            .ShouldBe(2, "the short read pruned the path (spec 388 A1's accepted cost)");
    }

    [Fact]
    public async Task A_refused_patch_through_the_real_gateway_logs_no_part_of_a_body_that_echoes_the_source()
    {
        var path = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        const string Persisted = "rtsp://10.0.8.1/h264?token=SECRET&pw=hunter2";
        var handler = new PagesHandler(_ => Body("1", Item(path.Value, "rtsp://admin:OBSERVED@10.0.8.2/drifted")))
        {
            PatchStatus = HttpStatusCode.BadRequest,
            PatchBody = $"{{\"error\":\"invalid source '{Persisted}'\"}}",
        };
        var gateway = GatewayOver(handler);
        SourceRepairSnapshot snapshot = new();
        snapshot.Commit(path, Persisted);
        CapturingLogger<MediaMtxReconciler> repairLogger = new();
        MediaMtxSourceRepair repair = new(repairLogger);

        for (var pass = 0; pass < 3; pass++)
        {
            await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
        }

        repairLogger.Entries.Count.ShouldBe(3);
        string[] secrets = ["SECRET", "hunter2", "admin", "OBSERVED"];
        foreach (var (_, message, exception) in repairLogger.Entries.Concat(logger.Entries))
        {
            var rendered = $"{message}\n{exception}";
            secrets.ShouldAllBe(secret => !rendered.Contains(secret, StringComparison.Ordinal));
        }
    }

    private List<(LogLevel Level, string Message, Exception? Exception)> Warnings() =>
        [.. logger.Entries.Where(entry => entry.Level == LogLevel.Warning)];

    private List<(LogLevel Level, string Message, Exception? Exception)> RepeatDebugLines() =>
        [.. logger.Entries.Where(entry => entry.Level == LogLevel.Debug
            && entry.Message.Contains("repeats at most every 10 minutes", StringComparison.Ordinal))];

    private MediaMtxRtspGateway GatewayOver(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") }, logger);

    private static string Item(string name, string source) => $"{{\"name\":\"{name}\",\"source\":\"{source}\"}}";

    private static string Body(string rawPageCount, params string[] items) =>
        $"{{\"pageCount\":{rawPageCount},\"items\":[{string.Join(",", items)}]}}";

    private sealed class UnboundedPagesHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class EventCapturingLogger : ILogger<MediaMtxRtspGateway>
    {
        private readonly List<(LogLevel Level, string? EventName, string Message)> entries = [];

        public IReadOnlyList<(LogLevel Level, string? EventName, string Message)> Entries => entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Add((logLevel, eventId.Name, formatter(state, exception)));
    }

    private sealed class PagesHandler(Func<int, string> bodyForPage) : HttpMessageHandler
    {
        private const int RequestCeiling = 1100;

        private readonly List<int> requestedPages = [];

        public List<int> RequestedPages => requestedPages;

        public HttpStatusCode PatchStatus { get; init; } = HttpStatusCode.OK;

        public string PatchBody { get; init; } = "{}";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (requestedPages.Count >= RequestCeiling)
            {
                throw new InvalidOperationException("unbounded read");
            }

            var uri = request.RequestUri ?? throw new InvalidOperationException("request had no URI");
            if (request.Method == HttpMethod.Patch)
            {
                return Task.FromResult(new HttpResponseMessage(PatchStatus)
                {
                    Content = new StringContent(PatchBody, Encoding.UTF8, "application/json"),
                });
            }

            cancellationToken.ThrowIfCancellationRequested();
            var page = uri.Query.TrimStart('?').Split('&')
                .Where(part => part.StartsWith("page=", StringComparison.Ordinal))
                .Select(part => int.Parse(part["page=".Length..], CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0)
                .First();
            requestedPages.Add(page);
            var body = bodyForPage(page);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
