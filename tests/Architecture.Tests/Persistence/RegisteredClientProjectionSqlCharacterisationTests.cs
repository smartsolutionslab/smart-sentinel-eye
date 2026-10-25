using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartSentinelEye.Identity.Application.Queries;
using SmartSentinelEye.Identity.Application.Queries.Handlers;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 459, #3154). The registered-client list projects <c>RegisteredClientSummaryDto</c>
/// inside the EF query. Moving that lambda into a mapping method must not change what
/// Postgres receives: a member the translator cannot see through silently becomes client
/// evaluation, and nothing else detects it. This drives the real handler over the real
/// <see cref="RegisteredClientQuerySource"/> and Npgsql provider with the connection open suppressed,
/// and pins the <c>SELECT</c> captured before the move.
/// </summary>
public class RegisteredClientProjectionSqlCharacterisationTests(ITestOutputHelper output)
{
    private sealed class SuppressOpenInterceptor : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result) => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) => new(InterceptionResult.Suppress());
    }

    private sealed class CaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Readers { get; } = [];

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default) =>
            new(InterceptionResult<object>.SuppressWithResult(0));

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Readers.Add(command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal));

            using DataTable empty = new();
            return new(InterceptionResult<DbDataReader>.SuppressWithResult(empty.CreateDataReader()));
        }
    }

    [Fact]
    public async Task The_select_projects_the_nine_summary_columns_as_plain_column_reads()
    {
        CaptureInterceptor capture = new();
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .AddInterceptors(new SuppressOpenInterceptor(), capture)
            .Options;
        await using IdentityDbContext context = new(options);
        ListDevicesQueryHandler handler = new(new RegisteredClientQuerySource(context));

        var result = await handler.HandleAsync(
            new ListDevicesQuery([FabIdentifier.From("hamburg")]),
            default);

        result.IsSuccess.ShouldBeTrue();
        var page = capture.Readers.ShouldHaveSingleItem();
        output.WriteLine(page);
        page.ShouldBe(
            "SELECT r.registered_client_id, r.version::int, r.client_id, r.kind, r.fab, r.registered_at::timestamptz, r.registered_by, r.disabled_at, r.last_rotated_at\n"
            + "FROM registered_clients AS r\n"
            + "WHERE r.kind = @kind AND r.fab = ANY (@fabs)\n"
            + "ORDER BY r.registered_at DESC");
    }
}
