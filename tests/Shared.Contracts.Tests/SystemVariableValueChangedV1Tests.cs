using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class SystemVariableValueChangedV1Tests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid variable = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();

        SystemVariableValueChangedV1 evt = new(
            variable, "oeeLine1", "Number", "82.4", FixedMoment, by);

        evt.Variable.ShouldBe(variable);
        evt.Name.ShouldBe("oeeLine1");
        evt.Type.ShouldBe("Number");
        evt.Value.ShouldBe("82.4");
        evt.ChangedAt.ShouldBe(FixedMoment);
        evt.ChangedBy.ShouldBe(by);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        SystemVariableValueChangedV1 evt = new(
            Guid.CreateVersion7(), "x", "String", "hello", FixedMoment, Guid.CreateVersion7());
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid variable = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();

        SystemVariableValueChangedV1 a = new(variable, "x", "Boolean", "true", FixedMoment, by);
        SystemVariableValueChangedV1 b = new(variable, "x", "Boolean", "true", FixedMoment, by);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        SystemVariableValueChangedV1 original = new(
            Guid.CreateVersion7(), "oeeLine1", "Number", "82.4", FixedMoment, Guid.CreateVersion7());

        string json = JsonSerializer.Serialize(original);
        SystemVariableValueChangedV1 deserialized =
            JsonSerializer.Deserialize<SystemVariableValueChangedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
