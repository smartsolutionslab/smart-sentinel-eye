using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

// Plan §5.5 cases through the real handler, plus a check that every registered field really is redacted.
public class AuditPayloadRedactionPostFixAdversarialTests
{
    private const string MarkerTail = "CRET";
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    private static IntegrationEventAuditHandler Build(InMemoryAuditEventRepository repo) =>
        new(new AuditingMessageHandler(repo, V1ResourceMap.Default, new FakeClock(At),
            Options.Create(new AuditMeasurementOptions()), NullLogger<AuditingMessageHandler>.Instance));

    private static EventMetadata Metadata() => new(Guid.CreateVersion7(), At, Fab: "munich", Actor: null);

    public static TheoryData<string> Spellings() =>
    [
        "rtsp://user:SECRET@cam/x", "rtsp://cam/SECRET/x", "rtsp://cam/x?token=SECRET", "rtsp://cam/x#SECRET",
        "rtsp://cam?SECRET", "rtsp://cam/%53E%43RET", @"rtsp://cam\SECRET", "rtsp://cam/x@SECRET", "RTSP://CAM/SECRET",
        "rtsp://[fd00::5]:554/SECRET", "rtsp://[fe80::1%25SECRET]/x", "rtsp://kämera.example/SECRET",
        "  rtsp://cam/SECRET  ", "rtsp://cam/SE\r\nCRET", "rtsp://cam/\0SECRET", "rtsp://cam:554;SECRET/x",
        "rtsp://cam/x?a=1&b=<SECRET>+", "rtsp://cam/" + new string('x', 2030) + "SECRET",
    ];

    [Theory]
    [MemberData(nameof(Spellings))]
    public async Task Registration_leaks_nothing(string url)
    {
        InMemoryAuditEventRepository repo = new();
        await Build(repo).Handle(new CameraRegisteredV1(Guid.CreateVersion7(), "n", url, At, Guid.CreateVersion7(), Metadata()), default);
        var raw = repo.Committed[0].Payload.Content.Value;
        raw.ShouldNotContain(MarkerTail, Case.Insensitive);
        AuditRowMapper.Map(repo.Committed[0]).Payload.ShouldNotContain(MarkerTail, Case.Insensitive);
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public async Task Address_change_leaks_nothing(string url)
    {
        InMemoryAuditEventRepository repo = new();
        await Build(repo).Handle(new CameraAddressChangedV1(Guid.CreateVersion7(), "munich", url, url + "2", At, Guid.CreateVersion7(), Metadata()), default);
        repo.Committed[0].Payload.Content.Value.ShouldNotContain(MarkerTail, Case.Insensitive);
    }

    // The encoded spellings never contain the literal marker tail, so Registration_leaks_nothing cannot fail
    // on them; each row names the fragment that an unredacted copy of that very input would still carry.
    public static TheoryData<string, string> EncodedSpellings() =>
        new()
        {
            { "rtsp://cam/%53E%43RET", "RET" },
            { "rtsp://cam/%53E%43RET", "%43" },
            { "rtsp://cam/%53%45%43%52%45%54", "%53" },
            { "rtsp://cam/%53%45%43%52%45%54", "%54" },
        };

    [Theory]
    [MemberData(nameof(EncodedSpellings))]
    public async Task Registration_of_an_encoded_url_keeps_no_fragment_of_it(string url, string fragment)
    {
        InMemoryAuditEventRepository repo = new();
        await Build(repo).Handle(new CameraRegisteredV1(Guid.CreateVersion7(), "n", url, At, Guid.CreateVersion7(), Metadata()), default);
        repo.Committed[0].Payload.Content.Value.ShouldNotContain(fragment, Case.Insensitive);
        AuditRowMapper.Map(repo.Committed[0]).Payload.ShouldNotContain(fragment, Case.Insensitive);
    }

    [Fact]
    public async Task A_70000_char_url_is_reduced_to_its_origin_not_the_placeholder()
    {
        InMemoryAuditEventRepository repo = new();
        var url = "rtsp://cam/" + new string('x', 70000) + "?token=SECRET";
        await Build(repo).Handle(new CameraRegisteredV1(Guid.CreateVersion7(), "n", url, At, Guid.CreateVersion7(), Metadata()), default);
        JsonDocument.Parse(repo.Committed[0].Payload.Content.Value).RootElement.GetProperty("Url").GetString().ShouldBe("rtsp://cam");
    }

    [Fact]
    public async Task Both_null_still_audits_with_no_detail_change()
    {
        InMemoryAuditEventRepository repo = new();
        await Build(repo).Handle(new CameraAddressChangedV1(Guid.CreateVersion7(), "munich", null!, null!, At, Guid.CreateVersion7(), Metadata()), default);
        var json = JsonDocument.Parse(repo.Committed[0].Payload.Content.Value).RootElement;
        json.GetProperty("PreviousUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("DetailChanged").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData("rtsp://CAM/x", "rtsp://cam/x", true)]
    [InlineData("rtsp://cam", "rtsp://cam/", true)]
    [InlineData("rtsp://cam/x", "rtsp://cam/x ", true)]
    [InlineData("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1%25eth1]/x", true)]
    [InlineData("rtsp://cam:554/x", "rtsp://cam/x", false)]
    public async Task DetailChanged_edges(string previous, string url, bool expected)
    {
        InMemoryAuditEventRepository repo = new();
        await Build(repo).Handle(new CameraAddressChangedV1(Guid.CreateVersion7(), "munich", previous, url, At, Guid.CreateVersion7(), Metadata()), default);
        var raw = repo.Committed[0].Payload.Content.Value;
        JsonDocument.Parse(raw).RootElement.GetProperty("DetailChanged").GetBoolean().ShouldBe(expected);
        raw.ShouldNotContain(MarkerTail, Case.Insensitive);
    }

    [Fact]
    public async Task Two_unparseable_spellings_report_the_detail_change_as_unknown()
    {
        InMemoryAuditEventRepository repo = new();
        await Build(repo).Handle(new CameraAddressChangedV1(Guid.CreateVersion7(), "munich", "garbage SECRET1", "garbage SECRET2", At, Guid.CreateVersion7(), Metadata()), default);
        var raw = repo.Committed[0].Payload.Content.Value;
        JsonDocument.Parse(raw).RootElement.GetProperty("DetailChanged").ValueKind.ShouldBe(JsonValueKind.Null);
        raw.ShouldNotContain(MarkerTail, Case.Insensitive);
    }

    [Fact]
    public async Task Every_registered_url_field_is_stripped_of_a_marker()
    {
        foreach (var (type, fields) in AuditPayloadRedaction.UrlFields)
        {
            InMemoryAuditEventRepository repo = new();
            var message = (IIntegrationEvent)Activator.CreateInstance(type, ConstructorArguments(type, fields))!;

            var handle = typeof(IntegrationEventAuditHandler).GetMethod(nameof(IntegrationEventAuditHandler.Handle), [type, typeof(CancellationToken)])!;
            await (Task)handle.Invoke(Build(repo), [message, CancellationToken.None])!;

            var json = JsonDocument.Parse(repo.Committed[0].Payload.Content.Value).RootElement;
            foreach (var field in fields)
            {
                json.GetProperty(field).GetString().ShouldBe("rtsp://cam", $"{type.Name}.{field}");
            }
        }
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("")]
    [InlineData("{} ")]
    public void A_serialisation_that_does_not_end_in_a_closing_brace_is_refused_without_echoing_it(string json)
    {
        var thrown = Should.Throw<InvalidOperationException>(
            () => AuditPayloadRedaction.AppendMember(typeof(CameraRegisteredV1), json, "\"DetailChanged\":true"));

        thrown.Message.ShouldContain(nameof(CameraRegisteredV1));
        thrown.Message.ShouldNotContain(json.Length == 0 ? "\u0001" : json);
    }

    [Fact]
    public void A_serialisation_that_ends_in_a_closing_brace_gets_the_member_appended()
    {
        AuditPayloadRedaction.AppendMember(typeof(CameraRegisteredV1), "{\"A\":1}", "\"B\":2")
            .ShouldBe("{\"A\":1,\"B\":2}");
    }

    private static object[] ConstructorArguments(Type type, IReadOnlyList<string> fields) =>
        [.. type.GetConstructors().Single().GetParameters().Select(parameter => (object)(parameter.ParameterType switch
        {
            var t when t == typeof(string) => fields.Contains(parameter.Name!) ? "rtsp://cam/SECRET?token=SECRET" : "munich",
            var t when t == typeof(Guid) => Guid.CreateVersion7(),
            var t when t == typeof(DateTimeOffset) => At,
            var t when t == typeof(EventMetadata) => Metadata(),
            var t => throw new InvalidOperationException($"No test value for {t.Name}."),
        }))];
}
