using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;
using static SmartSentinelEye.Integration.Tests.EventIngestion.EventTypeRegistryApi;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 143 FR-008 — the failure modes of <c>Idempotency-Key</c> on
/// <c>POST /event-types</c> that the primary 4a suite does not reach. That
/// suite asserts one case — a caller repeating its own key gets its own answer
/// back. Every other way a key can go wrong is untested.
///
/// <para>
/// <b>The first test is the security claim, and nothing in this repository has
/// ever asserted it.</b> <c>IdempotencyScope</c>'s own doc comment calls
/// <c>Caller</c> "a security boundary, not bookkeeping", and names the exact
/// exploit — two callers invent the same key string and the second is handed
/// the first one's resource. There is no test, at any level, in which two
/// different subjects present one key: not
/// <c>IdempotentCameraRegistrationIntegrationTests</c>, not
/// <c>IdempotentRegistrationIntegrationTests</c>, not
/// <c>IdempotentRequestTests</c> (whose fake store ignores the scope entirely).
/// The guarantee rests on one clause of one <c>ON CONFLICT</c> in
/// <c>IdempotencyStore</c>, unobserved.
/// </para>
///
/// <para>
/// <b>Red on arrival, all of it:</b> <c>/event-types</c> is not mapped, so every
/// request here answers <c>404 Not Found</c>.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class EventTypeRegistryIdempotencyIntegrationTests(AspireFixture aspire)
{
    /// <summary>
    /// Two subjects, one key string, two different kinds.
    ///
    /// <para>
    /// <b>The key is run-unique rather than the literal <c>"1"</c> the ADR
    /// names</b>, and the difference is not cosmetic. The integration database
    /// is shared across runs, so a literal key would be completed by the first
    /// run and replayed by the second — the test would then pass while
    /// registering nothing, which is the failure it exists to catch. What the
    /// case needs is one string used by two callers, not a famous string.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_key_one_caller_used_is_not_a_replay_for_another_caller()
    {
        string shared = $"shared-{Guid.NewGuid():N}";
        string dresdenKind = UniqueKind();
        string multiKind = UniqueKind();
        using HttpClient dresden = await ClientFor(DresdenOperator);
        using HttpClient multi = await ClientFor(MultiFabOperator);

        HttpResponseMessage first = await RegisterWithKeyAsync(dresden, dresdenKind, shared);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(first));

        HttpResponseMessage second = await RegisterWithKeyAsync(multi, multiKind, shared, fabId: "dresden");

        second.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(second));
        (await IdentifierOfAsync(second)).ShouldNotBe(
            await IdentifierOfAsync(first),
            "a key is scoped to the authenticated caller (ADR-0142). Two operators inventing the same "
            + "string must get two registrations; handing the second caller the first one's identifier "
            + "is the exploit IdempotencyScope.Caller exists to prevent.");

        (await ListRowsAsync(multi, await Diagnose(second))).EnumerateArray()
            .Count(row => row.GetProperty("kind").GetString() == multiKind)
            .ShouldBe(1, "the second caller's own kind must actually have been registered, not "
                + "swallowed by a replay of somebody else's request");
    }

    /// <summary>
    /// The same caller, the same key, a <b>different</b> body.
    ///
    /// <para>
    /// ADR-0142 makes a key mean "this operation, at most once" — so the second
    /// request is a replay and the second kind is never registered, silently.
    /// That is the documented behaviour and this test pins it; it is also a
    /// sharp edge an operator scripting registrations with a fixed key will
    /// meet, and <c>spec.md</c> FR-008 does not mention it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_key_reused_for_a_different_kind_replays_the_first_registration()
    {
        string key = $"reused-{Guid.NewGuid():N}";
        string firstKind = UniqueKind();
        string secondKind = UniqueKind();
        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage first = await RegisterWithKeyAsync(dresden, firstKind, key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(first));

        HttpResponseMessage replay = await RegisterWithKeyAsync(dresden, secondKind, key);

        replay.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(replay));
        (await IdentifierOfAsync(replay)).ShouldBe(
            await IdentifierOfAsync(first),
            "one key means one operation; a second body under it is a replay, not a second create");

        JsonElement rows = await ListRowsAsync(dresden, await Diagnose(replay));
        CountOf(rows, secondKind).ShouldBe(
            0, "the replayed body must not have been registered as well — that would be the key "
            + "applying twice, which is the one thing it promises not to do");
    }

    /// <summary>
    /// Silently ignoring an unusable key hands the caller the at-most-once
    /// guarantee it asked for without delivering it —
    /// <c>IdempotencyHeaders.TryRead</c>'s stated reason for returning a problem
    /// rather than <c>Option.None</c>. The precedent is
    /// <c>IdempotentRegistrationIntegrationTests.A_malformed_key_is_refused_rather_than_ignored</c>;
    /// <c>POST /event-types</c> needs its own, because the refusal lives in the
    /// endpoint the engineer writes, not in the helper.
    /// </summary>
    [Fact]
    public async Task A_malformed_key_is_refused_rather_than_ignored()
    {
        string kind = UniqueKind();
        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage refused = await RegisterWithKeyAsync(dresden, kind, "has a space");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Diagnose(refused));
        (await TitleOfAsync(refused)).ShouldBe("IDEMPOTENCY_KEY_MALFORMED");

        JsonElement rows = await ListRowsAsync(dresden, await Diagnose(refused));
        CountOf(rows, kind).ShouldBe(0, "a refused request writes nothing");
    }

    /// <summary>
    /// The length boundary, one character past it. <c>IdempotencyKey.MaxLength</c>
    /// is 128 and this value is 129 — the key reaches a primary key, so an
    /// unbounded one is a write amplification a caller controls.
    ///
    /// <para>
    /// Asserted at 129 rather than at some large round number so the case is
    /// off-by-one rather than merely long: a <c>&gt;=</c> written for a
    /// <c>&gt;</c> refuses the longest legal key and nothing else would say so.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_key_one_character_past_the_length_boundary_is_refused()
    {
        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage refused = await RegisterWithKeyAsync(dresden, UniqueKind(), new string('k', 129));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Diagnose(refused));
        (await TitleOfAsync(refused)).ShouldBe("IDEMPOTENCY_KEY_MALFORMED");
    }

    /// <summary>
    /// The other side of that boundary — a key of exactly
    /// <c>IdempotencyKey.MaxLength</c> is legal and must be honoured, so the
    /// refusal above cannot be satisfied by refusing everything long.
    /// </summary>
    [Fact]
    public async Task A_key_of_exactly_the_maximum_length_is_accepted()
    {
        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage accepted = await RegisterWithKeyAsync(
            dresden, UniqueKind(), $"{Guid.NewGuid():N}".PadRight(128, 'k'));

        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(accepted));
    }

    /// <summary>
    /// The release path. A create that refuses has nothing to replay, so
    /// <c>IdempotentRequest</c> frees the key instead of completing it — and a
    /// caller that fixes its request must be able to retry with the same one. An
    /// endpoint that wraps only the success in <c>ExecuteCreateAsync</c>, or
    /// completes on failure, wedges the key forever and nothing else in this
    /// spec's suite would notice.
    /// </summary>
    [Fact]
    public async Task A_key_whose_first_attempt_was_refused_can_be_used_again()
    {
        string key = $"released-{Guid.NewGuid():N}";
        string taken = UniqueKind();
        string fresh = UniqueKind();
        using HttpClient dresden = await ClientFor(DresdenOperator);
        (await RegisterAsync(dresden, taken)).StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage duplicate = await RegisterWithKeyAsync(dresden, taken, key);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Diagnose(duplicate));

        HttpResponseMessage retried = await RegisterWithKeyAsync(dresden, fresh, key);

        retried.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await Diagnose(retried) + Environment.NewLine
            + "a key whose attempt created nothing is released, not completed — otherwise a caller "
            + "that fixes its request can never retry under the key it already used");
    }

    private Task<HttpClient> ClientFor(string username) =>
        aspire.CreateAuthenticatedClientAsync(ResourceName, username, OperatorPassword);

    private async Task<string> Diagnose(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}"
        + $"event-ingestion log:{Environment.NewLine}{aspire.RecentLogs(ResourceName)}";
}
