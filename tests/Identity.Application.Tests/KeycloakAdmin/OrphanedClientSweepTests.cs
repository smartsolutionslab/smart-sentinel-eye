using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.KeycloakAdmin;

/// <summary>
/// Spec 320 (#2181) plan §8.2 — facts U1-U12. Red on <see cref="NotImplementedException"/>
/// until T011 implements <see cref="OrphanedClientSweep.SweepAsync"/> per plan §4.
///
/// <para>
/// Mirrors <see cref="KioskPrivilegeSweepTests"/>'s shape: a fake
/// <see cref="IKeycloakAdminClient"/> and <see cref="IRegisteredClientRepository"/>,
/// no mocking (ADR-0054). Unlike that precedent, this sweep also reads a
/// clock (S4's grace window) and a second port (S3's active-row check), so
/// <see cref="FakeClock"/> and <see cref="InMemoryRegisteredClientRepository"/>
/// join the fixture.
/// </para>
/// </summary>
public class OrphanedClientSweepTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan PastGrace = OrphanedClientSweep.GraceWindow + TimeSpan.FromMinutes(1);

    private static OrphanedClientSweep SweepOver(
        FakeKeycloakAdminClient keycloak,
        InMemoryRegisteredClientRepository clients,
        IClock? clock = null,
        ILogger<OrphanedClientSweep>? logger = null) =>
        new(
            keycloak,
            clients,
            clock ?? new FakeClock(Now),
            logger ?? NullLogger<OrphanedClientSweep>.Instance);

    private static RegisteredClientAggregate ActiveRow(string clientId, ClientKind kind, IClock clock) =>
        RegisteredClientAggregate.Register(
            ClientId.From(clientId),
            kind,
            FabIdentifier.From("munich"),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);

    [Fact]
    public async Task An_enabled_device_and_an_enabled_kiosk_orphan_both_past_grace_are_disabled()
    {
        FakeKeycloakAdminClient keycloak = new();
        keycloak.StampedClients.Add(new StampedClient("plc-orphan-1", "device", Enabled: true));
        keycloak.StampedClients.Add(new StampedClient("kiosk-orphan-1", "kiosk", Enabled: true));
        keycloak.ServiceAccountCreatedAt["plc-orphan-1"] = Now - PastGrace;
        keycloak.ServiceAccountCreatedAt["kiosk-orphan-1"] = Now - PastGrace;
        InMemoryRegisteredClientRepository clients = new();
        CapturingLogger<OrphanedClientSweep> logger = new();

        OrphanedClientSweepOutcome outcome =
            await SweepOver(keycloak, clients, logger: logger).SweepAsync(CancellationToken.None);

        outcome.Examined.ShouldBe(2);
        outcome.Disabled.ShouldBe(2);
        outcome.Refused.ShouldBeFalse();
        outcome.Unreachable.ShouldBeEmpty();
        keycloak.Disabled.ShouldBe(["plc-orphan-1", "kiosk-orphan-1"], ignoreOrder: true);

        IReadOnlyList<LoggedEntry> disabledEntries = logger.Named("DisabledOrphanedClient");
        disabledEntries.Count.ShouldBe(2, "one Warning per disabled client");
        disabledEntries.ShouldAllBe(entry => entry.Level == LogLevel.Warning);
        disabledEntries.Select(entry => entry.Field("ClientId")).ShouldBe(
            ["plc-orphan-1", "kiosk-orphan-1"], ignoreOrder: true);
        disabledEntries.Select(entry => entry.Field("Kind")).ShouldBe(
            ["device", "kiosk"], ignoreOrder: true);

        IReadOnlyList<LoggedEntry> summary = logger.Named("SweptOrphanedClients");
        summary.Count.ShouldBe(1, "one Information summary per pass, not one per client");
        summary[0].Field("Disabled").ShouldBe("2");
        summary[0].Field("Examined").ShouldBe("2");
    }

    [Fact]
    public async Task A_client_with_an_active_row_is_untouched_but_one_whose_only_row_is_disabled_is_an_orphan()
    {
        // #2728's replace path: the row stays Disabled and the client is
        // recreated enabled, so GetActiveClientIdsAsync's DisabledAt filter
        // cannot see it — the same orphan in every respect that matters
        // (spec 320 §1, the re-registration population).
        FakeKeycloakAdminClient keycloak = new();
        keycloak.StampedClients.Add(new StampedClient("plc-active-1", "device", Enabled: true));
        keycloak.StampedClients.Add(new StampedClient("plc-replaced-1", "device", Enabled: true));
        keycloak.ServiceAccountCreatedAt["plc-active-1"] = Now - PastGrace;
        keycloak.ServiceAccountCreatedAt["plc-replaced-1"] = Now - PastGrace;

        FakeClock clock = new(Now);
        InMemoryRegisteredClientRepository clients = new();
        clients.Add(ActiveRow("plc-active-1", ClientKind.Device, clock));
        RegisteredClientAggregate replacedRow = ActiveRow("plc-replaced-1", ClientKind.Device, clock);
        replacedRow.Disable(clock);
        clients.Add(replacedRow);

        OrphanedClientSweepOutcome outcome =
            await SweepOver(keycloak, clients, clock).SweepAsync(CancellationToken.None);

        keycloak.Disabled.ShouldBe(["plc-replaced-1"]);
        outcome.Disabled.ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_kind_is_never_a_candidate()
    {
        FakeKeycloakAdminClient keycloak = new();
        keycloak.StampedClients.Add(new StampedClient("mystery-1", "something-else", Enabled: true));
        keycloak.ServiceAccountCreatedAt["mystery-1"] = Now - PastGrace;
        InMemoryRegisteredClientRepository clients = new();

        OrphanedClientSweepOutcome outcome =
            await SweepOver(keycloak, clients).SweepAsync(CancellationToken.None);

        keycloak.Disabled.ShouldBeEmpty();
        outcome.Disabled.ShouldBe(0);
        outcome.Examined.ShouldBe(
            0, "an sse.kind outside OrphanedClientSweep.SweptKinds is never a candidate (spec 320 §4.5)");
    }

    [Fact]
    public async Task A_webhook_stamped_orphan_past_grace_is_now_disabled()
    {
        FakeKeycloakAdminClient keycloak = new();
        keycloak.StampedClients.Add(new StampedClient("webhook-orphan-1", "webhook", Enabled: true));
        keycloak.ServiceAccountCreatedAt["webhook-orphan-1"] = Now - PastGrace;
        InMemoryRegisteredClientRepository clients = new();

        OrphanedClientSweepOutcome outcome =
            await SweepOver(keycloak, clients).SweepAsync(CancellationToken.None);

        keycloak.Disabled.ShouldBe(
            ["webhook-orphan-1"],
            "a cancelled RotateWebhookClientCommandHandler create leaves the identical orphan shape "
            + "device/kiosk registration does (#2797) — the row is RegisteredClient/ClientKind.WebhookIntegration, "
            + "the same table this sweep already queries");
        outcome.Disabled.ShouldBe(1);
    }

    [Fact]
    public async Task A_webhook_stamped_client_with_an_active_row_is_left_alone()
    {
        FakeKeycloakAdminClient keycloak = new();
        keycloak.StampedClients.Add(new StampedClient("webhook-active-1", "webhook", Enabled: true));
        keycloak.ServiceAccountCreatedAt["webhook-active-1"] = Now - PastGrace;
        InMemoryRegisteredClientRepository clients = new();
        FakeClock clock = new(Now);
        clients.Add(ActiveRow("webhook-active-1", ClientKind.WebhookIntegration, clock));

        OrphanedClientSweepOutcome outcome =
            await SweepOver(keycloak, clients, clock).SweepAsync(CancellationToken.None);

        keycloak.Disabled.ShouldBeEmpty();
        outcome.Disabled.ShouldBe(0);
    }

    [Fact]
    public async Task Age_one_second_short_of_the_grace_window_is_spared_and_age_at_the_window_is_disabled()
    {
        FakeKeycloakAdminClient keycloak = new();
        keycloak.StampedClients.Add(new StampedClient("plc-almost", "device", Enabled: true));
        keycloak.StampedClients.Add(new StampedClient("plc-exactly", "device", Enabled: true));
        keycloak.ServiceAccountCreatedAt["plc-almost"] =
            Now - (OrphanedClientSweep.GraceWindow - TimeSpan.FromSeconds(1));
        keycloak.ServiceAccountCreatedAt["plc-exactly"] = Now - OrphanedClientSweep.GraceWindow;
        InMemoryRegisteredClientRepository clients = new();

        await SweepOver(keycloak, clients).SweepAsync(CancellationToken.None);

        keycloak.Disabled.ShouldNotContain(
            "plc-almost", "a registration one second inside the grace window may still be in flight");
        keycloak.Disabled.ShouldContain(
            "plc-exactly", "age exactly equal to the grace window is old enough (spec 320 §3)");
    }

    [Fact]
    public async Task An_already_disabled_stamped_client_with_no_row_is_left_alone_and_not_reported()
    {
        FakeKeycloakAdminClient keycloak = new();
        keycloak.StampedClients.Add(new StampedClient("plc-already-disabled", "device", Enabled: false));
        keycloak.ServiceAccountCreatedAt["plc-already-disabled"] = Now - PastGrace;
        InMemoryRegisteredClientRepository clients = new();
        CapturingLogger<OrphanedClientSweep> logger = new();

        OrphanedClientSweepOutcome outcome =
            await SweepOver(keycloak, clients, logger: logger).SweepAsync(CancellationToken.None);

        keycloak.Disabled.ShouldBeEmpty();
        outcome.Examined.ShouldBe(0, "only enabled stamped clients are candidates (S2)");
        outcome.Disabled.ShouldBe(0);
        logger.Entries.ShouldBeEmpty("an already-disabled client is not a repair and not worth a line");
    }

    [Fact]
    public async Task One_orphans_age_read_throwing_does_not_stop_the_other_from_being_disabled()
    {
        FakeKeycloakAdminClient keycloak = new();
        keycloak.StampedClients.Add(new StampedClient("plc-unreadable", "device", Enabled: true));
        keycloak.StampedClients.Add(new StampedClient("plc-readable", "device", Enabled: true));
        keycloak.ServiceAccountCreatedAtThrows["plc-unreadable"] =
            new HttpRequestException("Keycloak refused the service-account-user read.");
        keycloak.ServiceAccountCreatedAt["plc-readable"] = Now - PastGrace;
        InMemoryRegisteredClientRepository clients = new();
        CapturingLogger<OrphanedClientSweep> logger = new();

        OrphanedClientSweepOutcome outcome =
            await SweepOver(keycloak, clients, logger: logger).SweepAsync(CancellationToken.None);

        keycloak.Disabled.ShouldBe(["plc-readable"]);
        outcome.Disabled.ShouldBe(1);
        outcome.Unreachable.ShouldBe(["plc-unreadable"]);
        logger.Named("CouldNotSweepOrphanedClient")
            .Select(entry => entry.Field("ClientId"))
            .ShouldContain("plc-unreadable");
    }

    [Fact]
    public async Task One_orphans_disable_throwing_does_not_stop_the_other_from_being_disabled()
    {
        FakeKeycloakAdminClient keycloak = new();
        keycloak.StampedClients.Add(new StampedClient("plc-wont-disable", "device", Enabled: true));
        keycloak.StampedClients.Add(new StampedClient("plc-will-disable", "device", Enabled: true));
        keycloak.ServiceAccountCreatedAt["plc-wont-disable"] = Now - PastGrace;
        keycloak.ServiceAccountCreatedAt["plc-will-disable"] = Now - PastGrace;
        keycloak.DisableFailsFor["plc-wont-disable"] =
            new HttpRequestException("Keycloak refused the disable.");
        InMemoryRegisteredClientRepository clients = new();
        CapturingLogger<OrphanedClientSweep> logger = new();

        OrphanedClientSweepOutcome outcome =
            await SweepOver(keycloak, clients, logger: logger).SweepAsync(CancellationToken.None);

        keycloak.Disabled.ShouldBe(["plc-will-disable"]);
        outcome.Disabled.ShouldBe(1);
        outcome.Unreachable.ShouldBe(["plc-wont-disable"]);
        logger.Named("CouldNotSweepOrphanedClient")
            .Select(entry => entry.Field("ClientId"))
            .ShouldContain("plc-wont-disable");
    }

    [Fact]
    public async Task No_service_account_user_at_all_is_not_disabled_and_is_reported_unreachable()
    {
        FakeKeycloakAdminClient keycloak = new();
        keycloak.StampedClients.Add(new StampedClient("plc-no-service-account", "device", Enabled: true));
        // Deliberately no entry in ServiceAccountCreatedAt and no entry in
        // ServiceAccountCreatedAtThrows: the fake answers None, exactly as
        // production does for a client whose service account cannot be found.
        InMemoryRegisteredClientRepository clients = new();
        CapturingLogger<OrphanedClientSweep> logger = new();

        OrphanedClientSweepOutcome outcome =
            await SweepOver(keycloak, clients, logger: logger).SweepAsync(CancellationToken.None);

        keycloak.Disabled.ShouldBeEmpty();
        outcome.Disabled.ShouldBe(0);
        outcome.Unreachable.ShouldBe(["plc-no-service-account"]);
        logger.Named("CouldNotSweepOrphanedClient").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Three_orphans_among_four_refuses_the_whole_pass_and_logs_an_error()
    {
        FakeKeycloakAdminClient keycloak = new();
        InMemoryRegisteredClientRepository clients = new();
        FakeClock clock = new(Now);

        foreach (string orphan in new[] { "plc-orphan-a", "plc-orphan-b", "plc-orphan-c" })
        {
            keycloak.StampedClients.Add(new StampedClient(orphan, "device", Enabled: true));
            keycloak.ServiceAccountCreatedAt[orphan] = Now - PastGrace;
        }

        keycloak.StampedClients.Add(new StampedClient("plc-registered", "device", Enabled: true));
        clients.Add(ActiveRow("plc-registered", ClientKind.Device, clock));

        CapturingLogger<OrphanedClientSweep> logger = new();

        OrphanedClientSweepOutcome outcome =
            await SweepOver(keycloak, clients, clock, logger).SweepAsync(CancellationToken.None);

        outcome.Refused.ShouldBeTrue();
        outcome.Disabled.ShouldBe(0);
        keycloak.Disabled.ShouldBeEmpty(
            "3 of 4 enabled stamped clients orphaned looks like the wrong database, not a rare "
            + "cancellation (spec 320 §4.3)");

        IReadOnlyList<LoggedEntry> refusals = logger.Named("OrphanedClientSweepRefused");
        refusals.Count.ShouldBe(1);
        refusals[0].Level.ShouldBe(LogLevel.Error);
        refusals[0].Field("Candidates").ShouldBe("3");
        refusals[0].Field("Examined").ShouldBe("4");
    }

    [Fact]
    public async Task The_floor_of_two_and_exactly_half_both_stay_below_the_mass_disable_guard()
    {
        // Sub-case A: one orphan out of one eligible client. candidates(1) is
        // below the guard's floor of two, however large a fraction it is of
        // the fleet.
        FakeKeycloakAdminClient oneOfOne = new();
        oneOfOne.StampedClients.Add(new StampedClient("plc-lone-orphan", "device", Enabled: true));
        oneOfOne.ServiceAccountCreatedAt["plc-lone-orphan"] = Now - PastGrace;

        OrphanedClientSweepOutcome soleOutcome =
            await SweepOver(oneOfOne, new InMemoryRegisteredClientRepository())
                .SweepAsync(CancellationToken.None);

        soleOutcome.Refused.ShouldBeFalse();
        soleOutcome.Disabled.ShouldBe(1);

        // Sub-case B: two orphans out of four eligible clients — exactly
        // half, which the guard requires candidates to exceed, not merely
        // reach (spec 320 §4.3: "more than half").
        FakeKeycloakAdminClient twoOfFour = new();
        InMemoryRegisteredClientRepository clientsForTwoOfFour = new();
        FakeClock clock = new(Now);

        foreach (string orphan in new[] { "plc-orphan-x", "plc-orphan-y" })
        {
            twoOfFour.StampedClients.Add(new StampedClient(orphan, "device", Enabled: true));
            twoOfFour.ServiceAccountCreatedAt[orphan] = Now - PastGrace;
        }

        foreach (string registered in new[] { "plc-registered-x", "plc-registered-y" })
        {
            twoOfFour.StampedClients.Add(new StampedClient(registered, "device", Enabled: true));
            clientsForTwoOfFour.Add(ActiveRow(registered, ClientKind.Device, clock));
        }

        OrphanedClientSweepOutcome halfOutcome =
            await SweepOver(twoOfFour, clientsForTwoOfFour, clock).SweepAsync(CancellationToken.None);

        halfOutcome.Refused.ShouldBeFalse();
        halfOutcome.Disabled.ShouldBe(2);
    }

    [Fact]
    public async Task Keycloak_is_read_before_postgres()
    {
        List<string> callOrder = [];
        FakeKeycloakAdminClient keycloak = new() { CallLog = callOrder };
        InMemoryRegisteredClientRepository clients = new() { CallLog = callOrder };

        await SweepOver(keycloak, clients).SweepAsync(CancellationToken.None);

        callOrder.ShouldBe(
            [nameof(IKeycloakAdminClient.GetStampedClientsAsync), nameof(IRegisteredClientRepository.GetActiveClientIdsAsync)],
            "S5: Keycloak is read before Postgres, so a client created after the Keycloak read is "
            + "never a candidate and a row committed before the Postgres read protects its client "
            + "(spec 320 §4.2)");
    }

    [Fact]
    public async Task A_pass_with_nothing_to_do_logs_nothing()
    {
        FakeKeycloakAdminClient keycloak = new();
        InMemoryRegisteredClientRepository clients = new();
        CapturingLogger<OrphanedClientSweep> logger = new();

        OrphanedClientSweepOutcome outcome =
            await SweepOver(keycloak, clients, logger: logger).SweepAsync(CancellationToken.None);

        outcome.Examined.ShouldBe(0);
        outcome.Disabled.ShouldBe(0);
        logger.Entries.ShouldBeEmpty("an empty realm is silent, as spec 132 made KioskPrivilegeSweep");
    }

    [Fact]
    public async Task A_cancellation_from_either_port_propagates_rather_than_being_swallowed()
    {
        // From the age read.
        FakeKeycloakAdminClient keycloakCancelsOnRead = new();
        keycloakCancelsOnRead.StampedClients.Add(new StampedClient("plc-cancel-read", "device", Enabled: true));
        keycloakCancelsOnRead.ServiceAccountCreatedAtThrows["plc-cancel-read"] = new OperationCanceledException();

        await Should.ThrowAsync<OperationCanceledException>(
            () => SweepOver(keycloakCancelsOnRead, new InMemoryRegisteredClientRepository())
                .SweepAsync(CancellationToken.None));

        // From the disable call.
        FakeKeycloakAdminClient keycloakCancelsOnDisable = new();
        keycloakCancelsOnDisable.StampedClients.Add(new StampedClient("plc-cancel-disable", "device", Enabled: true));
        keycloakCancelsOnDisable.ServiceAccountCreatedAt["plc-cancel-disable"] = Now - PastGrace;
        keycloakCancelsOnDisable.DisableFailsFor["plc-cancel-disable"] = new OperationCanceledException();

        await Should.ThrowAsync<OperationCanceledException>(
            () => SweepOver(keycloakCancelsOnDisable, new InMemoryRegisteredClientRepository())
                .SweepAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_stamped_client_id_ClientId_From_rejects_is_not_disabled_and_is_reported_unreachable()
    {
        FakeKeycloakAdminClient keycloak = new();
        // '-' is not a letter or digit, so ClientId.From rejects this —
        // Keycloak's own clientId grammar is looser than ours (spec 320 §4,
        // the pass's own note on OrphanedClientSweep.cs).
        keycloak.StampedClients.Add(new StampedClient("-malformed", "device", Enabled: true));
        keycloak.ServiceAccountCreatedAt["-malformed"] = Now - PastGrace;
        InMemoryRegisteredClientRepository clients = new();
        CapturingLogger<OrphanedClientSweep> logger = new();

        OrphanedClientSweepOutcome outcome =
            await SweepOver(keycloak, clients, logger: logger).SweepAsync(CancellationToken.None);

        keycloak.Disabled.ShouldBeEmpty();
        outcome.Unreachable.ShouldBe(["-malformed"]);
        logger.Named("CouldNotSweepOrphanedClient").ShouldNotBeEmpty();
    }
}
