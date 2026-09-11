using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;

public sealed class ListCamerasQueryHandler(ICameraQuerySource cameras)
    : IQueryHandler<ListCamerasQuery, Result<CameraListPageDto, ListCamerasError>>
{
    private static readonly string[] AllowedSortFields = ["name", "registeredAt"];
    private static readonly string[] AllowedSortOrders = ["asc", "desc"];

    public async Task<Result<CameraListPageDto, ListCamerasError>> HandleAsync(
        ListCamerasQuery query,
        CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();
        (IReadOnlyList<FabIdentifier>? fabs, string? sort, string? order, int offset, int limit, bool includeRetired, _) = query;

        if (!AllowedSortFields.Contains(sort, StringComparer.Ordinal))
        {
            return Failure(ListCamerasFailures.InvalidSortField(sort, AllowedSortFields));
        }

        if (!AllowedSortOrders.Contains(order, StringComparer.Ordinal))
        {
            return Failure(ListCamerasFailures.InvalidSortOrder(order));
        }

        if (offset < 0 || limit <= 0)
        {
            return Failure(ListCamerasFailures.InvalidPagination("Offset must be non-negative and limit must be positive."));
        }

        if (limit > ListCamerasDefaults.MaximumLimit)
        {
            return Failure(ListCamerasFailures.LimitExceeded(limit, ListCamerasDefaults.MaximumLimit));
        }

        bool descending = order == "desc";

        // FR-005: only cameras in fabs the caller holds, and filtered before
        // the count so the total reflects what they can actually page through.
        IQueryable<Camera> visible = cameras.Cameras.Where(camera => fabs.Contains(camera.Fab));

        // FR-007, and filtered here rather than at the endpoint so the count
        // and the page agree: a total that included retired cameras while the
        // rows excluded them would page past the end of the list.
        //
        // Spelled as the partial unique index spells it
        // (`status <> 'Decommissioned'`) so the query and the constraint that
        // makes name reuse work cannot drift apart.
        if (!includeRetired)
        {
            visible = visible.Where(camera => camera.Status != CameraStatus.Decommissioned);
        }

        // Spec 055, and **it belongs here for the same reason as the two above**:
        // the count is taken from this query, so filtering before it is what
        // makes the reported total describe the matches rather than the fab. At
        // the endpoint, or after Skip/Take, or in the browser, the total would
        // describe a different population than the rows beside it — a filtered
        // list that reads as authoritative and says there are 250 when eleven
        // matched.
        //
        // **Matched against the name as the uniqueness constraint normalises
        // it**, so "matches" and "is the same name" agree by construction. A
        // search folding what uniqueness keeps would show two distinct cameras
        // as one match; folding nothing it keeps would hide one an operator
        // knows exists. Case-insensitivity falls out of that, and so does
        // accents *not* folding — `upper` maps `ü` to `Ü`, not to `U`.
        //
        // `ToUpperInvariant` rather than `ToUpper`, and it matters: the culture
        // forms disagree on the Turkish dotless i, which would make a match
        // depend on the server's locale.
        //
        // The predicate comes from the seam because EF and the in-memory fake
        // reach the normalised name differently — see ICameraQuerySource.
        string? fragment = query.TrimmedFragment;
        if (fragment is not null)
        {
            visible = visible.Where(cameras.NameContains(fragment.ToUpperInvariant()));
        }

        IQueryable<Camera> source = SortBy(visible, sort, descending);

        int total = await source.CountAsync(cancellationToken);

        List<CameraSummaryDto> items = await source
            .Skip(offset)
            .Take(limit)
            .Select(camera => new CameraSummaryDto(
                camera.Id.Value,
                camera.Version,
                camera.Fab.Value,
                camera.Name.Value,
                camera.Url.Value,
                camera.Registration.At,
                camera.Status.Value))
            .ToListAsync(cancellationToken);

        return Success(
            new CameraListPageDto(items, total, offset, limit));
    }

    // EF Core's converter exposes Name as a plain string column at query time, so
    // `OrderBy(c => c.Name)` translates to `ORDER BY name` in Postgres. For the
    // in-memory tests CameraName.IComparable orders by NormalizedValue, keeping
    // the unit and integration tests on the same lambda.
    private static IQueryable<Camera> SortBy(IQueryable<Camera> source, string field, bool descending) =>
        (field, descending) switch
        {
            // Fab reads well for a multi-fab listing, but it does not make the
            // key unique and the comment here used to claim it did (#2144).
            // `(fab, name_normalized)` is unique only for *live* rows — the
            // index is partial on `status <> 'Decommissioned'`, deliberately,
            // so retiring a camera releases its name. With includeRetired=true
            // a decommissioned camera and its live replacement tie.
            //
            // Id closes it. CameraIdentifier is a Guid v7 (ADR-0039/0090),
            // unique by construction for every row whether retired or not, so
            // the order is total and Skip/Take has a position to resume from.
            // Without it a page boundary inside a tie shows one row twice and
            // the other never.
            ("name", false) => source.OrderBy(camera => camera.Name).ThenBy(camera => camera.Fab).ThenBy(camera => camera.Id),
            ("name", true) => source.OrderByDescending(camera => camera.Name).ThenBy(camera => camera.Fab).ThenBy(camera => camera.Id),
            // Registration instants tie among live rows too, and no index has
            // ever claimed otherwise.
            ("registeredAt", false) => source.OrderBy(camera => camera.Registration.At).ThenBy(camera => camera.Fab).ThenBy(camera => camera.Id),
            ("registeredAt", true) => source.OrderByDescending(camera => camera.Registration.At).ThenBy(camera => camera.Fab).ThenBy(camera => camera.Id),
            _ => throw new InvalidOperationException($"Unhandled sort field '{field}'."),
        };
}
