using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class SystemVariableDefinedV1Tests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid variable = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();

        SystemVariableDefinedV1 evt = new(variable, "oeeLine1", "Number", FixedMoment, by);

        evt.Variable.ShouldBe(variable);
        evt.Name.ShouldBe("oeeLine1");
        evt.Type.ShouldBe("Number");
        evt.DefinedAt.ShouldBe(FixedMoment);
        evt.DefinedBy.ShouldBe(by);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        SystemVariableDefinedV1 evt = new(
            Guid.CreateVersion7(), "x", "String", FixedMoment, Guid.CreateVersion7());
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid variable = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();

        SystemVariableDefinedV1 a = new(variable, "x", "Number", FixedMoment, by);
        SystemVariableDefinedV1 b = new(variable, "x", "Number", FixedMoment, by);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        SystemVariableDefinedV1 original = new(
            Guid.CreateVersion7(), "oeeLine1", "Number", FixedMoment, Guid.CreateVersion7());

        string json = JsonSerializer.Serialize(original);
        SystemVariableDefinedV1 deserialized =
            JsonSerializer.Deserialize<SystemVariableDefinedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
