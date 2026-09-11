using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Spec 129 (#2139) — <b>the three answers <c>GetSubGroupNamesAsync</c>
/// promises, pinned at the one class that decides which is which</b>.
///
/// <para>
/// The interface has documented three since spec 019 ("never returns empty to
/// mean 'could not tell'") and could spell two, so the 404 arm flattened
/// "the group is not there" into the empty list that also means "the group is
/// there and holds nothing". Its one caller then had to hedge between two
/// faults with two different fixes.
/// </para>
///
/// <para>
/// The double sits at the <see cref="HttpMessageHandler"/> seam rather than at
/// <c>IKeycloakAdminClient</c>, for the reason <c>StubKeycloakHandler</c>
/// already records: the decision under test is the status-code mapping inside
/// <c>HttpKeycloakAdminClient</c>, and a double standing in for that class
/// cannot see it — <c>FakeKeycloakAdminClient</c> is green either way.
/// </para>
///
/// <para>
/// These four went green with the shape change that introduced them rather
/// than red before it, because a test able to express "absent" needs a type
/// able to express it. They are proved instead by counterfactual: with the 404
/// arm reverted to <c>Some([])</c>, the first two fail and the other two pass.
/// Recorded in spec 129's <c>verification.md</c>.
/// </para>
/// </summary>
public class FabGroupTreeTests
{
    private const string Realm = "smart-sentinel-eye";
    private const string FabsUuid = "99999999-9999-9999-9999-999999999999";

    private const string ChildlessFabsGroup = $$"""
        {"id":"{{FabsUuid}}","name":"fabs","path":"/fabs","subGroups":[]}
        """;

    [Fact]
    public async Task A_fabs_group_that_is_not_in_the_realm_is_absent()
    {
        StubKeycloakHandler keycloak = new((_, _) => ChildlessFabsGroup)
        {
            Refuse = _ => HttpStatusCode.NotFound,
        };

        Option<IReadOnlyList<string>> tree = await ClientOver(keycloak)
            .GetSubGroupNamesAsync("/fabs", CancellationToken.None);

        tree.HasValue.ShouldBeFalse(
            "a 404 on group-by-path is the realm saying the group is not there, and the "
            + "caller provisions storage off this answer — flattened into an empty list it "
            + "is indistinguishable from a group that exists and holds nothing, which is a "
            + "different fault with a different fix");
    }

    [Fact]
    public async Task A_fabs_group_that_is_there_and_holds_nothing_is_present_and_empty()
    {
        StubKeycloakHandler keycloak = new(ChildlessFlow);

        Option<IReadOnlyList<string>> tree = await ClientOver(keycloak)
            .GetSubGroupNamesAsync("/fabs", CancellationToken.None);

        tree.HasValue.ShouldBeTrue("the group answered; having no children is that answer");
        tree.Value.ShouldBeEmpty();
    }

    /// <summary>
    /// The control. Without it both tests above are satisfied by a client that
    /// answers <c>Some([])</c> to everything it does not 404 on, including the
    /// realm that actually has fabs in it.
    /// </summary>
    [Fact]
    public async Task A_fabs_group_that_holds_children_carries_their_names()
    {
        StubKeycloakHandler keycloak = new((_, _) => $$"""
            {"id":"{{FabsUuid}}","name":"fabs","path":"/fabs","subGroups":[
              {"id":"1","name":"munich","path":"/fabs/munich"},
              {"id":"2","name":"dresden","path":"/fabs/dresden"}]}
            """);

        Option<IReadOnlyList<string>> tree = await ClientOver(keycloak)
            .GetSubGroupNamesAsync("/fabs", CancellationToken.None);

        tree.Value.ShouldBe(["munich", "dresden"]);
    }

    /// <summary>
    /// FR-003, and the half that must not drift while the other two are being
    /// separated. A realm that declines to answer is neither of the states
    /// above: a lookup the token is not privileged for is a 403, and swallowing
    /// it into <c>None</c> would provision no storage for a realm full of fabs
    /// and let the run report why in a sentence about a missing group.
    /// </summary>
    [Fact]
    public async Task A_refused_lookup_is_neither_absent_nor_empty()
    {
        StubKeycloakHandler keycloak = new((_, _) => ChildlessFabsGroup)
        {
            Refuse = _ => HttpStatusCode.Forbidden,
        };

        await Should.ThrowAsync<HttpRequestException>(
            () => ClientOver(keycloak).GetSubGroupNamesAsync("/fabs", CancellationToken.None));
    }

    /// <summary>
    /// What Keycloak 26 actually does: <c>subGroups</c> comes back empty from
    /// <c>group-by-path</c> whether or not there are children, so the client
    /// follows up on <c>children</c> rather than believing it. Here that
    /// follow-up is empty too.
    /// </summary>
    private static string? ChildlessFlow(StubKeycloakHandler _, RecordedRequest request) =>
        request.PathAndQuery.Contains("/children", StringComparison.Ordinal)
            ? "[]"
            : ChildlessFabsGroup;

    private static HttpKeycloakAdminClient ClientOver(StubKeycloakHandler keycloak) =>
        new(new HttpClient(keycloak) { BaseAddress = new Uri("http://keycloak.test/") },
            Options.Create(new KeycloakAdminOptions { Realm = Realm }),
            NullLogger<HttpKeycloakAdminClient>.Instance);
}
