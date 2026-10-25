using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using Xunit.Abstractions;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 459, #3154). The camera list page projects <c>CameraSummaryDto</c>
/// inside the EF query. Moving that lambda into a mapping method must not change what
/// Postgres receives: a member the translator cannot see through silently becomes client
/// evaluation, and nothing else detects it. This drives the real handler over the real
/// <see cref="CameraQuerySource"/> and Npgsql provider with the connection open suppressed,
/// and pins the page <c>SELECT</c> captured before the move.
/// </summary>
public class ListCamerasProjectionSqlCharacterisationTests(ITestOutputHelper output)
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


        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Readers.Add(command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal));

            // The count runs as a single-row reader; any other statement is the page.
            using DataTable empty = new();
            if (command.CommandText.Contains("count(*)", StringComparison.OrdinalIgnoreCase))
            {
                empty.Columns.Add("count", typeof(int));
                empty.Rows.Add(0);
            }

            return new(InterceptionResult<DbDataReader>.SuppressWithResult(empty.CreateDataReader()));
        }
    }

    [Fact]
    public async Task The_page_select_projects_the_seven_summary_columns_as_plain_column_reads()
    {
        CaptureInterceptor capture = new();
        var options = new DbContextOptionsBuilder<CameraCatalogDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .AddInterceptors(new SuppressOpenInterceptor(), capture)
            .Options;
        await using CameraCatalogDbContext context = new(options);
        ListCamerasQueryHandler handler = new(new CameraQuerySource(context));

        var result = await handler.HandleAsync(
            new ListCamerasQuery([FabIdentifier.From("hamburg")], "name", "asc", 0, 25, false),
            default);

        result.IsSuccess.ShouldBeTrue();
        capture.Readers.Count.ShouldBe(2);
        var page = capture.Readers.Single(sql => !sql.Contains("count(*)", StringComparison.OrdinalIgnoreCase));
        output.WriteLine(page);
        page.ShouldBe(
            "SELECT c.camera_id, c.version::int, c.fab, c.name, c.rtsp_url, c.registered_at::timestamptz, c.status\n"
            + "FROM cameras AS c\n"
            + "WHERE c.fab = ANY (@fabs) AND c.status <> @Decommissioned\n"
            + "ORDER BY c.name, c.fab, c.camera_id\n"
            + "LIMIT @p3 OFFSET @p");
    }
}
