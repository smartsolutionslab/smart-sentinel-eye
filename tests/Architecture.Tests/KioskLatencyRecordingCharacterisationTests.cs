using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.StreamDistribution.Api;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins exactly what <c>POST /streams/kiosk-latency</c> records, refuses and drops (spec 341,
/// #2896, FR-006), so that reducing the complexity of <c>RecordKioskLatency</c> cannot change a
/// byte of what reaches the latency instruments (ADR-0118, ADR-0122).
///
/// <para>
/// The mapped <see cref="RequestDelegate"/> is driven in process, the same way
/// <c>DefaultEndpointsTests</c> drives the probes, so no Docker is needed. The three meters are
/// process-wide statics, so every test records under its own camera <see cref="Guid"/> and its
/// <see cref="MeterListener"/> keeps only the measurements carrying that camera; parallel tests
/// in this assembly cannot see each other's, and the listener is disposed with the test.
/// </para>
///
/// <para>
/// Tag sets are asserted exactly, not as "contains": an extra or leaked tag is behaviour.
/// </para>
///
/// <para>
/// NaN and infinity cannot arrive over HTTP with the default JSON options (named floating-point
/// literals are refused during model binding, before the handler runs), so the test host's JSON
/// options allow them. That reaches the handler's own finite check, which is defence in depth.
/// </para>
/// </summary>
public class KioskLatencyRecordingCharacterisationTests
{
    private const string SegmentInstrument = "sse.latency.segment.duration";

    private const string MeasurementRefusalBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"Measurement":["must be 'overlay_draw', 'receive_to_decoded', 'presentation_buffer', 'wall_skew' or 'label_delay'"]}}""";

    private const string CameraRefusalBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"Camera":["must name the tile's camera"]}}""";

    private const string ElapsedRefusalBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"ElapsedMilliseconds":["must be a finite number of milliseconds"]}}""";

    [Fact]
    public async Task A_caller_that_is_not_the_kiosk_gets_202_and_nothing_is_recorded()
    {
        var camera = Guid.NewGuid();
        using var observer = new MeterObserver(camera);

        var response = await PostAsync(Report("overlay_draw", camera, "42.5"), Operator());

        response.Status.ShouldBe(StatusCodes.Status202Accepted);
        observer.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_non_kiosk_caller_with_an_unknown_measurement_still_gets_202_and_nothing_is_recorded()
    {
        var camera = Guid.NewGuid();
        using var observer = new MeterObserver(camera);

        var response = await PostAsync(Report("nonsense", camera, "42.5"), Operator());

        response.Status.ShouldBe(StatusCodes.Status202Accepted);
        observer.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_overlay_draw_measurement_is_recorded_as_the_whole_composite_render_leg()
    {
        var camera = Guid.NewGuid();
        using var observer = new MeterObserver(camera);

        var response = await PostAsync(Report("overlay_draw", camera, "42.5"), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status202Accepted);
        var recorded = observer.Recorded.ShouldHaveSingleItem();
        recorded.Instrument.ShouldBe(SegmentInstrument);
        recorded.Value.ShouldBe(42.5);
        recorded.Tags.ShouldBe(SegmentTags("kiosk-overlay-draw", "overlay-composite-render", 50d, true, camera), ignoreOrder: true);
    }

    [Fact]
    public async Task The_receive_to_decoded_measurement_is_recorded_as_a_fragment_of_the_decode_leg()
    {
        var camera = Guid.NewGuid();
        using var observer = new MeterObserver(camera);

        var response = await PostAsync(Report("receive_to_decoded", camera, "17"), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status202Accepted);
        var recorded = observer.Recorded.ShouldHaveSingleItem();
        recorded.Instrument.ShouldBe(SegmentInstrument);
        recorded.Value.ShouldBe(17d);
        recorded.Tags.ShouldBe(SegmentTags("kiosk-receive-to-decoded", "sfu-to-kiosk-decode", 120d, false, camera), ignoreOrder: true);
    }

    [Fact]
    public async Task The_presentation_buffer_measurement_is_recorded_as_the_whole_presentation_buffer_leg()
    {
        var camera = Guid.NewGuid();
        using var observer = new MeterObserver(camera);

        var response = await PostAsync(Report("presentation_buffer", camera, "180"), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status202Accepted);
        var recorded = observer.Recorded.ShouldHaveSingleItem();
        recorded.Instrument.ShouldBe(SegmentInstrument);
        recorded.Value.ShouldBe(180d);
        recorded.Tags.ShouldBe(SegmentTags("kiosk-presentation-buffer", "presentation-buffer", 200d, true, camera), ignoreOrder: true);
    }

    [Fact]
    public async Task The_wall_skew_measurement_goes_to_its_own_instrument_and_not_to_the_latency_segments()
    {
        var camera = Guid.NewGuid();
        using var observer = new MeterObserver(camera);

        var response = await PostAsync(Report("wall_skew", camera, "12.5"), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status202Accepted);
        var recorded = observer.Recorded.ShouldHaveSingleItem();
        recorded.Instrument.ShouldBe("sse.wall.skew");
        recorded.Value.ShouldBe(12.5);
        recorded.Tags.ShouldBe(
            new Dictionary<string, object?> { ["bound_ms"] = 33d, ["camera"] = camera.ToString() },
            ignoreOrder: true);
    }

    [Fact]
    public async Task The_label_delay_measurement_goes_to_its_own_instrument_and_not_to_the_latency_segments()
    {
        var camera = Guid.NewGuid();
        using var observer = new MeterObserver(camera);

        var response = await PostAsync(Report("label_delay", camera, "90"), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status202Accepted);
        var recorded = observer.Recorded.ShouldHaveSingleItem();
        recorded.Instrument.ShouldBe("sse.overlay.label_delay");
        recorded.Value.ShouldBe(90d);
        recorded.Tags.ShouldBe(
            new Dictionary<string, object?> { ["cap_ms"] = 200d, ["camera"] = camera.ToString() },
            ignoreOrder: true);
    }

    [Fact]
    public async Task An_unknown_measurement_is_refused_with_the_measurement_message_and_nothing_is_recorded()
    {
        var camera = Guid.NewGuid();
        using var observer = new MeterObserver(camera);

        var response = await PostAsync(Report("nonsense", camera, "42.5"), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status400BadRequest);
        response.Body.ShouldBe(MeasurementRefusalBody);
        observer.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_camera_is_refused_with_the_camera_message_and_nothing_is_recorded()
    {
        using var observer = new MeterObserver(Guid.Empty);

        var response = await PostAsync(Report("overlay_draw", Guid.Empty, "42.5"), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status400BadRequest);
        response.Body.ShouldBe(CameraRefusalBody);
        observer.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_measurement_with_an_empty_camera_is_refused_on_the_measurement_first()
    {
        using var observer = new MeterObserver(Guid.Empty);

        var response = await PostAsync(Report("nonsense", Guid.Empty, "42.5"), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status400BadRequest);
        response.Body.ShouldBe(MeasurementRefusalBody);
        observer.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_camera_with_a_non_finite_elapsed_time_is_refused_on_the_camera_first()
    {
        using var observer = new MeterObserver(Guid.Empty);

        var response = await PostAsync(Report("overlay_draw", Guid.Empty, "\"NaN\""), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status400BadRequest);
        response.Body.ShouldBe(CameraRefusalBody);
        observer.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_measurement_with_an_empty_camera_and_a_non_finite_time_is_refused_on_the_measurement_only()
    {
        using var observer = new MeterObserver(Guid.Empty);

        var response = await PostAsync(Report("nonsense", Guid.Empty, "\"NaN\""), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status400BadRequest);
        response.Body.ShouldBe(MeasurementRefusalBody);
        observer.Recorded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("\"NaN\"")]
    [InlineData("\"Infinity\"")]
    [InlineData("\"-Infinity\"")]
    public async Task A_non_finite_elapsed_time_is_refused_and_nothing_is_recorded(string elapsed)
    {
        var camera = Guid.NewGuid();
        using var observer = new MeterObserver(camera);

        var response = await PostAsync(Report("overlay_draw", camera, elapsed), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status400BadRequest);
        response.Body.ShouldBe(ElapsedRefusalBody);
        observer.Recorded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("overlay_draw")]
    [InlineData("receive_to_decoded")]
    [InlineData("presentation_buffer")]
    [InlineData("wall_skew")]
    [InlineData("label_delay")]
    public async Task A_negative_elapsed_time_is_accepted_and_dropped_by_the_instrument(string measurement)
    {
        var camera = Guid.NewGuid();
        using var observer = new MeterObserver(camera);

        var response = await PostAsync(Report(measurement, camera, "-4"), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status202Accepted);
        observer.Recorded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("overlay_draw")]
    [InlineData("receive_to_decoded")]
    [InlineData("presentation_buffer")]
    [InlineData("wall_skew")]
    [InlineData("label_delay")]
    public async Task An_elapsed_time_over_sixty_seconds_is_accepted_and_dropped_by_the_instrument(string measurement)
    {
        var camera = Guid.NewGuid();
        using var observer = new MeterObserver(camera);

        var response = await PostAsync(Report(measurement, camera, "60001"), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status202Accepted);
        observer.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_elapsed_time_of_exactly_sixty_seconds_is_still_recorded()
    {
        var camera = Guid.NewGuid();
        using var observer = new MeterObserver(camera);

        var response = await PostAsync(Report("overlay_draw", camera, "60000"), Kiosk());

        response.Status.ShouldBe(StatusCodes.Status202Accepted);
        observer.Recorded.ShouldHaveSingleItem().Value.ShouldBe(60000d);
    }

    private static Dictionary<string, object?> SegmentTags(
        string segment,
        string leg,
        double budgetMilliseconds,
        bool isWholeLeg,
        Guid camera) =>
        new()
        {
            ["segment"] = segment,
            ["leg"] = leg,
            ["leg.budget_ms"] = budgetMilliseconds,
            ["segment.is_whole_leg"] = isWholeLeg,
            ["camera"] = camera.ToString(),
        };

    private static string Report(string measurement, Guid camera, string elapsedJson) =>
        $$"""{"measurement":"{{measurement}}","camera":"{{camera}}","elapsedMilliseconds":{{elapsedJson}}}""";

    private static ClaimsPrincipal Kiosk() => Principal(AuthenticationDefaults.KioskClientId);

    private static ClaimsPrincipal Operator() => Principal("management-web");

    private static ClaimsPrincipal Principal(string authorizedParty) =>
        new(new ClaimsIdentity([new Claim("azp", authorizedParty)], "test"));

    private static async Task<(int Status, string Body)> PostAsync(string json, ClaimsPrincipal user)
    {
        var builder = WebApplication.CreateBuilder([]);
        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
            options.SerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals);
        await using var app = builder.Build();
        app.MapStreamEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.RoutePattern.RawText == "/streams/kiosk-latency"
                && candidate.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains("POST"));

        var context = new DefaultHttpContext { RequestServices = app.Services, User = user };
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        var payload = Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(payload);
        context.Request.ContentLength = payload.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyPresent());
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    /// <summary>
    /// A <see cref="DefaultHttpContext"/> has no server in front of it to say a request can carry
    /// a body, and without that the binder reports the body as not provided.
    /// </summary>
    private sealed class BodyPresent : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed record Measurement(string Instrument, double Value, Dictionary<string, object?> Tags);

    /// <summary>
    /// Collects what the three latency meters record for one camera, and nothing else.
    /// </summary>
    private sealed class MeterObserver : IDisposable
    {
        private static readonly HashSet<string> MeterNames =
        [
            LatencyBudget.MeterName,
            WallSkew.MeterName,
            LabelDelay.MeterName,
        ];

        private readonly object gate = new();
        private readonly List<Measurement> recorded = [];
        private readonly MeterListener listener = new();

        public MeterObserver(Guid camera)
        {
            var expected = camera.ToString();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (MeterNames.Contains(instrument.Meter.Name))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                Dictionary<string, object?> captured = [];
                foreach (var tag in tags)
                {
                    captured[tag.Key] = tag.Value;
                }

                if (!captured.TryGetValue("camera", out var seen) || !Equals(seen, expected))
                {
                    return;
                }

                lock (gate)
                {
                    recorded.Add(new Measurement(instrument.Name, value, captured));
                }
            });
            listener.Start();
        }

        public IReadOnlyList<Measurement> Recorded
        {
            get
            {
                lock (gate)
                {
                    return [.. recorded];
                }
            }
        }

        public void Dispose() => listener.Dispose();
    }
}
