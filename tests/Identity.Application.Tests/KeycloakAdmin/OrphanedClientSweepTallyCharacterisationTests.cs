using System.Globalization;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.KeycloakAdmin;

/// <summary>
/// Characterisation (spec 348, ADR-0144), captured green on the unmodified
/// <c>OrphanedClientSweep</c> before <c>ClassifyAsync</c> and
/// <c>DisableAllAsync</c> start sharing one tally object. The sweep disables
/// live Keycloak credentials, so what the existing tests leave open is pinned
/// here in one mixed pass: the order of <c>Unreachable</c> (classification
/// failures in candidate order, then disable failures in orphan order), the
/// per-client log entries in order with the exception present where it is
/// today and absent where it is today, the exact call sequence against both
/// ports, and the outcome a refused pass hands back.
///
/// <para>
/// Counterfactual this file was seen to catch: appending a disable failure to
/// <c>Unreachable</c> before a classification failure (or swapping the age-read
/// and absent-service-account branches' add) turns
/// <see cref="A_mixed_pass_reports_the_unreachable_clients_in_classification_then_disable_order"/> red.
/// </para>
/// </summary>
public class OrphanedClientSweepTallyCharacterisationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly OperatorIdentifier Operator =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000003", CultureInfo.InvariantCulture));

    private sealed class Harness
    {
        public Harness()
        {
            Keycloak.CallLog = Log;
            Repository.CallLog = Log;
            Sweep = new OrphanedClientSweep(Keycloak, Repository, new FakeClock(Now), Logger);
        }

        public List<string> Log { get; } = [];

        public FakeKeycloakAdminClient Keycloak { get; } = new();

        public InMemoryRegisteredClientRepository Repository { get; } = new();

        public CapturingLogger<OrphanedClientSweep> Logger { get; } = new();

        public OrphanedClientSweep Sweep { get; }

        public void Stamp(string clientId, string kind, TimeSpan? age = null, bool enabled = true)
        {
            Keycloak.StampedClients.Add(new StampedClient(clientId, kind, enabled));
            if (age is { } serviceAccountAge)
            {
                Keycloak.ServiceAccountCreatedAt[clientId] = Now - serviceAccountAge;
            }
        }

        public void SeedActiveRow(string clientId) =>
            Repository.Seed(
                RegisteredClientAggregate.Register(
                    ClientId.From(clientId),
                    ClientKind.Device,
                    FabIdentifier.From("munich"),
                    Operator,
                    new FakeClock(Now)));
    }

    [Fact]
    public async Task A_mixed_pass_reports_the_unreachable_clients_in_classification_then_disable_order()
    {
        Harness harness = new();
        var ageFailure = new InvalidOperationException("age read failed");
        var disableFailure = new HttpRequestException("disable refused");
        harness.Stamp("bad id!", "device");
        harness.Stamp("plc.munich.age-throws", "device");
        harness.Keycloak.ServiceAccountCreatedAtThrows["plc.munich.age-throws"] = ageFailure;
        harness.Stamp("plc.munich.no-account", "device");
        harness.Stamp("plc.munich.active", "device", age: TimeSpan.FromHours(5));
        harness.SeedActiveRow("plc.munich.active");
        harness.Stamp("plc.munich.young", "device", age: TimeSpan.FromMinutes(3));
        harness.Stamp("kiosk.munich.orphan-ok", "kiosk", age: TimeSpan.FromMinutes(25));
        harness.Stamp("webhook-orphan-fail", "webhook", age: TimeSpan.FromMinutes(40));
        harness.Keycloak.DisableFailsFor["webhook-orphan-fail"] = disableFailure;
        harness.Stamp("plc.munich.disabled", "device", age: TimeSpan.FromHours(9), enabled: false);
        harness.Stamp("other.thing", "other", age: TimeSpan.FromHours(9));

        var outcome = await harness.Sweep.SweepAsync(CancellationToken.None);

        outcome.Examined.ShouldBe(7);
        outcome.Disabled.ShouldBe(1);
        outcome.Refused.ShouldBeFalse();
        outcome.Unreachable.ShouldBe(
            ["bad id!", "plc.munich.age-throws", "plc.munich.no-account", "webhook-orphan-fail"]);
        harness.Keycloak.Disabled.ShouldBe(["kiosk.munich.orphan-ok"]);
    }

    [Fact]
    public async Task A_mixed_pass_reads_and_disables_in_exactly_this_order()
    {
        Harness harness = new();
        harness.Stamp("bad id!", "device");
        harness.Stamp("plc.munich.age-throws", "device");
        harness.Keycloak.ServiceAccountCreatedAtThrows["plc.munich.age-throws"] = new InvalidOperationException("age read failed");
        harness.Stamp("plc.munich.no-account", "device");
        harness.Stamp("plc.munich.active", "device", age: TimeSpan.FromHours(5));
        harness.SeedActiveRow("plc.munich.active");
        harness.Stamp("plc.munich.young", "device", age: TimeSpan.FromMinutes(3));
        harness.Stamp("kiosk.munich.orphan-ok", "kiosk", age: TimeSpan.FromMinutes(25));
        harness.Stamp("webhook-orphan-fail", "webhook", age: TimeSpan.FromMinutes(40));
        harness.Keycloak.DisableFailsFor["webhook-orphan-fail"] = new HttpRequestException("disable refused");

        await harness.Sweep.SweepAsync(CancellationToken.None);

        harness.Log.ShouldBe(
            [
                "GetStampedClientsAsync",
                "GetActiveClientIdsAsync",
                "GetServiceAccountCreatedAtAsync",
                "GetServiceAccountCreatedAtAsync",
                "GetServiceAccountCreatedAtAsync",
                "GetServiceAccountCreatedAtAsync",
                "GetServiceAccountCreatedAtAsync",
                "DisableClientAsync",
                "DisableClientAsync",
            ]);
    }

    [Fact]
    public async Task A_mixed_pass_logs_each_client_in_order_with_the_exception_where_there_is_one()
    {
        Harness harness = new();
        var ageFailure = new InvalidOperationException("age read failed");
        var disableFailure = new HttpRequestException("disable refused");
        harness.Stamp("bad id!", "device");
        harness.Stamp("plc.munich.age-throws", "device");
        harness.Keycloak.ServiceAccountCreatedAtThrows["plc.munich.age-throws"] = ageFailure;
        harness.Stamp("plc.munich.no-account", "device");
        harness.Stamp("kiosk.munich.orphan-ok", "kiosk", age: TimeSpan.FromMinutes(25));
        harness.Stamp("webhook-orphan-fail", "webhook", age: TimeSpan.FromMinutes(40));
        harness.Keycloak.DisableFailsFor["webhook-orphan-fail"] = disableFailure;
        harness.Stamp("plc.munich.young", "device", age: TimeSpan.FromMinutes(3));

        await harness.Sweep.SweepAsync(CancellationToken.None);

        var entries = harness.Logger.Entries;
        entries.Select(entry => entry.EventId.Name).ShouldBe(
            [
                "CouldNotSweepOrphanedClient",
                "CouldNotSweepOrphanedClient",
                "CouldNotSweepOrphanedClient",
                "DisabledOrphanedClient",
                "CouldNotSweepOrphanedClient",
                "SweptOrphanedClients",
            ]);

        entries[0].Level.ShouldBe(LogLevel.Warning);
        entries[0].Message.ShouldBe(
            "Could not reconcile stamped Keycloak client 'bad id!' during the orphan sweep; it was left untouched.");
        entries[0].Exception.ShouldBeOfType<ArgumentException>();

        entries[1].Message.ShouldBe(
            "Could not reconcile stamped Keycloak client 'plc.munich.age-throws' during the orphan sweep; it was left untouched.");
        entries[1].Exception.ShouldBeSameAs(ageFailure);

        entries[2].Message.ShouldBe(
            "Could not reconcile stamped Keycloak client 'plc.munich.no-account' during the orphan sweep; it was left untouched.");
        entries[2].Exception.ShouldBeNull();

        entries[3].Level.ShouldBe(LogLevel.Warning);
        entries[3].Message.ShouldBe(
            "Disabled orphaned Keycloak client 'kiosk.munich.orphan-ok' (kind kiosk), whose service account is "
            + "00:25:00 old with no active registered-client row.");

        entries[4].Message.ShouldBe(
            "Could not reconcile stamped Keycloak client 'webhook-orphan-fail' during the orphan sweep; it was left untouched.");
        entries[4].Exception.ShouldBeSameAs(disableFailure);

        entries[5].Level.ShouldBe(LogLevel.Information);
        entries[5].Message.ShouldBe(
            "Orphan sweep disabled 1 of 6 enabled, stamped device/kiosk clients with no active registered-client row.");
    }

    [Fact]
    public async Task A_refused_pass_hands_back_the_classification_phase_unreachable_list_and_disables_nothing()
    {
        Harness harness = new();
        harness.Stamp("bad id!", "device");
        harness.Stamp("plc.munich.no-account", "device");
        harness.Stamp("plc.munich.orphan-a", "device", age: TimeSpan.FromMinutes(20));
        harness.Stamp("kiosk.munich.orphan-b", "kiosk", age: TimeSpan.FromMinutes(30));
        harness.Stamp("webhook-orphan-c", "webhook", age: TimeSpan.FromMinutes(50));

        var outcome = await harness.Sweep.SweepAsync(CancellationToken.None);

        outcome.Refused.ShouldBeTrue();
        outcome.Examined.ShouldBe(5);
        outcome.Disabled.ShouldBe(0);
        outcome.Unreachable.ShouldBe(["bad id!", "plc.munich.no-account"]);
        harness.Keycloak.Disabled.ShouldBeEmpty();
        harness.Log.ShouldNotContain("DisableClientAsync");
        harness.Logger.Entries.Select(entry => entry.EventId.Name).ShouldBe(
            [
                "CouldNotSweepOrphanedClient",
                "CouldNotSweepOrphanedClient",
                "OrphanedClientSweepRefused",
            ]);
        var refusal = harness.Logger.Entries[2];
        refusal.Level.ShouldBe(LogLevel.Error);
        refusal.Message.ShouldBe(
            "Refused to disable 3 of 5 enabled, stamped device/kiosk clients — more than half the fleet looks "
            + "orphaned, which is more likely the wrong database than a rare cancellation. Nothing was disabled.");
    }

    [Fact]
    public async Task A_cancellation_reading_a_service_account_age_propagates_instead_of_being_tallied()
    {
        Harness harness = new();
        harness.Stamp("plc.munich.cancelled", "device");
        harness.Keycloak.ServiceAccountCreatedAtThrows["plc.munich.cancelled"] = new OperationCanceledException("cancelled");

        var thrown = await Should.ThrowAsync<OperationCanceledException>(
            () => harness.Sweep.SweepAsync(CancellationToken.None));

        // An OperationCanceledException leaves an async method as a cancelled task: its type survives the await, its message does not.
        thrown.ShouldNotBeNull();
        harness.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cancellation_disabling_an_orphan_propagates_instead_of_being_tallied()
    {
        Harness harness = new();
        harness.Stamp("plc.munich.orphan-a", "device", age: TimeSpan.FromMinutes(20));
        harness.Stamp("plc.munich.healthy-1", "device", age: TimeSpan.FromMinutes(1));
        harness.Stamp("plc.munich.healthy-2", "device", age: TimeSpan.FromMinutes(1));
        harness.Keycloak.DisableFailsFor["plc.munich.orphan-a"] = new OperationCanceledException("cancelled");

        var thrown = await Should.ThrowAsync<OperationCanceledException>(
            () => harness.Sweep.SweepAsync(CancellationToken.None));

        thrown.ShouldNotBeNull();
        harness.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_clean_pass_reports_nothing_unreachable_and_logs_only_the_summary()
    {
        Harness harness = new();
        harness.Stamp("plc.munich.orphan-a", "device", age: TimeSpan.FromMinutes(20));
        harness.Stamp("plc.munich.healthy-1", "device", age: TimeSpan.FromMinutes(1));
        harness.Stamp("plc.munich.healthy-2", "device", age: TimeSpan.FromMinutes(1));

        var outcome = await harness.Sweep.SweepAsync(CancellationToken.None);

        outcome.Unreachable.ShouldBeEmpty();
        outcome.Disabled.ShouldBe(1);
        harness.Logger.Entries.Select(entry => entry.EventId.Name).ShouldBe(
            ["DisabledOrphanedClient", "SweptOrphanedClients"]);
        harness.Logger.Entries[1].Message.ShouldBe(
            "Orphan sweep disabled 1 of 3 enabled, stamped device/kiosk clients with no active registered-client row.");
    }
}
