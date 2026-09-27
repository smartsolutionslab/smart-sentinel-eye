using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;
using static SmartSentinelEye.Integration.Tests.EventIngestion.EventSourceModeApi;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Phase 4a (spec 269 T003f) — the <c>/event-sources</c> HTTP surface,
/// against the real stack. Every case here is red on arrival for the same
/// reason: the group is not mapped yet, so every request answers <c>404 Not
/// Found</c> regardless of what the test expects — including
/// <see cref="An_anonymous_caller_is_refused_with_401"/>: an unmapped route
/// answers 404 to an anonymous caller too, not 401, so this case is red for
/// the same reason as every other one in this file. None of this file's
/// cases are green on arrival.
///
/// <para>
/// <b>Reserved pairs.</b> <c>Source</c> is a closed four-value VO with no
/// delete endpoint (<see cref="EventSourceModeApi"/>'s remarks), so two
/// scenarios need a pair that has <i>never</i> been declared and never will
/// be by any other test in this file:
/// <c>munich</c> + <c>webhook</c> (<see cref="Changing_an_undeclared_pair_is_404"/>)
/// and <c>dresden</c> + <c>webhook</c>
/// (<see cref="A_repeated_declare_with_the_same_idempotency_key_replays_the_same_201"/>).
/// Every other test either restores no state (it never reaches a successful
/// write) or uses <see cref="EventSourceModeApi.SetAsync"/>, which is
/// idempotent and safe to call from any run in any order. <b>The two
/// reserved-pair tests are the one place that idempotency does not reach</b>:
/// they can only pass against a database where that exact pair has never
/// been declared, which — because the dev database is shared across runs
/// (spec 143's own convention) — makes them effectively single-use unless a
/// human resets those two rows. Flagged for phase 5/6 rather than solved
/// here; there is no fifth source to fall back to.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class EventSourceModeIntegrationTests(AspireFixture aspire)
{
    [Fact]
    public async Task A_declared_mode_is_listed_back()
    {
        using HttpClient hamburg = await ClientFor(HamburgOperator);
        await SetAsync(hamburg, "plc", "strict");

        JsonElement rows = await ListRowsAsync(hamburg, "listing after declaring hamburg/plc");

        JsonElement row = RowFor(rows, "plc")
            ?? throw new InvalidOperationException("hamburg/plc must be listed after being set strict");
        row.GetProperty("fab").GetString().ShouldBe("hamburg");
        row.GetProperty("mode").GetString().ShouldBe("strict");
    }

    [Fact]
    public async Task Declaring_the_same_pair_twice_is_refused_with_409()
    {
        using HttpClient hamburg = await ClientFor(HamburgOperator);
        await SetAsync(hamburg, "webhook", "discovery");

        HttpResponseMessage refused = await DeclareAsync(hamburg, "webhook", "strict");

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Diagnose(refused));
        (await TitleOfAsync(refused)).ShouldBe("SOURCE_MODE_ALREADY_DECLARED");
    }

    [Fact]
    public async Task An_unknown_source_or_mode_is_refused_with_400()
    {
        using HttpClient hamburg = await ClientFor(HamburgOperator);

        HttpResponseMessage badSource = await DeclareAsync(hamburg, "mqtt", "strict");
        badSource.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Diagnose(badSource));
        (await TitleOfAsync(badSource)).ShouldBe("SOURCE_MODE_INVALID_INPUT");

        HttpResponseMessage badMode = await DeclareAsync(hamburg, "plc", "paranoid");
        badMode.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Diagnose(badMode));
        (await TitleOfAsync(badMode)).ShouldBe("SOURCE_MODE_INVALID_INPUT");

        HttpResponseMessage badRouteSource = await ChangeAsync(hamburg, "not-a-source", "discovery", expectedVersion: 0);
        badRouteSource.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Diagnose(badRouteSource));
        (await TitleOfAsync(badRouteSource)).ShouldBe("SOURCE_MODE_INVALID_INPUT");
    }

    [Fact]
    public async Task A_multi_fab_caller_that_names_no_fab_is_refused_with_400()
    {
        using HttpClient multi = await ClientFor(MultiFabOperator);

        HttpResponseMessage refused = await DeclareAsync(multi, "plc", "strict");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Diagnose(refused));
        (await TitleOfAsync(refused)).ShouldBe("EVENT_FAB_REQUIRED");
    }

    /// <summary>
    /// A refusal must be shown to have written nothing, not merely to have
    /// answered — the other fab's list is asserted unchanged.
    /// </summary>
    [Fact]
    public async Task Naming_a_fab_the_caller_does_not_hold_declares_nothing()
    {
        using HttpClient munich = await ClientFor(MunichOperator);
        JsonElement before = await ListRowsAsync(munich, "munich's list before the refused cross-fab attempt");
        int countBefore = before.EnumerateArray().Count();

        using HttpClient dresden = await ClientFor(DresdenOperator);
        HttpResponseMessage refused = await DeclareAsync(dresden, "plc", "strict", fabId: "munich");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await Diagnose(refused));

        JsonElement after = await ListRowsAsync(munich, "munich's list after the refused cross-fab attempt");
        after.EnumerateArray().Count().ShouldBe(countBefore, "a 403 must not have declared a munich row");
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_with_401()
    {
        using HttpClient anonymous = aspire.CreateServiceClient(ResourceName);

        HttpResponseMessage refused = await DeclareAsync(anonymous, "plc", "strict");

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Diagnose(refused));
    }

    [Fact]
    public async Task Changing_without_If_Match_is_refused_with_428()
    {
        using HttpClient hamburg = await ClientFor(HamburgOperator);
        await SetAsync(hamburg, "manual", "discovery");

        HttpResponseMessage refused = await ChangeAsync(hamburg, "manual", "strict", expectedVersion: null);

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired, await Diagnose(refused));
    }

    [Fact]
    public async Task Changing_with_a_stale_If_Match_is_refused_with_409()
    {
        using HttpClient hamburg = await ClientFor(HamburgOperator);
        await SetAsync(hamburg, "inference", "discovery");
        JsonElement rows = await ListRowsAsync(hamburg, "reading hamburg/inference's current version");
        int currentVersion = (RowFor(rows, "inference")
            ?? throw new InvalidOperationException("hamburg/inference must exist after SetAsync"))
            .GetProperty("version").GetInt32();

        HttpResponseMessage refused = await ChangeAsync(hamburg, "inference", "strict", currentVersion + 99);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Diagnose(refused));
        (await TitleOfAsync(refused)).ShouldBe("SOURCE_MODE_STALE");
    }

    /// <summary>Reserved pair — see this class's remarks. Never declared elsewhere.</summary>
    [Fact]
    public async Task Changing_an_undeclared_pair_is_404()
    {
        using HttpClient munich = await ClientFor(MunichOperator);

        HttpResponseMessage refused = await ChangeAsync(munich, "webhook", "strict", expectedVersion: 0);

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await Diagnose(refused));
        (await TitleOfAsync(refused)).ShouldBe("SOURCE_MODE_NOT_DECLARED");
    }

    [Fact]
    public async Task Listing_never_shows_another_fabs_modes()
    {
        using HttpClient hamburg = await ClientFor(HamburgOperator);
        await SetAsync(hamburg, "plc", "discovery"); // ensure at least one row exists to check

        JsonElement rows = await ListRowsAsync(hamburg, "hamburg's own listing");

        rows.EnumerateArray().Select(row => row.GetProperty("fab").GetString())
            .ShouldAllBe(fab => fab == "hamburg", customMessage: "every row a single-fab operator sees belongs to that fab");
    }

    /// <summary>Reserved pair — see this class's remarks. Never declared elsewhere.</summary>
    [Fact]
    public async Task A_repeated_declare_with_the_same_idempotency_key_replays_the_same_201()
    {
        string key = $"key-{Guid.CreateVersion7():N}";
        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage first = await DeclareWithKeyAsync(dresden, "webhook", "strict", key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(first));
        Guid firstIdentifier = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetGuid();

        HttpResponseMessage second = await DeclareWithKeyAsync(dresden, "webhook", "strict", key);
        second.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(second));
        Guid secondIdentifier = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetGuid();

        secondIdentifier.ShouldBe(
            firstIdentifier, "a replay must return the identifier the first attempt created, not a second one");
    }

    private Task<HttpClient> ClientFor(string username) =>
        aspire.CreateAuthenticatedClientAsync(ResourceName, username, OperatorPassword);

    private async Task<string> Diagnose(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}"
        + $"event-ingestion log:{Environment.NewLine}{aspire.RecentLogs(ResourceName)}";
}
