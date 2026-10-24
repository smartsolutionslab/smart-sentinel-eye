using System.Globalization;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Redaction;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Redaction;

/// <summary>
/// Spec 449 (#2990), U1. Per-row decision of the in-place redaction of camera URLs stored before spec 377.
/// The size oracle is the capture handler itself (<see cref="AuditPayloadRedaction.Serialize"/>), not a
/// re-derivation: the stored <c>payload_size_bytes</c> must stay on the handler's serialisation basis,
/// escaping included. Markers are synthetic.
/// </summary>
public class StoredCameraUrlRewriteTests
{
    private const string Registered = nameof(CameraRegisteredV1);
    private const string AddressChanged = nameof(CameraAddressChangedV1);

    private static readonly DateTimeOffset At =
        DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    private static Dictionary<string, string?> Stored(string? url, string? previousUrl = null) =>
        new() { ["Url"] = url, ["PreviousUrl"] = previousUrl };

    private static StoredCameraUrlRewrite.Plan PlanOf(string kind, IReadOnlyDictionary<string, string?> stored)
    {
        var plan = StoredCameraUrlRewrite.PlanFor(kind, stored);

        plan.HasValue.ShouldBeTrue("a stored value that is not its origin must produce a plan");
        return plan.Value;
    }

    private static EventMetadata Metadata() => new(Guid.CreateVersion7(), At, Fab: "munich", Actor: null);

    private static int Bytes(string json) => Encoding.UTF8.GetByteCount(json);

    [Theory]
    [InlineData("rtsp://cam-a.test:8554/live?token=SECRETMARK", "rtsp://cam-a.test:8554")]
    [InlineData("rtsp://cam-a.test/SECRETMARK/path", "rtsp://cam-a.test")]
    [InlineData("rtsp://USERPWMARK:USERPWMARK@cam-a.test/stream", "rtsp://cam-a.test")]
    [InlineData("rtsp://cam-a.test/stream#SECRETMARK", "rtsp://cam-a.test")]
    public void A_registered_url_that_is_more_than_its_origin_is_planned_as_its_origin(string stored, string origin)
    {
        var plan = PlanOf(Registered, Stored(stored));

        var change = plan.Changes.ShouldHaveSingleItem();
        change.Key.ShouldBe("Url");
        change.Old.ShouldBe(stored);
        change.New.ShouldBe(origin);
    }

    [Fact]
    public void A_registered_url_that_is_already_its_origin_needs_no_plan()
    {
        StoredCameraUrlRewrite.PlanFor(Registered, Stored("rtsp://cam-a.test:8554")).HasValue.ShouldBeFalse();
    }

    [Fact]
    public void The_unparseable_placeholder_needs_no_plan()
    {
        StoredCameraUrlRewrite.PlanFor(Registered, Stored(UrlRedaction.Unparseable)).HasValue.ShouldBeFalse();
    }

    [Fact]
    public void A_stored_value_that_cannot_be_read_as_an_address_is_planned_as_the_placeholder()
    {
        var plan = PlanOf(Registered, Stored("SECRETMARK is not an address"));

        plan.Changes.ShouldHaveSingleItem().New.ShouldBe(UrlRedaction.Unparseable);
    }

    [Fact]
    public void A_null_value_is_left_out_of_the_plan_and_all_null_needs_no_plan()
    {
        var plan = PlanOf(AddressChanged, Stored("rtsp://cam-c.test/new?k=SECRETMARK", previousUrl: null));

        plan.Changes.Select(change => change.Key).ShouldBe(["Url"]);
        StoredCameraUrlRewrite.PlanFor(AddressChanged, Stored(null, null)).HasValue.ShouldBeFalse();
    }

    [Fact]
    public void Of_two_keys_only_the_one_that_is_not_its_origin_is_planned()
    {
        var plan = PlanOf(AddressChanged, Stored("rtsp://cam-c.test", previousUrl: "rtsp://cam-b.test/old?k=SECRETMARK"));

        var change = plan.Changes.ShouldHaveSingleItem();
        change.Key.ShouldBe("PreviousUrl");
        change.New.ShouldBe("rtsp://cam-b.test");
    }

    [Fact]
    public void Both_keys_are_planned_when_neither_is_its_origin()
    {
        var plan = PlanOf(AddressChanged, Stored("rtsp://cam-c.test/new?k=SECRETMARK", "rtsp://cam-b.test/old"));

        plan.Changes.Select(change => change.Key).ShouldBe(["PreviousUrl", "Url"], ignoreOrder: true);
    }

    [Fact]
    public void The_size_delta_for_a_registered_camera_is_what_the_capture_handler_would_have_saved()
    {
        const string url = "rtsp://USERPWMARK:USERPWMARK@cam-a.test:8554/live?token=SECRETMARK&x=1";
        CameraRegisteredV1 message = new(Guid.CreateVersion7(), "north-gate", url, At, Guid.CreateVersion7(), Metadata());
        var before = Bytes(JsonSerializer.Serialize(message));
        var after = Bytes(AuditPayloadRedaction.Serialize(message));

        var plan = PlanOf(Registered, Stored(url));

        plan.SizeDelta.ShouldBe(before - after);
        plan.SizeDelta.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_size_delta_for_an_address_change_is_what_the_capture_handler_would_have_saved()
    {
        const string previous = "rtsp://cam-b.test/old?token=SECRETMARK&x=1";
        const string current = "rtsp://USERPWMARK@cam-c.test:8554/new/SECRETMARK";
        CameraAddressChangedV1 full = new(
            Guid.CreateVersion7(), "munich", previous, current, At, Guid.CreateVersion7(), Metadata());
        var origins = full with
        {
            PreviousUrl = UrlRedaction.ToOrigin(previous),
            Url = UrlRedaction.ToOrigin(current),
        };

        // Rows stored before spec 377 are the raw serialisation of the full message. The handler redacts both
        // sides alike, so it anchors the origin side: its output is that serialisation plus the DetailChanged
        // member, which a pre-377 row never had.
        var rawOrigins = JsonSerializer.Serialize(origins);
        AuditPayloadRedaction.Serialize(full).ShouldStartWith(rawOrigins[..^1], Case.Sensitive);
        var expected = Bytes(JsonSerializer.Serialize(full)) - Bytes(rawOrigins);
        var plan = PlanOf(AddressChanged, Stored(current, previous));

        plan.SizeDelta.ShouldBe(expected);
        plan.SizeDelta.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void An_unknown_event_kind_is_a_programming_error_not_an_empty_plan()
    {
        Should.Throw<InvalidOperationException>(
            () => StoredCameraUrlRewrite.PlanFor("OverlayPublishedV1", Stored("rtsp://cam-a.test/x?k=SECRETMARK")));
    }

    [Fact]
    public void The_registered_fields_are_exactly_the_two_the_sql_side_selects_and_rewrites()
    {
        // AuditUrlRedactionStep selects and updates only Url and PreviousUrl. A field added to the register
        // must come with that SQL, so this goes red first.
        AuditPayloadRedaction.UrlFields.Values.SelectMany(fields => fields).Distinct()
            .ShouldBe(["Url", "PreviousUrl"], ignoreOrder: true);
    }

    [Fact]
    public void The_event_kinds_are_exactly_the_registered_types_of_the_capture_handler()
    {
        StoredCameraUrlRewrite.EventKinds.ShouldBe(
            AuditPayloadRedaction.UrlFields.Keys.Select(type => type.Name),
            ignoreOrder: true);
    }

    [Fact]
    public void The_keys_of_each_kind_are_exactly_the_registered_fields_of_the_capture_handler()
    {
        foreach (var (type, fields) in AuditPayloadRedaction.UrlFields)
        {
            StoredCameraUrlRewrite.KeysFor(type.Name).ShouldBe(fields, ignoreOrder: true, customMessage: type.Name);
        }
    }
}
