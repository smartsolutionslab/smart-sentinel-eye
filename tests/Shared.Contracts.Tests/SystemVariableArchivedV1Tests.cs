using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

namespace SmartSentinelEye.Shared.Contracts.Tests;

public class SystemVariableArchivedV1Tests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid variable = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();

        SystemVariableArchivedV1 evt = new(variable, "oeeLine1", FixedMoment, by, Metadata: TestMetadata);

        evt.Variable.ShouldBe(variable);
        evt.Name.ShouldBe("oeeLine1");
        evt.ArchivedAt.ShouldBe(FixedMoment);
        evt.ArchivedBy.ShouldBe(by);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        SystemVariableArchivedV1 evt = new(
            Guid.CreateVersion7(), "x", FixedMoment, Guid.CreateVersion7(), Metadata: TestMetadata);
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid variable = Guid.CreateVersion7();
        Guid by = Guid.CreateVersion7();

        SystemVariableArchivedV1 a = new(variable, "x", FixedMoment, by, Metadata: TestMetadata);
        SystemVariableArchivedV1 b = new(variable, "x", FixedMoment, by, Metadata: TestMetadata);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        SystemVariableArchivedV1 original = new(
            Guid.CreateVersion7(), "oeeLine1", FixedMoment, Guid.CreateVersion7(), Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        SystemVariableArchivedV1 deserialized =
            JsonSerializer.Deserialize<SystemVariableArchivedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
