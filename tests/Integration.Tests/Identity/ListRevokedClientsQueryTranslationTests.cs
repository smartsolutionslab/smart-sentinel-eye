using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Identity.Application.Queries.Handlers;
using SmartSentinelEye.Identity.Infrastructure.Persistence;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Verifies that <see cref="ListRevokedClientsQueryHandler.BuildQuery"/> is
/// fully translatable to SQL. <c>ClientId</c> and <c>DisabledAt</c> both map
/// through EF Core value converters, and grouping by the converted CLR
/// property's <c>.Value</c> (rather than the value object itself) silently
/// falls back to client evaluation and then throws
/// <c>InvalidOperationException</c> — the exact regression a phase-6 review
/// of spec 270 found: the in-memory fake
/// <c>ListRevokedClientsQueryHandlerTests</c> passed while the query never
/// translated against real Postgres, so <c>GET /registered-clients/revoked</c>
/// 500'd on every call.
///
/// <para>
/// Offline by design, mirroring <c>ListEventsTranslationTests</c>:
/// <see cref="EntityFrameworkQueryableExtensions.ToQueryString"/> generates
/// the command text from the model and provider without opening a
/// connection, so there is no <c>AspireCollection</c> / Docker dependency.
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public sealed class ListRevokedClientsQueryTranslationTests
{
    private static IdentityDbContext NewContext()
    {
        DbContextOptions<IdentityDbContext> options =
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql("Host=localhost;Database=translation-check")
                .Options;
        return new IdentityDbContext(options);
    }

    [Fact]
    public void The_grouped_revocation_query_translates_to_sql()
    {
        using IdentityDbContext context = NewContext();

        IQueryable<RevokedClientEntry> queryable = ListRevokedClientsQueryHandler.BuildQuery(context.RegisteredClients);

        string sql = queryable.ToQueryString();

        sql.ShouldContain("GROUP BY");
        sql.ShouldContain("client_id");
        sql.ShouldContain("disabled_at");
    }

    [Fact]
    public void Grouping_by_the_converted_propertys_Value_does_not_translate()
    {
        // Pins the failure mode the value-object grouping form avoids:
        // grouping by member access on a value-converted CLR type
        // (`client.ClientId.Value`) cannot be translated, so the query throws
        // at execution time. If EF Core ever starts supporting this, the
        // value-object grouping form stays correct — but this documents why
        // BuildQuery must not reintroduce `.Value` inside GroupBy.
        using IdentityDbContext context = NewContext();

        Should.Throw<InvalidOperationException>(() =>
            context.RegisteredClients
                .Where(client => client.DisabledAt != null)
                .GroupBy(client => client.ClientId.Value)
                .Select(group => new RevokedClientEntry(
                    group.Key, group.Max(client => (DateTimeOffset)client.DisabledAt!)))
                .ToQueryString());
    }
}
