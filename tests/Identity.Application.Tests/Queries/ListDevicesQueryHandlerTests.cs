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

public class ListDevicesQueryHandlerTests
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

    private static ListDevicesQueryHandler HandlerFor(params RegisteredClientAggregate[] clients) =>
        new(new InMemoryRegisteredClientQuerySource([.. clients]));

    [Fact]
    public async Task Lists_only_device_clients_and_omits_kiosks_and_webhooks()
    {
        ListDevicesQueryHandler handler = HandlerFor(
            Build(ClientKind.Device, "plc-station-4", "munich"),
            Build(ClientKind.Kiosk, "kiosk-3", "munich"),
            Build(ClientKind.WebhookIntegration, "hook-grafana", "munich"));

        Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
            await handler.HandleAsync(new ListDevicesQuery(Option<FabIdentifier>.None), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(dto => dto.ClientId).ShouldBe(["plc-station-4"]);
        result.Value.ShouldAllBe(dto => dto.Kind == ClientKind.Device.Value);
    }

    [Fact]
    public async Task Filters_to_the_requested_fab_when_one_is_supplied()
    {
        ListDevicesQueryHandler handler = HandlerFor(
            Build(ClientKind.Device, "plc-munich", "munich"),
            Build(ClientKind.Device, "plc-dresden", "dresden"));

        Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
            await handler.HandleAsync(
                new ListDevicesQuery(Option<FabIdentifier>.Some(FabIdentifier.From("munich"))),
                CancellationToken.None);

        result.Value.Select(dto => dto.ClientId).ShouldBe(["plc-munich"]);
    }

    [Fact]
    public async Task Returns_devices_across_all_fabs_when_no_fab_is_supplied()
    {
        ListDevicesQueryHandler handler = HandlerFor(
            Build(ClientKind.Device, "plc-munich", "munich"),
            Build(ClientKind.Device, "plc-dresden", "dresden"));

        Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
            await handler.HandleAsync(new ListDevicesQuery(Option<FabIdentifier>.None), CancellationToken.None);

        result.Value.Select(dto => dto.ClientId).ShouldBe(["plc-munich", "plc-dresden"], ignoreOrder: true);
    }

    [Fact]
    public async Task Returns_an_empty_list_when_no_device_matches()
    {
        ListDevicesQueryHandler handler = HandlerFor(
            Build(ClientKind.Kiosk, "kiosk-3", "munich"));

        Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
            await handler.HandleAsync(new ListDevicesQuery(Option<FabIdentifier>.None), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Summary_dto_never_exposes_a_client_secret_property()
    {
        // The secret is write-once and never persisted; assert structurally
        // that the read-side DTO has no field that could carry it.
        ListDevicesQueryHandler handler = HandlerFor(Build(ClientKind.Device, "plc-station-4", "munich"));

        Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError> result =
            await handler.HandleAsync(new ListDevicesQuery(Option<FabIdentifier>.None), CancellationToken.None);

        result.Value.ShouldHaveSingleItem();
        typeof(RegisteredClientSummaryDto)
            .GetProperties()
            .Select(property => property.Name)
            .ShouldNotContain(name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }
}
