using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.MigrationRunner;

namespace SmartSentinelEye.MigrationRunner.Tests;

/// <summary>
/// Spec 019 T012. Two rules live here and nowhere else: a group name the domain
/// cannot use is skipped rather than fatal (FR-005), and nothing usable is
/// fatal rather than empty (FR-011).
/// </summary>
public class KeycloakProvisionedFabSourceTests
{
    [Fact]
    public async Task Returns_every_fab_group_under_fabs()
    {
        KeycloakProvisionedFabSource source = Source(["munich", "dresden", "berlin"]);

        IReadOnlyList<FabIdentifier> fabs = await source.GetFabsAsync(CancellationToken.None);

        fabs.Select(fab => fab.Value).ShouldBe(["munich", "dresden", "berlin"]);
    }

    /// <summary>
    /// FR-005. One badly named group must not cost every other fab its storage
    /// — which is what throwing here would do, on the run that provisions them.
    /// </summary>
    [Fact]
    public async Task Skips_a_group_whose_name_is_not_a_usable_fab_and_keeps_the_rest()
    {
        KeycloakProvisionedFabSource source = Source(["munich", "NOT-A-FAB", "x", "dresden"]);

        IReadOnlyList<FabIdentifier> fabs = await source.GetFabsAsync(CancellationToken.None);

        // 'NOT-A-FAB' is uppercase and 'x' is below the two-character minimum.
        fabs.Select(fab => fab.Value).ShouldBe(["munich", "dresden"]);
    }

    [Fact]
    public async Task Deduplicates_repeated_names()
    {
        KeycloakProvisionedFabSource source = Source(["munich", "munich"]);

        IReadOnlyList<FabIdentifier> fabs = await source.GetFabsAsync(CancellationToken.None);

        fabs.Count.ShouldBe(1);
    }

    /// <summary>
    /// FR-011, and the one that keeps the silence from coming back. Returning
    /// an empty list here would provision nothing and report success — which is
    /// indistinguishable, from the outside, from a realm that genuinely has no
    /// fabs, and identical in effect to the defect this feature closes.
    /// </summary>
    [Fact]
    public async Task Throws_rather_than_returning_empty_when_nothing_is_usable()
    {
        KeycloakProvisionedFabSource source = Source(["NOT-A-FAB"]);

        await Should.ThrowAsync<InvalidOperationException>(
            () => source.GetFabsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Throws_when_the_realm_has_no_fab_groups_at_all()
    {
        KeycloakProvisionedFabSource source = Source([]);

        await Should.ThrowAsync<InvalidOperationException>(
            () => source.GetFabsAsync(CancellationToken.None));
    }

    /// <summary>An unreachable realm surfaces, rather than becoming "no fabs".</summary>
    [Fact]
    public async Task Propagates_a_failure_to_reach_the_realm()
    {
        KeycloakProvisionedFabSource source = new(
            new ThrowingKeycloakAdminClient(), NullLogger<KeycloakProvisionedFabSource>.Instance);

        await Should.ThrowAsync<HttpRequestException>(
            () => source.GetFabsAsync(CancellationToken.None));
    }

    private static KeycloakProvisionedFabSource Source(string[] groupNames) =>
        new(new StubKeycloakAdminClient(groupNames), NullLogger<KeycloakProvisionedFabSource>.Instance);

    private sealed class StubKeycloakAdminClient(string[] groupNames) : IKeycloakAdminClient
    {
        public Task<IReadOnlyList<string>> GetSubGroupNamesAsync(
            string parentPath, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(groupNames);

        public Task<KeycloakClientCredentials> CreateClientAsync(
            KeycloakClientRepresentation representation, string fabGroupPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KeycloakClientCredentials> RotateClientSecretAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisableClientAsync(string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingKeycloakAdminClient : IKeycloakAdminClient
    {
        public Task<IReadOnlyList<string>> GetSubGroupNamesAsync(
            string parentPath, CancellationToken cancellationToken) =>
            throw new HttpRequestException("realm unreachable");

        public Task<KeycloakClientCredentials> CreateClientAsync(
            KeycloakClientRepresentation representation, string fabGroupPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KeycloakClientCredentials> RotateClientSecretAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisableClientAsync(string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
