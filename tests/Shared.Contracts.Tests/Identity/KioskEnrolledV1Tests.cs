using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.Shared.Contracts.Tests.Identity;

public class KioskEnrolledV1Tests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-29T08:14:33Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public void Exposes_every_field_via_the_positional_constructor()
    {
        Guid id = Guid.CreateVersion7();
        KioskEnrolledV1 evt = new(id, "kiosk-3", "munich", Moment, Metadata: TestMetadata);

        evt.RegisteredClientIdentifier.ShouldBe(id);
        evt.ClientId.ShouldBe("kiosk-3");
        evt.Fab.ShouldBe("munich");
        evt.EnrolledAt.ShouldBe(Moment);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it() =>
        new KioskEnrolledV1(Guid.CreateVersion7(), "k", "f", Moment, Metadata: TestMetadata)
            .ShouldBeAssignableTo<IIntegrationEvent>();

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid id = Guid.CreateVersion7();
        new KioskEnrolledV1(id, "k", "f", Moment, Metadata: TestMetadata)
            .ShouldBe(new KioskEnrolledV1(id, "k", "f", Moment, Metadata: TestMetadata));
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        KioskEnrolledV1 original = new(Guid.CreateVersion7(), "kiosk-3", "munich", Moment, Metadata: TestMetadata);
        string json = JsonSerializer.Serialize(original);
        JsonSerializer.Deserialize<KioskEnrolledV1>(json).ShouldBe(original);
    }
}
