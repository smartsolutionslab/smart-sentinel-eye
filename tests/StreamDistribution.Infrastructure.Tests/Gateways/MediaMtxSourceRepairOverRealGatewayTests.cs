using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #2960 / spec 378, adversarial pass. The pass driven through the real
/// <see cref="MediaMtxRtspGateway"/> against a stateful MediaMTX double that
/// stores what a PATCH body carries and lists it back as JSON: the comparison is
/// ordinal, so any lossy step in that round trip (escaping, encoding) would make
/// the two never compare equal and patch the path every sweep, restarting its
/// pull each time.
/// </summary>
public class MediaMtxSourceRepairOverRealGatewayTests
{
    [Theory]
    [InlineData("rtsp://10.0.8.1:554/h264")]
    [InlineData("rtsp://cam/a%20b")]
    [InlineData("rtsp://bücher.example/kamera")]
    [InlineData("rtsp://cam/x?a=1&b=%3C2%3E+c")]
    [InlineData("rtsp://[fd00::5]:8554/x")]
    [InlineData("rtsp://cam/caméra/日本")]
    [InlineData("RTSP://CAM/H264/")]
    public async Task A_repaired_source_round_trips_exactly_so_the_next_sweeps_send_nothing(string persisted)
    {
        var path = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        MediaMtxConfigDouble mediamtx = new();
        mediamtx.Configure(path.Value, "rtsp://10.0.9.9/other");
        SourceRepairSnapshot snapshot = new();
        snapshot.Commit(path, persisted);
        var gateway = GatewayOver(mediamtx);
        MediaMtxSourceRepair pass = new(new CapturingLogger<MediaMtxReconciler>());

        await pass.RepairAsync(gateway, snapshot, CancellationToken.None);
        await pass.RepairAsync(gateway, snapshot, CancellationToken.None);
        await pass.RepairAsync(gateway, snapshot, CancellationToken.None);

        mediamtx.Patches.ShouldBe(1, "one repair, then equality: anything more is a patch storm");
        mediamtx.Source(path.Value).ShouldBe(persisted);
    }

    [Fact]
    public async Task A_path_listed_on_two_pages_while_mediamtx_shifts_does_not_throw_and_the_later_value_wins()
    {
        var path = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        var name = path.Value;
        var gateway = GatewayOver(new PagesDouble(new Dictionary<int, string>
        {
            [0] = $"{{\"pageCount\":2,\"items\":[{{\"name\":\"{name}\",\"source\":\"rtsp://10.0.0.1/first\"}}]}}",
            [1] = $"{{\"pageCount\":2,\"items\":[{{\"name\":\"{name}\",\"source\":\"rtsp://10.0.0.2/second\"}}]}}",
        }));

        var sources = await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        sources.Count.ShouldBe(1);
        sources[path].ShouldBe("rtsp://10.0.0.2/second");
    }

    [Fact]
    public async Task An_empty_last_page_and_a_page_count_that_shrinks_mid_read_end_the_read_cleanly()
    {
        var name = $"cam-{Guid.NewGuid()}";
        var pagesDouble = new PagesDouble(new Dictionary<int, string>
        {
            [0] = $"{{\"pageCount\":3,\"items\":[{{\"name\":\"{name}\",\"source\":\"rtsp://10.0.0.1/x\"}}]}}",
            [1] = "{\"pageCount\":2,\"items\":[]}",
        });
        var gateway = GatewayOver(pagesDouble);

        var sources = await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        sources.Count.ShouldBe(1);
        pagesDouble.RequestedPages.ShouldBe([0, 1], "the latest pageCount bounds the read");
    }

    [Fact]
    public async Task A_refused_patch_whose_error_body_echoes_the_secret_does_not_carry_it_into_any_log_or_exception()
    {
        var path = MediaMtxPath.From($"cam-{Guid.NewGuid()}");
        MediaMtxConfigDouble mediamtx = new()
        {
            PatchRefusal = source => $"{{\"error\":\"invalid source '{source}'\"}}",
        };
        mediamtx.Configure(path.Value, "rtsp://10.0.9.9/other");
        SourceRepairSnapshot snapshot = new();
        snapshot.Commit(path, "rtsp://cam/stream?token=SECRET");
        CapturingLogger<MediaMtxRtspGateway> gatewayLogger = new();
        CapturingLogger<MediaMtxReconciler> repairLogger = new();
        MediaMtxRtspGateway gateway = new(
            new HttpClient(mediamtx) { BaseAddress = new Uri("http://mediamtx.test") },
            gatewayLogger);

        var repaired = await new MediaMtxSourceRepair(repairLogger).RepairAsync(gateway, snapshot, CancellationToken.None);

        repaired.ShouldBe(0);
        var entries = repairLogger.Entries.Concat(gatewayLogger.Entries).ToList();
        entries.ShouldNotBeEmpty();
        entries.ShouldAllBe(
            entry => !entry.Message.Contains("SECRET", StringComparison.Ordinal)
                && (entry.Exception == null || !entry.Exception.ToString().Contains("SECRET", StringComparison.Ordinal)));
    }

    private static MediaMtxRtspGateway GatewayOver(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") },
            new CapturingLogger<MediaMtxRtspGateway>());

    /// <summary>
    /// MediaMTX's config store as far as list and patch go: a PATCH body's
    /// <c>source</c> is stored verbatim (spec 378 §1, conf/path.go) and the list
    /// serialises it back with HTML-escaping, as Go's encoder does.
    /// </summary>
    private sealed class MediaMtxConfigDouble : HttpMessageHandler
    {
        private readonly Dictionary<string, string> sources = new(StringComparer.Ordinal);

        public int Patches { get; private set; }

        public Func<string, string>? PatchRefusal { get; init; }

        public void Configure(string name, string source) => sources[name] = source;

        public string Source(string name) => sources[name];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("request had no URI");

            if (request.Method == HttpMethod.Patch)
            {
                Patches++;
                var name = uri.AbsolutePath["/v3/config/paths/patch/".Length..];
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                var source = JsonDocument.Parse(body).RootElement.GetProperty("source").GetString()!;
                if (PatchRefusal is { } refusal)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(refusal(source), Encoding.UTF8, "application/json"),
                    };
                }

                sources[name] = source;

                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var items = sources.Select(entry => new { name = entry.Key, source = entry.Value });
            var listed = JsonSerializer.Serialize(new { itemCount = sources.Count, pageCount = 1, items });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(listed, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class PagesDouble(Dictionary<int, string> pages) : HttpMessageHandler
    {
        private readonly List<int> requestedPages = [];

        public IReadOnlyList<int> RequestedPages => requestedPages;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("request had no URI");
            var page = uri.Query.TrimStart('?').Split('&')
                .Where(part => part.StartsWith("page=", StringComparison.Ordinal))
                .Select(part => int.Parse(part["page=".Length..], CultureInfo.InvariantCulture))
                .First();
            requestedPages.Add(page);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(pages[page], Encoding.UTF8, "application/json"),
            });
        }
    }
}
