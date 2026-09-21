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
/// <b>Every row here is damaged, never hand-inserted.</b> Registering once
/// through the real endpoint and then backdating the row it wrote is what makes
/// the recipe honest: <c>caller</c> is the operator identifier the endpoint
/// derives from the bearer token, and a hand-written value would land the row
/// in a different scope, leave the real claim untouched, and pass while
/// proving nothing (<c>plan.md</c> §*Testability*).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class StaleIdempotencyReservationIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

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
    /// identifier or answer <c>409</c> — both are acceptable outcomes for the
    /// loser. What must never happen is the one thing a plain <c>DO NOTHING</c>
    /// -&gt; <c>DO UPDATE</c> without the <c>SET</c> would allow: <b>both</b>
    /// candidate names actually created.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_concurrent_reclaims_of_one_stale_reservation_only_one_wins()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        string key = NewKey();
        string firstName = NewName();
        string candidateA = NewName();
        string candidateB = NewName();

        await RegisterAsync(cameras, firstName, key);
        await DamageToUnfinishedAsync(key, TimeSpan.FromMinutes(30));

        Task<HttpResponseMessage> requestA = SendAsync(cameras, candidateA, key);
        Task<HttpResponseMessage> requestB = SendAsync(cameras, candidateB, key);
        HttpResponseMessage[] responses = await Task.WhenAll(requestA, requestB);

        try
        {
            responses.ShouldAllBe(
                response => response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.Conflict,
                $"both concurrent attempts must answer either 201 or 409, never an error: "
                + $"{string.Join(", ", responses.Select(r => r.StatusCode))}");

            string[] names = await NamesAsync(cameras);
            bool createdA = names.Contains(candidateA);
            bool createdB = names.Contains(candidateB);

            (createdA ^ createdB).ShouldBeTrue(
                $"exactly one of the two concurrent reclaims must have run the work — "
                + $"candidateA created: {createdA}, candidateB created: {createdB}. Both true is the "
                + "double-application SET reserved_at = NOW() exists to prevent; both false means "
                + "neither reclaimed a reservation that should have been reclaimable by one of them.");
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
        string secondName = NewName();

        await RegisterAsync(cameras, firstName, key);

        await DamageToUnfinishedAsync(key, TimeSpan.FromSeconds(10));

        using HttpResponseMessage response = await SendAsync(cameras, secondName, key);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe(IdempotencyHeaders.InProgressErrorCode);

        (await NamesAsync(cameras)).ShouldNotContain(
            secondName, "a still-live reservation must not let a second attempt through.");
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
        string secondName = NewName();

        Guid firstIdentifier = await RegisterAsync(cameras, firstName, key);

        await AgeCompletedReservationAsync(key, TimeSpan.FromDays(30));

        using HttpResponseMessage response = await SendAsync(cameras, secondName, key);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        Guid replayedIdentifier = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetGuid();
        replayedIdentifier.ShouldBe(
            firstIdentifier,
            "a completed key must be replayed no matter how old its reservation is — reclaiming a "
            + "completed row would turn a replayable answer back into a fresh registration.");

        (await NamesAsync(cameras)).ShouldNotContain(
            secondName, "the second body must never have been registered — that would be the key "
            + "applying twice, which is the one thing it promises not to do.");
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
            Content = JsonContent.Create(new { name, rtspUrl = "rtsp://camera.test/stream" }),
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
