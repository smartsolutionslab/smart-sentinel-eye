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

public class ListKiosksQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    private static RegisteredClientAggregate Build(ClientKind kind, string clientId, string fab) =>
        new RegisteredClientBuilder()
            .WithClientId(clientId)
            .WithKind(kind)
            .WithFab(fab)
            .WithClock(Now)
            .Build();

    private static ListKiosksQueryHandler HandlerFor(params RegisteredClientAggregate[] clients) =>
        new(new InMemoryRegisteredClientQuerySource([.. clients]));

    [Fact]
    public async Task Lists_only_kiosk_clients_and_omits_devices_and_webhooks()
    {
        ListKiosksQueryHandler handler = HandlerFor(
            Build(ClientKind.Kiosk, "kiosk-3", "munich"),
            Build(ClientKind.Device, "plc-station-4", "munich"),
            Build(ClientKind.WebhookIntegration, "hook-grafana", "munich"));

        Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
            await handler.HandleAsync(new ListKiosksQuery(Option<FabIdentifier>.None), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(dto => dto.ClientId).ShouldBe(["kiosk-3"]);
        result.Value.ShouldAllBe(dto => dto.Kind == ClientKind.Kiosk.Value);
    }

    [Fact]
    public async Task Filters_to_the_requested_fab_when_one_is_supplied()
    {
        ListKiosksQueryHandler handler = HandlerFor(
            Build(ClientKind.Kiosk, "kiosk-munich", "munich"),
            Build(ClientKind.Kiosk, "kiosk-dresden", "dresden"));

        Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
            await handler.HandleAsync(
                new ListKiosksQuery(Option<FabIdentifier>.Some(FabIdentifier.From("dresden"))),
                CancellationToken.None);

        result.Value.Select(dto => dto.ClientId).ShouldBe(["kiosk-dresden"]);
    }

    [Fact]
    public async Task Returns_an_empty_list_when_no_kiosk_matches()
    {
        ListKiosksQueryHandler handler = HandlerFor(
            Build(ClientKind.Device, "plc-station-4", "munich"));

        Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
            await handler.HandleAsync(new ListKiosksQuery(Option<FabIdentifier>.None), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Projects_audit_fields_without_a_client_secret()
    {
        ListKiosksQueryHandler handler = HandlerFor(Build(ClientKind.Kiosk, "kiosk-3", "munich"));

        Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
            await handler.HandleAsync(new ListKiosksQuery(Option<FabIdentifier>.None), CancellationToken.None);

        RegisteredClientSummaryDto dto = result.Value.ShouldHaveSingleItem();
        dto.ClientId.ShouldBe("kiosk-3");
        dto.Kind.ShouldBe("Kiosk");
        dto.Fab.ShouldBe("munich");
        dto.RegisteredAt.ShouldBe(Now);
        dto.DisabledAt.ShouldBeNull();
        typeof(RegisteredClientSummaryDto)
            .GetProperties()
            .Select(property => property.Name)
            .ShouldNotContain(name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }
}
