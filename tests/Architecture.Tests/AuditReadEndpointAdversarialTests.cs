using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.AuditObservability.Api;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Adversarial (spec 396, #2927), at the endpoint: the claims are read off a
/// <see cref="ClaimsPrincipal"/> by the real <c>FabClaims</c> and checked by the real
/// <see cref="DefaultFabAuthorizationGuard"/>, so what is exercised is the string the token
/// carries, not a pre-parsed list. Pins that a hidden <c>Unresolved</c> row's 404 is
/// byte-identical to an unknown id's, and the claim spellings — space-separated, nested
/// sub-group, bare parent, wrong case — that decide whether a caller is scoped at all.
/// </summary>
public class AuditReadEndpointAdversarialTests
{
    private const string Single = "/audit/{auditIdentifier:guid}";
    private const string SearchRoute = "/audit/";
    private const string Timeline = "/audit/{resourceKind}/{resourceIdentifier}";

    private const string Camera = "44444444-4444-4444-4444-444444444444";

    private static readonly Guid UnresolvedIdentifier = Guid.Parse("000000c1-0000-4000-8000-000000000000");
    private static readonly Guid NotApplicableIdentifier = Guid.Parse("000000d1-0000-4000-8000-000000000000");
    private static readonly Guid BerlinIdentifier = Guid.Parse("000000b1-0000-4000-8000-000000000000");
    private static readonly Guid MunichIdentifier = Guid.Parse("000000a1-0000-4000-8000-000000000000");

    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-03-10T12:00:00Z", CultureInfo.InvariantCulture);

    private static readonly PropertyInfo IdentifierProperty =
        typeof(AuditEventEntity).GetProperty(nameof(AuditEventEntity.Id))!;

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Start.AddHours(1);
    }

    private sealed class RowsSource(IEnumerable<AuditEventEntity> rows) : IAuditEventQuerySource
    {
        public IQueryable<AuditEventEntity> AuditEvents { get; } = new TestAsyncEnumerable<AuditEventEntity>(rows);
    }

    private static AuditEventEntity Row(Guid identifier, string? fab, FabScope scope, int minute)
    {
        V1Envelope envelope = new(
            EventTypeName: "StreamHealthChangedV1",
            OccurredAt: Start.AddMinutes(minute),
            Fab: fab is null ? Option<FabIdentifier>.None : Option<FabIdentifier>.Some(FabIdentifier.From(fab)),
            Actor: ActorIdentifier.System,
            ActorUsername: Option<string>.None,
            EventIdentifier: EventIdentifier.From(Guid.CreateVersion7()),
            Payload: """{"lastError":"rtsp://10.0.0.7:554 refused"}""",
            FabScope: scope);
        V1Mapping mapping = new(
            Option<ResourceKind>.Some(ResourceKind.Stream),
            Option<ResourceIdentifier>.Some(ResourceIdentifier.From(Camera)));
        var row = AuditEventEntity.From(envelope, mapping, new FixedClock());
        IdentifierProperty.SetValue(row, AuditEventIdentifier.From(identifier));
        return row;
    }

    private static AuditEventEntity[] AllRows() =>
    [
        Row(MunichIdentifier, "munich", FabScope.Owned, 4),
        Row(BerlinIdentifier, "berlin", FabScope.Owned, 3),
        Row(UnresolvedIdentifier, null, FabScope.Owned, 2),
        Row(NotApplicableIdentifier, null, FabScope.Neutral, 1),
    ];

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication app;

        public Host(params AuditEventEntity[] rows)
        {
            var builder = WebApplication.CreateBuilder([]);
            RowsSource source = new(rows);
            builder.Services
                .AddSingleton<IFabAuthorizationGuard>(new DefaultFabAuthorizationGuard())
                .AddSingleton(new SearchAuditQueryHandler(source))
                .AddSingleton(new GetResourceTimelineQueryHandler(source))
                .AddSingleton(new GetAuditEventQueryHandler(source));
            app = builder.Build();
            app.MapAuditEndpoints();
        }

        public Task<InProcessResponse> SendAsync(string pattern, ClaimsPrincipal user, InProcessRequest request) =>
            InProcessEndpointDriver.SendAsync(app, "GET", pattern, user, request);

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }

    private static ClaimsPrincipal WithGroups(params string[] groupClaimValues) =>
        new(new ClaimsIdentity(
            [
                new Claim("sub", "0199aaaa-0000-7000-8000-0000000000aa"),
                .. groupClaimValues.Select(value => new Claim("groups", value)),
            ],
            "test"));

    private static InProcessRequest ById(Guid identifier) =>
        new(Route: new Dictionary<string, string> { ["auditIdentifier"] = identifier.ToString() });

    private static async Task<InProcessResponse> GetSingle(ClaimsPrincipal user, Guid identifier, params AuditEventEntity[] rows)
    {
        await using Host host = new(rows);
        return await host.SendAsync(Single, user, ById(identifier));
    }

    // ----- GET /audit/{id}: the hidden row is indistinguishable from an unknown id -----

    public static TheoryData<string[]> ScopedGroupClaims() => new()
    {
        { ["/fabs/munich"] },
        { ["/fabs/berlin"] },
        { ["/fabs/munich /fabs/berlin"] },
        { ["/fabs/munich\t/fabs/MUNICH"] },
        { ["/fabs/munich", "/fabs/munich"] },
        { ["/fabs/munich/ops", "/fabs/munich"] },
        { ["/fabs/", "/fabs/munich"] },
        { ["/operators", "/fabs/hamburg"] },
    };

    [Theory]
    [MemberData(nameof(ScopedGroupClaims))]
    public async Task A_hidden_unresolved_row_answers_byte_for_byte_what_an_unknown_identifier_answers(string[] groups)
    {
        var user = WithGroups(groups);

        var hidden = await GetSingle(user, UnresolvedIdentifier, AllRows());
        var unknown = await GetSingle(user, UnresolvedIdentifier);

        hidden.Status.ShouldBe(404);
        hidden.Status.ShouldBe(unknown.Status);
        hidden.Body.ShouldBe(unknown.Body);
        hidden.Location.ShouldBe(unknown.Location);
        hidden.Body.ShouldContain("AUDIT_EVENT_NOT_FOUND");
        hidden.Body.ShouldNotContain("lastError");
    }

    [Fact]
    public async Task A_not_applicable_row_is_returned_to_a_scoped_caller()
    {
        var response = await GetSingle(WithGroups("/fabs/munich"), NotApplicableIdentifier, AllRows());

        response.Status.ShouldBe(200);
        response.Body.ShouldContain(NotApplicableIdentifier.ToString());
    }

    [Fact]
    public async Task A_resolved_row_of_another_fab_is_still_refused_by_the_guard_not_hidden()
    {
        // The 403-vs-404 existence oracle the spec leaves out of scope: the refusal names the
        // row's fab, so a munich caller learns both that the identifier exists and its fab.
        var refusal = await Should.ThrowAsync<FabAuthorizationException>(
            () => GetSingle(WithGroups("/fabs/munich"), BerlinIdentifier, AllRows()));

        refusal.FabId.ShouldBe("berlin");
    }

    // ----- Residual: claim spellings that leave a caller unscoped -----
    //
    // Pinned as they are (spec 396 §Out of scope). "/fabs/munich/ops" is what the realm's
    // full-path group mapper emits for a member of a sub-group of munich; "/fabs" is the
    // parent group itself. Each of these callers is refused GET /audit?fabId=munich by the
    // guard, yet reads the Unresolved row of every fab by identifier.

    public static TheoryData<string[]> UnscopedGroupClaims() => new()
    {
        { [] },
        { ["/fabs"] },
        { ["/fabs/"] },
        { ["/fabs/munich/ops"] },
        { ["/fabs/MUNICH"] },
        { ["/FABS/munich"] },
        { ["fabs/munich"] },
        { ["/fabs/munich/"] },
        { ["/operators"] },
    };

    [Theory]
    [MemberData(nameof(UnscopedGroupClaims))]
    public async Task Residual_a_caller_with_no_usable_fab_group_reads_the_unresolved_row(string[] groups)
    {
        var response = await GetSingle(WithGroups(groups), UnresolvedIdentifier, AllRows());

        response.Status.ShouldBe(200);
        response.Body.ShouldContain("lastError");
    }

    [Theory]
    [MemberData(nameof(UnscopedGroupClaims))]
    public async Task Residual_the_same_caller_is_refused_when_it_names_the_fab_it_belongs_to(string[] groups)
    {
        await using Host host = new(AllRows());

        await Should.ThrowAsync<FabAuthorizationException>(() => host.SendAsync(
            SearchRoute,
            WithGroups(groups),
            new InProcessRequest(Query: "?fabId=munich")));
    }

    // ----- GET /audit: fabId spellings -----

    [Theory]
    [InlineData("?fabId=")]
    [InlineData("?fabId=%20")]
    [InlineData("?fabId=%09")]
    [InlineData("?pageSize=200")]
    [InlineData("?fabId=munich")]
    [InlineData("?fabId=munich&fabId=munich")]
    [InlineData("?fabId=munich&fabId=berlin")]
    [InlineData("?fabId=MUNICH")]
    [InlineData("?fabId=munich%20")]
    [InlineData("?fabId=%2Ffabs%2Fmunich")]
    public async Task No_fabId_spelling_hands_a_munich_caller_the_unresolved_row(string query)
    {
        await using Host host = new(AllRows());

        InProcessResponse response;
        try
        {
            response = await host.SendAsync(SearchRoute, WithGroups("/fabs/munich"), new InProcessRequest(Query: query));
        }
        catch (FabAuthorizationException)
        {
            return;
        }

        response.Status.ShouldBeOneOf(200, 400);
        if (query is "?pageSize=200" or "?fabId=munich")
        {
            response.Status.ShouldBe(200);
            response.Body.ShouldContain(MunichIdentifier.ToString());
        }

        response.Body.ShouldNotContain(UnresolvedIdentifier.ToString());
        response.Body.ShouldNotContain(BerlinIdentifier.ToString());
    }

    [Fact]
    public async Task A_munich_search_without_a_fab_returns_exactly_its_row_and_the_not_applicable_row()
    {
        await using Host host = new(AllRows());

        var response = await host.SendAsync(SearchRoute, WithGroups("/fabs/munich"), new InProcessRequest(Query: "?pageSize=200"));

        response.Status.ShouldBe(200);
        response.Body.ShouldContain(MunichIdentifier.ToString());
        response.Body.ShouldContain(NotApplicableIdentifier.ToString());
        response.Body.ShouldNotContain(UnresolvedIdentifier.ToString());
        response.Body.ShouldNotContain(BerlinIdentifier.ToString());
    }

    // ----- GET /audit/{kind}/{id}: the timeline -----

    [Theory]
    [InlineData("?fabId=munich")]
    [InlineData("?fabId=munich&pageSize=1")]
    [InlineData("?fabId=munich&pageSize=200")]
    public async Task The_stream_timeline_hides_the_unresolved_row_from_its_own_fab(string query)
    {
        await using Host host = new(AllRows());

        var response = await host.SendAsync(
            Timeline,
            WithGroups("/fabs/munich"),
            new InProcessRequest(
                Route: new Dictionary<string, string> { ["resourceKind"] = "stream", ["resourceIdentifier"] = Camera },
                Query: query));

        response.Status.ShouldBe(200);
        // Oldest first: a one-row page holds the NotApplicable row, a wider one also the munich row.
        response.Body.ShouldContain((query.EndsWith("pageSize=1", StringComparison.Ordinal) ? NotApplicableIdentifier : MunichIdentifier).ToString());
        response.Body.ShouldNotContain(UnresolvedIdentifier.ToString());
        response.Body.ShouldNotContain(BerlinIdentifier.ToString());
    }
}
