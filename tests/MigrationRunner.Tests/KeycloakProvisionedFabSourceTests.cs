using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.MigrationRunner;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.MigrationRunner.Tests;

/// <summary>
/// Spec 019 T012. Two rules live here and nowhere else: a group name the domain
/// cannot use is skipped rather than fatal (FR-005), and nothing usable is
/// fatal rather than empty (FR-011).
///
/// <para>
/// A third thing is pinned here since #2062, and it is a reading rather than a
/// rule: the two fatal verdicts are two messages, not one.
/// </para>
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

    /// <summary>
    /// The two fatal verdicts must not read as one. Both are
    /// <see cref="InvalidOperationException"/>, so neither test above can tell
    /// them apart — and before f059b237 they genuinely were one message, "no
    /// usable fab found under '/fabs'", for a realm with no such group and for
    /// a realm whose groups are all named unusably alike. Naming the abort is
    /// what issue #2062 is for, and nothing else here holds that.
    ///
    /// <para>
    /// Asserts the discriminator, not the prose: the names verdict quotes the
    /// name it rejected, and the two verdicts are not the same sentence.
    /// Rewording either message keeps this green; collapsing them back into one
    /// shared string does not. <c>ShouldNotContain("NOT-A-FAB")</c> on the
    /// absent verdict would be the obvious third assertion and is deliberately
    /// missing: that call never saw the name, so no implementation could put it
    /// there, and an assertion nothing can fail is worse than none.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Tells_a_realm_with_no_fab_groups_apart_from_one_whose_groups_are_unusable()
    {
        InvalidOperationException unusable = await Should.ThrowAsync<InvalidOperationException>(
            () => Source(["NOT-A-FAB"]).GetFabsAsync(CancellationToken.None));
        InvalidOperationException absent = await Should.ThrowAsync<InvalidOperationException>(
            () => Source([]).GetFabsAsync(CancellationToken.None));

        unusable.Message.ShouldContain("NOT-A-FAB");
        absent.Message.ShouldNotBe(unusable.Message);
    }

    /// <summary>
    /// #2139. Three causes abort this run, and until now two of them shared a
    /// sentence: the verdict for a realm with no <c>/fabs</c> group hedged —
    /// "the group is absent, or present with no children" — because the value
    /// it reasoned over could not tell those apart. They are different faults
    /// with different fixes: re-import a realm that lost the group, or declare
    /// fabs under a group that has none.
    ///
    /// <para>
    /// <b>Which assertion is carrying this test, stated rather than implied.</b>
    /// The first is the red: before the fix, absent and childless are one
    /// message. The other two are already green — <c>95a2cf4f</c> separated the
    /// unusable-names verdict from the rest — and are here so that a later
    /// collapse of any pair turns this red rather than only the pair nobody
    /// re-checked. Each was confirmed to fail on a tree where its own pair is
    /// collapsed; none is carrying another (verification.md).
    /// </para>
    ///
    /// <para>
    /// Asserts distinctness, not prose, for the reason the test below it
    /// records: rewording any of the three keeps this green, and only a
    /// collapse turns it red.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Tells_an_absent_fabs_group_apart_from_one_that_has_no_children()
    {
        InvalidOperationException absent = await Should.ThrowAsync<InvalidOperationException>(
            () => SourceWithoutTheFabsGroup().GetFabsAsync(CancellationToken.None));
        InvalidOperationException childless = await Should.ThrowAsync<InvalidOperationException>(
            () => Source([]).GetFabsAsync(CancellationToken.None));
        InvalidOperationException unusable = await Should.ThrowAsync<InvalidOperationException>(
            () => Source(["NOT-A-FAB"]).GetFabsAsync(CancellationToken.None));

        absent.Message.ShouldNotBe(childless.Message);
        childless.Message.ShouldNotBe(unusable.Message);
        absent.Message.ShouldNotBe(unusable.Message);
    }

    /// <summary>
    /// FR-005 and FR-011 still hold for a realm that has no <c>/fabs</c> group
    /// at all: the run aborts rather than provisioning nothing and reporting
    /// success. The control on the test above, which a source that threw three
    /// differently-worded nothings would also satisfy — and on any future
    /// reading of "absent" as "wait and ask again", which would hang this test
    /// instead of failing it.
    /// </summary>
    [Fact]
    public async Task Throws_when_the_fabs_group_itself_is_not_in_the_realm()
    {
        await Should.ThrowAsync<InvalidOperationException>(
            () => SourceWithoutTheFabsGroup().GetFabsAsync(CancellationToken.None));
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

    /// <summary>A realm whose <c>/fabs</c> group is there, holding these names.</summary>
    private static KeycloakProvisionedFabSource Source(string[] groupNames) =>
        Over(Option<IReadOnlyList<string>>.Some(groupNames));

    /// <summary>A realm with no <c>/fabs</c> group at all (#2139).</summary>
    private static KeycloakProvisionedFabSource SourceWithoutTheFabsGroup() =>
        Over(Option<IReadOnlyList<string>>.None);

    private static KeycloakProvisionedFabSource Over(Option<IReadOnlyList<string>> tree) =>
        new(new StubKeycloakAdminClient(tree), NullLogger<KeycloakProvisionedFabSource>.Instance);

    private sealed class StubKeycloakAdminClient(Option<IReadOnlyList<string>> tree) : IKeycloakAdminClient
    {
        public Task<Option<IReadOnlyList<string>>> GetSubGroupNamesAsync(
            string parentPath, CancellationToken cancellationToken) =>
            Task.FromResult(tree);

        public Task<KeycloakClientCredentials> CreateClientAsync(
            KeycloakClientRepresentation representation, string fabGroupPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KeycloakClientCredentials> ReadClientSecretAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetEnrolledKioskClientIdsAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> StripInheritedRealmRolesAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();


        public Task<KeycloakClientCredentials> RotateClientSecretAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisableClientAsync(string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingKeycloakAdminClient : IKeycloakAdminClient
    {
        public Task<Option<IReadOnlyList<string>>> GetSubGroupNamesAsync(
            string parentPath, CancellationToken cancellationToken) =>
            throw new HttpRequestException("realm unreachable");

        public Task<IReadOnlyList<string>> GetEnrolledKioskClientIdsAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> StripInheritedRealmRolesAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KeycloakClientCredentials> CreateClientAsync(
            KeycloakClientRepresentation representation, string fabGroupPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KeycloakClientCredentials> ReadClientSecretAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KeycloakClientCredentials> RotateClientSecretAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisableClientAsync(string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
