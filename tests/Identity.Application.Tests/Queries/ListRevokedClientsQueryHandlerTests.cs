using System.Globalization;
using SmartSentinelEye.Identity.Application.Queries;
using SmartSentinelEye.Identity.Application.Queries.Handlers;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Queries;

/// <summary>
/// Spec 270 T006 (ADR-0160, plan.md §4.2/§7). Backs
/// <c>GET /registered-clients/revoked</c> (FR-007/FR-008): every disabled
/// <c>RegisteredClient</c>, across every kind and every fab — this query is
/// deliberately not fab-scoped, unlike its <c>ListKiosksQuery</c> /
/// <c>ListDevicesQuery</c> siblings, because its one caller is a platform
/// service account in no fab group (plan.md §4.2) — with the latest
/// <c>DisabledAt</c> per client id (FR-001's "latest wins").
///
/// <para>
/// Mirrors <c>ListKiosksQueryHandlerTests</c>' shape and fakes
/// (<see cref="InMemoryRegisteredClientQuerySource"/>,
/// <see cref="RegisteredClientBuilder"/>).
/// </para>
///
/// <para>
/// <b>Red today: compile.</b> Neither <c>ListRevokedClientsQuery</c> nor
/// <c>ListRevokedClientsQueryHandler</c> exists on <c>develop</c>.
/// </para>
/// </summary>
public class ListRevokedClientsQueryHandlerTests
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

    private static RegisteredClientAggregate Disable(RegisteredClientAggregate client, DateTimeOffset at)
    {
        client.Disable(new FakeClock(at));
        return client;
    }

    private static ListRevokedClientsQueryHandler HandlerFor(params RegisteredClientAggregate[] clients) =>
        new(new InMemoryRegisteredClientQuerySource([.. clients]));

    [Fact]
    public async Task Active_rows_are_excluded()
    {
        RegisteredClientAggregate active = Build(ClientKind.Kiosk, "kiosk-active", "munich");
        ListRevokedClientsQueryHandler handler = HandlerFor(active);

        Result<IReadOnlyList<RevokedClientEntry>, ListClientsError> result =
            await handler.HandleAsync(new ListRevokedClientsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Disabled_kiosk_device_and_webhook_rows_are_all_included()
    {
        RegisteredClientAggregate kiosk = Disable(Build(ClientKind.Kiosk, "kiosk-1", "munich"), Now.AddHours(1));
        RegisteredClientAggregate device = Disable(Build(ClientKind.Device, "device-1", "munich"), Now.AddHours(1));
        RegisteredClientAggregate webhook =
            Disable(Build(ClientKind.WebhookIntegration, "hook-1", "munich"), Now.AddHours(1));
        ListRevokedClientsQueryHandler handler = HandlerFor(kiosk, device, webhook);

        Result<IReadOnlyList<RevokedClientEntry>, ListClientsError> result =
            await handler.HandleAsync(new ListRevokedClientsQuery(), CancellationToken.None);

        result.Value.Select(entry => entry.ClientId).ShouldBe(
            ["kiosk-1", "device-1", "hook-1"], ignoreOrder: true);
    }

    [Fact]
    public async Task Rows_from_two_fabs_are_both_included()
    {
        RegisteredClientAggregate munich =
            Disable(Build(ClientKind.Kiosk, "kiosk-munich", "munich"), Now.AddHours(1));
        RegisteredClientAggregate dresden =
            Disable(Build(ClientKind.Kiosk, "kiosk-dresden", "dresden"), Now.AddHours(1));
        ListRevokedClientsQueryHandler handler = HandlerFor(munich, dresden);

        Result<IReadOnlyList<RevokedClientEntry>, ListClientsError> result =
            await handler.HandleAsync(new ListRevokedClientsQuery(), CancellationToken.None);

        result.Value.Select(entry => entry.ClientId).ShouldBe(
            ["kiosk-munich", "kiosk-dresden"], ignoreOrder: true);
    }

    /// <summary>
    /// Disable, re-register, disable again (spec.md's re-registration
    /// scenario): two separate aggregate rows share one client id, and only
    /// the later <c>DisabledAt</c> is reported (FR-001).
    /// </summary>
    [Fact]
    public async Task The_same_client_id_disabled_twice_yields_one_entry_with_the_later_DisabledAt()
    {
        RegisteredClientAggregate firstRegistration =
            Disable(Build(ClientKind.Kiosk, "kiosk-269", "munich"), Now.AddMinutes(30));
        RegisteredClientAggregate secondRegistration =
            Disable(Build(ClientKind.Kiosk, "kiosk-269", "munich"), Now.AddDays(1));
        ListRevokedClientsQueryHandler handler = HandlerFor(firstRegistration, secondRegistration);

        Result<IReadOnlyList<RevokedClientEntry>, ListClientsError> result =
            await handler.HandleAsync(new ListRevokedClientsQuery(), CancellationToken.None);

        RevokedClientEntry entry = result.Value.ShouldHaveSingleItem();
        entry.ClientId.ShouldBe("kiosk-269");
        entry.DisabledAt.ShouldBe(
            Now.AddDays(1),
            "two disables of the same client id must collapse to one entry carrying the later "
            + "instant, per FR-001's 'the latest DisabledAt per client id wins'.");
    }
}
