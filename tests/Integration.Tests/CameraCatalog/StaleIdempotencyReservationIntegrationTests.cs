using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.ServiceDefaults.Idempotency;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// #2290 — a crashed idempotent attempt wedges its key at 409 forever, and the
/// table has no reaper. Against the real stack (ADR-0103), because the defect
/// is in one <c>ON CONFLICT</c> clause and a fake store that ignores the SQL
/// proves nothing about it (<c>spec.md</c> §*Why no existing test could have
/// caught this*).
///
/// <para>
/// <b>Most rows here are damaged, never hand-inserted.</b> Registering once
/// through the real endpoint and then backdating the row it wrote is what makes
/// the recipe honest: <c>caller</c> is the operator identifier the endpoint
/// derives from the bearer token, and a hand-written value would land the row
/// in a different scope, leave the real claim untouched, and pass while
/// proving nothing (<c>plan.md</c> §*Testability*). The one exception is
/// <see cref="SeedUnfinishedReservationAsync"/>, added for spec 302
/// (#2424/#2492) — see its own doc comment for why that fact needs a row that
/// was never completed in the first place, and why seeding it with the
/// <i>correct</i> scope and fingerprint is not the wrong-scope hand-insert this
/// paragraph warns against.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class StaleIdempotencyReservationIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = SeededCredentials.OpDresden;
    private const string CameraRtspUrl = "rtsp://camera.test/stream";

    /// <summary>
    /// <c>CameraCatalog.Api.CameraEndpoints</c>'s own <c>RegisterEndpoint</c>
    /// constant, mirrored here because it is private to that class (and this
    /// test project has no reference to <c>CameraCatalog.Api</c> — see
    /// <see cref="SeedRegisterCameraRequest"/>) —
    /// <see cref="SeedUnfinishedReservationAsync"/> needs the exact same
    /// literal to seed a row <c>BeginAsync</c> would recognise.
    /// </summary>
    private const string RegisterCameraEndpoint = "POST /cameras";

    /// <summary>
    /// Mirrors <c>CameraCatalog.Api.Requests.RegisterCameraRequest</c>'s exact
    /// shape — same property names, same declaration order — purely so
    /// <see cref="IdempotencyFingerprint.Of{TRequest}"/> serialises it
    /// byte-for-byte identically: the fingerprint envelope only ever sees the
    /// serialised field values, never the CLR type name, so a type with the
    /// same shape fingerprints the same. A local type rather than a
    /// <c>ProjectReference</c> to <c>CameraCatalog.Api</c> — a project
    /// dependency no other integration test here needs — keeps this test-only
    /// fix test-side, consistent with every other fact in this file reaching
    /// the system only over HTTP.
    /// </summary>
    private sealed record SeedRegisterCameraRequest
    {
        public required string Name { get; init; }

        public required string RtspUrl { get; init; }
    }

    /// <summary>
    /// T003 — the red case. Today the damaged row is still read back as
    /// in-progress no matter how old it is, so this answers 409 after the usual
    /// ~5 s of polling.
    /// </summary>
    [Fact]
    public async Task A_reservation_older_than_the_bound_is_reclaimed_by_the_next_caller()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        string key = NewKey();
        string firstName = NewName();
        string secondName = NewName();

        Guid firstIdentifier = await RegisterAsync(cameras, firstName, key);

        await DamageToUnfinishedAsync(key, TimeSpan.FromMinutes(30));

        using HttpResponseMessage response = await SendAsync(cameras, secondName, key);

        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync()
            + $" -- a reservation older than {IdempotencyReclamation.StaleAfter} with no "
            + "resource_identifier must be reclaimed by the next caller, not refused forever.");

        Guid secondIdentifier = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetGuid();
        secondIdentifier.ShouldNotBe(
            firstIdentifier,
            "a reclaim must do the work again, not replay the dead attempt's answer — a replay would "
            + "hand back the first camera's identifier.");

        (await NamesAsync(cameras)).ShouldContain(
            secondName,
            "the reclaimed reservation's work must actually have run and created the second camera, "
            + "not merely changed the shape of the response.");
    }

    /// <summary>
    /// Phase-6 review — the single claim the whole fix rests on had no test:
    /// <c>SET reserved_at = NOW()</c> in <c>BeginAsync</c>'s conflict clause is
    /// what makes a second concurrent reclaimer lose the race rather than both
    /// running the work. Two requests fired together, genuinely concurrently
    /// (never awaited one at a time — that would just be T003 twice), against
    /// one stale reservation.
    ///
    /// <para>
    /// Whichever wins may answer <c>201</c> immediately or, if it loses the
    /// race for the row lock, fall through to the same in-progress wait an
    /// ordinary concurrent retry hits and then either replay the winner's
    /// identifier (<c>201</c>) or answer <c>409</c> — both are acceptable
    /// outcomes for the loser, but only a <c>409</c> whose title is
    /// <see cref="IdempotencyHeaders.InProgressErrorCode"/> is a legitimate
    /// one. A <c>409</c> titled <c>CAMERA_NAME_TAKEN</c> — the domain's own
    /// unique-name conflict — looks identical at the status-code level but
    /// means something very different: both sides actually ran the create
    /// and collided on <c>ux_cameras_fab_name_normalized_active</c>, which is
    /// the exact double-application the <c>SET reserved_at = NOW()</c> fence
    /// below exists to prevent. Accepting any <c>409</c> title would let a
    /// broken fence pass this fact silently. What must never happen is the
    /// one thing a plain <c>DO NOTHING</c> -&gt; <c>DO UPDATE</c> without the
    /// <c>SET</c> would allow: <b>both</b> concurrent attempts actually
    /// running the create.
    /// </para>
    ///
    /// <para>
    /// Spec 302 (#2424/#2492) forced both concurrent retries onto the <i>same</i>
    /// name: a mismatched fingerprint is now refused before the stale/in-progress
    /// logic this fact exists to exercise ever runs, so <c>candidateA</c>/
    /// <c>candidateB</c> as two different names no longer reaches that logic at
    /// all — see <see cref="SeedUnfinishedReservationAsync"/> for why the
    /// reservation itself also had to stop going through the real endpoint. With
    /// one shared name, "both ran the create" would surface as a domain-level
    /// name conflict rather than two distinct cameras, so the proof is now: at
    /// most one distinct identifier is ever created, every <c>409</c> is the
    /// legitimate in-progress one, and the name is registered exactly once.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_concurrent_reclaims_of_one_stale_reservation_only_one_wins()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        string key = NewKey();
        string name = NewName();

        await SeedUnfinishedReservationAsync(key, name, TimeSpan.FromMinutes(30));

        Task<HttpResponseMessage> requestA = SendAsync(cameras, name, key);
        Task<HttpResponseMessage> requestB = SendAsync(cameras, name, key);
        HttpResponseMessage[] responses = await Task.WhenAll(requestA, requestB);

        try
        {
            responses.ShouldAllBe(
                response => response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.Conflict,
                $"both concurrent attempts must answer either 201 or 409, never an error: "
                + $"{string.Join(", ", responses.Select(r => r.StatusCode))}");

            Guid[] createdIdentifiers = new Guid[responses.Length];
            int createdCount = 0;
            foreach (HttpResponseMessage response in responses)
            {
                if (response.StatusCode == HttpStatusCode.Created)
                {
                    createdIdentifiers[createdCount++] =
                        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetGuid();
                }
                else
                {
                    JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
                    problem.GetProperty("title").GetString().ShouldBe(
                        IdempotencyHeaders.InProgressErrorCode,
                        "the only legitimate 409 for the loser is the in-progress answer an ordinary "
                        + "concurrent retry hits — a 409 titled CAMERA_NAME_TAKEN (or anything else) "
                        + "means both sides actually ran the create and collided in the domain instead "
                        + "of being fenced, which is exactly what SET reserved_at = NOW() exists to "
                        + "prevent.");
                }
            }

            createdIdentifiers.Take(createdCount).Distinct().Count().ShouldBe(
                1,
                "a 201 answer here is either the winner's own create or the loser's replay of it, so "
                + "every 201 must carry the same identifier — two different identifiers would mean the "
                + "create ran twice, the exact double-application SET reserved_at = NOW() exists to "
                + "prevent.");

            string[] names = await NamesAsync(cameras);
            names.Count(registered => registered == name).ShouldBe(
                1,
                "exactly one concurrent reclaim must have run the create — the reservation was seeded "
                + "unfinished with the name never previously registered, so one of the two attempts "
                + "must have created it, and only once.");
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>
    /// T004 — the control that must stay green. A too-eager reclaim would
    /// break this: the first attempt is still within the bound, so it is a
    /// live reservation and must still be refused.
    /// </summary>
    [Fact]
    public async Task A_reservation_that_is_still_young_is_still_refused()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        string key = NewKey();
        string firstName = NewName();

        await RegisterAsync(cameras, firstName, key);

        await DamageToUnfinishedAsync(key, TimeSpan.FromSeconds(10));

        // Spec 302 (#2424/#2492): the retry must present firstName, not a
        // different name — a mismatched fingerprint is refused (422) before
        // this fact's in-progress logic ever runs. Reusing firstName matches
        // the seeded fingerprint, falls through to the in-progress check this
        // fact exists to exercise, and — because in-progress never re-runs the
        // create — never collides with firstName's already-registered camera.
        using HttpResponseMessage response = await SendAsync(cameras, firstName, key);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe(IdempotencyHeaders.InProgressErrorCode);
    }

    /// <summary>
    /// T005 — the ADR-0142 §Consequences guard, and the single most important
    /// assertion in this suite. Aged 30 days past the bound on purpose: a
    /// reaper that ignored <c>resource_identifier</c> would pass every other
    /// test here and fail only on this one.
    /// </summary>
    [Fact]
    public async Task A_completed_key_is_replayed_however_old_it_is()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        string key = NewKey();
        string firstName = NewName();

        Guid firstIdentifier = await RegisterAsync(cameras, firstName, key);

        await AgeCompletedReservationAsync(key, TimeSpan.FromDays(30));

        // Spec 302 (#2424/#2492): the retry must present firstName, not a
        // different name, or the fingerprint mismatch would refuse it (422)
        // before the completed-replay logic this fact exercises ever runs.
        using HttpResponseMessage response = await SendAsync(cameras, firstName, key);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        Guid replayedIdentifier = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetGuid();
        replayedIdentifier.ShouldBe(
            firstIdentifier,
            "a completed key must be replayed no matter how old its reservation is — reclaiming a "
            + "completed row would turn a replayable answer back into a fresh registration.");
    }

    /// <summary>
    /// T006 — <c>IdempotencyScope.Caller</c> is a security boundary, not
    /// bookkeeping. A stale reservation belongs to the caller who reserved it,
    /// never to whoever else happens to present the same key string.
    /// </summary>
    [Fact]
    public async Task A_stale_reservation_is_not_another_callers_to_reclaim()
    {
        using HttpClient admin = await aspire.CreateAdminClientAsync("camera-catalog");
        using HttpClient dresden = await aspire.CreateAuthenticatedClientAsync(
            "camera-catalog", DresdenOperator, OperatorPassword);
        string key = NewKey();
        string firstName = NewName();
        string secondName = NewName();

        Guid firstIdentifier = await RegisterAsync(admin, firstName, key);

        await DamageToUnfinishedAsync(key, TimeSpan.FromMinutes(30));

        using HttpResponseMessage response = await SendAsync(dresden, secondName, key, fabId: "dresden");

        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync()
            + " -- a different caller presenting the same key string owns none of the first caller's "
            + "reservation and must be free to claim its own.");
        Guid secondIdentifier = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetGuid();
        secondIdentifier.ShouldNotBe(
            firstIdentifier,
            "the second operator must never receive the first operator's identifier — that is exactly "
            + "the exploit IdempotencyScope.Caller exists to prevent.");

        // Row-level, not just response-level (phase-6 review): a status code
        // and a differing identifier both hold even under a broken reclaim
        // that dropped `caller` from its predicate — dresden would then have
        // taken over admin's row rather than inserted its own. Two rows under
        // one key string, with admin's still unfinished, is what proves
        // dresden claimed a row of its own instead.
        (await RowCountAsync(key)).ShouldBe(
            2, "the first operator's reservation must still exist as its own row, not be taken over.");
        (await UnfinishedRowSurvivesAsync(key)).ShouldBeTrue(
            "the first operator's reservation must still be unfinished and untouched — reclamation is "
            + "scoped to one caller at a time.");
    }

    /// <summary>
    /// T013(a) — driving the sweep directly, never waiting for a real timer
    /// tick. A stale unfinished reservation is exactly what US1 also reclaims,
    /// but nobody has to retry for the sweep to remove it.
    /// </summary>
    [Fact]
    public async Task A_stale_unfinished_reservation_vanishes_after_the_sweep()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        string key = NewKey();
        await RegisterAsync(cameras, NewName(), key);
        await DamageToUnfinishedAsync(key, TimeSpan.FromMinutes(30));

        using IdempotencyReservationSweepHostedService<CameraCatalogDbContext> sweep = await CreateSweepAsync();
        await sweep.RunOnceAsync(CancellationToken.None);

        (await RowExistsAsync(key)).ShouldBeFalse(
            "nothing will ever retry this key, so the sweep — not a caller — must be what removes it.");
    }

    /// <summary>
    /// T013(b) — the ADR-0142 guard again, and not optional here either. A
    /// sweep that only checked <c>reserved_at</c> would delete this row and
    /// turn a replayable answer into nothing.
    /// </summary>
    [Fact]
    public async Task An_aged_completed_reservation_survives_the_sweep_with_its_identifier_intact()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        string key = NewKey();
        Guid identifier = await RegisterAsync(cameras, NewName(), key);
        await AgeCompletedReservationAsync(key, TimeSpan.FromDays(30));

        using IdempotencyReservationSweepHostedService<CameraCatalogDbContext> sweep = await CreateSweepAsync();
        await sweep.RunOnceAsync(CancellationToken.None);

        (await IdentifierOfAsync(key)).ShouldBe(
            identifier, "a completed key must survive the sweep however old it is, identifier intact.");
    }

    /// <summary>T013(c) — a live reservation is not the sweep's to touch.</summary>
    [Fact]
    public async Task A_fresh_unfinished_reservation_survives_the_sweep()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        string key = NewKey();
        await RegisterAsync(cameras, NewName(), key);
        await DamageToUnfinishedAsync(key, TimeSpan.FromSeconds(10));

        using IdempotencyReservationSweepHostedService<CameraCatalogDbContext> sweep = await CreateSweepAsync();
        await sweep.RunOnceAsync(CancellationToken.None);

        (await RowExistsAsync(key)).ShouldBeTrue(
            "a reservation still within the bound is still legitimately in flight and must survive.");
    }

    private static string NewName() => $"cam-{Guid.CreateVersion7():N}";

    private static string NewKey() => $"key-{Guid.CreateVersion7():N}";

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient cameras, string name, string? key, string fabId = Fab)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/cameras?fabId={fabId}")
        {
            Content = JsonContent.Create(new { name, rtspUrl = CameraRtspUrl }),
        };

        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return cameras.SendAsync(request);
    }

    private static async Task<Guid> RegisterAsync(HttpClient cameras, string name, string key)
    {
        using HttpResponseMessage response = await SendAsync(cameras, name, key);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetGuid();
    }

    private static async Task<string[]> NamesAsync(HttpClient cameras)
    {
        using HttpResponseMessage listed = await cameras.GetAsync("/cameras?limit=200");
        listed.EnsureSuccessStatusCode();
        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return [.. page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()!)];
    }

    /// <summary>
    /// The load-bearing helper (T001): registers nothing itself, only damages a
    /// row the endpoint already wrote, to the same shape
    /// <c>IdempotencyKeyTable</c> gives an attempt still running. The interval
    /// is parameterised so this one recipe serves both the stale case (T003)
    /// and the still-live control (T004).
    /// </summary>
    private async Task DamageToUnfinishedAsync(string key, TimeSpan age)
    {
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE idempotency_key
               SET resource_identifier = NULL, completed_at = NULL, reserved_at = NOW() - {1}
             WHERE key = {0};
            """,
            key, age);
    }

    /// <summary>
    /// Spec 302 (#2424/#2492) phase-6 review — a third damage recipe, needed
    /// only by <see cref="Two_concurrent_reclaims_of_one_stale_reservation_only_one_wins"/>.
    ///
    /// <para>
    /// <see cref="DamageToUnfinishedAsync"/> simulates "crashed before
    /// completing" by actually completing the create (through the real
    /// endpoint) and then unwinding the row's columns — the camera it created
    /// still exists even though the row claims otherwise. That dishonesty
    /// never mattered before, because every retry deliberately used a
    /// <i>different</i> name from the original reservation. The fingerprint
    /// mismatch check this spec adds now refuses any retry whose body does not
    /// match the original before the stale/in-progress logic ever runs, so the
    /// concurrent-reclaim fact's two retries must present the <b>same</b> name
    /// as the reservation they are racing to reclaim — and that name is already
    /// a real, registered camera under <c>DamageToUnfinishedAsync</c>'s recipe,
    /// which would make the winning reclaim's create collide with
    /// <c>ux_cameras_fab_name_normalized_active</c> instead of answering 201.
    /// </para>
    ///
    /// <para>
    /// A row that was truly never completed never ran the create in the first
    /// place, so this seeds the row directly — with the exact <c>(key,
    /// endpoint, caller)</c> scope and the <c>fab</c>/<c>request_fingerprint</c>
    /// a real <c>BeginAsync</c> would have written for this request (computed
    /// with the production <see cref="IdempotencyFingerprint.Of{TRequest}"/>
    /// hashing pipeline, never hand-rolled — see
    /// <see cref="SeedRegisterCameraRequest"/> for why its input is a local
    /// mirror type rather than the real <c>RegisterCameraRequest</c>),
    /// <c>resource_identifier = NULL</c>,
    /// <c>completed_at = NULL</c>, and a backdated <c>reserved_at</c> — without
    /// ever calling the real endpoint first. The name is therefore free for
    /// whichever concurrent retry wins the race to create for the first time,
    /// which is a more honest model of "crashed before completing" than
    /// <see cref="DamageToUnfinishedAsync"/>'s recipe, not a shortcut around it.
    /// This is not the wrong-scope hand-insert this file's own doc comment
    /// warns against — the scope and fingerprint are exactly what the real
    /// endpoint would have written.
    /// </para>
    /// </summary>
    private async Task SeedUnfinishedReservationAsync(string key, string name, TimeSpan age)
    {
        string token = await aspire.GetAdminAccessTokenAsync();

        // Guid.Parse + ToString(), not the raw claim string: CameraEndpoints'
        // ResolveOperator parses the subject into a Guid and stores
        // registeredBy.Value.ToString()'s canonical lowercase-hyphenated
        // format, which need not match the raw JWT claim's own casing.
        string caller = Guid.Parse(SubjectOf(token)).ToString();

        SeedRegisterCameraRequest request = new() { Name = name, RtspUrl = CameraRtspUrl };
        string fingerprint = IdempotencyFingerprint.Of(request).Value;

        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO idempotency_key
                (key, endpoint, caller, resource_identifier, reserved_at, completed_at, fab, request_fingerprint)
            VALUES
                ({0}, {1}, {2}, NULL, NOW() - {3}, NULL, {4}, {5});
            """,
            key, RegisterCameraEndpoint, caller, age, Fab, fingerprint);
    }

    /// <summary>
    /// The admin token's subject claim — the same value <c>ResolveOperator</c>
    /// turns into the <c>caller</c> column, needed by
    /// <see cref="SeedUnfinishedReservationAsync"/> to seed a row under the
    /// exact scope a real reservation for this token would use.
    /// </summary>
    private static string SubjectOf(string token)
    {
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        return handler.ReadJwtToken(token).Claims
            .First(claim => string.Equals(claim.Type, "sub", StringComparison.Ordinal))
            .Value;
    }

    /// <summary>
    /// The other damage recipe (T005): backdates <c>reserved_at</c> without
    /// touching <c>resource_identifier</c> or <c>completed_at</c> — a completed
    /// row, aged, never an unfinished one. Deliberately not the same helper as
    /// <see cref="DamageToUnfinishedAsync"/>: nulling the identifier here would
    /// test something else entirely.
    /// </summary>
    private async Task AgeCompletedReservationAsync(string key, TimeSpan age)
    {
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE idempotency_key
               SET reserved_at = NOW() - {1}
             WHERE key = {0};
            """,
            key, age);
    }

    private async Task<bool> RowExistsAsync(string key)
    {
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        int[] rows = await db.Database
            .SqlQueryRaw<int>("""SELECT 1 AS "Value" FROM idempotency_key WHERE key = {0};""", key)
            .ToArrayAsync();

        return rows.Length > 0;
    }

    /// <summary>
    /// How many rows share this key string — one per distinct
    /// <c>(key, endpoint, caller)</c>, so two different callers presenting the
    /// same key produce two rows, never one shared row.
    /// </summary>
    private async Task<int> RowCountAsync(string key)
    {
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        int[] rows = await db.Database
            .SqlQueryRaw<int>("""SELECT 1 AS "Value" FROM idempotency_key WHERE key = {0};""", key)
            .ToArrayAsync();

        return rows.Length;
    }

    /// <summary>
    /// Whether a still-unfinished (<c>resource_identifier IS NULL</c>) row
    /// survives for this key — used to prove a caller's own live reservation
    /// was left alone by a different caller's reclaim attempt.
    /// </summary>
    private async Task<bool> UnfinishedRowSurvivesAsync(string key)
    {
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        int[] rows = await db.Database
            .SqlQueryRaw<int>(
                """SELECT 1 AS "Value" FROM idempotency_key WHERE key = {0} AND resource_identifier IS NULL;""",
                key)
            .ToArrayAsync();

        return rows.Length > 0;
    }

    private async Task<Guid?> IdentifierOfAsync(string key)
    {
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        Guid?[] rows = await db.Database
            .SqlQueryRaw<Guid?>(
                """SELECT resource_identifier AS "Value" FROM idempotency_key WHERE key = {0};""", key)
            .ToArrayAsync();

        return rows.Length == 0 ? null : rows[0];
    }

    /// <summary>
    /// T013's "resolved instance" — a minimal <see cref="ServiceProvider"/>
    /// wired to the real database, so <c>RunOnceAsync</c> is driven directly
    /// rather than by waiting for a real timer tick.
    /// </summary>
    private async Task<IdempotencyReservationSweepHostedService<CameraCatalogDbContext>> CreateSweepAsync()
    {
        string? connectionString =
            await aspire.App.GetConnectionStringAsync(AspireFixture.CameraCatalogConnectionName);

        ServiceCollection services = new();
        services.AddDbContext<CameraCatalogDbContext>(options => options.UseNpgsql(connectionString));
        ServiceProvider provider = services.BuildServiceProvider();

        return new IdempotencyReservationSweepHostedService<CameraCatalogDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Options.Create(new IdempotencyReservationSweepOptions()),
            NullLogger<IdempotencyReservationSweepHostedService<CameraCatalogDbContext>>.Instance);
    }
}
