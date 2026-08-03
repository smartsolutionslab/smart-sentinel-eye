using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.ServiceDefaults.Tests.Authorization;

/// <summary>
/// The ADR-0114 decision table, exercised directly.
///
/// <para>
/// These exist because the multi-fab rows have no reachable user: the realm
/// defines one fab group and every seeded account is in it or in none, so
/// FR-009 — refuse a multi-fab operator who names no fab — cannot be driven
/// through an integration test. It is also the branch that will never execute
/// in the current single-fab deployment, which makes it the one most likely
/// to rot unnoticed.
/// </para>
/// </summary>
public class FabResolutionTests
{
    private const string Ambiguous = "RULE_FAB_REQUIRED";

    // ---- writes: exactly one fab, or a refusal ----

    [Fact]
    public async Task One_fab_and_none_named_is_inferred()
    {
        (string fab, IResult problem) = await FabResolution.ResolveForWriteAsync(
            With("/fabs/munich"), fabId: "", new DefaultFabAuthorizationGuard(), Ambiguous, default);

        fab.ShouldBe("munich");
        problem.ShouldBeNull();
    }

    [Fact]
    public async Task One_fab_and_that_fab_named_is_accepted()
    {
        (string fab, IResult problem) = await FabResolution.ResolveForWriteAsync(
            With("/fabs/munich"), fabId: "munich", new DefaultFabAuthorizationGuard(), Ambiguous, default);

        fab.ShouldBe("munich");
        problem.ShouldBeNull();
    }

    // The row with no reachable user, and the reason this file exists.
    [Fact]
    public async Task Several_fabs_and_none_named_is_refused_rather_than_guessed()
    {
        (string fab, IResult problem) = await FabResolution.ResolveForWriteAsync(
            With("/fabs/munich", "/fabs/dresden"), fabId: "", new DefaultFabAuthorizationGuard(), Ambiguous, default);

        fab.ShouldBeNull();
        ProblemHttpResult result = problem.ShouldBeOfType<ProblemHttpResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        result.ProblemDetails.Title.ShouldBe(Ambiguous);
    }

    [Fact]
    public async Task Several_fabs_and_one_of_theirs_named_is_accepted()
    {
        (string fab, IResult problem) = await FabResolution.ResolveForWriteAsync(
            With("/fabs/munich", "/fabs/dresden"), fabId: "dresden", new DefaultFabAuthorizationGuard(), Ambiguous, default);

        fab.ShouldBe("dresden");
        problem.ShouldBeNull();
    }

    [Fact]
    public async Task Naming_a_fab_the_caller_lacks_is_refused()
    {
        await Should.ThrowAsync<FabAuthorizationException>(() =>
            FabResolution.ResolveForWriteAsync(
                With("/fabs/munich"), fabId: "dresden", new DefaultFabAuthorizationGuard(), Ambiguous, default));
    }

    [Fact]
    public async Task A_caller_assigned_to_no_fab_is_refused_on_a_write()
    {
        // Refused, not answered with a default. An operator in no fab is a
        // misconfiguration and should look like one.
        await Should.ThrowAsync<FabAuthorizationException>(() =>
            FabResolution.ResolveForWriteAsync(
                With("/operators"), fabId: "", new DefaultFabAuthorizationGuard(), Ambiguous, default));
    }

    // ---- reads: every fab the caller holds, no choice required ----

    [Fact]
    public async Task A_read_spans_every_fab_the_caller_holds()
    {
        IReadOnlyList<string> fabs = await FabResolution.ResolveForReadAsync(
            With("/fabs/munich", "/fabs/dresden"), fabId: "", new DefaultFabAuthorizationGuard(), default);

        // No RULE_FAB_REQUIRED here: listing does not have to choose, which is
        // the deliberate asymmetry with a write.
        fabs.ShouldBe(["munich", "dresden"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_read_narrowed_to_one_fab_returns_only_that_one()
    {
        IReadOnlyList<string> fabs = await FabResolution.ResolveForReadAsync(
            With("/fabs/munich", "/fabs/dresden"), fabId: "munich", new DefaultFabAuthorizationGuard(), default);

        fabs.ShouldBe(["munich"]);
    }

    [Fact]
    public async Task A_read_narrowed_to_a_fab_the_caller_lacks_is_refused()
    {
        await Should.ThrowAsync<FabAuthorizationException>(() =>
            FabResolution.ResolveForReadAsync(
                With("/fabs/munich"), fabId: "berlin", new DefaultFabAuthorizationGuard(), default));
    }

    [Fact]
    public async Task A_caller_assigned_to_no_fab_is_refused_on_a_read()
    {
        await Should.ThrowAsync<FabAuthorizationException>(() =>
            FabResolution.ResolveForReadAsync(
                With(), fabId: "", new DefaultFabAuthorizationGuard(), default));
    }

    private static ClaimsPrincipal With(params string[] groups) =>
        new(new ClaimsIdentity(
            groups.Select(g => new Claim(DefaultFabAuthorizationGuard.GroupClaimType, g))));
}
