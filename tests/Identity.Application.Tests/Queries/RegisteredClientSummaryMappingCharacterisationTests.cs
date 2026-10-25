using System.Globalization;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.Queries;
using SmartSentinelEye.Identity.Application.Queries.Handlers;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Queries;

// Spec 459 (#3154), phase 4a: every RegisteredClientSummaryDto field on the shared projection
// as it stands, driven through the webhook list (the only kind that can be rotated) and the device list. The two optional moments are given distinct
// values so a crosswise assignment is red.
public class RegisteredClientSummaryMappingCharacterisationTests
{
    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);

    private static async Task<IReadOnlyList<RegisteredClientSummaryDto>> ListWebhooks(
        params RegisteredClientAggregate[] clients)
    {
        ListWebhookClientsQueryHandler handler = new(new InMemoryRegisteredClientQuerySource([.. clients]));

        var result = await handler.HandleAsync(
            new ListWebhookClientsQuery([FabIdentifier.From("hamburg")]), CancellationToken.None);

        return result.Value;
    }

    private static async Task<IReadOnlyList<RegisteredClientSummaryDto>> List(
        params RegisteredClientAggregate[] clients)
    {
        ListDevicesQueryHandler handler = new(new InMemoryRegisteredClientQuerySource([.. clients]));

        var result = await handler.HandleAsync(
            new ListDevicesQuery([FabIdentifier.From("hamburg")]), CancellationToken.None);

        return result.Value;
    }

    [Fact]
    public async Task A_rotated_then_disabled_client_maps_every_field_from_its_own_source()
    {
        var registeredBy = Guid.Parse("1a1a1a1a-0000-7000-8000-00000000001a");
        var registeredAt = At("2026-05-01T08:00:00Z");
        var rotatedAt = At("2026-05-02T09:15:00Z");
        var disabledAt = At("2026-05-03T10:30:00Z");
        var client = new RegisteredClientBuilder()
            .WithClientId("press-9-plc")
            .WithKind(ClientKind.WebhookIntegration)
            .WithFab("hamburg")
            .WithRegisteredBy(OperatorIdentifier.From(registeredBy))
            .WithClock(registeredAt)
            .Build();
        client.Rotate(new FakeClock(rotatedAt));
        client.Disable(new FakeClock(disabledAt));

        var dto = (await ListWebhooks(client)).ShouldHaveSingleItem();

        dto.RegisteredClientIdentifier.ShouldBe(client.Id.Value);
        dto.Version.ShouldBe(client.Version);
        dto.ClientId.ShouldBe("press-9-plc");
        dto.Kind.ShouldBe(ClientKind.WebhookIntegration.Value);
        dto.Fab.ShouldBe("hamburg");
        dto.RegisteredAt.ShouldBe(registeredAt);
        dto.RegisteredBy.ShouldBe(registeredBy);
        dto.LastRotatedAt.ShouldBe(rotatedAt);
        dto.DisabledAt.ShouldBe(disabledAt);
    }

    [Fact]
    public async Task A_fresh_client_has_neither_a_rotation_nor_a_disable_moment()
    {
        var client = new RegisteredClientBuilder()
            .WithClientId("press-9-plc")
            .WithFab("hamburg")
            .WithClock(At("2026-05-01T08:00:00Z"))
            .Build();

        var dto = (await List(client)).ShouldHaveSingleItem();

        dto.DisabledAt.ShouldBeNull();
        dto.LastRotatedAt.ShouldBeNull();
    }
}
