using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartSentinelEye.ServiceDefaults.Persistence;

namespace SmartSentinelEye.ServiceDefaults.Tests.Persistence;

/// <summary>
/// Spec 034 T007–T010.
///
/// <para>
/// Most of what follows asserts what the handler must <b>not</b> do. That ratio
/// is the shape of the feature: the risk is not building the wrong thing, it is
/// building something slightly too wide and breaking two refusals that already
/// work.
/// </para>
/// </summary>
public class UniqueConstraintExceptionHandlerTests
{
    private const string ConstraintName = "ux_cameras_fab_name_normalized_active";
    private const string CollidingValue = "line-3-inlet";

    /// <summary>
    /// <b>T007 — the ordering trap, and the assertion the whole feature rests
    /// on.</b>
    ///
    /// <para>
    /// <c>DbUpdateConcurrencyException</c> <b>derives from</b>
    /// <c>DbUpdateException</c>. A handler matching the base type would answer
    /// for every lost update as well, reporting it as a name collision — and
    /// because <c>ConcurrencyConflictExceptionHandler</c> is registered before
    /// this one, that is not hypothetical ordering trivia but the live failure
    /// mode.
    /// </para>
    ///
    /// <para>
    /// The caller would be told to choose a different name for a change that
    /// only needed re-reading. This assertion lives in the handler's own tests
    /// rather than in a registration test <b>because it must hold regardless of
    /// order</b> — and it is what fails if someone later widens the match.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_lost_update_is_declined_and_left_to_the_concurrency_handler()
    {
        DefaultHttpContext context = NewContext();

        bool handled = await new UniqueConstraintExceptionHandler()
            .TryHandleAsync(context, new DbUpdateConcurrencyException("row count was unexpected"), CancellationToken.None);

        handled.ShouldBeFalse();

        // Declined means untouched: a handler that returns false after writing a
        // status has already corrupted the response the next handler will write.
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        (await BodyOfAsync(context)).ShouldBeEmpty();
    }

    /// <summary>T008 — the mapping, through the arm that actually fires.</summary>
    [Fact]
    public async Task A_unique_violation_wrapped_by_EF_becomes_a_conflict()
    {
        DefaultHttpContext context = NewContext();

        bool handled = await new UniqueConstraintExceptionHandler()
            .TryHandleAsync(context, WrappedUniqueViolation(), CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status409Conflict);

        JsonElement problem = JsonDocument.Parse(await BodyOfAsync(context)).RootElement;
        problem.GetProperty("title").GetString().ShouldBe("RESOURCE_ALREADY_EXISTS");
        problem.GetProperty("status").GetInt32().ShouldBe(409);
    }

    /// <summary>
    /// T008 — the bare arm. Cheap, and it documents which one is theoretical:
    /// EF wraps every provider exception, so in practice only the wrapped arm
    /// above ever fires.
    /// </summary>
    [Fact]
    public async Task A_bare_unique_violation_becomes_a_conflict_too()
    {
        DefaultHttpContext context = NewContext();

        bool handled = await new UniqueConstraintExceptionHandler()
            .TryHandleAsync(context, UniqueViolation(), CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// Another SQLSTATE is not this handler's business. Without this, the
    /// matching could degrade to "any PostgresException" and still pass every
    /// assertion above.
    ///
    /// <para>
    /// <c>42P01</c> specifically, because that is what
    /// <c>DirectWriteHonestyIntegrationTests</c> provokes by dropping a table
    /// and requires to stay a fault. A handler wide enough to catch it would
    /// tell an operator to choose a different name for storage that does not
    /// exist.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_different_storage_failure_is_declined()
    {
        DefaultHttpContext context = NewContext();

        bool handled = await new UniqueConstraintExceptionHandler().TryHandleAsync(
            context,
            new DbUpdateException("update failed", Postgres(PostgresErrorCodes.UndefinedTable)),
            CancellationToken.None);

        handled.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task An_unrelated_exception_is_declined()
    {
        DefaultHttpContext context = NewContext();

        bool handled = await new UniqueConstraintExceptionHandler()
            .TryHandleAsync(context, new InvalidOperationException("something else"), CancellationToken.None);

        handled.ShouldBeFalse();
    }

    /// <summary>
    /// <b>T009 / T010 — the leak check, asserted on the rendered JSON.</b>
    ///
    /// <para>
    /// On the serialized body, <b>not</b> on the <c>ProblemDetails</c> object: a
    /// field that is set and then not serialized passes an object-level check
    /// while still shipping in the response the moment serialization changes.
    /// </para>
    ///
    /// <para>
    /// <b>Why the colliding value is checked, and why that is not
    /// over-cautious (FR-008).</b> Postgres puts the values that collided into
    /// its own <c>Detail</c>, verbatim. In a multi-fab deployment those values
    /// can belong to a fab the caller cannot see — so echoing them would turn
    /// this refusal into exactly the enumeration oracle that spec 029 FR-006,
    /// spec 030 FR-008 and spec 033 are all built to prevent. The constraint and
    /// table names are the obvious things to check; the value is the one that
    /// gets forgotten.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_response_says_nothing_about_the_storage_or_what_collided()
    {
        DefaultHttpContext context = NewContext();

        await new UniqueConstraintExceptionHandler()
            .TryHandleAsync(context, WrappedUniqueViolation(), CancellationToken.None);

        string body = await BodyOfAsync(context);

        body.ShouldNotContain(ConstraintName, Case.Insensitive);
        body.ShouldNotContain("cameras", Case.Insensitive);
        body.ShouldNotContain(CollidingValue, Case.Insensitive);

        // And the SQLSTATE itself, which says "unique_violation" to anyone who
        // looks it up and nothing to the operator reading it.
        body.ShouldNotContain("23505", Case.Insensitive);
    }

    /// <summary>
    /// The wording is a requirement, not decoration. It must not offer the
    /// stale-version remedy, because re-reading resolves that refusal and does
    /// nothing for this one — a caller given the wrong advice retries forever
    /// against a name that belongs to somebody else.
    /// </summary>
    [Fact]
    public async Task The_wording_points_at_a_different_name_rather_than_at_re_reading()
    {
        DefaultHttpContext context = NewContext();

        await new UniqueConstraintExceptionHandler()
            .TryHandleAsync(context, WrappedUniqueViolation(), CancellationToken.None);

        string detail = JsonDocument.Parse(await BodyOfAsync(context))
            .RootElement.GetProperty("detail").GetString()!;

        detail.ShouldContain("already exists", Case.Insensitive);
        detail.ShouldContain("different", Case.Insensitive);

        detail.ShouldNotContain("re-read", Case.Insensitive);
        detail.ShouldNotContain("reapply", Case.Insensitive);
    }

    /// <summary>
    /// ADR-0119, asserted at the source rather than left to the architecture
    /// test. This failure <em>is</em> caused by concurrency, so the tempting
    /// name is one that says so — and a caller told "concurrency" re-reads and
    /// retries against a name that is not theirs.
    /// </summary>
    [Fact]
    public void The_code_is_named_for_the_remedy_and_not_for_the_cause()
    {
        UniqueConstraintExceptionHandler.ErrorCode.ShouldNotEndWith("_STALE", Case.Sensitive);

        foreach (string meansLostUpdate in new[]
                 {
                     "VERSION_MISMATCH", "VERSION_CONFLICT", "VERSION_OUTDATED",
                     "STALE_VERSION", "REVISION_MISMATCH", "CONCURRENCY_CONFLICT",
                 })
        {
            UniqueConstraintExceptionHandler.ErrorCode
                .ShouldNotContain(meansLostUpdate, Case.Sensitive);
        }
    }

    private static DefaultHttpContext NewContext() =>
        new() { Response = { Body = new MemoryStream() } };

    private static async Task<string> BodyOfAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync(CancellationToken.None);
    }

    private static DbUpdateException WrappedUniqueViolation() =>
        new("update failed", UniqueViolation());

    /// <summary>
    /// Shaped like the real thing: Postgres reports the constraint by name and
    /// quotes the colliding value in its detail. Both are here so the leak test
    /// has something real to fail on — a synthetic exception with empty fields
    /// would pass it vacuously.
    /// </summary>
    private static PostgresException UniqueViolation() =>
        Postgres(
            PostgresErrorCodes.UniqueViolation,
            constraintName: ConstraintName,
            detail: $"Key (fab, name_normalized)=(munich, {CollidingValue.ToUpperInvariant()}) already exists.");

    private static PostgresException Postgres(
        string sqlState, string constraintName = "", string detail = "") =>
        new(
            messageText: $"duplicate key value violates unique constraint \"{constraintName}\"",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState,
            detail: detail,
            constraintName: constraintName,
            tableName: "cameras");
}
