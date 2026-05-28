using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class SystemVariableValueRequestedV1Tests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Exposes_every_field_via_the_positional_constructor()
    {
        Guid causing = Guid.CreateVersion7();
        SystemVariableValueRequestedV1 evt = new("oeeLine1", "82.5", Moment, causing);

        evt.Name.ShouldBe("oeeLine1");
        evt.Value.ShouldBe("82.5");
        evt.RequestedAt.ShouldBe(Moment);
        evt.CausingEventIdentifier.ShouldBe(causing);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        SystemVariableValueRequestedV1 evt = new("x", "1", Moment, Guid.CreateVersion7());
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid causing = Guid.CreateVersion7();
        SystemVariableValueRequestedV1 a = new("x", "1", Moment, causing);
        SystemVariableValueRequestedV1 b = new("x", "1", Moment, causing);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        SystemVariableValueRequestedV1 original =
            new("oeeLine1", "82.5", Moment, Guid.CreateVersion7());

        string json = JsonSerializer.Serialize(original);
        SystemVariableValueRequestedV1 deserialized =
            JsonSerializer.Deserialize<SystemVariableValueRequestedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
